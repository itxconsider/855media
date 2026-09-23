using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Dubbing;

/// <summary>
/// High-performance AI translation engine powered by Google Gemini (Gemini 2.0 Flash / 1.5 Flash).
/// Specifically tuned for Cambodian cinema dubbing, character pronoun consistency,
/// and natural spoken Khmer phrasing.
/// </summary>
public class GeminiTranslationService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(45) };

    public const string DefaultModel = "gemini-2.0-flash";

    /// <summary>
    /// Translates a single line of movie dialogue with character context using Gemini.
    /// </summary>
    public async Task<string?> TranslateLineAsync(
        string apiKey,
        string text,
        string sourceLang = "auto",
        string? speakerName = null,
        string? characterGender = null,
        string model = DefaultModel,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(text))
            return null;

        string cleanApiKey = apiKey.Trim();
        string cleanModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(
            "You are an expert Cambodian cinema dubbing director and dialogue translator."
        );
        promptBuilder.AppendLine(
            "Translate the following dialogue line into natural, spoken, idiomatic Khmer (ភាសាខ្មែរ) for movie dubbing."
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Rules:");
        promptBuilder.AppendLine(
            "1. Use natural Cambodian movie speech (ភាសាកុន/ភាពយន្ត), avoiding textbook or literal machine translations."
        );
        promptBuilder.AppendLine(
            "2. Keep the phrasing concise to match the original actor's visual mouth timing."
        );

        if (!string.IsNullOrWhiteSpace(speakerName) || !string.IsNullOrWhiteSpace(characterGender))
        {
            promptBuilder.AppendLine(
                $"Speaker Context: {speakerName ?? "Unknown"} ({characterGender ?? "Neutral"})"
            );
        }

        promptBuilder.AppendLine(
            $"Source Language: {(string.IsNullOrWhiteSpace(sourceLang) || sourceLang.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "Auto-detect" : sourceLang)}"
        );
        promptBuilder.AppendLine($"Original Dialogue: \"{text.Trim()}\"");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(
            "Return ONLY the translated Khmer dialogue line. Do NOT include quotes, explanations, or markdown."
        );

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = promptBuilder.ToString() } } } },
            generationConfig = new { temperature = 0.3, maxOutputTokens = 300 },
        };

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={cleanApiKey}";
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await HttpClient.PostAsync(url, jsonContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Gemini API Error ({(int)response.StatusCode}): {err}",
                null,
                response.StatusCode
            );
        }

        var resJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(resJson);

        if (
            doc.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.GetArrayLength() > 0
        )
        {
            var firstCandidate = candidates[0];
            if (
                firstCandidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.GetArrayLength() > 0
            )
            {
                var translated = parts[0].GetProperty("text").GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    // Clean any markdown quotes or code fences if present
                    translated = StripMarkdownFences(translated);
                    return SubtitleTranslationService.PolishKhmerDialogue(translated);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Translates a batch of subtitle segments with full scene context using Gemini.
    /// Returns a dictionary mapping Segment Index -> Polished Khmer dialogue.
    /// </summary>
    public async Task<Dictionary<int, string>> TranslateBatchAsync(
        string apiKey,
        IReadOnlyList<SubtitleSegment> segments,
        string sourceLang = "auto",
        string model = DefaultModel,
        CancellationToken cancellationToken = default
    )
    {
        var results = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(apiKey) || segments.Count == 0)
            return results;

        string cleanApiKey = apiKey.Trim();
        string cleanModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        var inputItems = segments
            .Select(s => new
            {
                index = s.Index,
                speaker = string.IsNullOrWhiteSpace(s.SpeakerName) ? "Speaker" : s.SpeakerName,
                gender = string.IsNullOrWhiteSpace(s.DetectedGender) ? "Neutral" : s.DetectedGender,
                original = s.OriginalText,
            })
            .ToList();

        string inputJson = JsonSerializer.Serialize(inputItems);

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(
            "You are a master Cambodian cinema dubbing director and professional movie translator."
        );
        promptBuilder.AppendLine(
            "Translate the following movie subtitle scenes into natural, spoken, idiomatic Khmer (ភាសាខ្មែរ)."
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Movie Dubbing Rules:");
        promptBuilder.AppendLine(
            "1. Natural Cinema Phrasing: Translate for spoken cinema (ភាសាកុន/ភាពយន្ត), avoiding textbook or literal machine translation."
        );
        promptBuilder.AppendLine(
            "2. Character & Relationship Consistency: Use appropriate respectful or intimate pronouns based on character names and conversational context:"
        );
        promptBuilder.AppendLine(
            "   - Couples/lovers: address each other as 'បង' (older/male) and 'អូន' (younger/female)."
        );
        promptBuilder.AppendLine(
            "   - Historical/Royal/Court dramas: use 'លោកម្ចាស់' (lord/master), 'ព្រះករុណា' (your majesty), 'ទ្រង់' (highness), 'លោកគ្រូ' (master/teacher)."
        );
        promptBuilder.AppendLine(
            "   - Modern/Casual: use lively spoken Cambodian particles (e.g. 'ហ្នឹងហើយ', 'ទៅណាដែរ?', 'ស្អីគេ', 'បិទមាត់ទៅ!')."
        );
        promptBuilder.AppendLine(
            "3. Speech Pacing: Keep lines concise so Khmer speech fits the actor's visual mouth timing and screen duration."
        );
        promptBuilder.AppendLine(
            "4. Output format: Return a JSON array of objects with 'index' and 'khmer' fields ONLY."
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Scenes to translate:");
        promptBuilder.AppendLine(inputJson);

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = promptBuilder.ToString() } } } },
            generationConfig = new
            {
                temperature = 0.3,
                maxOutputTokens = 8192,
                responseMimeType = "application/json",
            },
        };

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={cleanApiKey}";
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await HttpClient.PostAsync(url, jsonContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Gemini API Error ({(int)response.StatusCode}): {err}",
                null,
                response.StatusCode
            );
        }

        var resJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(resJson);

        if (
            doc.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.GetArrayLength() > 0
        )
        {
            var firstCandidate = candidates[0];
            if (
                firstCandidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.GetArrayLength() > 0
            )
            {
                var text = parts[0].GetProperty("text").GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    text = StripMarkdownFences(text);
                    ParseGeminiJsonArray(text, results);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Parses Gemini's JSON array of translated dialogue objects.
    /// </summary>
    public static void ParseGeminiJsonArray(string jsonText, Dictionary<int, string> results)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            JsonElement arrayElem;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                arrayElem = doc.RootElement;
            }
            else if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("translations", out var tProp)
                && tProp.ValueKind == JsonValueKind.Array
            )
            {
                arrayElem = tProp;
            }
            else
            {
                return;
            }

            foreach (var item in arrayElem.EnumerateArray())
            {
                int index = 0;
                if (
                    item.TryGetProperty("index", out var idxProp)
                    || item.TryGetProperty("id", out idxProp)
                )
                {
                    if (idxProp.ValueKind == JsonValueKind.Number)
                    {
                        index = idxProp.GetInt32();
                    }
                    else if (
                        idxProp.ValueKind == JsonValueKind.String
                        && int.TryParse(idxProp.GetString(), out int parsedIdx)
                    )
                    {
                        index = parsedIdx;
                    }
                }

                string? khmer = null;
                if (
                    item.TryGetProperty("khmer", out var kProp)
                    || item.TryGetProperty("translation", out kProp)
                    || item.TryGetProperty("text", out kProp)
                )
                {
                    khmer = kProp.GetString();
                }

                if (index > 0 && !string.IsNullOrWhiteSpace(khmer))
                {
                    // Polish formatting, punctuation, and Khmer cinematic particles
                    results[index] = SubtitleTranslationService.PolishKhmerDialogue(khmer);
                }
            }
        }
        catch
        {
            // Fallback regex parser in case JSON has minor trailing syntax anomalies
            var regex = new Regex(
                @"[""'](?:index|id)[""']\s*:\s*(\d+)\s*,\s*[""'](?:khmer|translation|text)[""']\s*:\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase
            );

            foreach (Match m in regex.Matches(jsonText))
            {
                if (int.TryParse(m.Groups[1].Value, out int idx))
                {
                    var val = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        results[idx] = SubtitleTranslationService.PolishKhmerDialogue(val);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Classifies acting emotion and dramatic tone for a batch of subtitle segments using Gemini AI.
    /// Returns a dictionary mapping Segment Index -> Detected Emotion name (matching ActorEmotionEngine constants).
    /// </summary>
    public async Task<Dictionary<int, string>> DetectEmotionsBatchAsync(
        string apiKey,
        IReadOnlyList<SubtitleSegment> segments,
        string model = DefaultModel,
        CancellationToken cancellationToken = default
    )
    {
        var results = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(apiKey) || segments.Count == 0)
            return results;

        string cleanApiKey = apiKey.Trim();
        string cleanModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        var inputItems = segments
            .Select(s => new
            {
                index = s.Index,
                speaker = string.IsNullOrWhiteSpace(s.SpeakerName) ? "Speaker" : s.SpeakerName,
                original = s.OriginalText,
                khmer = s.KhmerText,
            })
            .ToList();

        string inputJson = JsonSerializer.Serialize(inputItems);

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(
            "You are a professional cinema dubbing director and voice actor coach."
        );
        promptBuilder.AppendLine(
            "Analyze the dramatic tone and emotional performance required for each dialogue scene below."
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Available Emotion Tones (ONLY choose from this exact list):");
        promptBuilder.AppendLine(
            "Normal, Crying, Laughing, Sad, Happy, Angry, Whisper, Fear, Villain, Narrator, Elder, Action, Romantic, Youth, Sarcastic, Scream, Frustrated, Strong, Soft"
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Rules:");
        promptBuilder.AppendLine(
            "1. Consider conversational subtext, character hostility, grief, romantic attraction, battle urgency, or comedy."
        );
        promptBuilder.AppendLine(
            "2. Output ONLY a valid JSON array of objects with 'index' and 'emotion' properties."
        );
        promptBuilder.AppendLine(
            "Example: [{\"index\": 1, \"emotion\": \"Angry\"}, {\"index\": 2, \"emotion\": \"Sad\"}]"
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Dialogue Scenes to Analyze:");
        promptBuilder.AppendLine(inputJson);

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={cleanApiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = promptBuilder.ToString() } } },
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 8192,
                responseMimeType = "application/json",
            },
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await HttpClient.PostAsync(url, jsonContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return results;

        var resJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(resJson);

        if (
            doc.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.GetArrayLength() > 0
        )
        {
            var firstCandidate = candidates[0];
            if (
                firstCandidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.GetArrayLength() > 0
            )
            {
                var text = parts[0].GetProperty("text").GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    text = StripMarkdownFences(text);
                    ParseGeminiEmotionsJson(text, results);
                }
            }
        }

        return results;
    }

    public static void ParseGeminiEmotionsJson(string jsonText, Dictionary<int, string> results)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            JsonElement arrayElem;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                arrayElem = doc.RootElement;
            }
            else if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("emotions", out var eProp)
                && eProp.ValueKind == JsonValueKind.Array
            )
            {
                arrayElem = eProp;
            }
            else
            {
                return;
            }

            var validEmotions = new HashSet<string>(
                ActorEmotionEngine.EmotionNames,
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var item in arrayElem.EnumerateArray())
            {
                int index = 0;
                if (
                    item.TryGetProperty("index", out var idxProp)
                    || item.TryGetProperty("id", out idxProp)
                )
                {
                    if (idxProp.ValueKind == JsonValueKind.Number)
                        index = idxProp.GetInt32();
                    else if (
                        idxProp.ValueKind == JsonValueKind.String
                        && int.TryParse(idxProp.GetString(), out int parsedIdx)
                    )
                        index = parsedIdx;
                }

                string? emotion = null;
                if (
                    item.TryGetProperty("emotion", out var emProp)
                    || item.TryGetProperty("tone", out emProp)
                )
                {
                    emotion = emProp.GetString()?.Trim();
                }

                if (index > 0 && !string.IsNullOrWhiteSpace(emotion))
                {
                    var matched = validEmotions.FirstOrDefault(e =>
                        string.Equals(e, emotion, StringComparison.OrdinalIgnoreCase)
                    );
                    if (matched != null)
                    {
                        results[index] = matched;
                    }
                }
            }
        }
        catch
        {
            // Fallback regex in case of slight JSON deviations
            var regex = new Regex(
                @"[""'](?:index|id)[""']\s*:\s*(\d+)\s*,\s*[""'](?:emotion|tone)[""']\s*:\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase
            );
            var validEmotions = new HashSet<string>(
                ActorEmotionEngine.EmotionNames,
                StringComparer.OrdinalIgnoreCase
            );
            foreach (Match m in regex.Matches(jsonText))
            {
                if (int.TryParse(m.Groups[1].Value, out int idx))
                {
                    var em = m.Groups[2].Value.Trim();
                    var matched = validEmotions.FirstOrDefault(e =>
                        string.Equals(e, em, StringComparison.OrdinalIgnoreCase)
                    );
                    if (matched != null)
                        results[idx] = matched;
                }
            }
        }
    }

    /// <summary>
    /// Analyzes a batch of subtitle dialogue lines to perform cinema-grade speaker diarization,
    /// identifying character names, vocal gender (Male/Female/Child), tone archetypes, and emotional tones.
    /// </summary>
    public async Task<List<GeminiDiarizationItem>> DiarizeAndAssignSpeakersBatchAsync(
        string apiKey,
        IReadOnlyList<SubtitleSegment> segments,
        string model = DefaultModel,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<GeminiDiarizationItem>();
        if (string.IsNullOrWhiteSpace(apiKey) || segments.Count == 0)
            return results;

        string cleanApiKey = apiKey.Trim();
        string cleanModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        var inputItems = segments
            .Select(s => new
            {
                index = s.Index,
                original = s.OriginalText,
                khmer = s.KhmerText,
            })
            .ToList();

        string inputJson = JsonSerializer.Serialize(inputItems);

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(
            "You are an expert Hollywood & Cambodian cinema dubbing casting director and dialogue diarization specialist."
        );
        promptBuilder.AppendLine(
            "Analyze the movie dialogue script below. For each dialogue line, determine:"
        );
        promptBuilder.AppendLine(
            "1. 'speaker': The name of the character speaking the line (e.g. 'Hero', 'Heroine', 'Elena', 'Captain', 'Child', 'Villain', 'Narrator'). If not explicitly prefixed, identify distinct characters based on conversational turns and relationships."
        );
        promptBuilder.AppendLine(
            "2. 'gender': The voice gender category ('Male', 'Female', or 'Child')."
        );
        promptBuilder.AppendLine(
            "3. 'toneArchetype': The character's vocal archetype from this exact list: 'Hero', 'Villain', 'Elder', 'Youth', 'Action', 'Narrator', 'Romantic', 'Comic'."
        );
        promptBuilder.AppendLine(
            "4. 'emotion': The acting delivery tone from this exact list: 'Normal', 'Crying', 'Laughing', 'Sad', 'Happy', 'Angry', 'Whisper', 'Fear', 'Villain', 'Narrator', 'Elder', 'Action', 'Romantic', 'Youth', 'Sarcastic', 'Scream', 'Frustrated', 'Strong', 'Soft'."
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(
            "Output ONLY a valid JSON array of objects with properties: 'index', 'speaker', 'gender', 'toneArchetype', 'emotion'."
        );
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Dialogue script:");
        promptBuilder.AppendLine(inputJson);

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={cleanApiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = promptBuilder.ToString() } } },
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 8192,
                responseMimeType = "application/json",
            },
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        using var response = await HttpClient.PostAsync(url, jsonContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return results;

        var resJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(resJson);

        if (
            doc.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.GetArrayLength() > 0
        )
        {
            var firstCandidate = candidates[0];
            if (
                firstCandidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.GetArrayLength() > 0
            )
            {
                var text = parts[0].GetProperty("text").GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    text = StripMarkdownFences(text);
                    ParseGeminiDiarizationJson(text, results);
                }
            }
        }

        return results;
    }

    public static void ParseGeminiDiarizationJson(
        string jsonText,
        List<GeminiDiarizationItem> results
    )
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            JsonElement arrayElem;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                arrayElem = doc.RootElement;
            }
            else if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && (
                    doc.RootElement.TryGetProperty("lines", out var lProp)
                    || doc.RootElement.TryGetProperty("characters", out lProp)
                    || doc.RootElement.TryGetProperty("dialogue", out lProp)
                )
                && lProp.ValueKind == JsonValueKind.Array
            )
            {
                arrayElem = lProp;
            }
            else
            {
                return;
            }

            var validEmotions = new HashSet<string>(
                ActorEmotionEngine.EmotionNames,
                StringComparer.OrdinalIgnoreCase
            );
            var validArchetypes = new HashSet<string>(
                new[]
                {
                    "Hero",
                    "Villain",
                    "Elder",
                    "Youth",
                    "Action",
                    "Narrator",
                    "Romantic",
                    "Comic",
                },
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var item in arrayElem.EnumerateArray())
            {
                int index = 0;
                if (
                    item.TryGetProperty("index", out var idxProp)
                    || item.TryGetProperty("id", out idxProp)
                )
                {
                    if (idxProp.ValueKind == JsonValueKind.Number)
                        index = idxProp.GetInt32();
                    else if (
                        idxProp.ValueKind == JsonValueKind.String
                        && int.TryParse(idxProp.GetString(), out int p)
                    )
                        index = p;
                }

                string speaker = "Speaker";
                if (
                    item.TryGetProperty("speaker", out var spProp)
                    || item.TryGetProperty("character", out spProp)
                    || item.TryGetProperty("name", out spProp)
                )
                {
                    speaker = spProp.GetString()?.Trim() ?? "Speaker";
                }

                string gender = VoiceGenderDetector.GenderMale;
                if (item.TryGetProperty("gender", out var genProp))
                {
                    var gStr = genProp.GetString()?.Trim() ?? "";
                    if (gStr.Contains("child", StringComparison.OrdinalIgnoreCase))
                        gender = VoiceGenderDetector.GenderChild;
                    else if (gStr.Contains("female", StringComparison.OrdinalIgnoreCase))
                        gender = VoiceGenderDetector.GenderFemale;
                    else
                        gender = VoiceGenderDetector.GenderMale;
                }

                string toneArchetype = "Hero";
                if (
                    item.TryGetProperty("toneArchetype", out var archProp)
                    || item.TryGetProperty("archetype", out archProp)
                )
                {
                    var aStr = archProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(aStr))
                    {
                        var matched = validArchetypes.FirstOrDefault(a =>
                            string.Equals(a, aStr, StringComparison.OrdinalIgnoreCase)
                        );
                        if (matched != null)
                            toneArchetype = matched;
                    }
                }

                string emotion = ActorEmotionEngine.EmotionNormal;
                if (
                    item.TryGetProperty("emotion", out var emProp)
                    || item.TryGetProperty("tone", out emProp)
                )
                {
                    var eStr = emProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(eStr))
                    {
                        var matched = validEmotions.FirstOrDefault(e =>
                            string.Equals(e, eStr, StringComparison.OrdinalIgnoreCase)
                        );
                        if (matched != null)
                            emotion = matched;
                    }
                }

                if (index > 0)
                {
                    results.Add(
                        new GeminiDiarizationItem(index, speaker, gender, toneArchetype, emotion)
                    );
                }
            }
        }
        catch
        {
            // Fallback regex parser for resilient extraction
            var regex = new Regex(
                @"[""'](?:index|id)[""']\s*:\s*(\d+).*?[""'](?:speaker|character|name)[""']\s*:\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
            foreach (Match m in regex.Matches(jsonText))
            {
                if (int.TryParse(m.Groups[1].Value, out int idx))
                {
                    string sp = m.Groups[2].Value.Trim();
                    string gen =
                        VoiceGenderDetector.DetectGenderFromName(sp)
                        ?? VoiceGenderDetector.GenderMale;
                    string arch = VoiceGenderDetector.DetectToneArchetypeFromName(sp, gen);
                    results.Add(
                        new GeminiDiarizationItem(
                            idx,
                            sp,
                            gen,
                            arch,
                            ActorEmotionEngine.EmotionNormal
                        )
                    );
                }
            }
        }
    }

    private static string StripMarkdownFences(string input)
    {
        var clean = input.Trim();
        if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            clean = clean[7..];
        else if (clean.StartsWith("```"))
            clean = clean[3..];

        if (clean.EndsWith("```"))
            clean = clean[..^3];

        return clean.Trim();
    }
}

public record GeminiDiarizationItem(
    int Index,
    string SpeakerName,
    string Gender,
    string ToneArchetype,
    string Emotion
);
