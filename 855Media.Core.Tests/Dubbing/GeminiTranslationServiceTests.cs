using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class GeminiTranslationServiceTests
{
    private readonly GeminiTranslationService _service = new();

    [Fact]
    public void ParseGeminiJsonArray_StandardArray_ParsesAndPolishesKhmer()
    {
        // Arrange
        var json = """
            [
                { "index": 1, "khmer": "តើឯងកំពុងធ្វើអ្វី?" },
                { "index": 2, "khmer": "បិទមាត់របស់អ្នក!" }
            ]
            """;
        var results = new Dictionary<int, string>();

        // Act
        GeminiTranslationService.ParseGeminiJsonArray(json, results);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results.ContainsKey(1));
        Assert.True(results.ContainsKey(2));
        Assert.Contains("ធ្វើអី", results[1]); // Polished spoken Khmer
        Assert.Equal("បិទមាត់ទៅ!", results[2]); // Natural cinema dialogue
    }

    [Fact]
    public void ParseGeminiJsonArray_NestedTranslationsObject_ParsesCorrectly()
    {
        // Arrange
        var json = """
            {
                "translations": [
                    { "index": 5, "khmer": "ជម្រាបសួរលោកម្ចាស់" },
                    { "index": 6, "khmer": "ព្រះករុណាថ្លៃវិសេស" }
                ]
            }
            """;
        var results = new Dictionary<int, string>();

        // Act
        GeminiTranslationService.ParseGeminiJsonArray(json, results);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("ជម្រាបសួរលោកម្ចាស់", results[5]);
        Assert.Equal("ព្រះករុណាថ្លៃវិសេស", results[6]);
    }

    [Fact]
    public void ParseGeminiJsonArray_WithMarkdownFences_ParsesSuccessfully()
    {
        // Arrange
        var json = """
            ```json
            [
                { "index": 10, "khmer": "ស្អីគេហ្នឹង?" }
            ]
            ```
            """;
        var cleanJson = json.Trim();
        if (cleanJson.StartsWith("```json"))
            cleanJson = cleanJson[7..];
        if (cleanJson.EndsWith("```"))
            cleanJson = cleanJson[..^3];
        cleanJson = cleanJson.Trim();

        var results = new Dictionary<int, string>();

        // Act
        GeminiTranslationService.ParseGeminiJsonArray(cleanJson, results);

        // Assert
        Assert.Single(results);
        Assert.Equal("ស្អីគេហ្នឹង?", results[10]);
    }

    [Fact]
    public async Task TranslateLineAsync_EmptyApiKey_ReturnsNullImmediately()
    {
        var result = await _service.TranslateLineAsync(
            string.Empty,
            "Hello world",
            cancellationToken: CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public void ParseGeminiEmotionsJson_StandardArray_ParsesValidEmotions()
    {
        var json = """
            [
                { "index": 1, "emotion": "Angry" },
                { "index": 2, "emotion": "crying" },
                { "index": 3, "emotion": "Action" },
                { "index": 4, "emotion": "InvalidEmotionName" }
            ]
            """;
        var results = new Dictionary<int, string>();

        GeminiTranslationService.ParseGeminiEmotionsJson(json, results);

        Assert.Equal(3, results.Count);
        Assert.Equal(ActorEmotionEngine.EmotionAngry, results[1]);
        Assert.Equal(ActorEmotionEngine.EmotionCrying, results[2]);
        Assert.Equal(ActorEmotionEngine.EmotionAction, results[3]);
        Assert.False(results.ContainsKey(4));
    }

    [Fact]
    public void ParseGeminiEmotionsJson_NestedEmotionsObject_ParsesCorrectly()
    {
        var json = """
            {
                "emotions": [
                    { "index": 10, "emotion": "Villain" },
                    { "index": 11, "tone": "Whisper" }
                ]
            }
            """;
        var results = new Dictionary<int, string>();

        GeminiTranslationService.ParseGeminiEmotionsJson(json, results);

        Assert.Equal(2, results.Count);
        Assert.Equal(ActorEmotionEngine.EmotionVillain, results[10]);
        Assert.Equal(ActorEmotionEngine.EmotionWhisper, results[11]);
    }

    [Fact]
    public void ParseGeminiDiarizationJson_StandardArray_ParsesCastAndLineAssignments()
    {
        var json = """
            [
                { "index": 1, "speaker": "Hero (Knight)", "gender": "Male", "toneArchetype": "Hero", "emotion": "Normal" },
                { "index": 2, "speaker": "Princess Elena", "gender": "Female", "toneArchetype": "Romantic", "emotion": "Fear" },
                { "index": 3, "speaker": "Little Boy", "gender": "Child", "toneArchetype": "Youth", "emotion": "Crying" },
                { "index": 4, "speaker": "Dark Wizard", "gender": "Male", "toneArchetype": "Villain", "emotion": "Villain" }
            ]
            """;
        var results = new List<GeminiDiarizationItem>();

        GeminiTranslationService.ParseGeminiDiarizationJson(json, results);

        Assert.Equal(4, results.Count);
        Assert.Equal("Hero (Knight)", results[0].SpeakerName);
        Assert.Equal("Male", results[0].Gender);
        Assert.Equal("Hero", results[0].ToneArchetype);

        Assert.Equal("Princess Elena", results[1].SpeakerName);
        Assert.Equal("Female", results[1].Gender);
        Assert.Equal("Romantic", results[1].ToneArchetype);
        Assert.Equal("Fear", results[1].Emotion);

        Assert.Equal("Little Boy", results[2].SpeakerName);
        Assert.Equal("Child", results[2].Gender);
        Assert.Equal("Youth", results[2].ToneArchetype);
        Assert.Equal("Crying", results[2].Emotion);

        Assert.Equal("Dark Wizard", results[3].SpeakerName);
        Assert.Equal("Villain", results[3].ToneArchetype);
    }
}
