using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public class SubtitleTranslationService
{
    private static readonly HttpClient HttpClient = new();
    private readonly GeminiTranslationService _geminiService = new();

    public async Task<List<SubtitleSegment>> ExtractOrGenerateSubtitlesAsync(
        string ffmpegPath,
        string videoFilePath,
        string sourceLang,
        CancellationToken cancellationToken = default
    )
    {
        var segments = new List<SubtitleSegment>();

        // 1. Check if an external .srt / .vtt file exists alongside the video
        var srtPath = Path.ChangeExtension(videoFilePath, ".srt");
        var vttPath = Path.ChangeExtension(videoFilePath, ".vtt");

        if (File.Exists(srtPath))
        {
            var content = await File.ReadAllTextAsync(srtPath, Encoding.UTF8, cancellationToken);
            return ParseSrt(content);
        }

        if (File.Exists(vttPath))
        {
            var content = await File.ReadAllTextAsync(vttPath, Encoding.UTF8, cancellationToken);
            return ParseSrt(content);
        }

        // 2. Try extracting embedded subtitle stream from the video via FFmpeg
        var tempSrt = Path.Combine(Path.GetTempPath(), $"sub_{Guid.NewGuid():N}.srt");
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(videoFilePath);
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add("0:s:0");
            process.StartInfo.ArgumentList.Add(tempSrt);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            ChildProcessTracker.Track(process);
            await process.WaitForExitWithCancellationAsync(cancellationToken);

            if (process.ExitCode == 0 && File.Exists(tempSrt) && new FileInfo(tempSrt).Length > 0)
            {
                var content = await File.ReadAllTextAsync(
                    tempSrt,
                    Encoding.UTF8,
                    cancellationToken
                );
                var parsed = ParseSrt(content);
                if (parsed.Count > 0)
                    return parsed;
            }
        }
        catch
        {
            // Non-fatal, continue below
        }
        finally
        {
            if (File.Exists(tempSrt))
            {
                try
                {
                    File.Delete(tempSrt);
                }
                catch { }
            }
        }

        return segments;
    }

    public async Task<string> TranslateTextAsync(
        string text,
        string targetLang = "en",
        string sourceLang = "auto",
        string? geminiApiKey = null,
        string? speakerName = null,
        string? characterGender = null,
        string geminiModel = GeminiTranslationService.DefaultModel,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var tl = string.IsNullOrWhiteSpace(targetLang) ? "en" : targetLang.ToLowerInvariant();
        var sl =
            string.IsNullOrWhiteSpace(sourceLang)
            || sourceLang.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? "auto"
                : sourceLang.ToLowerInvariant();

        if (sl == tl && sl != "auto")
            return text;

        // Tier 0: Google Gemini AI (Context-aware cinema dialogue translation)
        if (!string.IsNullOrWhiteSpace(geminiApiKey))
        {
            try
            {
                var aiTranslated = await _geminiService.TranslateLineAsync(
                    geminiApiKey,
                    text,
                    sourceLang,
                    speakerName,
                    characterGender,
                    geminiModel,
                    cancellationToken
                );

                if (!string.IsNullOrWhiteSpace(aiTranslated))
                    return aiTranslated;
            }
            catch
            {
                // Smooth failover to web translation tiers if Gemini key is invalid or rate limited
            }
        }

        var encoded = Uri.EscapeDataString(text);

        // Tier 1: Google clients5 dict-chrome-ex (high reliability, low rate-limit)
        try
        {
            var url1 =
                $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={sl}&tl={tl}&q={encoded}";

            using var req1 = new HttpRequestMessage(HttpMethod.Get, url1);
            req1.Headers.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
            );

            using var res1 = await HttpClient.SendAsync(req1, cancellationToken);
            if (res1.IsSuccessStatusCode)
            {
                var json1 = await res1.Content.ReadAsStringAsync(cancellationToken);
                using var doc1 = JsonDocument.Parse(json1);

                if (
                    doc1.RootElement.ValueKind == JsonValueKind.Array
                    && doc1.RootElement.GetArrayLength() > 0
                )
                {
                    var sb = new StringBuilder();
                    foreach (var elem in doc1.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(elem.GetString());
                        }
                        else if (elem.ValueKind == JsonValueKind.Array && elem.GetArrayLength() > 0)
                        {
                            var first = elem[0];
                            if (first.ValueKind == JsonValueKind.String)
                                sb.Append(first.GetString());
                        }
                    }

                    var result1 = sb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(result1))
                        return result1;
                }
            }
        }
        catch
        {
            // Fallback to Tier 2
        }

        // Tier 2: Google gtx translation endpoint
        try
        {
            var url2 =
                $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={encoded}";

            using var req2 = new HttpRequestMessage(HttpMethod.Get, url2);
            req2.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            using var res2 = await HttpClient.SendAsync(req2, cancellationToken);
            if (res2.IsSuccessStatusCode)
            {
                var json2 = await res2.Content.ReadAsStringAsync(cancellationToken);
                using var doc2 = JsonDocument.Parse(json2);

                var sb2 = new StringBuilder();
                if (
                    doc2.RootElement.ValueKind == JsonValueKind.Array
                    && doc2.RootElement.GetArrayLength() > 0
                )
                {
                    var sentences = doc2.RootElement[0];
                    if (sentences.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sentence in sentences.EnumerateArray())
                        {
                            if (
                                sentence.ValueKind == JsonValueKind.Array
                                && sentence.GetArrayLength() > 0
                            )
                            {
                                sb2.Append(sentence[0].GetString());
                            }
                        }
                    }
                }

                var result2 = sb2.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(result2))
                    return result2;
            }
        }
        catch
        {
            // Fallback to Tier 3
        }

        // Tier 3: MyMemory Translation API
        try
        {
            var srcLangPair = sl == "auto" ? (tl == "en" ? "es" : "en") : sl;
            var url3 =
                $"https://api.mymemory.translated.net/get?q={encoded}&langpair={srcLangPair}|{tl}";

            using var req3 = new HttpRequestMessage(HttpMethod.Get, url3);
            req3.Headers.Add("User-Agent", "855Media/1.0");

            using var res3 = await HttpClient.SendAsync(req3, cancellationToken);
            if (res3.IsSuccessStatusCode)
            {
                var json3 = await res3.Content.ReadAsStringAsync(cancellationToken);
                using var doc3 = JsonDocument.Parse(json3);
                if (
                    doc3.RootElement.TryGetProperty("responseData", out var respData)
                    && respData.TryGetProperty("translatedText", out var transText)
                )
                {
                    var result3 = transText.GetString()?.Trim();
                    if (
                        !string.IsNullOrWhiteSpace(result3)
                        && !result3.StartsWith(
                            "MYMEMORY WARNING",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        return result3;
                }
            }
        }
        catch
        {
            // Final fallback
        }

        return text;
    }

    public async Task<string> TranslateToKhmerAsync(
        string text,
        string sourceLang = "auto",
        string? geminiApiKey = null,
        string? speakerName = null,
        string? characterGender = null,
        string geminiModel = GeminiTranslationService.DefaultModel,
        CancellationToken cancellationToken = default
    )
    {
        var raw = await TranslateTextAsync(
            text,
            "km",
            sourceLang,
            geminiApiKey,
            speakerName,
            characterGender,
            geminiModel,
            cancellationToken
        );
        return PolishKhmerDialogue(raw);
    }

    public async Task<string> TranslateToEnglishAsync(
        string text,
        string sourceLang = "auto",
        string? geminiApiKey = null,
        CancellationToken cancellationToken = default
    ) =>
        await TranslateTextAsync(
            text,
            "en",
            sourceLang,
            geminiApiKey,
            cancellationToken: cancellationToken
        );

    public async Task TranslateSegmentsToEnglishAsync(
        IEnumerable<SubtitleSegment> segments,
        string sourceLang = "auto",
        string? geminiApiKey = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var list = segments.ToList();
        if (list.Count == 0)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seg = list[i];
            if (string.IsNullOrWhiteSpace(seg.OriginalText))
                continue;

            if (sourceLang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                seg.EnglishText = seg.OriginalText;
            }
            else
            {
                seg.EnglishText = await TranslateToEnglishAsync(
                    seg.OriginalText,
                    sourceLang,
                    geminiApiKey,
                    cancellationToken
                );
            }
            progress?.Report((double)(i + 1) / list.Count);
        }
    }

    /// <summary>
    /// Translates subtitle segments in batches using Google Gemini AI, maintaining scene context and character consistency.
    /// Automatically falls back to multi-tier web translation for any segments that Gemini could not process.
    /// </summary>
    public async Task TranslateSegmentsWithGeminiAsync(
        string geminiApiKey,
        IReadOnlyList<SubtitleSegment> segments,
        string sourceLang = "auto",
        string geminiModel = GeminiTranslationService.DefaultModel,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default
    )
    {
        if (segments.Count == 0)
            return;

        int total = segments.Count;
        int completed = 0;
        const int batchSize = 40;

        var batches = segments
            .Where(s => !string.IsNullOrWhiteSpace(s.OriginalText))
            .Chunk(batchSize)
            .ToList();

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 2,
                CancellationToken = cancellationToken,
            },
            async (batch, ct) =>
            {
                Dictionary<int, string>? geminiMap = null;
                try
                {
                    geminiMap = await _geminiService.TranslateBatchAsync(
                        geminiApiKey,
                        batch,
                        sourceLang,
                        geminiModel,
                        ct
                    );
                }
                catch
                {
                    // Failover to web translation if Gemini batch has an error
                }

                foreach (var seg in batch)
                {
                    if (
                        geminiMap != null
                        && geminiMap.TryGetValue(seg.Index, out var translated)
                        && !string.IsNullOrWhiteSpace(translated)
                    )
                    {
                        seg.KhmerText = translated;
                    }
                    else
                    {
                        // Fallback to web translation tiers
                        seg.KhmerText = await TranslateToKhmerAsync(
                            seg.OriginalText,
                            sourceLang,
                            cancellationToken: ct
                        );
                    }

                    int current = Interlocked.Increment(ref completed);
                    progressCallback?.Invoke(current, total);
                }
            }
        );
    }

    public List<SubtitleSegment> ParseSrt(string srtContent)
    {
        var segments = new List<SubtitleSegment>();
        if (string.IsNullOrWhiteSpace(srtContent))
            return segments;

        var blocks = Regex.Split(srtContent.Trim(), @"\r?\n\r?\n");
        var timeRegex = new Regex(
            @"(?:(?<sh>\d{1,2}):)?(?<sm>\d{2}):(?<ss>\d{2})[,.](?<sms>\d{3})\s*-->\s*(?:(?<eh>\d{1,2}):)?(?<em>\d{2}):(?<es>\d{2})[,.](?<ems>\d{3})"
        );

        int index = 1;
        foreach (var block in blocks)
        {
            var lines = block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                continue;

            // Find line with timestamps
            Match? match = null;
            int textStartIndex = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = timeRegex.Match(lines[i]);
                if (m.Success)
                {
                    match = m;
                    textStartIndex = i + 1;
                    break;
                }
            }

            if (match == null || !match.Success)
                continue;

            int sh = match.Groups["sh"].Success ? int.Parse(match.Groups["sh"].Value) : 0;
            int sm = int.Parse(match.Groups["sm"].Value);
            int ss = int.Parse(match.Groups["ss"].Value);
            int sms = int.Parse(match.Groups["sms"].Value);
            var startTime = new TimeSpan(0, sh, sm, ss, sms);

            int eh = match.Groups["eh"].Success ? int.Parse(match.Groups["eh"].Value) : 0;
            int em = int.Parse(match.Groups["em"].Value);
            int es = int.Parse(match.Groups["es"].Value);
            int ems = int.Parse(match.Groups["ems"].Value);
            var endTime = new TimeSpan(0, eh, em, es, ems);

            var textBuilder = new StringBuilder();
            for (int i = textStartIndex; i < lines.Length; i++)
            {
                if (textBuilder.Length > 0)
                    textBuilder.Append(' ');
                textBuilder.Append(lines[i].Trim());
            }

            var rawText = textBuilder.ToString();
            // Strip HTML/formatting tags like <i></i>
            rawText = Regex.Replace(rawText, @"<[^>]+>", string.Empty);

            var cleanedText = DialogueSenseEngine.ImproveOriginalDialogue(rawText);

            // Filter out pure non-speech markers (e.g. [Music], ♪, ..., empty/punctuation after cleanup)
            if (
                !string.IsNullOrWhiteSpace(cleanedText)
                && cleanedText != "."
                && cleanedText != "。"
                && cleanedText != "។"
                && !cleanedText.Equals("[music]", StringComparison.OrdinalIgnoreCase)
                && !cleanedText.Equals("(music)", StringComparison.OrdinalIgnoreCase)
            )
            {
                segments.Add(
                    new SubtitleSegment
                    {
                        Index = index++,
                        StartTime = startTime,
                        EndTime = endTime,
                        OriginalText = cleanedText,
                        KhmerText = string.Empty,
                    }
                );
            }
        }

        return segments;
    }

    /// <summary>
    /// Transforms mechanical machine-translated Khmer text into natural, idiomatic movie dialogue.
    /// Replaces textbook constructs, literal idioms, and stiff pronouns with conversational Cambodian cinema phrasing.
    /// </summary>
    public static string PolishKhmerDialogue(string rawKhmer)
    {
        if (string.IsNullOrWhiteSpace(rawKhmer))
            return string.Empty;

        var text = rawKhmer.Trim();

        // Decode any HTML entities left from web translation APIs
        text = text.Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">");

        // 1. Literal English-to-Khmer translation idiom fixes
        var idiomReplacements = new (string Pattern, string Replacement)[]
        {
            // "What the hell / What on earth" -> literal "ឋាននរក" -> "ស្អីគេ"
            (@"តើឋាននរក\s*(អ្វី|អី)?", "ស្អីគេ"),
            (@"ឋាននរក\s*(អ្វី|អី)?", "ស្អីគេ"),
            // "What is going on / What happened"
            (@"តើមានរឿងអ្វីកើតឡើង\??", "មានរឿងអីកើតឡើងហ្នឹង?"),
            (@"តើមានអ្វីកើតឡើង\??", "មានរឿងអីកើតឡើង?"),
            // "Shut up" -> "បិទមាត់របស់អ្នក" -> "បិទមាត់ទៅ!"
            (@"បិទមាត់(របស់)?(អ្នក|ឯង)", "បិទមាត់ទៅ!"),
            (@"បិទមាត់ទៅ", "បិទមាត់ទៅ!"),
            // "Are you crazy / Are you insane"
            (@"តើ(អ្នក|ឯង)ឆ្កួត(ទេ|ឬ)\??", "$1ឆ្កួតទេដឹង?"),
            (@"(អ្នក|ឯង)ឆ្កួតទេ\??", "$1ឆ្កួតទេដឹង?"),
            // "Don't worry" -> "កុំបារម្ភអំពី(វា)?" -> "កុំបារម្ភអី"
            (@"កុំបារម្ភ(អំពី|ពី)?(វា)?(អី)?", "កុំបារម្ភអី"),
            // "Oh my god" -> "ឱព្រះជាម្ចាស់របស់ខ្ញុំ" -> "ព្រះអើយ!"
            (@"ឱព្រះជាម្ចាស់(របស់ខ្ញុំ)?", "ព្រះអើយ!"),
            (@"ព្រះជាម្ចាស់អើយ", "ព្រះអើយ!"),
            // "Leave me alone / Get lost"
            (@"ទុកឱ្យខ្ញុំនៅម្នាក់ឯង(ទៅ)?", "ទុកឱ្យខ្ញុំនៅម្នាក់ឯងទៅ!"),
            (@"ចេញ(ឱ្យ)?ឆ្ងាយពីខ្ញុំ(ទៅ)?", "ទៅឱ្យឆ្ងាយទៅ!"),
            (@"ទៅឆ្ងាយពីខ្ញុំ", "ទៅឱ្យឆ្ងាយទៅ!"),
            // "Hurry up"
            (@"ប្រញាប់ឡើង(មក)?", "លឿនឡើង!"),
            // "Take care"
            (@"ថែរក្សាខ្លួន(អ្នក)?", "មើលថែខ្លួនផង!"),
            (@"មើលថែខ្លួនឯង", "មើលថែខ្លួនផង!"),
            // "I don't care"
            (@"ខ្ញុំមិនខ្វល់(អំពីវា)?ទេ", "ខ្ញុំមិនខ្វល់ទេ!"),
            // "No way / Impossible"
            (@"មិនអាចទៅរួចទេ", "មិនអាចទេ!"),
            // "Help me"
            (@"ជួយខ្ញុំ(ផង)?", "ជួយផង!"),
            // English idioms if untranslated
            (@"\bwhat the hell\b", "ស្អីគេ"),
            (@"\bshut up\b", "បិទមាត់ទៅ!"),
            (@"\boh my god\b", "ព្រះអើយ!"),
            (@"\bhurry up\b", "លឿនឡើង!"),
            (@"\bdon'?t worry\b", "កុំបារម្ភអី"),
            (@"\bare you crazy\b", "ឯងឆ្កួតទេដឹង?"),
            // Chinese drama idioms & cinema spoken fixes (DramaBox / C-dramas)
            (@"怎么回事\??", "មានរឿងអីកើតឡើងហ្នឹង?"),
            (@"发生(了)?什么(事)?\??", "មានរឿងអីកើតឡើង?"),
            (@"放开我(!|！)?", "លែងខ្ញុំទៅ!"),
            (@"放手(!|！)?", "លែងទៅ!"),
            (@"救命(啊)?(!|！)?", "ជួយផង!"),
            (@"快走(!|！)?", "លឿនឡើង!"),
            (@"快跑(!|！)?", "រត់ទៅ!"),
            (@"住手(!|！)?", "ឈប់ទៅ!"),
            (@"闭嘴(!|！)?", "បិទមាត់ទៅ!"),
            (@"不可能(!|！)?", "មិនអាចទេ!"),
            (@"别担心", "កុំបារម្ភអី"),
            (@"放心(吧)?", "កុំបារម្ភអី"),
            (@"滚开(!|！)?", "ទៅឱ្យឆ្ងាយទៅ!"),
            (@"滚(!|！)?", "ទៅឱ្យឆ្ងាយទៅ!"),
            (@"你疯了(吗)?\??", "ឯងឆ្កួតទេដឹង?"),
            (@"王爷", "លោកម្ចាស់"),
            (@"陛下", "ព្រះករុណា"),
            (@"(师傅|师父)", "លោកគ្រូ"),
            (@"殿下", "ទ្រង់"),
            (@"老天爷(啊)?", "ព្រះអើយ!"),
        };

        foreach (var (pattern, replacement) in idiomReplacements)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        // 2. Natural spoken particle replacements:
        // Replace textbook "ធ្វើអ្វី" -> "ធ្វើអី" (casual movie speech)
        text = Regex.Replace(text, @"ធ្វើអ្វី", "ធ្វើអី");
        text = Regex.Replace(text, @"ទៅណា\?", "ទៅណាដែរ?");
        text = Regex.Replace(text, @"តើឯង", "ឯង");
        text = Regex.Replace(text, @"តើអ្នក", "អ្នក");
        text = Regex.Replace(text, @"តើខ្ញុំ", "ខ្ញុំ");
        text = Regex.Replace(text, @"តើពួកគេ", "ពួកគេ");

        // Clean up redundant textbook "តើ" at start of line followed by question mark
        if (text.StartsWith("តើ") && text.Contains('?'))
        {
            text = text[2..].TrimStart();
        }

        // Clean up duplicate punctuation (e.g. "??", "!!", " !?")
        text = Regex.Replace(text, @"\?{2,}", "?");
        text = Regex.Replace(text, @"!{2,}", "!");
        text = Regex.Replace(text, @"\s+([!?.,])", "$1");

        return text.Trim();
    }

    /// <summary>
    /// Generates an SRT subtitle document from subtitle segments.
    /// Supports Khmer-only, Original-only, or Bilingual dual subtitles with speaker tags.
    /// </summary>
    public static string GenerateSrt(
        IEnumerable<SubtitleSegment> segments,
        bool includeOriginal = false,
        bool includeSpeakerTag = true,
        bool useEnglishText = false
    )
    {
        var sb = new StringBuilder();
        int counter = 1;

        foreach (var seg in segments.OrderBy(s => s.StartTime))
        {
            sb.AppendLine(counter.ToString());
            var startStr =
                $"{seg.StartTime.Hours:D2}:{seg.StartTime.Minutes:D2}:{seg.StartTime.Seconds:D2},{seg.StartTime.Milliseconds:D3}";
            var endStr =
                $"{seg.EndTime.Hours:D2}:{seg.EndTime.Minutes:D2}:{seg.EndTime.Seconds:D2},{seg.EndTime.Milliseconds:D3}";
            sb.AppendLine($"{startStr} --> {endStr}");

            var speakerPrefix =
                includeSpeakerTag && !string.IsNullOrWhiteSpace(seg.SpeakerName)
                    ? $"[{seg.SpeakerName}] "
                    : string.Empty;

            var targetText = useEnglishText
                ? (!string.IsNullOrWhiteSpace(seg.EnglishText) ? seg.EnglishText : seg.OriginalText)
                : (!string.IsNullOrWhiteSpace(seg.KhmerText) ? seg.KhmerText : seg.OriginalText);

            if (
                includeOriginal
                && !string.IsNullOrWhiteSpace(seg.OriginalText)
                && seg.OriginalText != targetText
            )
            {
                sb.AppendLine($"{speakerPrefix}{targetText}");
                sb.AppendLine(seg.OriginalText);
            }
            else
            {
                sb.AppendLine($"{speakerPrefix}{targetText}");
            }

            sb.AppendLine();
            counter++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a WebVTT (.vtt) subtitle document from subtitle segments.
    /// </summary>
    public static string GenerateVtt(
        IEnumerable<SubtitleSegment> segments,
        bool includeOriginal = false,
        bool includeSpeakerTag = true
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        int counter = 1;
        foreach (var seg in segments.OrderBy(s => s.StartTime))
        {
            sb.AppendLine(counter.ToString());
            var startStr =
                $"{seg.StartTime.Hours:D2}:{seg.StartTime.Minutes:D2}:{seg.StartTime.Seconds:D2}.{seg.StartTime.Milliseconds:D3}";
            var endStr =
                $"{seg.EndTime.Hours:D2}:{seg.EndTime.Minutes:D2}:{seg.EndTime.Seconds:D2}.{seg.EndTime.Milliseconds:D3}";
            sb.AppendLine($"{startStr} --> {endStr}");

            var speakerPrefix =
                includeSpeakerTag && !string.IsNullOrWhiteSpace(seg.SpeakerName)
                    ? $"<v {seg.SpeakerName}>"
                    : string.Empty;
            var speakerSuffix =
                includeSpeakerTag && !string.IsNullOrWhiteSpace(seg.SpeakerName)
                    ? "</v>"
                    : string.Empty;

            var khmer = !string.IsNullOrWhiteSpace(seg.KhmerText)
                ? seg.KhmerText
                : seg.OriginalText;

            if (
                includeOriginal
                && !string.IsNullOrWhiteSpace(seg.OriginalText)
                && seg.OriginalText != khmer
            )
            {
                sb.AppendLine($"{speakerPrefix}{khmer}{speakerSuffix}");
                sb.AppendLine(seg.OriginalText);
            }
            else
            {
                sb.AppendLine($"{speakerPrefix}{khmer}{speakerSuffix}");
            }

            sb.AppendLine();
            counter++;
        }

        return sb.ToString();
    }
}
