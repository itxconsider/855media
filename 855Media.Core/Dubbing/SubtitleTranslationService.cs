using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            await process.WaitForExitAsync(cancellationToken);

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

    public async Task<string> TranslateToKhmerAsync(
        string text,
        string sourceLang = "auto",
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sl =
            string.IsNullOrWhiteSpace(sourceLang)
            || sourceLang.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? "auto"
                : sourceLang.ToLowerInvariant();

        var encoded = Uri.EscapeDataString(text);

        // Tier 1: Google clients5 dict-chrome-ex (high reliability, low rate-limit)
        try
        {
            var url1 =
                $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={sl}&tl=km&q={encoded}";

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
                $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl=km&dt=t&q={encoded}";

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
            var srcLangPair = sl == "auto" ? "en" : sl;
            var url3 =
                $"https://api.mymemory.translated.net/get?q={encoded}&langpair={srcLangPair}|km";

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

    public List<SubtitleSegment> ParseSrt(string srtContent)
    {
        var segments = new List<SubtitleSegment>();
        if (string.IsNullOrWhiteSpace(srtContent))
            return segments;

        var blocks = Regex.Split(srtContent.Trim(), @"\r?\n\r?\n");
        var timeRegex = new Regex(
            @"(?<sh>\d{1,2}):(?<sm>\d{2}):(?<ss>\d{2})[,.](?<sms>\d{3})\s*-->\s*(?<eh>\d{1,2}):(?<em>\d{2}):(?<es>\d{2})[,.](?<ems>\d{3})"
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

            var startTime = new TimeSpan(
                0,
                int.Parse(match.Groups["sh"].Value),
                int.Parse(match.Groups["sm"].Value),
                int.Parse(match.Groups["ss"].Value),
                int.Parse(match.Groups["sms"].Value)
            );

            var endTime = new TimeSpan(
                0,
                int.Parse(match.Groups["eh"].Value),
                int.Parse(match.Groups["em"].Value),
                int.Parse(match.Groups["es"].Value),
                int.Parse(match.Groups["ems"].Value)
            );

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

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                segments.Add(
                    new SubtitleSegment
                    {
                        Index = index++,
                        StartTime = startTime,
                        EndTime = endTime,
                        OriginalText = rawText,
                        KhmerText = string.Empty,
                    }
                );
            }
        }

        return segments;
    }
}
