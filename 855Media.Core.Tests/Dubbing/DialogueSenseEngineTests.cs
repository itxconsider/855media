using System;
using System.Collections.Generic;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class DialogueSenseEngineTests
{
    [Fact]
    public void ImproveOriginalDialogue_CleansNoiseTags_AndHallucinatedRepeats()
    {
        // Arrange
        var input =
            "[Music] uh, you know you know you know that that that we should go to the cinema , right ? [Applause]";

        // Act
        var result = DialogueSenseEngine.ImproveOriginalDialogue(input);

        // Assert
        Assert.DoesNotContain("[Music]", result);
        Assert.DoesNotContain("[Applause]", result);
        Assert.DoesNotContain("you know you know you know", result);
        Assert.DoesNotContain("that that that", result);
        Assert.StartsWith("You know", result);
        Assert.Contains("we should go to the cinema, right?", result);
    }

    [Fact]
    public void ImproveOriginalDialogue_CapitalizesSentenceAndPronounI()
    {
        // Arrange
        var input = "when i told him that i'm ready, he was happy";

        // Act
        var result = DialogueSenseEngine.ImproveOriginalDialogue(input);

        // Assert
        Assert.StartsWith("When", result);
        Assert.Contains(" I ", result);
        Assert.Contains(" I'm ", result);
        Assert.EndsWith(".", result);
    }

    [Fact]
    public void ImproveOriginalDialogue_RemovesSubtitleFormattingTags()
    {
        // Arrange
        var input = "{\\an8}<i>>> Hello, everyone!</i>";

        // Act
        var result = DialogueSenseEngine.ImproveOriginalDialogue(input);

        // Assert
        Assert.Equal("Hello, everyone!", result);
    }

    [Theory]
    [InlineData("This is a complete sentence.", true)]
    [InlineData("Is this a question?", true)]
    [InlineData("Look out!", true)]
    [InlineData("She said, \"I will come!\"", true)]
    [InlineData("She said, \"I will come.\"", true)]
    [InlineData("これはテストです。", true)] // CJK
    [InlineData("នេះជាការសាកល្បង។", true)] // Khmer
    [InlineData("Because I was tired", false)]
    [InlineData("Hello Mr.", false)] // Abbreviation
    [InlineData("Wait for Dr.", false)] // Abbreviation
    public void IsCompleteSentence_EvaluatesCorrectly(string input, bool expected)
    {
        var actual = DialogueSenseEngine.IsCompleteSentence(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FindSenseSplitPoint_SplitsAtClauseBoundary()
    {
        // Arrange
        var input =
            "The warrior reached the summit, because the dragon was sleeping inside the cavern.";

        // Act
        int splitIdx = DialogueSenseEngine.FindSenseSplitPoint(input);

        // Assert
        Assert.True(splitIdx > 0);
        var part1 = input[..splitIdx].Trim();
        var part2 = input[splitIdx..].Trim();

        // Must split around comma or "because"
        Assert.Contains("warrior reached the summit", part1);
        Assert.Contains("dragon was sleeping", part2);
    }

    [Fact]
    public void SplitOriginalDialogueSenseToSense_ProducesTwoValidClauses()
    {
        // Arrange
        var input = "We waited at the station for hours, but the last train never arrived.";

        // Act
        var (part1, part2) = DialogueSenseEngine.SplitOriginalDialogueSenseToSense(input);

        // Assert
        Assert.Equal("We waited at the station for hours,", part1);
        Assert.Equal("But the last train never arrived.", part2);
    }

    [Fact]
    public void SplitSegmentSenseToSense_SplitsProportionally_AndResetsAudioCache()
    {
        // Arrange
        var charId = Guid.NewGuid();
        var seg = new SubtitleSegment
        {
            Index = 1,
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(20),
            OriginalText =
                "The king entered the grand hall, and all the guards bowed down immediately.",
            KhmerText = "ស្តេចបានយាងចូលទៅក្នុងសាលធំ ហើយកងការពារទាំងអស់បានក្រាបថ្វាយបង្គំភ្លាមៗ។",
            CharacterId = charId,
            SpeakerName = "Narrator",
            SpeakerColor = "#3B82F6",
            Emotion = "Dramatic",
            AudioClipPath = "C:\\cached\\audio.wav",
            AudioDurationSeconds = 9.8,
        };

        // Act
        var (first, second) = DialogueSenseEngine.SplitSegmentSenseToSense(seg);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), first.StartTime);
        Assert.True(
            first.EndTime > TimeSpan.FromSeconds(10) && first.EndTime < TimeSpan.FromSeconds(20)
        );
        Assert.Equal(first.EndTime, second.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(20), second.EndTime);

        // Audio clips must be reset so stale full audio is not played
        Assert.Null(first.AudioClipPath);
        Assert.Equal(0, first.AudioDurationSeconds);
        Assert.Null(second.AudioClipPath);
        Assert.Equal(0, second.AudioDurationSeconds);

        // Metadata preserved
        Assert.Equal(charId, second.CharacterId);
        Assert.Equal("Narrator", second.SpeakerName);
        Assert.Equal("Dramatic", second.Emotion);

        // Sense matching
        Assert.Contains("The king entered the grand hall", first.OriginalText);
        Assert.Contains("bowed down immediately", second.OriginalText);
        Assert.Contains("ស្តេចបានយាងចូល", first.KhmerText);
    }

    [Fact]
    public void MergeSegmentsSenseToSense_CombinesText_AndPunctuationSmoothly()
    {
        // Arrange
        var seg1 = new SubtitleSegment
        {
            Index = 1,
            StartTime = TimeSpan.FromSeconds(2),
            EndTime = TimeSpan.FromSeconds(4),
            OriginalText = "When the sun rises,",
            KhmerText = "នៅពេលព្រះអាទិត្យរះ",
        };
        var seg2 = new SubtitleSegment
        {
            Index = 2,
            StartTime = TimeSpan.FromSeconds(4.2),
            EndTime = TimeSpan.FromSeconds(7),
            OriginalText = "we will start our journey.",
            KhmerText = "យើងនឹងចាប់ផ្តើមដំណើររបស់យើង។",
        };

        // Act
        var merged = DialogueSenseEngine.MergeSegmentsSenseToSense(seg1, seg2);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(2), merged.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(7), merged.EndTime);
        Assert.Equal("When the sun rises, we will start our journey.", merged.OriginalText);
        Assert.Contains("នៅពេលព្រះអាទិត្យរះ", merged.KhmerText);
        Assert.Contains("យើងនឹងចាប់ផ្តើមដំណើររបស់យើង", merged.KhmerText);
        Assert.Null(merged.AudioClipPath);
    }

    [Fact]
    public void ReconstructSentences_MergesSentenceFragments_FromSameSpeaker()
    {
        // Arrange
        var speakerId = Guid.NewGuid();
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(2.5),
                OriginalText = "Because I was very tired,",
                CharacterId = speakerId,
                SpeakerName = "Hero",
            },
            new()
            {
                Index = 2,
                StartTime = TimeSpan.FromSeconds(2.8),
                EndTime = TimeSpan.FromSeconds(4.5),
                OriginalText = "I decided to stay at home.",
                CharacterId = speakerId,
                SpeakerName = "Hero",
            },
            new()
            {
                Index = 3,
                StartTime = TimeSpan.FromSeconds(6.0),
                EndTime = TimeSpan.FromSeconds(8.0),
                OriginalText = "Are you coming with us?",
                CharacterId = Guid.NewGuid(), // Different speaker
                SpeakerName = "Friend",
            },
        };

        // Act
        var reconstructed = DialogueSenseEngine.ReconstructSentences(segments, maxGapSeconds: 2.0);

        // Assert
        Assert.Equal(2, reconstructed.Count);
        Assert.Equal(1, reconstructed[0].Index);
        Assert.Equal(2, reconstructed[1].Index);

        // First two segments merged into one complete sentence
        Assert.Equal(TimeSpan.FromSeconds(1), reconstructed[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(4.5), reconstructed[0].EndTime);
        Assert.Equal(
            "Because I was very tired, I decided to stay at home.",
            reconstructed[0].OriginalText
        );

        // Third segment kept separate because speaker is different and previous sentence was complete
        Assert.Equal("Friend", reconstructed[1].SpeakerName);
        Assert.Equal("Are you coming with us?", reconstructed[1].OriginalText);
    }

    [Fact]
    public void ImproveOriginalDialogue_CleansChineseNoise_AndDeduplicatesStuttersAndSpaces()
    {
        // Arrange: Chinese drama line with noise tags, spoken fillers, looped stutters, and stray spaces
        var input = "【音乐】那个... 谢谢谢谢谢谢 ， 我们 走 吧 ， 快点 迟到 了 ！ [掌声]";

        // Act
        var result = DialogueSenseEngine.ImproveOriginalDialogue(input);

        // Assert
        Assert.DoesNotContain("【音乐】", result);
        Assert.DoesNotContain("[掌声]", result);
        Assert.DoesNotContain("那个", result);
        Assert.DoesNotContain("谢谢谢谢谢谢", result);
        Assert.Contains("谢谢", result);
        Assert.DoesNotContain("我们 走 吧", result);
        Assert.Contains("我们走吧", result);
        Assert.Contains("快点迟到了！", result);
    }

    [Fact]
    public void FindSenseSplitPoint_SplitsChineseAtConjunction()
    {
        // Arrange: Chinese sentence with conjunction "但是" (but)
        var input = "虽然敌人已经包围了城堡但是将军依然冷静地指挥战斗。";

        // Act
        int splitIdx = DialogueSenseEngine.FindSenseSplitPoint(input);

        // Assert: split point must split right at or before "但是"
        Assert.True(splitIdx > 0);
        var part1 = input[..splitIdx];
        var part2 = input[splitIdx..];
        Assert.Contains("虽然敌人已经包围了城堡", part1);
        Assert.StartsWith("但是", part2);
    }

    [Fact]
    public void SplitOriginalDialogueSenseToSense_SplitsChineseSentenceCleanly()
    {
        // Arrange
        var input = "虽然敌人已经包围了城堡，但是将军依然冷静指挥战斗。";

        // Act
        var (part1, part2) = DialogueSenseEngine.SplitOriginalDialogueSenseToSense(input);

        // Assert
        Assert.Equal("虽然敌人已经包围了城堡，", part1);
        Assert.Equal("但是将军依然冷静指挥战斗。", part2);
    }

    [Fact]
    public void MergeSegmentsSenseToSense_JoinsChineseWithoutSpaces()
    {
        // Arrange
        var seg1 = new SubtitleSegment
        {
            Index = 1,
            StartTime = TimeSpan.FromSeconds(1),
            EndTime = TimeSpan.FromSeconds(3),
            OriginalText = "王爷请留步。",
            KhmerText = "លោកម្ចាស់សូមឈប់សិន។",
        };
        var seg2 = new SubtitleSegment
        {
            Index = 2,
            StartTime = TimeSpan.FromSeconds(3),
            EndTime = TimeSpan.FromSeconds(5.5),
            OriginalText = "本王有要事在身。",
            KhmerText = "ខ្ញុំមានការសំខាន់ត្រូវទៅធ្វើ។",
        };

        // Act
        var merged = DialogueSenseEngine.MergeSegmentsSenseToSense(seg1, seg2);

        // Assert: Chinese lines joined directly with comma, no spaces between Chinese characters
        Assert.Equal("王爷请留步，本王有要事在身。", merged.OriginalText);
        Assert.DoesNotContain(" ", merged.OriginalText);
        Assert.Contains("លោកម្ចាស់", merged.KhmerText);
    }
}
