using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class SubtitleTranslationServiceTests
{
    private readonly SubtitleTranslationService _service = new();

    [Fact]
    public void PolishKhmerDialogue_ReplacesCommonIdioms_WithCinematicKhmer()
    {
        // Arrange
        var input = "តើឯងកំពុងធ្វើអ្វី? What the hell! Shut up and hurry up!";

        // Act
        var polished = SubtitleTranslationService.PolishKhmerDialogue(input);

        // Assert
        Assert.DoesNotContain("ធ្វើអ្វី", polished);
        Assert.Contains("ធ្វើអី", polished);
        Assert.Contains("ស្អីគេ", polished);
        Assert.Contains("បិទមាត់ទៅ!", polished);
        Assert.Contains("លឿនឡើង!", polished);
    }

    [Fact]
    public void PolishKhmerDialogue_ReplacesLiteralMachineTranslatedKhmer()
    {
        // Arrange
        var input = "តើឯងកំពុងធ្វើអ្វី? ឋាននរកអ្វី! បិទមាត់របស់អ្នក ហើយប្រញាប់ឡើង!";

        // Act
        var polished = SubtitleTranslationService.PolishKhmerDialogue(input);

        // Assert
        Assert.DoesNotContain("ធ្វើអ្វី", polished);
        Assert.Contains("ធ្វើអី", polished);
        Assert.Contains("ស្អីគេ", polished);
        Assert.Contains("បិទមាត់ទៅ!", polished);
        Assert.Contains("លឿនឡើង!", polished);
    }

    [Fact]
    public void PolishKhmerDialogue_RemovesRedundantFormalQuestionPrefix()
    {
        // Arrange
        var input = "តើអ្នកសុខសប្បាយទេ?";

        // Act
        var polished = SubtitleTranslationService.PolishKhmerDialogue(input);

        // Assert
        Assert.False(polished.StartsWith("តើ"));
        Assert.StartsWith("អ្នកសុខសប្បាយទេ?", polished);
    }

    [Fact]
    public void PolishKhmerDialogue_ReplacesChineseDramaColloquialisms()
    {
        // Arrange
        var input = "怎么回事？王爷，放开我！救命啊！快走！";

        // Act
        var polished = SubtitleTranslationService.PolishKhmerDialogue(input);

        // Assert
        Assert.Contains("មានរឿងអីកើតឡើងហ្នឹង?", polished);
        Assert.Contains("លោកម្ចាស់", polished);
        Assert.Contains("លែងខ្ញុំទៅ!", polished);
        Assert.Contains("ជួយផង!", polished);
        Assert.Contains("លឿនឡើង!", polished);
    }

    [Fact]
    public void PolishKhmerDialogue_HandlesEmptyOrNull()
    {
        Assert.Equal(string.Empty, SubtitleTranslationService.PolishKhmerDialogue(string.Empty));
        Assert.Equal(string.Empty, SubtitleTranslationService.PolishKhmerDialogue("   "));
    }

    [Fact]
    public void GenerateSrt_SingleKhmer_FormatsCorrectly()
    {
        // Arrange
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(3.5),
                OriginalText = "Hello everyone",
                KhmerText = "សួស្តីអ្នកទាំងអស់គ្នា",
                SpeakerName = "Sophea",
            },
            new()
            {
                Index = 2,
                StartTime = TimeSpan.FromSeconds(4),
                EndTime = TimeSpan.FromSeconds(6.25),
                OriginalText = "Good morning",
                KhmerText = "អរុណសួស្តី",
                SpeakerName = "Bora",
            },
        };

        // Act
        var srt = SubtitleTranslationService.GenerateSrt(
            segments,
            includeOriginal: false,
            includeSpeakerTag: false
        );

        // Assert
        Assert.Contains(
            "1\r\n00:00:01,000 --> 00:00:03,500\r\nសួស្តីអ្នកទាំងអស់គ្នា",
            srt.Replace("\r\n", "\n").Replace("\n", "\r\n")
        );
        Assert.Contains(
            "2\r\n00:00:04,000 --> 00:00:06,250\r\nអរុណសួស្តី",
            srt.Replace("\r\n", "\n").Replace("\n", "\r\n")
        );
    }

    [Fact]
    public void GenerateSrt_BilingualWithSpeakerTag_IncludesBothLinesAndSpeaker()
    {
        // Arrange
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(3),
                OriginalText = "Where are you going?",
                KhmerText = "ឯងទៅណា?",
                SpeakerName = "Dara",
            },
        };

        // Act
        var srt = SubtitleTranslationService.GenerateSrt(
            segments,
            includeOriginal: true,
            includeSpeakerTag: true
        );

        // Assert
        Assert.Contains("[Dara] ឯងទៅណា?", srt);
        Assert.Contains("Where are you going?", srt);
    }

    [Fact]
    public void GenerateVtt_OutputsValidWebVttHeaderAndDotTimestamps()
    {
        // Arrange
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(2.5),
                EndTime = TimeSpan.FromSeconds(5.75),
                KhmerText = "តោះទៅជាមួយគ្នា",
                OriginalText = "Let's go together",
            },
        };

        // Act
        var vtt = SubtitleTranslationService.GenerateVtt(
            segments,
            includeOriginal: false,
            includeSpeakerTag: false
        );

        // Assert
        Assert.StartsWith("WEBVTT", vtt);
        Assert.Contains("00:00:02.500 --> 00:00:05.750", vtt);
        Assert.Contains("តោះទៅជាមួយគ្នា", vtt);
    }

    [Fact]
    public async Task DubbingProject_RoundtripsMasteringAndLipSyncSettingsAsync()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"dub_proj_test_{Guid.NewGuid():N}.855dub");
        try
        {
            var project = new DubbingProject
            {
                VideoFilePath = "C:\\test\\video.mp4",
                EnableLoudnessNormalization = true,
                EnableSmartTimeStretch = true,
            };

            // Act
            await project.SaveAsync(tempPath);
            var loaded = await DubbingProject.LoadAsync(tempPath);

            // Assert
            Assert.True(loaded.EnableLoudnessNormalization);
            Assert.True(loaded.EnableSmartTimeStretch);

            // Test setting to false
            project.EnableLoudnessNormalization = false;
            project.EnableSmartTimeStretch = false;
            await project.SaveAsync(tempPath);
            var loadedFalse = await DubbingProject.LoadAsync(tempPath);

            Assert.False(loadedFalse.EnableLoudnessNormalization);
            Assert.False(loadedFalse.EnableSmartTimeStretch);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void DubbingJob_DefaultFlags_AreEnabledForCinemaQuality()
    {
        var job = new DubbingJob();
        Assert.True(job.EnableLoudnessNormalization);
        Assert.True(job.EnableSmartTimeStretch);
    }

    [Fact]
    public void ParseSrt_FiltersOutOpeningMusicAndNonSpeechAtZero()
    {
        var srt =
            @"1
00:00:00,000 --> 00:00:03,500
[Music]

2
00:00:04,200 --> 00:00:06,800
Hello, welcome to our presentation.
";
        var segments = _service.ParseSrt(srt);

        Assert.Single(segments);
        Assert.Equal(1, segments[0].Index);
        Assert.Equal(TimeSpan.FromSeconds(4.2), segments[0].StartTime);
        Assert.Equal("Hello, welcome to our presentation.", segments[0].OriginalText);
    }

    [Fact]
    public void ParseSrt_FiltersOutPunctuationOnlyAndMusicHallucinations()
    {
        var srt =
            @"1
00:00:00,000 --> 00:00:02,100
♪ ♪

2
00:00:02,150 --> 00:00:03,000
.

3
00:00:03,500 --> 00:00:05,500
(music)

4
00:00:06,000 --> 00:00:09,000
Real actor dialogue starts here.
";
        var segments = _service.ParseSrt(srt);

        Assert.Single(segments);
        Assert.Equal(TimeSpan.FromSeconds(6.0), segments[0].StartTime);
        Assert.Equal("Real actor dialogue starts here.", segments[0].OriginalText);
    }

    [Fact]
    public void GenerateSrt_WithUseEnglishText_OutputsEnglishSubtitles()
    {
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(3),
                OriginalText = "Bonjour le monde",
                KhmerText = "សួស្តីពិភពលោក",
                EnglishText = "Hello world",
            },
        };

        var srt = SubtitleTranslationService.GenerateSrt(
            segments,
            includeOriginal: false,
            includeSpeakerTag: false,
            useEnglishText: true
        );

        Assert.Contains("Hello world", srt);
        Assert.DoesNotContain("សួស្តីពិភពលោក", srt);
    }

    [Fact]
    public async Task TranslateToEnglishAsync_WithEmptyString_ReturnsEmpty()
    {
        var result = await _service.TranslateToEnglishAsync("");
        Assert.Equal(string.Empty, result);

        var resultWhitespace = await _service.TranslateToEnglishAsync("   ");
        Assert.Equal(string.Empty, resultWhitespace);
    }

    [Fact]
    public async Task TranslateSegmentsToEnglishAsync_KeepsEnglishTextWhenAlreadyEnglish()
    {
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(2),
                OriginalText = "Good morning everyone.",
            },
        };

        await _service.TranslateSegmentsToEnglishAsync(segments, sourceLang: "en");

        Assert.Equal("Good morning everyone.", segments[0].EnglishText);
    }
}
