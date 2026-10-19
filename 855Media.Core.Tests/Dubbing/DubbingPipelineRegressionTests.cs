using System.Net;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class DubbingPipelineRegressionTests
{
    private sealed class TranslationHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public string? GeminiRequestBody { get; private set; }
        public bool FailGemini { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.Host == "generativelanguage.googleapis.com")
            {
                GeminiRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(
                    FailGemini ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK
                )
                {
                    Content = new StringContent(
                        """{"candidates":[{"content":{"parts":[{"text":"[{\"index\":2,\"khmer\":\"សួស្តី\"}]"}]}}]}"""
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[\"Hello\"]"),
            };
        }
    }

    [Fact]
    public async Task EnglishTranslation_WithGeminiKey_UsesEnglishTarget()
    {
        var handler = new TranslationHandler();
        using var client = new HttpClient(handler);
        var service = new SubtitleTranslationService(client);

        var result = await service.TranslateToEnglishAsync("Bonjour", "fr", "configured-key");

        Assert.Equal("Hello", result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("clients5.google.com", request.Host);
        Assert.Contains("tl=en", request.Query);
        Assert.Null(handler.GeminiRequestBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeminiTranslation_TranslatesOnlyMissingLinesIncludingDuringFallback(
        bool failGemini
    )
    {
        var handler = new TranslationHandler { FailGemini = failGemini };
        using var client = new HttpClient(handler);
        var service = new SubtitleTranslationService(client);
        var segments = new[]
        {
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "EDITED_LINE",
                KhmerText = "Keep my edit",
            },
            new SubtitleSegment { Index = 2, OriginalText = "Hello" },
        };
        var progress = new List<(int, int)>();

        await service.TranslateSegmentsWithGeminiAsync(
            "configured-key",
            segments,
            progressCallback: (current, total) => progress.Add((current, total))
        );

        Assert.Equal("Keep my edit", segments[0].KhmerText);
        Assert.Equal(failGemini ? "Hello" : "សួស្តី", segments[1].KhmerText);
        Assert.NotNull(handler.GeminiRequestBody);
        Assert.DoesNotContain("EDITED_LINE", handler.GeminiRequestBody);
        Assert.Equal((2, 2), progress.Last());
    }

    [Theory]
    [InlineData(false, 4, 2, false)]
    [InlineData(true, 4, 2, true)]
    [InlineData(true, 1, 2, false)]
    public void TimelineAudioFilter_RespectsTimeStretchSetting(
        bool enabled,
        double clipSeconds,
        double slotSeconds,
        bool shouldStretch
    )
    {
        var filter = DubbingPipeline.BuildTimelineAudioFilter(
            enabled,
            TimeSpan.FromSeconds(clipSeconds),
            TimeSpan.FromSeconds(slotSeconds)
        );
        Assert.Equal(shouldStretch, filter.Contains("atempo="));
        Assert.Contains("sample_rates=44100:channel_layouts=stereo", filter);
    }

    [Fact]
    public void SpeechRate_AppliesOnlyTheSelectedEmotionToTheBaseRate()
    {
        var character = new MovieCharacter { BaseSpeechRate = "+15%", EmotionPreset = "Angry" };
        Assert.Equal("+35%", character.SpeechRate);
        Assert.Equal(
            "+35%",
            ActorEmotionEngine.ComputeCharacterSpeechRate(
                character,
                ActorEmotionEngine.GetConfig("Angry")
            )
        );
        Assert.Equal(
            "+9%",
            ActorEmotionEngine.ComputeCharacterSpeechRate(
                character,
                ActorEmotionEngine.GetConfig("Whisper")
            )
        );
    }

    [Fact]
    public async Task CastAutomation_AssignsUnassignedLinesAndPreservesExistingCast()
    {
        var job = new DubbingJob { VideoFilePath = "nonexistent.mp4" };
        var narrator = new MovieCharacter { Name = "Custom Narrator" };
        job.Characters.Add(narrator);
        // Include a matching default label to ensure it is not mistaken for an assignment.
        job.Characters.Add(new MovieCharacter { Name = "Hero (Male)" });
        var assigned = new SubtitleSegment
        {
            Index = 1,
            OriginalText = "Introduction",
            CharacterId = narrator.Id,
            SpeakerName = narrator.Name,
        };
        var prefixed = new SubtitleSegment { Index = 2, OriginalText = "MARY: Hello." };
        var unassigned = new SubtitleSegment { Index = 3, OriginalText = "Good morning." };
        job.Segments.Add(assigned);
        job.Segments.Add(prefixed);
        job.Segments.Add(unassigned);

        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(job, "missing-ffmpeg");

        Assert.Equal(narrator.Id, assigned.CharacterId);
        Assert.Equal("Introduction", assigned.OriginalText);
        Assert.Equal("Hello.", prefixed.OriginalText);
        Assert.Equal("MARY", prefixed.SpeakerName);
        Assert.NotNull(prefixed.CharacterId);
        Assert.NotNull(unassigned.CharacterId);
        Assert.Contains(job.Characters, c => c.Id == unassigned.CharacterId);
        var characterCount = job.Characters.Count;
        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(job, "missing-ffmpeg");
        Assert.Equal(characterCount, job.Characters.Count);
    }

    [Fact]
    public async Task GeminiTranslation_PreservesExistingEditsAndReportsCompletion()
    {
        var segments = new[]
        {
            new SubtitleSegment { OriginalText = "Hello", KhmerText = "My edited translation" },
            new SubtitleSegment { OriginalText = "" },
        };
        var progress = new List<(int Current, int Total)>();

        await new SubtitleTranslationService().TranslateSegmentsWithGeminiAsync(
            "unused-key",
            segments,
            progressCallback: (current, total) => progress.Add((current, total))
        );

        Assert.Equal("My edited translation", segments[0].KhmerText);
        Assert.Equal((2, 2), progress.Last());
    }

    [Fact]
    public void PreserveAudioAssets_KeepsClipsAndTracksAfterWorkspaceCleanup()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "855Media_AudioAssets_Test",
            Guid.NewGuid().ToString("N")
        );
        var workspace = Path.Combine(root, "work");
        Directory.CreateDirectory(workspace);
        try
        {
            var clip = Path.Combine(workspace, "clip.wav");
            File.WriteAllBytes(clip, [1, 2, 3, 4]);
            var job = new DubbingJob
            {
                OutputFilePath = Path.Combine(root, "movie.mp4"),
                ExtractedDialoguePath = clip,
                ExtractedBgmPath = clip,
                DubbedAudioPath = clip,
            };
            job.Segments.Add(new SubtitleSegment { AudioClipPath = clip });

            DubbingPipeline.PreserveAudioAssets(job);
            Directory.Delete(workspace, true);

            Assert.True(job.Segments[0].HasAudioClip);
            Assert.Equal(
                new byte[] { 1, 2, 3, 4 },
                File.ReadAllBytes(job.Segments[0].AudioClipPath!)
            );
            Assert.Equal(job.Segments[0].AudioClipPath, job.ExtractedDialoguePath);
            Assert.True(File.Exists(job.ExtractedBgmPath));
            Assert.True(File.Exists(job.DubbedAudioPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
