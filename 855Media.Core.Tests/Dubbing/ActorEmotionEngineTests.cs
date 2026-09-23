using System;
using System.IO;
using System.Text.Json;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class ActorEmotionEngineTests
{
    [Fact]
    public void Archetypes_ContainsAllElevenArchetypes()
    {
        var archetypes = ActorEmotionEngine.AllArchetypes;
        Assert.Equal(11, archetypes.Count);

        var names = ActorEmotionEngine.ArchetypeNames;
        Assert.Contains("Hero", names);
        Assert.Contains("Villain", names);
        Assert.Contains("Elder", names);
        Assert.Contains("Action", names);
        Assert.Contains("Narrator", names);
        Assert.Contains("Romantic", names);
        Assert.Contains("Youth", names);
        Assert.Contains("Comic", names);
        Assert.Contains("AntiHero", names);
        Assert.Contains("Leader", names);
        Assert.Contains("Gentle", names);
    }

    [Theory]
    [InlineData("Hero", "Hero")]
    [InlineData("Villain", "Villain")]
    [InlineData("villain antagonist", "Villain")]
    [InlineData("Elder", "Elder")]
    [InlineData("Action", "Action")]
    [InlineData("Narrator", "Narrator")]
    [InlineData("Romantic", "Romantic")]
    [InlineData("Youth", "Youth")]
    [InlineData("Comic", "Comic")]
    [InlineData("AntiHero", "AntiHero")]
    [InlineData("Leader", "Leader")]
    [InlineData("Gentle", "Gentle")]
    [InlineData("NonExistentArchetype", "Hero")]
    [InlineData("", "Hero")]
    [InlineData(null, "Hero")]
    public void GetArchetype_ResolvesExpectedOrFallsBackToHero(string? query, string expected)
    {
        var arch = ActorEmotionEngine.GetArchetype(query);
        Assert.Equal(expected, arch.Name);
        Assert.False(string.IsNullOrWhiteSpace(arch.SampleLine));
    }

    [Fact]
    public void Emotions_ContainsAllNineteenEmotions()
    {
        var emotions = ActorEmotionEngine.AllEmotions;
        Assert.Equal(19, emotions.Count);

        var names = ActorEmotionEngine.EmotionNames;
        Assert.Contains(ActorEmotionEngine.EmotionNormal, names);
        Assert.Contains(ActorEmotionEngine.EmotionCrying, names);
        Assert.Contains(ActorEmotionEngine.EmotionLaughing, names);
        Assert.Contains(ActorEmotionEngine.EmotionSad, names);
        Assert.Contains(ActorEmotionEngine.EmotionHappy, names);
        Assert.Contains(ActorEmotionEngine.EmotionAngry, names);
        Assert.Contains(ActorEmotionEngine.EmotionWhisper, names);
        Assert.Contains(ActorEmotionEngine.EmotionFear, names);
        Assert.Contains(ActorEmotionEngine.EmotionVillain, names);
        Assert.Contains(ActorEmotionEngine.EmotionNarrator, names);
        Assert.Contains(ActorEmotionEngine.EmotionElder, names);
        Assert.Contains(ActorEmotionEngine.EmotionAction, names);
        Assert.Contains(ActorEmotionEngine.EmotionRomantic, names);
        Assert.Contains(ActorEmotionEngine.EmotionYouth, names);
        Assert.Contains(ActorEmotionEngine.EmotionSarcastic, names);
        Assert.Contains(ActorEmotionEngine.EmotionScream, names);
        Assert.Contains(ActorEmotionEngine.EmotionFrustrated, names);
        Assert.Contains(ActorEmotionEngine.EmotionStrong, names);
        Assert.Contains(ActorEmotionEngine.EmotionSoft, names);
    }

    [Theory]
    [InlineData("+15%", "+0%", "+15%")]
    [InlineData("+15%", "-10%", "+5%")]
    [InlineData("+10%", "+20%", "+30%")]
    [InlineData("-20%", "-30%", "-40%")] // Clamped at -40%
    [InlineData("+50%", "+30%", "+60%")] // Clamped at +60%
    public void ComputeEffectiveRate_CorrectlyCalculatesAndClamps(
        string baseRate,
        string emotionOffset,
        string expected
    )
    {
        var rate = ActorEmotionEngine.ComputeEffectiveRate(baseRate, emotionOffset);
        Assert.Equal(expected, rate);
    }

    [Theory]
    [InlineData(0, null, 0, "+0Hz")]
    [InlineData(2, null, 0, "+15Hz")] // 2 semitones * 7.5 = 15Hz
    [InlineData(-2, null, 0, "-15Hz")] // -2 semitones * 7.5 = -15Hz
    [InlineData(-3, "-20Hz", -3, "-65Hz")] // (-3 + -3)*7.5 - 20 = -45 - 20 = -65Hz
    [InlineData(4, "+32Hz", 4, "+85Hz")] // (4+4)*7.5 + 32 = 60 + 32 = 92 -> clamped to +85Hz
    [InlineData(0, "+30Hz", 3, "+52Hz")] // Scream: (0 + 3)*7.5 + 30 = 22.5 + 30 = 52.5 -> banker's rounding to +52Hz
    public void ComputeEffectiveTtsPitch_CalculatesHzCorrectly(
        int pitchShift,
        string? emotionPitch,
        int emotionOffset,
        string expected
    )
    {
        var pitch = ActorEmotionEngine.ComputeEffectiveTtsPitch(
            pitchShift,
            emotionPitch,
            emotionOffset
        );
        Assert.Equal(expected, pitch);
    }

    [Fact]
    public void BuildActorAcousticFilter_CombinesWarmthClarityAndEmotionDsp()
    {
        // Positive warmth & clarity with Crying vibrato
        var filter = ActorEmotionEngine.BuildActorAcousticFilter(
            ActorEmotionEngine.EmotionCrying,
            warmth: 0.5,
            clarity: 0.4
        );

        Assert.Contains("equalizer=f=180:t=q:w=1.2:g=2.5", filter);
        Assert.Contains("equalizer=f=3800:t=q:w=1.4:g=1.8", filter);
        Assert.Contains("vibrato=", filter);
        Assert.Contains("tremolo=", filter);
    }

    [Fact]
    public void BuildActorAcousticFilter_Scream_AppliesLimiterAndHighPresence()
    {
        var filter = ActorEmotionEngine.BuildActorAcousticFilter(
            ActorEmotionEngine.EmotionScream,
            warmth: 0.2,
            clarity: 0.5
        );

        Assert.Contains("equalizer=f=2800:t=q:w=1.2:g=5.5", filter);
        Assert.Contains("treble=g=4.0", filter);
        Assert.Contains("alimiter=limit=0.98", filter);
        Assert.Contains("volume=1.28", filter);
    }

    [Fact]
    public void BuildActorAcousticFilter_Frustrated_AppliesMidRangeThroatConstriction()
    {
        var filter = ActorEmotionEngine.BuildActorAcousticFilter(
            ActorEmotionEngine.EmotionFrustrated,
            warmth: 0.1,
            clarity: 0.2
        );

        Assert.Contains("equalizer=f=850:t=q:w=1.4:g=3.8", filter);
        Assert.Contains("equalizer=f=2200:t=q:w=1.2:g=2.8", filter);
        Assert.Contains("compand=", filter);
        Assert.Contains("volume=1.08", filter);
    }

    [Fact]
    public void BuildActorAcousticFilter_Strong_AppliesChestFundamentalAndPresenceBite()
    {
        var filter = ActorEmotionEngine.BuildActorAcousticFilter(
            ActorEmotionEngine.EmotionStrong,
            warmth: 0.3,
            clarity: 0.4
        );

        Assert.Contains("equalizer=f=180:t=q:w=1.0:g=3.8", filter);
        Assert.Contains("equalizer=f=2600:t=q:w=1.2:g=3.2", filter);
        Assert.Contains("compand=", filter);
        Assert.Contains("volume=1.20", filter);
    }

    [Fact]
    public void BuildActorAcousticFilter_Soft_AppliesLowpassAndGentleMidScoop()
    {
        var filter = ActorEmotionEngine.BuildActorAcousticFilter(
            ActorEmotionEngine.EmotionSoft,
            warmth: 0.2,
            clarity: -0.2
        );

        Assert.Contains("lowpass=f=6800", filter);
        Assert.Contains("equalizer=f=250:t=q:w=1.1:g=2.2", filter);
        Assert.Contains("equalizer=f=2400:t=q:w=1.3:g=-2.5", filter);
        Assert.Contains("volume=0.82", filter);
    }

    [Fact]
    public void BuildActorAcousticFilter_NeutralSettings_ReturnsEmpty()
    {
        var filter = ActorEmotionEngine.BuildActorAcousticFilter(
            ActorEmotionEngine.EmotionNormal,
            warmth: 0.0,
            clarity: 0.0
        );

        Assert.Empty(filter);
    }

    [Theory]
    // Scream detections
    [InlineData("Noooo! Get away from me! [scream]", "", ActorEmotionEngine.EmotionScream)]
    [InlineData("Stop it right now!!!", "", ActorEmotionEngine.EmotionScream)]
    [InlineData("", "ស្រែកខ្លាំងៗ ឈឺចាប់ខ្លាំងណាស់!", ActorEmotionEngine.EmotionScream)]
    [InlineData("She was shrieking in agony", "", ActorEmotionEngine.EmotionScream)]
    // Frustration detections
    [InlineData(
        "I'm so frustrated with this whole situation! [groan]",
        "",
        ActorEmotionEngine.EmotionFrustrated
    )]
    [InlineData("Damn it, I can't take it anymore", "", ActorEmotionEngine.EmotionFrustrated)]
    [InlineData("", "ខ្ញុំធុញថប់ណាស់ ទ្រាំមិនបានទេ!", ActorEmotionEngine.EmotionFrustrated)]
    [InlineData("", "មួម៉ៅខ្លាំងណាស់ ហត់ចិត្ត", ActorEmotionEngine.EmotionFrustrated)]
    // Strong & Powerful detections
    [InlineData("Follow my command, soldiers! [strong]", "", ActorEmotionEngine.EmotionStrong)]
    [InlineData("", "ស្តាប់បញ្ជាមេបញ្ជាការ! ត្រូវតែរឹងមាំ", ActorEmotionEngine.EmotionStrong)]
    // Soft & Gentle detections
    [InlineData("Sleep peacefully, my gentle child [soft]", "", ActorEmotionEngine.EmotionSoft)]
    [InlineData("", "សម្រាកចុះ និយាយស្រទន់ទន់ភ្លន់", ActorEmotionEngine.EmotionSoft)]
    // Other emotions
    [InlineData("I can't stop crying, my heart is broken", "", ActorEmotionEngine.EmotionCrying)]
    [InlineData("", "ខ្ញុំយំមិនចេញទេ ស្រក់ទឹកភ្នែក", ActorEmotionEngine.EmotionCrying)]
    [InlineData("That was hilarious! haha [laugh]", "", ActorEmotionEngine.EmotionLaughing)]
    [InlineData("", "កំប្លែងណាស់ ហាហា សើចសប្បាយ", ActorEmotionEngine.EmotionLaughing)]
    [InlineData("Get out of my way right now!!", "", ActorEmotionEngine.EmotionAngry)]
    [InlineData("What are you doing?!", "", ActorEmotionEngine.EmotionAngry)]
    [InlineData("GET OUT OF HERE NOW!", "", ActorEmotionEngine.EmotionAngry)]
    [InlineData("", "ឈប់ភ្លាម! ខ្ញុំខឹងខ្លាំងណាស់!", ActorEmotionEngine.EmotionAngry)]
    [InlineData("Psst, keep your voice down, whisper", "", ActorEmotionEngine.EmotionWhisper)]
    [InlineData(
        "[whispering quietly] Don't let them hear you",
        "",
        ActorEmotionEngine.EmotionWhisper
    )]
    [InlineData("", "ខ្សឹបតិចៗ កុំឱ្យគេឮ", ActorEmotionEngine.EmotionWhisper)]
    [InlineData("There's a monster outside, I'm terrified!", "", ActorEmotionEngine.EmotionFear)]
    [InlineData("(gasps in terror) What is that creature?!", "", ActorEmotionEngine.EmotionFear)]
    [InlineData("", "ខ្លាចណាស់ ជួយផង!", ActorEmotionEngine.EmotionFear)]
    [InlineData("Open fire! Take cover and move move!", "", ActorEmotionEngine.EmotionAction)]
    [InlineData("", "ប្រយុទ្ធ! បាញ់! គេចចេញ!", ActorEmotionEngine.EmotionAction)]
    [InlineData("Oh really? What a surprise, genius.", "", ActorEmotionEngine.EmotionSarcastic)]
    [InlineData("", "ពូកែណាស់តើ សមមុខហើយ!", ActorEmotionEngine.EmotionSarcastic)]
    [InlineData("Mommy, I want to play with my toy!", "", ActorEmotionEngine.EmotionYouth)]
    [InlineData("", "ម៉ាក់ កូនចង់ញ៉ាំនំ", ActorEmotionEngine.EmotionYouth)]
    [InlineData(
        "Listen closely, my child, patience is a virtue.",
        "",
        ActorEmotionEngine.EmotionElder
    )]
    [InlineData("", "កូនអើយ ចាំសម្តីតា", ActorEmotionEngine.EmotionElder)]
    [InlineData(
        "Once upon a time, in a world long forgotten...",
        "",
        ActorEmotionEngine.EmotionNarrator
    )]
    [InlineData("", "កាលពីរាប់ពាន់ឆ្នាំមុន នៅក្នុងពិភពលោក...", ActorEmotionEngine.EmotionNarrator)]
    [InlineData("NOOO! AAARGH!!!", "", ActorEmotionEngine.EmotionScream)]
    [InlineData("(screams in agony) Stop it please!", "", ActorEmotionEngine.EmotionScream)]
    [InlineData(
        "Ugh, damn it, I can't take this anymore!",
        "",
        ActorEmotionEngine.EmotionFrustrated
    )]
    [InlineData("", "មួម៉ៅ ធុញណាស់ អស់សង្ឃឹម", ActorEmotionEngine.EmotionFrustrated)]
    [InlineData(
        "Kneel before me, you pathetic mortal! Destroy them all!",
        "",
        ActorEmotionEngine.EmotionVillain
    )]
    [InlineData("", "កម្ទេចពួកវា លុតជង្គង់ចុះ!", ActorEmotionEngine.EmotionVillain)]
    [InlineData("My love, I want to be with you forever.", "", ActorEmotionEngine.EmotionRomantic)]
    [InlineData("", "បងស្រឡាញ់អូន បងសម្លាញ់", ActorEmotionEngine.EmotionRomantic)]
    [InlineData("By my command, stand firm soldiers!", "", ActorEmotionEngine.EmotionStrong)]
    [InlineData("Shh, it's okay, don't worry, rest easy.", "", ActorEmotionEngine.EmotionSoft)]
    public void DetectEmotion_IdentifiesEnglishAndKhmerCues(string en, string km, string expected)
    {
        var emotion = ActorEmotionEngine.DetectEmotion(en, km);
        Assert.Equal(expected, emotion);
    }

    [Fact]
    public void MovieCharacter_ApplyToneArchetype_UpdatesAllVocalParameters()
    {
        var character = new MovieCharacter { Name = "Main Villain" };

        // Apply Villain
        character.ToneArchetype = "Villain";

        Assert.Equal("Villain", character.ToneArchetype);
        Assert.Equal(-3, character.PitchShift);
        Assert.Equal("+5%", character.BaseSpeechRate);
        Assert.True(character.ToneWarmth >= 0.75); // High chest warmth
        Assert.True(character.ToneClarity <= 0.0); // Dark, brooding treble
        Assert.Equal(ActorEmotionEngine.EmotionVillain, character.EmotionPreset);

        // Apply Youth
        character.ToneArchetype = "Youth";

        Assert.Equal("Youth", character.ToneArchetype);
        Assert.Equal(+4, character.PitchShift);
        Assert.Equal("+20%", character.BaseSpeechRate);
        Assert.True(character.ToneWarmth < 0.0); // Light chest body
        Assert.True(character.ToneClarity >= 0.5); // Bright airy consonant presence
        Assert.Equal(ActorEmotionEngine.EmotionYouth, character.EmotionPreset);

        // Apply AntiHero
        character.ToneArchetype = "AntiHero";

        Assert.Equal("AntiHero", character.ToneArchetype);
        Assert.Equal(-1, character.PitchShift);
        Assert.Equal("+14%", character.BaseSpeechRate);
        Assert.True(character.ToneWarmth >= 0.30);
        Assert.True(character.ToneClarity >= 0.40);
        Assert.Equal(ActorEmotionEngine.EmotionFrustrated, character.EmotionPreset);
    }

    [Fact]
    public void DubbingProject_SavesAndLoadsToneProperties()
    {
        var testId = Guid.NewGuid();
        var character = new MovieCharacter
        {
            Id = testId,
            Name = "General",
            ToneArchetype = "Action",
            ToneWarmth = 0.45,
            ToneClarity = 0.55,
            PitchShift = 1,
        };

        var charData = new MovieCharacterData
        {
            Id = character.Id,
            Name = character.Name,
            ToneArchetype = character.ToneArchetype,
            ToneWarmth = character.ToneWarmth,
            ToneClarity = character.ToneClarity,
            PitchShift = character.PitchShift,
        };

        var json = JsonSerializer.Serialize(charData);
        var loaded = JsonSerializer.Deserialize<MovieCharacterData>(json);

        Assert.NotNull(loaded);
        Assert.Equal("Action", loaded.ToneArchetype);
        Assert.Equal(0.45, loaded.ToneWarmth, 2);
        Assert.Equal(0.55, loaded.ToneClarity, 2);
        Assert.Equal(1, loaded.PitchShift);
    }

    [Fact]
    public async Task ApplyActorAcousticFilterAsync_NonExistentFile_ReturnsFalseSafely()
    {
        var result = await ActorEmotionEngine.ApplyActorAcousticFilterAsync(
            "ffmpeg",
            "non_existent_audio_file.wav",
            "non_existent_output.wav",
            ActorEmotionEngine.EmotionAngry
        );

        Assert.False(result);
    }

    [Fact]
    public async Task ApplyActorAcousticFilterAsync_WhenCancelled_ThrowsOrReturnsPromptly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancellation should be respected immediately without hanging
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await ActorEmotionEngine.ApplyActorAcousticFilterAsync(
                "ffmpeg",
                "dummy.wav",
                "dummy_out.wav",
                ActorEmotionEngine.EmotionNormal,
                cancellationToken: cts.Token
            );
        });
    }
}
