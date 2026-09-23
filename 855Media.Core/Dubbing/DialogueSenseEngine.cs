using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace _855Media.Core.Dubbing;

/// <summary>
/// High-accuracy dialogue improvement, semantic sense-to-sense splitting,
/// and sentence reconstruction engine for audio dubbing and subtitles.
/// Ensures complete sentence matching, clean punctuation, and coherent alignment
/// between original dialogue and translated Khmer speech.
/// </summary>
public static class DialogueSenseEngine
{
    private static readonly HashSet<string> LatinAbbreviations = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "Mr",
        "Mrs",
        "Ms",
        "Dr",
        "Prof",
        "Sr",
        "Jr",
        "vs",
        "etc",
        "e.g",
        "i.e",
        "approx",
        "apt",
        "dept",
        "est",
        "fig",
        "gen",
        "gov",
        "inc",
        "ltd",
        "corp",
        "min",
        "max",
        "no",
        "st",
        "ave",
        "blvd",
        "rd",
        "am",
        "pm",
    };

    private static readonly string[] MajorConjunctionsEnglish =
    [
        " because ",
        " although ",
        " though ",
        " however ",
        " therefore ",
        " but ",
        " and ",
        " when ",
        " while ",
        " so ",
        " since ",
        " yet ",
        " whereas ",
        " unless ",
        " otherwise ",
        " meanwhile ",
        " instead ",
    ];

    private static readonly string[] KhmerClauseMarkers =
    [
        " ហើយ ",
        " ប៉ុន្តែ ",
        " ពីព្រោះ ",
        " ព្រោះ ",
        " ដូច្នេះ ",
        " ខណៈដែល ",
        " ប្រសិនបើ ",
        " ដែល ",
        " បើ ",
        " ទើប ",
        " តែ ",
    ];

    private static readonly string[] MajorConjunctionsChinese =
    [
        // Contrast / Concession (Although / But)
        "但是",
        "可是",
        "不过",
        "然而",
        "却",
        "虽然",
        "尽管",
        // Cause & Effect (Because / So / Therefore)
        "因为",
        "所以",
        "由于",
        "因此",
        "因而",
        // Condition / Hypotheses (If / Then)
        "如果",
        "要是",
        "假如",
        "既然",
        "那么",
        "只要",
        "只有",
        // Progression & Coordination (And / Besides / Furthermore)
        "而且",
        "并且",
        "以及",
        "另外",
        "还有",
        "甚至",
        // Action / Sequence
        "为了",
        "然后",
        "于是",
        "接着",
    ];

    /// <summary>
    /// Cleans transcription noise, hallucinated repeats, stuttering artifacts,
    /// subtitle styling tags, and normalizes punctuation and sentence capitalization.
    /// Supports English, Khmer, and Chinese (with CJK spacing and punctuation normalization).
    /// </summary>
    public static string ImproveOriginalDialogue(string? rawText, string? sourceLang = null)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        var text = rawText.Trim();

        // 1. Strip Subtitle Formatting & Positioning Tags (e.g. {\an8}, <font ...>, <i>)
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = Regex.Replace(text, @"\{[^}]+\}", string.Empty);

        // 2. Strip Non-Speech Sound Annotations and Noise Tags (Western & Chinese brackets)
        // e.g. [Music], [Applause], 【音乐】, 【掌声】, （笑声）, （叹气）, ♪♪, ♫, >>
        text = Regex.Replace(
            text,
            @"\[(music|applause|laughter|silence|gasp|sigh|crying|whispering|singing|sound|snicker|clears throat|groan|screaming|cheering|chuckles?|bell|cheers|crowd)[^\]]*\]",
            string.Empty,
            RegexOptions.IgnoreCase
        );

        text = Regex.Replace(
            text,
            @"\((applause|laughter|sighs?|music|coughs?|gasps?|screams?|whispering|chuckles?|clears throat|groans?)[^)]*\)",
            string.Empty,
            RegexOptions.IgnoreCase
        );

        // Chinese fullwidth & halfwidth noise annotations
        text = Regex.Replace(
            text,
            @"[【\[（\(](音乐|掌声|笑声|哭声|叹气|喘气|尖叫|脚步声|背景音乐|歌声|音效|欢呼声|咳嗽声|吸气声|叹息声|叹气声)[】\]）\)]",
            string.Empty,
            RegexOptions.IgnoreCase
        );

        text = Regex.Replace(text, @"[♪♫►▼■★●▲▶◄♦]+", string.Empty);
        text = Regex.Replace(text, @"^[\s>»\-–—]+", string.Empty);

        // 3. Decode HTML Entities
        text = text.Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">");

        // 4. Remove Hallucinated Word Repeats (frequent in Whisper AI on silence/music)
        // Western: "you know you know you know" -> "you know", "that that that" -> "that"
        text = Regex.Replace(text, @"\b([A-Za-z]+)(?:\s+\1\b){2,}", "$1", RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            @"\b([A-Za-z]+\s+[A-Za-z]+)(?:\s+\1\b)+",
            "$1",
            RegexOptions.IgnoreCase
        );

        // Chinese: loop repeats like 对对对对对 -> 对, 谢谢谢谢谢谢 -> 谢谢, 不是不是不是 -> 不是
        text = Regex.Replace(text, @"([\u4e00-\u9fa5]{1,3})\1{2,}", "$1");

        // 5. Clean Leading Stutters / Speech Fillers (e.g. "uh, ", "um, ", "那个...", "就是...")
        text = Regex.Replace(
            text,
            @"^(uh|um|er|ah|eh|mm)\b[,\s]+",
            string.Empty,
            RegexOptions.IgnoreCase
        );
        text = Regex.Replace(text, @"^(?:[呃嗯啊哎呀]|那个|就是|这个)+[，,、\s…\.]+", string.Empty);

        // 6. Normalize Spacing Around Punctuation
        text = Regex.Replace(text, @"\s+([,.:;?!…~%])", "$1");
        text = Regex.Replace(text, @"([(\[{])\s+", "$1");
        text = Regex.Replace(text, @"\s+([)\]}])", "$1");

        // 7. Collapse Duplicate Punctuation
        text = Regex.Replace(text, @"\?{2,}", "?");
        text = Regex.Replace(text, @"!{2,}", "!");
        text = Regex.Replace(text, @",{2,}", ",");
        text = Regex.Replace(text, @":{2,}", ":");
        text = Regex.Replace(text, @";{2,}", ";");
        text = Regex.Replace(text, @"(?<!\.)\.{2}(?!\.)", ".");
        text = Regex.Replace(text, @"\.{4,}", "...");

        // Normalize Quotes
        text = text.Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'');

        // 8. Chinese Specific Normalization: Remove stray spaces between Chinese characters and normalize fullwidth punctuation
        if (IsCjkText(text))
        {
            // Remove stray spaces between Chinese characters
            text = Regex.Replace(text, @"(?<=[\u4e00-\u9fa5])\s+(?=[\u4e00-\u9fa5])", string.Empty);
            text = Regex.Replace(
                text,
                @"(?<=[\u4e00-\u9fa5])\s+(?=[，。！？、：；”」』）])",
                string.Empty
            );
            text = Regex.Replace(
                text,
                @"(?<=[，。！？、：；“「『（])\s+(?=[\u4e00-\u9fa5])",
                string.Empty
            );

            // Normalize halfwidth punctuation to Chinese fullwidth equivalents
            text = text.Replace(',', '，')
                .Replace('?', '？')
                .Replace('!', '！')
                .Replace(';', '；')
                .Replace(':', '：');

            if (text.EndsWith('.') && !text.EndsWith("..."))
            {
                text = text[..^1] + "。";
            }

            text = Regex.Replace(text, @"，{2,}", "，");
            text = Regex.Replace(text, @"。{2,}", "。");
            text = Regex.Replace(text, @"？{2,}", "？");
            text = Regex.Replace(text, @"！{2,}", "！");
            text = Regex.Replace(text, @"、{2,}", "、");
        }

        // Collapse general whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 9. Capitalize First Letter and Standalone Pronoun "I" (for Latin scripts)
        bool isLatin = text.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
        if (isLatin)
        {
            // Capitalize start of line
            if (char.IsLower(text[0]))
            {
                text = char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
            }

            // Capitalize after sentence terminators: ". ", "! ", "? "
            text = Regex.Replace(
                text,
                @"([.!?]\s+)([a-z])",
                m =>
                    m.Groups[1].Value
                    + char.ToUpper(m.Groups[2].Value[0], CultureInfo.InvariantCulture)
            );

            // Capitalize standalone pronoun "I" and contractions
            text = Regex.Replace(text, @"\bi\b", "I");
            text = Regex.Replace(text, @"\bi'([a-z]+)\b", m => "I'" + m.Groups[1].Value);
        }

        // 10. Add Natural Ending Punctuation if Missing
        if (text.Length >= 2 && !EndsWithSentencePunctuation(text))
        {
            if (IsCjkText(text))
                text += "。";
            else if (IsKhmerText(text))
                text += "។";
            else if (isLatin)
                text += ".";
        }

        return text;
    }

    /// <summary>
    /// Checks whether the text ends with sentence-terminating punctuation across multiple languages,
    /// accounting for quotes, brackets, and Latin abbreviations.
    /// </summary>
    public static bool IsCompleteSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimEnd(' ', '\t', '\r', '\n', '"', '\'', '”', '’', ')', ']', '}', '»');
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        char last = trimmed[^1];

        // East Asian (CJK) terminators
        if (last is '。' or '！' or '？' or '…')
            return true;

        // Southeast Asian (Khmer / Thai) terminators
        if (last is '។' or 'ฯ' or 'ៗ')
            return true;

        // South Asian (Devanagari) & Arabic terminators
        if (last is '।' or '؟')
            return true;

        // Western question & exclamation marks
        if (last is '?' or '!')
            return true;

        // Western period & ellipsis
        if (last == '.')
        {
            if (trimmed.EndsWith("...", StringComparison.Ordinal))
                return true;

            // Check if last word before period is an abbreviation (e.g. "Mr.", "Dr.")
            var lastSpace = trimmed.LastIndexOf(' ');
            var word = (lastSpace >= 0 ? trimmed[(lastSpace + 1)..^1] : trimmed[..^1]).Trim();
            if (LatinAbbreviations.Contains(word))
                return false;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the optimal semantic clause / sense split point in a dialogue string.
    /// Balances linguistic clause hierarchy (sentence breaks > punctuation > conjunctions > word boundaries)
    /// against distance from the midpoint (50% length), ensuring natural speech cadence.
    /// </summary>
    public static int FindSenseSplitPoint(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 6)
            return -1;

        double mid = text.Length / 2.0;
        double bestScore = double.MinValue;
        int bestIdx = -1;

        void ConsiderCandidate(int idx, double baseScore)
        {
            if (idx <= 2 || idx >= text.Length - 2)
                return;

            double dist = Math.Abs(idx - mid);
            double penalty = (dist / mid) * 55.0;
            double score = baseScore - penalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = idx;
            }
        }

        // 1. Sentence-level terminators with space or newline (e.g. ". ", "! ", "? ")
        var sentenceMatches = Regex.Matches(text, @"[.!?។。！？](\s+|\n+)");
        foreach (Match m in sentenceMatches)
        {
            ConsiderCandidate(m.Index + 1, 100.0);
        }

        // 2. Major clause punctuation: semicolon, colon, em-dash, dash
        var clauseMatches = Regex.Matches(text, @"([;:]|\s+[-–—]\s+)(\s*)");
        foreach (Match m in clauseMatches)
        {
            ConsiderCandidate(m.Index + m.Length, 85.0);
        }

        // 3. Comma boundaries (e.g. ", ", "、", "，")
        var commaMatches = Regex.Matches(text, @"([,،、，])(\s*)");
        foreach (Match m in commaMatches)
        {
            ConsiderCandidate(m.Index + m.Length, 75.0);
        }

        // 4. Coordinating & Subordinating Conjunctions (English / Western)
        foreach (var conj in MajorConjunctionsEnglish)
        {
            int pos = 0;
            while ((pos = text.IndexOf(conj, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                ConsiderCandidate(pos, 65.0);
                pos += conj.Length;
            }
        }

        // 5. Khmer Clause Markers (e.g. ហើយ, ប៉ុន្តែ, ពីព្រោះ)
        foreach (var marker in KhmerClauseMarkers)
        {
            int pos = 0;
            while ((pos = text.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
            {
                ConsiderCandidate(pos, 65.0);
                pos += marker.Length;
            }
        }

        // 6. Chinese Conjunctions / Clause Markers (e.g. 但是, 所以, 因为, 如果, 虽然, 而且, 可是, 不过, 然而)
        foreach (var conj in MajorConjunctionsChinese)
        {
            int pos = 0;
            while ((pos = text.IndexOf(conj, pos, StringComparison.Ordinal)) >= 0)
            {
                ConsiderCandidate(pos, 68.0);
                pos += conj.Length;
            }
        }

        // 7. Word boundaries (spaces)
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] == ' ')
            {
                ConsiderCandidate(i, 35.0);
            }
        }

        // 8. For CJK / scripts without spaces, consider character boundary
        if (bestIdx < 0 && (IsCjkText(text) || IsKhmerText(text)))
        {
            int charMid = text.Length / 2;
            return charMid;
        }

        return bestIdx;
    }

    /// <summary>
    /// Splits an original dialogue sentence into two grammatically coherent parts at the natural sense boundary.
    /// Cleans up punctuation and capitalizes the second part if in Latin script.
    /// Supports English, Khmer, and Chinese.
    /// </summary>
    public static (string Part1, string Part2) SplitOriginalDialogueSenseToSense(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, string.Empty);

        int splitIdx = FindSenseSplitPoint(text);
        if (splitIdx <= 0 || splitIdx >= text.Length)
        {
            // Fallback: simple word space near middle
            int midSpace = text.Length / 2;
            int spaceIdx = text.IndexOf(' ', midSpace);
            if (spaceIdx < 0)
                spaceIdx = text.LastIndexOf(' ', midSpace);

            if (spaceIdx > 0)
                splitIdx = spaceIdx;
            else
                return (text.Trim(), string.Empty);
        }

        string part1 = text[..splitIdx].Trim();
        string part2 = text[splitIdx..].Trim();

        // Clean trailing punctuation on Part 1 if it ends with dangling comma or hyphen
        if (
            part1.EndsWith(',')
            || part1.EndsWith('-')
            || part1.EndsWith('–')
            || part1.EndsWith('—')
            || part1.EndsWith('，')
            || part1.EndsWith('、')
        )
        {
            part1 = part1[..^1].Trim();
        }

        // Clean leading punctuation on Part 2
        if (
            part2.StartsWith('，')
            || part2.StartsWith('、')
            || part2.StartsWith(',')
            || part2.StartsWith('-')
            || part2.StartsWith('–')
            || part2.StartsWith('—')
        )
        {
            part2 = part2[1..].Trim();
        }

        // Capitalize Part 2 if it's Latin script and starts with lowercase
        if (
            part2.Length > 0
            && char.IsLower(part2[0])
            && part2.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
        )
        {
            part2 = char.ToUpper(part2[0], CultureInfo.InvariantCulture) + part2[1..];
        }

        // Ensure ending punctuation on both parts for natural speech intonation
        if (part1.Length >= 2 && !EndsWithSentencePunctuation(part1))
        {
            part1 += IsCjkText(part1) ? "，" : ",";
        }

        if (part2.Length >= 2 && !EndsWithSentencePunctuation(part2))
        {
            if (IsCjkText(part2))
                part2 += "。";
            else if (IsKhmerText(part2))
                part2 += "។";
            else
                part2 += ".";
        }

        return (part1, part2);
    }

    /// <summary>
    /// Splits Khmer dialogue into two matching semantic parts.
    /// Utilizes Khmer clause markers (ហើយ, ប៉ុន្តែ, ពីព្រោះ, ។, spaces) to maintain natural Cambodian movie phrasing.
    /// </summary>
    public static (string Part1, string Part2) SplitKhmerDialogueSenseToSense(string khmerText)
    {
        if (string.IsNullOrWhiteSpace(khmerText))
            return (string.Empty, string.Empty);

        int splitIdx = FindSenseSplitPoint(khmerText);
        if (splitIdx <= 0 || splitIdx >= khmerText.Length)
        {
            int mid = khmerText.Length / 2;
            int sp = khmerText.IndexOf(' ', mid);
            if (sp < 0)
                sp = khmerText.LastIndexOf(' ', mid);

            splitIdx = sp > 0 ? sp : mid;
        }

        string part1 = khmerText[..splitIdx].Trim();
        string part2 = khmerText[splitIdx..].Trim();

        // Ensure proper Khmer terminators
        if (
            part1.Length >= 2
            && !part1.EndsWith('។')
            && !part1.EndsWith('?')
            && !part1.EndsWith('!')
            && !part1.EndsWith(',')
        )
        {
            part1 += " ";
        }

        if (
            part2.Length >= 2
            && !part2.EndsWith('។')
            && !part2.EndsWith('?')
            && !part2.EndsWith('!')
        )
        {
            part2 += "។";
        }

        return (part1.Trim(), part2.Trim());
    }

    /// <summary>
    /// Splits a SubtitleSegment into two matching segments:
    /// - Splits OriginalText at the natural sense/clause boundary
    /// - Splits KhmerText to match the semantic clauses
    /// - Adjusts timestamps proportionally according to speech/character weight rather than a rigid 50/50 cut
    /// - Resets audio cache to prevent playing old rendered audio for half-length lines.
    /// </summary>
    public static (SubtitleSegment First, SubtitleSegment Second) SplitSegmentSenseToSense(
        SubtitleSegment target,
        string? sourceLang = null
    )
    {
        ArgumentNullException.ThrowIfNull(target);

        var (part1Orig, part2Orig) = SplitOriginalDialogueSenseToSense(target.OriginalText);

        string part1Khmer = string.Empty;
        string part2Khmer = string.Empty;

        if (!string.IsNullOrWhiteSpace(target.KhmerText))
        {
            (part1Khmer, part2Khmer) = SplitKhmerDialogueSenseToSense(target.KhmerText);
        }

        // Calculate proportional time split based on relative text weight
        int len1 = Math.Max(1, part1Orig.Length);
        int len2 = Math.Max(1, part2Orig.Length);
        double ratio = (double)len1 / (len1 + len2);
        ratio = Math.Clamp(ratio, 0.25, 0.75);

        var originalEnd = target.EndTime;
        var midpoint = target.StartTime + TimeSpan.FromSeconds(target.DurationSeconds * ratio);

        // Update target (first segment)
        target.EndTime = midpoint;
        target.OriginalText = part1Orig;
        target.KhmerText = part1Khmer;
        target.AudioClipPath = null;
        target.AudioDurationSeconds = 0;

        // Create second segment matching speaker, character, and emotion
        var second = new SubtitleSegment
        {
            StartTime = midpoint,
            EndTime = originalEnd,
            OriginalText = part2Orig,
            KhmerText = part2Khmer,
            CharacterId = target.CharacterId,
            SpeakerName = target.SpeakerName,
            SpeakerColor = target.SpeakerColor,
            Emotion = target.Emotion,
            AudioClipPath = null,
            AudioDurationSeconds = 0,
        };

        return (target, second);
    }

    /// <summary>
    /// Merges two segments into a single cohesive dialogue sentence:
    /// - Smoothly joins OriginalText with language-aware spacing and punctuation cleanup
    /// - Joins and polishes KhmerText into natural Cambodian cinematic dialogue
    /// - Extends timestamp to cover both scenes
    /// - Resets audio cache to trigger fresh synthesis of the combined complete line.
    /// </summary>
    public static SubtitleSegment MergeSegmentsSenseToSense(
        SubtitleSegment first,
        SubtitleSegment second
    )
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        first.EndTime = second.EndTime;

        // Join OriginalText
        var t1 = first.OriginalText?.Trim() ?? string.Empty;
        var t2 = second.OriginalText?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(t2))
        {
            if (string.IsNullOrWhiteSpace(t1))
            {
                first.OriginalText = t2;
            }
            else
            {
                // Remove trailing hyphen or comma before combining if continuing sentence
                if (t1.EndsWith('-') || t1.EndsWith('–') || t1.EndsWith('—'))
                {
                    t1 = t1[..^1].Trim();
                }

                // If t1 did not end with sentence terminator and t2 is Latin, lowercase t2's first letter (unless "I")
                if (
                    !IsCompleteSentence(t1)
                    && t2.Length > 0
                    && char.IsUpper(t2[0])
                    && !t2.StartsWith("I ", StringComparison.Ordinal)
                    && !t2.StartsWith("I'", StringComparison.Ordinal)
                )
                {
                    t2 = char.ToLower(t2[0], CultureInfo.InvariantCulture) + t2[1..];
                }

                if (IsCjkText(t1) || IsKhmerText(t1) || IsCjkText(t2))
                {
                    if (IsCjkText(t1) && t1.EndsWith('。'))
                    {
                        t1 = t1[..^1] + "，";
                    }
                    first.OriginalText = $"{t1}{t2}".Trim();
                }
                else
                {
                    first.OriginalText = $"{t1} {t2}".Trim();
                }
            }
        }

        // Join KhmerText
        var k1 = first.KhmerText?.Trim() ?? string.Empty;
        var k2 = second.KhmerText?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(k2))
        {
            if (string.IsNullOrWhiteSpace(k1))
            {
                first.KhmerText = k2;
            }
            else
            {
                // If k1 ends with Khmer full stop '។', replace with space to continue combined thought
                if (k1.EndsWith('។'))
                {
                    k1 = k1[..^1].Trim();
                }

                var combinedKhmer = $"{k1} {k2}".Trim();
                first.KhmerText = SubtitleTranslationService.PolishKhmerDialogue(combinedKhmer);
            }
        }

        // Reset audio clip to re-synthesize full line
        first.AudioClipPath = null;
        first.AudioDurationSeconds = 0;

        return first;
    }

    /// <summary>
    /// Reconstructs fragmented subtitle segments (e.g. from Whisper transcription) into complete,
    /// natural dialogue sentences. Preserves speaker continuity, respects natural speech pauses,
    /// and enforces maximum duration bounds.
    /// </summary>
    public static List<SubtitleSegment> ReconstructSentences(
        IEnumerable<SubtitleSegment> segments,
        double maxGapSeconds = 2.0,
        double maxDurationSeconds = 12.0
    )
    {
        var inputList = segments.OrderBy(s => s.StartTime).ToList();
        if (inputList.Count < 2)
            return inputList;

        var result = new List<SubtitleSegment>();
        var cur = inputList[0];

        for (int i = 1; i < inputList.Count; i++)
        {
            var next = inputList[i];

            double gap =
                (next.StartTime > cur.EndTime) ? (next.StartTime - cur.EndTime).TotalSeconds : 0;
            double combinedDuration = (next.EndTime - cur.StartTime).TotalSeconds;

            bool sameSpeaker;
            if (cur.CharacterId.HasValue || next.CharacterId.HasValue)
            {
                sameSpeaker = cur.CharacterId == next.CharacterId;
            }
            else if (
                !string.IsNullOrWhiteSpace(cur.SpeakerName)
                || !string.IsNullOrWhiteSpace(next.SpeakerName)
            )
            {
                sameSpeaker = string.Equals(
                    cur.SpeakerName,
                    next.SpeakerName,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            else
            {
                // When character IDs are unassigned, only merge if gap is a tight intra-sentence pause (< 0.6s)
                sameSpeaker = gap <= 0.6;
            }

            bool curComplete = IsCompleteSentence(cur.OriginalText);

            // Merge if current line is an incomplete sentence fragment, spoken by the same character,
            // with a natural speech pause gap, and not exceeding max scene duration
            if (
                !curComplete
                && sameSpeaker
                && gap <= maxGapSeconds
                && combinedDuration <= maxDurationSeconds
            )
            {
                cur = MergeSegmentsSenseToSense(cur, next);
            }
            else
            {
                // Finalize current segment's original text
                cur.OriginalText = ImproveOriginalDialogue(cur.OriginalText);
                result.Add(cur);
                cur = next;
            }
        }

        cur.OriginalText = ImproveOriginalDialogue(cur.OriginalText);
        result.Add(cur);

        // Re-index sequentially
        for (int idx = 0; idx < result.Count; idx++)
        {
            result[idx].Index = idx + 1;
        }

        return result;
    }

    private static bool EndsWithSentencePunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        char last = text[^1];
        return last
            is '.'
                or '!'
                or '?'
                or ':'
                or ';'
                or '~'
                or '…'
                or '。'
                or '！'
                or '？'
                or 'ฯ'
                or '។'
                or '।'
                or '؟'
                or '"'
                or '\''
                or '”'
                or '’'
                or ')'
                or ']'
                or '}';
    }

    private static bool IsCjkText(string text)
    {
        return text.Any(c => (c >= '\u4e00' && c <= '\u9fff') || (c >= '\u3040' && c <= '\u30ff'));
    }

    private static bool IsKhmerText(string text)
    {
        return text.Any(c => c >= '\u1780' && c <= '\u17ff');
    }
}
