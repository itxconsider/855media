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
    public void PitchThresholds_ShouldCorrectlyClassifyVoiceRanges(
        double pitchHz,
        string expectedCategory
    )
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
                EndTime = TimeSpan.FromSeconds(3),
            },
            new SubtitleSegment
            {
                Index = 2,
                OriginalText = "Good morning everyone, I am the father.",
                KhmerText = "ជំរាបសួរអ្នកទាំងអស់គ្នា ខ្ញុំជាឪពុក។",
                StartTime = TimeSpan.FromSeconds(4),
                EndTime = TimeSpan.FromSeconds(7),
            },
            new SubtitleSegment
            {
                Index = 3,
                OriginalText = "ក្មេងស្រីម្ជូរ ឃ្លានបាយហើយ",
                StartTime = TimeSpan.FromSeconds(8),
                EndTime = TimeSpan.FromSeconds(10),
            },
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
        Assert.Contains(
            results[1].Gender,
            new[] { VoiceGenderDetector.GenderChild, VoiceGenderDetector.GenderFemale }
        );

        // Segment 2 has "father" -> Male
        Assert.Equal(VoiceGenderDetector.GenderMale, results[2].Gender);

        // Segment 3 has "ក្មេងស្រី" (little girl) -> Child
        Assert.Equal(VoiceGenderDetector.GenderChild, results[3].Gender);
    }

    [Theory]
    [InlineData(120.0, 118.0, 122.0)] // Adult male pitch
    [InlineData(210.0, 206.0, 214.0)] // Adult female pitch (must not be halved to 105 Hz)
    [InlineData(320.0, 314.0, 326.0)] // Child pitch (must not be halved to 160 Hz)
    public void CalculateMedianPitchHz_WithSynthesizedAudio_AccuratelyDetectsPitchWithoutOctaveError(
        double targetFreq,
        double minExpected,
        double maxExpected
    )
    {
        int sampleRate = 16000;
        int durationSamples = (int)(sampleRate * 0.5); // 500ms
        var samples = new short[durationSamples];

        for (int i = 0; i < durationSamples; i++)
        {
            double t = (double)i / sampleRate;
            // Add fundamental + slight harmonic overtone + minor noise to simulate voice timbre
            double val =
                Math.Sin(2.0 * Math.PI * targetFreq * t) * 16000.0
                + Math.Sin(4.0 * Math.PI * targetFreq * t) * 4000.0;
            samples[i] = (short)Math.Clamp(val, short.MinValue, short.MaxValue);
        }

        double detectedPitch = VoiceGenderDetector.CalculateMedianPitchHz(samples, sampleRate);

        Assert.InRange(detectedPitch, minExpected, maxExpected);
    }

    [Theory]
    [InlineData("[Heroine]: Watch out for the trap!", "Female")]
    [InlineData("[Hero]: I am right behind you.", "Male")]
    [InlineData("[Child]: Can we play outside now?", "Child")]
    [InlineData("Mom: Breakfast is ready, everyone.", "Female")]
    [InlineData("Dad: Don't forget your backpack.", "Male")]
    public void DetectGenderFromText_WithSpeakerPrefixes_AccuratelyClassifies(
        string dialogue,
        string expectedGender
    )
    {
        string? result = VoiceGenderDetector.DetectGenderFromText(dialogue, null);
        Assert.Equal(expectedGender, result);
    }

    [Theory]
    [InlineData("Where did he go?")]
    [InlineData("Tell her that I will be late.")]
    [InlineData("Why are you all wet, baby?")]
    [InlineData("Where are the kids playing?")]
    public void DetectGenderFromText_WithThirdPersonOrCasualTerms_DoesNotFalseTrigger(
        string neutralDialogue
    )
    {
        string? result = VoiceGenderDetector.DetectGenderFromText(neutralDialogue, null);
        // Neutral sentences talking about others or using casual terms should not falsely classify
        Assert.Null(result);
    }

    [Fact]
    public void DetectGenderFromText_WithKhmerFirstPerson_AccuratelyClassifies()
    {
        // ខ្ញុំបាទ (Male 1st person)
        string? maleResult = VoiceGenderDetector.DetectGenderFromText(
            null,
            "ខ្ញុំបាទសូមគោរពជម្រាបសួរលោកអ្នកនាង"
        );
        Assert.Equal(VoiceGenderDetector.GenderMale, maleResult);

        // នាងខ្ញុំ (Female 1st person)
        string? femaleResult = VoiceGenderDetector.DetectGenderFromText(
            null,
            "នាងខ្ញុំសូមអរគុណច្រើនសម្រាប់ការគាំទ្រ"
        );
        Assert.Equal(VoiceGenderDetector.GenderFemale, femaleResult);
    }

    [Theory]
    [InlineData("[Heroine]: Watch out!", true, "Heroine", "Watch out!")]
    [InlineData("Mom: Dinner is ready", true, "Mom", "Dinner is ready")]
    [InlineData("Actor A - Hello world", true, "Actor A", "Hello world")]
    [InlineData("តួA ៖ សួស្តី", true, "តួA", "សួស្តី")]
    [InlineData("【Villain】: You cannot win", true, "Villain", "You cannot win")]
    [InlineData("[whispering quietly]", false, "", "[whispering quietly]")]
    [InlineData(
        "Just normal dialogue without prefix",
        false,
        "",
        "Just normal dialogue without prefix"
    )]
    public void TryExtractSpeakerPrefix_WorksAccurately(
        string raw,
        bool expectedSuccess,
        string expectedName,
        string expectedText
    )
    {
        bool success = VoiceGenderDetector.TryExtractSpeakerPrefix(
            raw,
            out var speakerName,
            out var dialogueText
        );
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedName, speakerName);
            Assert.Equal(expectedText, dialogueText);
        }
    }

    [Theory]
    [InlineData("Alice", "Female")]
    [InlineData("Elena", "Female")]
    [InlineData("Mother", "Female")]
    [InlineData("Queen", "Female")]
    [InlineData("Sreymom", "Female")]
    [InlineData("John", "Male")]
    [InlineData("David", "Male")]
    [InlineData("Father", "Male")]
    [InlineData("King", "Male")]
    [InlineData("Piseth", "Male")]
    [InlineData("Tommy", "Child")]
    [InlineData("Little Boy", "Child")]
    [InlineData("ក្មេង", "Child")]
    [InlineData("កុមារ", "Child")]
    public void DetectGenderFromName_AccuratelyClassifies(string name, string expectedGender)
    {
        string? result = VoiceGenderDetector.DetectGenderFromName(name);
        Assert.Equal(expectedGender, result);
    }

    [Theory]
    [InlineData("Dark Shadow Villain", "Villain")]
    [InlineData("Evil Monster", "Villain")]
    [InlineData("Little Kid", "Youth")]
    [InlineData("Old Grandpa", "Elder")]
    [InlineData("Wise Master Monk", "Elder")]
    [InlineData("Movie Trailer Narrator", "Narrator")]
    [InlineData("Warrior Commander", "Action")]
    [InlineData("Sweetheart Lover", "Romantic")]
    [InlineData("Funny Clown", "Comic")]
    [InlineData("Hero", "Hero")]
    public void DetectToneArchetypeFromName_AccuratelyInfersArchetype(
        string name,
        string expectedArchetype
    )
    {
        string archetype = VoiceGenderDetector.DetectToneArchetypeFromName(name);
        Assert.Equal(expectedArchetype, archetype);
    }
}
