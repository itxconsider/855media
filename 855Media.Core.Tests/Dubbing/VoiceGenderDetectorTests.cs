using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class VoiceGenderDetectorTests
{
    [Fact]
    public void Constants_ShouldDefineMaleFemaleAndChild()
    {
        Assert.Equal("Male", VoiceGenderDetector.GenderMale);
        Assert.Equal("Female", VoiceGenderDetector.GenderFemale);
        Assert.Equal("Child", VoiceGenderDetector.GenderChild);
        Assert.Equal(165.0, VoiceGenderDetector.GenderPitchThresholdHz);
        Assert.Equal(265.0, VoiceGenderDetector.ChildPitchThresholdHz);
    }

    [Theory]
    [InlineData(120.0, "Male")]
    [InlineData(150.0, "Male")]
    [InlineData(180.0, "Female")]
    [InlineData(220.0, "Female")]
    [InlineData(280.0, "Child")]
    [InlineData(340.0, "Child")]
    [InlineData(420.0, "Child")]
    public void PitchThresholds_ShouldCorrectlyClassifyVoiceRanges(double pitchHz, string expectedCategory)
    {
        string category = pitchHz switch
        {
            >= VoiceGenderDetector.ChildPitchThresholdHz => VoiceGenderDetector.GenderChild,
            >= VoiceGenderDetector.GenderPitchThresholdHz => VoiceGenderDetector.GenderFemale,
            _ => VoiceGenderDetector.GenderMale,
        };

        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public async Task DetectGendersForSegmentsAsync_WithChildCues_DetectsChildVoice()
    {
        var segments = new List<SubtitleSegment>
        {
            new SubtitleSegment
            {
                Index = 1,
                OriginalText = "Mommy look at that puppy!",
                KhmerText = "ម៉ាក់មើលកូនឆ្កែនោះ!",
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(3)
            },
            new SubtitleSegment
            {
                Index = 2,
                OriginalText = "Good morning everyone, I am the father.",
                KhmerText = "ជំរាបសួរអ្នកទាំងអស់គ្នា ខ្ញុំជាឪពុក។",
                StartTime = TimeSpan.FromSeconds(4),
                EndTime = TimeSpan.FromSeconds(7)
            },
            new SubtitleSegment
            {
                Index = 3,
                OriginalText = "ក្មេងស្រីម្ជូរ ឃ្លានបាយហើយ",
                StartTime = TimeSpan.FromSeconds(8),
                EndTime = TimeSpan.FromSeconds(10)
            }
        };

        // Pass non-existent video path to test text and conversational heuristic fallback
        var results = await VoiceGenderDetector.DetectGendersForSegmentsAsync(
            "ffmpeg",
            "dummy_non_existent.mp4",
            segments,
            CancellationToken.None
        );

        Assert.True(results.ContainsKey(1));
        Assert.True(results.ContainsKey(2));
        Assert.True(results.ContainsKey(3));

        // Segment 1 has "mommy" (child-directed cue) -> Child or Female
        Assert.Contains(results[1].Gender, new[] { VoiceGenderDetector.GenderChild, VoiceGenderDetector.GenderFemale });

        // Segment 2 has "father" -> Male
        Assert.Equal(VoiceGenderDetector.GenderMale, results[2].Gender);

        // Segment 3 has "ក្មេងស្រី" (little girl) -> Child
        Assert.Equal(VoiceGenderDetector.GenderChild, results[3].Gender);
    }
}
