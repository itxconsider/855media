using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class DubbingPipelineAutomationTests
{
    [Fact]
    public async Task EnsureAutomatedCastAndEmotionsAsync_ExtractsPrefixesAndAssignsCast()
    {
        var job = new DubbingJob { VideoFilePath = "test_video.mp4" };

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "JOHN: We must hold the fortress at all costs!",
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(3),
            }
        );

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 2,
                OriginalText = "MARY: [whispering] I hear enemies approaching.",
                StartTime = TimeSpan.FromSeconds(3.5),
                EndTime = TimeSpan.FromSeconds(6),
            }
        );

        var logs = new List<string>();
        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(
            job,
            "ffmpeg",
            log: msg => logs.Add(msg),
            ct: CancellationToken.None
        );

        // Verify characters were extracted
        Assert.Equal(2, job.Characters.Count);
        var john = job.Characters.FirstOrDefault(c =>
            c.Name.Equals("JOHN", StringComparison.OrdinalIgnoreCase)
        );
        var mary = job.Characters.FirstOrDefault(c =>
            c.Name.Equals("MARY", StringComparison.OrdinalIgnoreCase)
        );

        Assert.NotNull(john);
        Assert.NotNull(mary);

        Assert.Equal(VoiceGenderDetector.GenderMale, john.Gender);
        Assert.Equal(VoiceGenderDetector.GenderFemale, mary.Gender);

        // Verify segments were assigned
        Assert.Equal(john.Id, job.Segments[0].CharacterId);
        Assert.Equal(mary.Id, job.Segments[1].CharacterId);

        // Verify clean text without speaker prefix
        Assert.Equal("We must hold the fortress at all costs!", job.Segments[0].OriginalText);
        Assert.Equal("[whispering] I hear enemies approaching.", job.Segments[1].OriginalText);

        // Verify Mary's line detected whispering emotion
        Assert.Equal(ActorEmotionEngine.EmotionWhisper, job.Segments[1].Emotion);
    }

    [Fact]
    public async Task EnsureAutomatedCastAndEmotionsAsync_DetectsEmotionsAcrossSegments()
    {
        var job = new DubbingJob { VideoFilePath = "test_video.mp4" };

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "[crying] Why did this have to happen?",
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(4),
            }
        );

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 2,
                OriginalText = "[screaming] Get out of here right now!",
                StartTime = TimeSpan.FromSeconds(4.5),
                EndTime = TimeSpan.FromSeconds(7),
            }
        );

        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(
            job,
            "ffmpeg",
            log: null,
            ct: CancellationToken.None
        );

        Assert.Equal(ActorEmotionEngine.EmotionCrying, job.Segments[0].Emotion);
        Assert.True(
            job.Segments[1].Emotion == ActorEmotionEngine.EmotionAngry
                || job.Segments[1].Emotion == ActorEmotionEngine.EmotionScream
        );
    }

    [Fact]
    public async Task EnsureAutomatedCastAndEmotionsAsync_FallbackAcousticCastWhenNoPrefixes()
    {
        var job = new DubbingJob
        {
            VideoFilePath = "nonexistent_video.mp4", // Forces text heuristic / turn fallback
        };

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "I need to speak with the commander immediately.",
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(2.5),
            }
        );

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 2,
                OriginalText = "Yes sir, right away!",
                StartTime = TimeSpan.FromSeconds(3),
                EndTime = TimeSpan.FromSeconds(5),
            }
        );

        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(
            job,
            "ffmpeg",
            log: null,
            ct: CancellationToken.None
        );

        // Should populate standard cast (Hero, Female Lead, Child, Villain)
        Assert.True(job.Characters.Count >= 3);
        Assert.All(job.Segments, s => Assert.NotNull(s.CharacterId));
        Assert.All(job.Segments, s => Assert.NotNull(s.SpeakerName));
        Assert.All(job.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.DetectedGender)));
    }

    [Fact]
    public async Task EnsureAutomatedCastAndEmotionsAsync_PreservesExistingCharacters()
    {
        var job = new DubbingJob { VideoFilePath = "test_video.mp4" };

        var customChar = new MovieCharacter
        {
            Id = Guid.NewGuid(),
            Name = "Custom Narrator",
            Gender = VoiceGenderDetector.GenderMale,
            BaseVoice = "km-KH-PisethNeural",
        };
        job.Characters.Add(customChar);

        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "Long ago in the ancient kingdom...",
                CharacterId = customChar.Id,
                SpeakerName = customChar.Name,
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(3),
            }
        );

        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(
            job,
            "ffmpeg",
            log: null,
            ct: CancellationToken.None
        );

        // Characters should not be overwritten
        Assert.Single(job.Characters);
        Assert.Equal(customChar.Id, job.Segments[0].CharacterId);
        Assert.Equal("Custom Narrator", job.Segments[0].SpeakerName);
    }

    [Fact]
    public async Task EnsureAutomatedCastAndEmotionsAsync_PreservesSegmentTimingAndCount()
    {
        var job = new DubbingJob { VideoFilePath = "test_video.mp4" };
        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "First scene cut line.",
                StartTime = TimeSpan.FromSeconds(1.2),
                EndTime = TimeSpan.FromSeconds(3.8),
            }
        );
        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 2,
                OriginalText = "Second scene cut line after pause.",
                StartTime = TimeSpan.FromSeconds(6.5),
                EndTime = TimeSpan.FromSeconds(9.0),
            }
        );

        await DubbingPipeline.EnsureAutomatedCastAndEmotionsAsync(
            job,
            "ffmpeg",
            log: null,
            ct: CancellationToken.None
        );

        Assert.Equal(2, job.Segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(1.2), job.Segments[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(3.8), job.Segments[0].EndTime);
        Assert.Equal(TimeSpan.FromSeconds(6.5), job.Segments[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(9.0), job.Segments[1].EndTime);
    }
}
