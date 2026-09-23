using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public record GenderDetectionResult(
    string Gender, // "Male", "Female", or "Child"
    double MedianPitchHz,
    double Confidence, // 0.0 to 1.0
    string Source // "Acoustic Pitch", "Text Cues", "Temporal Continuity", etc.
);

public static class VoiceGenderDetector
{
    public const string GenderMale = "Male";
    public const string GenderFemale = "Female";
    public const string GenderChild = "Child";

    // Standard human fundamental frequency (F0) thresholds (Hz)
    // Adult Male: typically 85 Hz - 160 Hz (average ~120 Hz)
    // Adult Female: typically 165 Hz - 265 Hz (average ~210 Hz)
    // Child / High Voice: typically 265 Hz - 450+ Hz (average ~330 Hz)
    public const double GenderPitchThresholdHz = 165.0;
    public const double ChildPitchThresholdHz = 265.0;

    // Speaker Prefix Regex matching [Name], [Name]:, (Name), (Name):, {Name}, 【Name】, Name:, Name -, Name ៖ (Khmer colon), **Name:**, "Name:"
    public static readonly Regex SpeakerPrefixRegex = new(
        @"^[\s""'\*]*(?:(?:\[(?<name>[^\]]+)\]|\((?<name>[^\)]+)\)|\{(?<name>[^\}]+)\}|【(?<name>[^】]+)】)\s*(?:[:：\-—–]|៖)?|(?<name>[A-Za-z0-9_\u1780-\u17FF\s\.\-]{2,30})\s*(?:[:：\-—–]|៖))\s*[""'\*]*(?<text>.*)$",
        RegexOptions.Compiled
    );

    // 1. Explicit Speaker Tags & Prefixes (Highest confidence indicator in scripts)
    private static readonly Regex ChildSpeakerTagRegex = new(
        @"^[\s\(\[]*(?:child|kid|kids|baby|little\s+girl|little\s+boy|daughter|son|toddler|actor\s*c|tommy|billy|timmy|lily|ក្មេង|កូន|កុមារ|កុមារី|កូនតូច|ចៅ|ចៅប្រុស|ចៅស្រី)[\s\)\]]*[:\-—–៖]|\[(?:child|kid|kids|baby|girl|boy|toddler|ក្មេង|កុមារ)\]|\((?:child|kid|kids|baby|girl|boy|toddler|ក្មេង|កុមារ)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex FemaleSpeakerTagRegex = new(
        @"^[\s\(\[]*(?:female|woman|girl|heroine|mother|mom|mommy|mama|sister|wife|queen|princess|actress|lady|madam|madame|aunt|grandma|grandmother|mary|elena|sarah|lisa|anna|alice|emma|olivia|sophia|chloe|grace|jessica|rachel|emily|lucy|mia|lily|hannah|victoria|diana|bella|clara|rose|eva|sophie|susan|jenny|linda|sreymom|sophea|bopha|theary|chinda|rachana|sokha|thida|actor\s*b|នាងខ្ញុំ|កញ្ញា|លោកស្រី|អ្នកស្រី|អ្នកមីង|យាយ|ម៉ែ|ម្តាយ|បងស្រី|ប្អូនស្រី|កូនស្រី|តួស្រី)[\s\)\]]*[:\-—–៖]|\[(?:female|heroine|woman|actress|queen|princess|actor\s*b)\]|\((?:female|heroine|woman|actress|queen|princess|actor\s*b)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex MaleSpeakerTagRegex = new(
        @"^[\s\(\[]*(?:male|man|boy|guy|hero|father|dad|daddy|papa|brother|husband|king|prince|actor|actor\s*a|john|david|michael|jack|james|robert|william|thomas|charles|daniel|matthew|anthony|mark|paul|steven|andrew|joshua|kevin|brian|george|edward|ronald|timothy|jason|ryan|gary|nicholas|eric|stephen|larry|justin|scott|frank|alexander|peter|piseth|dara|sok|vibol|rithy|vannak|bora|sir|mr|lord|master|doctor|captain|villain|ខ្ញុំបាទ|លោកពូ|តា|ឪពុក|ពុក|បងប្រុស|ប្អូនប្រុស|កូនប្រុស|លោកម្ចាស់|ព្រះករុណា|តួប្រុស)[\s\)\]]*[:\-—–៖]|\[(?:male|hero|man|king|prince|actor\s*a)\]|\((?:male|hero|man|king|prince|actor\s*a)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // 2. First-Person Gender Self-References (Strong speaker identification)
    private static readonly Regex FemaleSelfRefRegex = new(
        @"\b(i am|i'm)\s+(a\s+|the\s+)?(woman|girl|lady|mother|mom|wife|queen|princess|heroine|sister)\b|នាងខ្ញុំ|ខ្ញុំជាម្តាយ|ខ្ញុំជាម៉ាក់|ខ្ញុំជាកូនស្រី",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex MaleSelfRefRegex = new(
        @"\b(i am|i'm)\s+(a\s+|the\s+)?(man|guy|boy|father|dad|husband|king|prince|hero|brother)\b|ខ្ញុំបាទ|ខ្ញុំជាឪពុក|ខ្ញុំជាពុក|ខ្ញុំជាកូនប្រុស",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ChildSelfRefRegex = new(
        @"\b(i am|i'm)\s+(a\s+|the\s+)?(child|kid|baby|little\s+girl|little\s+boy)\b|ខ្ញុំជាកូនក្មេង|ខ្ញុំនៅក្មេង",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // 3. Dialogue Keywords & Vocatives (Low weight, avoids 3rd-person pronouns to prevent false triggers)
    private static readonly Regex ChildDialogueRegex = new(
        @"\b(little\s+girl|little\s+boy|toddler|childhood)\b|ក្មេងស្រី|ក្មេងប្រុស|កុមារ|កុមារី",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex FemaleDialogueRegex = new(
        @"\b(mommy|mother|mom|mama|mrs|miss|ms|madam|madame|wife|queen|princess|heroine|sreymom)\b|កញ្ញា|លោកស្រី|អ្នកស្រី|អ្នកមីង|យាយ|ម៉ែ|ម្តាយ|អូនសម្លាញ់",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex MaleDialogueRegex = new(
        @"\b(father|dad|papa|husband|king|mr|sir|uncle|prince|hero|piseth)\b|លោកពូ|តា|ឪពុក|ពុក|បុរស",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// Extracts a speaker name and stripped dialogue text from prefix patterns like "John: Hello", "[Heroine] Watch out!", etc.
    /// Returns true if a valid speaker prefix was found (excluding stage directions like "[whispering]").
    /// </summary>
    public static bool TryExtractSpeakerPrefix(
        string? rawText,
        out string speakerName,
        out string dialogueText
    )
    {
        speakerName = string.Empty;
        dialogueText = rawText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        var match = SpeakerPrefixRegex.Match(rawText.Trim());
        if (!match.Success)
            return false;

        var nameCandidate = match.Groups["name"].Value.Trim();
        if (nameCandidate.Length < 2 || nameCandidate.Length > 35)
            return false;

        // Verify it is not an acting stage direction / emotion cue like [whispering] or [screams]
        if (ActorEmotionEngine.IsStageDirection(nameCandidate))
            return false;

        speakerName = nameCandidate;
        dialogueText = match.Groups["text"].Value.Trim();
        return true;
    }

    /// <summary>
    /// Determines gender ("Male", "Female", or "Child") directly from a character/speaker name.
    /// </summary>
    public static string? DetectGenderFromName(string? speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return null;

        var name = speakerName.Trim();

        // 1. Child
        if (
            Regex.IsMatch(
                name,
                @"\b(child|kid|kids|baby|toddler|little\s+boy|little\s+girl|son|daughter|pupil|student|actor\s*c|tommy|billy|timmy|lily|ក្មេង|កុមារ|កុមារី|កូនតូច|ចៅ|ចៅប្រុស|ចៅស្រី)\b",
                RegexOptions.IgnoreCase
            )
        )
        {
            return GenderChild;
        }

        // 2. Female
        if (
            Regex.IsMatch(
                name,
                @"\b(female|woman|girl|heroine|mother|mom|mommy|mama|sister|wife|queen|princess|actress|lady|madam|madame|aunt|grandma|grandmother|mary|elena|sarah|lisa|anna|alice|emma|olivia|sophia|chloe|grace|jessica|rachel|emily|lucy|mia|lily|hannah|victoria|diana|bella|clara|rose|eva|sophie|susan|jenny|linda|sreymom|sophea|bopha|theary|chinda|rachana|sokha|thida|actor\s*b|នាងខ្ញុំ|កញ្ញា|លោកស្រី|អ្នកស្រី|អ្នកមីង|យាយ|ម៉ែ|ម្តាយ|បងស្រី|ប្អូនស្រី|កូនស្រី|តួស្រី)\b",
                RegexOptions.IgnoreCase
            )
        )
        {
            return GenderFemale;
        }

        // 3. Male
        if (
            Regex.IsMatch(
                name,
                @"\b(male|man|boy|guy|hero|father|dad|daddy|papa|brother|husband|king|prince|actor|actor\s*a|john|david|michael|jack|james|robert|william|thomas|charles|daniel|matthew|anthony|mark|paul|steven|andrew|joshua|kevin|brian|george|edward|ronald|timothy|jason|ryan|gary|nicholas|eric|stephen|larry|justin|scott|frank|alexander|peter|piseth|dara|sok|vibol|rithy|vannak|bora|sir|mr|lord|master|doctor|captain|villain|ខ្ញុំបាទ|លោកពូ|តា|ឪពុក|ពុក|បងប្រុស|ប្អូនប្រុស|កូនប្រុស|លោកម្ចាស់|ព្រះករុណា|តួប្រុស)\b",
                RegexOptions.IgnoreCase
            )
        )
        {
            return GenderMale;
        }

        return null;
    }

    /// <summary>
    /// Infers the cinema tone archetype (Hero, Villain, Elder, Youth, Action, Narrator, Romantic, Comic) from character name.
    /// </summary>
    public static string DetectToneArchetypeFromName(
        string? speakerName,
        string? detectedGender = null
    )
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return detectedGender == GenderFemale ? "Hero" : "Hero";

        var name = speakerName.Trim();

        if (
            Regex.IsMatch(
                name,
                @"\b(villain|antagonist|dark|evil|monster|demon|shadow|boss|enemy|assassin|killer|fiend)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Villain";

        if (
            Regex.IsMatch(
                name,
                @"\b(child|kid|kids|baby|toddler|little|son|daughter|pupil|youth|boy|girl|actor\s*c|ក្មេង|កុមារ|កូនតូច|ចៅ)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Youth";

        if (
            Regex.IsMatch(
                name,
                @"\b(elder|old|grandpa|grandma|grandfather|grandmother|master|mentor|guru|monk|priest|sage|veteran|តា|យាយ|ព្រះតេជគុណ)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Elder";

        if (
            Regex.IsMatch(
                name,
                @"\b(narrator|voiceover|announcer|commentary|intro|host|storyteller|trailer|speaker|voice\s*over)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Narrator";

        if (
            Regex.IsMatch(
                name,
                @"\b(action|warrior|soldier|general|commander|police|agent|fighter|guard|captain|knight|heroic)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Action";

        if (
            Regex.IsMatch(
                name,
                @"\b(romantic|lover|sweetheart|wife|husband|couple|bride|groom|darling|honey)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Romantic";

        if (
            Regex.IsMatch(
                name,
                @"\b(comic|funny|clown|jester|friend|sidekick|humor)\b",
                RegexOptions.IgnoreCase
            )
        )
            return "Comic";

        return "Hero";
    }

    /// <summary>
    /// Scans dialogue text for gender and child cues using weighted scoring.
    /// Prioritizes speaker tags and 1st-person self-references, avoiding 3rd-person pronoun confusion.
    /// </summary>
    public static string? DetectGenderFromText(string? originalText, string? khmerText)
    {
        var text = $"{originalText ?? string.Empty} {khmerText ?? string.Empty}".Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // 1. Check if line starts with an explicit speaker prefix tag
        if (
            TryExtractSpeakerPrefix(originalText, out var origSpeaker, out _)
            || TryExtractSpeakerPrefix(khmerText, out origSpeaker, out _)
        )
        {
            var genderFromName = DetectGenderFromName(origSpeaker);
            if (!string.IsNullOrWhiteSpace(genderFromName))
                return genderFromName;
        }

        // 2. Explicit speaker prefixes and bracket tags (Weight: 10)
        int childScore = ChildSpeakerTagRegex.IsMatch(text) ? 10 : 0;
        int femaleScore = FemaleSpeakerTagRegex.IsMatch(text) ? 10 : 0;
        int maleScore = MaleSpeakerTagRegex.IsMatch(text) ? 10 : 0;

        // 3. First-person gender self-references (Weight: 5)
        childScore += ChildSelfRefRegex.Matches(text).Count * 5;
        femaleScore += FemaleSelfRefRegex.Matches(text).Count * 5;
        maleScore += MaleSelfRefRegex.Matches(text).Count * 5;

        // 4. Distinctive vocatives and context cues (Weight: 1)
        childScore += ChildDialogueRegex.Matches(text).Count;
        femaleScore += FemaleDialogueRegex.Matches(text).Count;
        maleScore += MaleDialogueRegex.Matches(text).Count;

        if (childScore > femaleScore && childScore > maleScore)
            return GenderChild;
        if (femaleScore > maleScore && femaleScore >= childScore)
            return GenderFemale;
        if (maleScore > femaleScore && maleScore >= childScore)
            return GenderMale;

        return null;
    }

    /// <summary>
    /// Calculates fundamental frequency (F0) pitch of 16kHz 16-bit mono PCM audio using the YIN algorithm:
    /// 1. Difference Function
    /// 2. Cumulative Mean Normalized Difference Function (CMNDF)
    /// 3. Absolute thresholding (first dip below 0.18) with octave-halving protection
    /// 4. Sub-sample parabolic interpolation
    /// </summary>
    public static double CalculateMedianPitchHz(short[] samples, int sampleRate = 16000)
    {
        // Require at least 60ms of audio
        if (samples.Length < sampleRate * 0.06)
            return 0;

        int windowSize = (int)(sampleRate * 0.025); // 25ms window (400 samples at 16kHz)
        int hopSize = (int)(sampleRate * 0.015); // 15ms hop (240 samples at 16kHz)
        int minLag = (int)Math.Ceiling(sampleRate / 480.0); // ~480 Hz upper limit (~34 samples)
        int maxLag = (int)Math.Floor(sampleRate / 70.0); // ~70 Hz lower limit (~228 samples)
        int frameSizeNeeded = windowSize + maxLag;

        if (samples.Length < frameSizeNeeded)
            return 0;

        var pitchValues = new List<double>();
        const double yinThreshold = 0.18;

        for (int offset = 0; offset + frameSizeNeeded <= samples.Length; offset += hopSize)
        {
            // RMS energy check: skip silence and quiet background noise
            double sumSq = 0;
            for (int i = 0; i < windowSize; i++)
            {
                double s = samples[offset + i];
                sumSq += s * s;
            }
            double rms = Math.Sqrt(sumSq / windowSize);
            if (rms < 300.0) // Low energy threshold for 16-bit audio
                continue;

            // Step 1: Difference function d[tau]
            var d = new double[maxLag + 1];
            d[0] = 0;
            for (int tau = 1; tau <= maxLag; tau++)
            {
                double sumDiff = 0;
                for (int j = 0; j < windowSize; j++)
                {
                    double diff = samples[offset + j] - samples[offset + j + tau];
                    sumDiff += diff * diff;
                }
                d[tau] = sumDiff;
            }

            // Step 2: Cumulative Mean Normalized Difference Function (CMNDF)
            var dPrime = new double[maxLag + 1];
            dPrime[0] = 1.0;
            double runningSum = 0;
            for (int tau = 1; tau <= maxLag; tau++)
            {
                runningSum += d[tau];
                dPrime[tau] = runningSum > 0 ? (d[tau] * tau) / runningSum : 1.0;
            }

            // Step 3: Absolute Thresholding (Find first dip below threshold)
            int tauEst = -1;
            for (int tau = minLag; tau <= maxLag; tau++)
            {
                if (dPrime[tau] < yinThreshold)
                {
                    // Follow local valley to its bottom
                    while (tau + 1 <= maxLag && dPrime[tau + 1] < dPrime[tau])
                    {
                        tau++;
                    }
                    tauEst = tau;
                    break;
                }
            }

            // Fallback: If no value fell below threshold, find global minimum
            if (tauEst == -1)
            {
                double minVal = double.MaxValue;
                int minTau = -1;
                for (int tau = minLag; tau <= maxLag; tau++)
                {
                    if (dPrime[tau] < minVal)
                    {
                        minVal = dPrime[tau];
                        minTau = tau;
                    }
                }

                // Accept only if minimum shows strong periodic harmonic structure (< 0.38)
                if (minVal < 0.38 && minTau > 0)
                {
                    tauEst = minTau;
                }
            }

            if (tauEst <= 0)
                continue;

            // Step 4: Octave-Halving / Subharmonic Protection
            // If tauEst is large (low pitch), verify if half-period (double pitch) has a significant dip
            if (tauEst >= 2 * minLag)
            {
                int halfTau = (int)Math.Round(tauEst / 2.0);
                int searchStart = Math.Max(minLag, halfTau - 2);
                int searchEnd = Math.Min(maxLag, halfTau + 2);
                double bestHalfVal = double.MaxValue;
                int bestHalfTau = -1;

                for (int t = searchStart; t <= searchEnd; t++)
                {
                    if (dPrime[t] < bestHalfVal)
                    {
                        bestHalfVal = dPrime[t];
                        bestHalfTau = t;
                    }
                }

                if (bestHalfTau > 0 && bestHalfVal < 0.28)
                {
                    tauEst = bestHalfTau;
                }
            }

            // Step 5: Sub-sample Parabolic Interpolation for exact pitch precision
            double preciseTau = tauEst;
            if (tauEst > minLag && tauEst < maxLag)
            {
                double y0 = dPrime[tauEst - 1];
                double y1 = dPrime[tauEst];
                double y2 = dPrime[tauEst + 1];

                double denom = 2.0 * (y0 - 2.0 * y1 + y2);
                if (Math.Abs(denom) > 1e-9)
                {
                    double delta = (y0 - y2) / denom;
                    if (Math.Abs(delta) <= 1.0)
                    {
                        preciseTau = tauEst + delta;
                    }
                }
            }

            if (preciseTau > 0)
            {
                double pitch = (double)sampleRate / preciseTau;
                if (pitch >= 70.0 && pitch <= 480.0)
                {
                    pitchValues.Add(pitch);
                }
            }
        }

        if (pitchValues.Count == 0)
            return 0;

        pitchValues.Sort();
        return pitchValues[pitchValues.Count / 2];
    }

    /// <summary>
    /// Analyzes an entire media file and classifies male/female/child speaking turns across all segments.
    /// Fast: extracts a 16kHz mono scratch WAV pre-filtered with vocal bandpass (80-3400 Hz),
    /// uses YIN F0 pitch estimation, and applies conversational turn & temporal continuity smoothing.
    /// </summary>
    public static Task<Dictionary<int, GenderDetectionResult>> DetectGendersForSegmentsAsync(
        string ffmpegPath,
        string mediaFilePath,
        IReadOnlyList<SubtitleSegment> segments,
        CancellationToken cancellationToken
    ) =>
        DetectGendersForSegmentsAsync(ffmpegPath, mediaFilePath, segments, null, cancellationToken);

    public static async Task<Dictionary<int, GenderDetectionResult>> DetectGendersForSegmentsAsync(
        string ffmpegPath,
        string mediaFilePath,
        IReadOnlyList<SubtitleSegment> segments,
        Action<int, int, string>? progressCallback = null,
        CancellationToken cancellationToken = default
    )
    {
        var results = new Dictionary<int, GenderDetectionResult>();
        if (segments.Count == 0)
            return results;

        bool hasAudio =
            !string.IsNullOrWhiteSpace(mediaFilePath)
            && File.Exists(mediaFilePath)
            && !string.IsNullOrWhiteSpace(ffmpegPath)
            && File.Exists(ffmpegPath);

        if (!hasAudio)
        {
            // Intelligent text-based analysis with natural conversational turn & continuity smoothing
            string lastGender = GenderMale;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var textGender = DetectGenderFromText(seg.OriginalText, seg.KhmerText);
                string chosen;
                string source;
                double conf;

                if (!string.IsNullOrWhiteSpace(textGender))
                {
                    chosen = textGender;
                    source = "Text Cues";
                    conf = 0.85;
                }
                else
                {
                    bool isTurn = false;
                    if (i > 0)
                    {
                        var prevSeg = segments[i - 1];
                        double gapSec = (seg.StartTime - prevSeg.EndTime).TotalSeconds;
                        string text = (
                            seg.OriginalText ?? seg.KhmerText ?? string.Empty
                        ).TrimStart();
                        bool hasDash = text.StartsWith("-") || text.StartsWith("—");

                        // Dialogue turn occurs upon explicit subtitle dash or natural conversational pause (>= 1.5s)
                        if (hasDash || gapSec >= 1.5)
                        {
                            isTurn = true;
                        }
                    }

                    if (isTurn)
                    {
                        chosen = (lastGender == GenderMale) ? GenderFemale : GenderMale;
                        source = "Dialogue Turn";
                        conf = 0.70;
                    }
                    else
                    {
                        chosen = lastGender;
                        source = i == 0 ? "Default" : "Temporal Continuity";
                        conf = 0.65;
                    }
                }

                lastGender = chosen;
                results[seg.Index] = new GenderDetectionResult(chosen, 0, conf, source);
            }
            return results;
        }

        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "855Media_Pitch_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempDir);
        var tempPcmWav = Path.Combine(tempDir, "audio_16k.wav");

        try
        {
            progressCallback?.Invoke(
                0,
                segments.Count,
                "Extracting speech audio for pitch analysis..."
            );

            // 1. Extract audio downsampled to 16kHz mono PCM with vocal speech bandpass filter (80 - 3400 Hz)
            // Limit extraction duration up to the last subtitle line (+3s) to avoid decoding entire multi-hour videos
            double maxEndSec = segments.Max(s => s.EndTime.TotalSeconds) + 3.0;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
            };

            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-nostats");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(mediaFilePath);
            if (maxEndSec > 0)
            {
                psi.ArgumentList.Add("-t");
                psi.ArgumentList.Add(
                    maxEndSec.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                );
            }
            psi.ArgumentList.Add("-vn");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add("highpass=f=80,lowpass=f=3400");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("16000");
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("pcm_s16le");
            psi.ArgumentList.Add(tempPcmWav);

            using (var proc = Process.Start(psi))
            {
                if (proc != null)
                {
                    ChildProcessTracker.Track(proc);
                    // Asynchronously drain standard error to completely prevent pipe buffer deadlocks
                    var errReadTask = proc.StandardError.ReadToEndAsync(cancellationToken);
                    await proc.WaitForExitWithCancellationAsync(cancellationToken);
                    await errReadTask;
                }
            }

            if (!File.Exists(tempPcmWav) || new FileInfo(tempPcmWav).Length < 44)
            {
                // Fallback to text analysis if audio extraction failed
                foreach (var seg in segments)
                {
                    var textGender = DetectGenderFromText(seg.OriginalText, seg.KhmerText);
                    results[seg.Index] = new GenderDetectionResult(
                        textGender ?? GenderMale,
                        0,
                        textGender != null ? 0.80 : 0.50,
                        textGender != null ? "Text Cues" : "Default"
                    );
                }
                return results;
            }

            using var fs = File.OpenRead(tempPcmWav);
            int sampleRate = 16000;
            int bytesPerSample = 2; // 16-bit
            int bytesPerSec = sampleRate * bytesPerSample;
            long dataStartOffset = 44; // Standard WAV header size

            // Pass 1: Acoustic Pitch Analysis & Text Verification for each segment
            int processedSegCount = 0;
            foreach (var seg in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double startSec = Math.Max(0, seg.StartTime.TotalSeconds);
                double durSec = Math.Min(seg.Duration.TotalSeconds, 4.0); // Analyze up to 4s of dialogue

                long byteOffset = dataStartOffset + (long)(startSec * bytesPerSec);
                int byteLength = (int)(durSec * bytesPerSec);

                double pitchHz = 0;

                if (byteOffset < fs.Length && byteLength > 0)
                {
                    int actualLength = (int)Math.Min(byteLength, fs.Length - byteOffset);
                    var buffer = new byte[actualLength];

                    fs.Seek(byteOffset, SeekOrigin.Begin);
                    int read = fs.Read(buffer, 0, actualLength);

                    int sampleCount = read / 2;
                    var samples = new short[sampleCount];
                    for (int s = 0; s < sampleCount; s++)
                    {
                        samples[s] = BitConverter.ToInt16(buffer, s * 2);
                    }

                    pitchHz = CalculateMedianPitchHz(samples, sampleRate);
                }

                var textGender = DetectGenderFromText(seg.OriginalText, seg.KhmerText);

                string finalGender;
                double confidence;
                string source;

                // 1. Explicit text cues (Speaker tags, 1st-person self-references) have highest authority
                if (!string.IsNullOrWhiteSpace(textGender))
                {
                    finalGender = textGender;
                    if (pitchHz > 0)
                    {
                        bool agrees =
                            (textGender == GenderChild && pitchHz >= ChildPitchThresholdHz)
                            || (
                                textGender == GenderFemale
                                && pitchHz >= GenderPitchThresholdHz
                                && pitchHz < ChildPitchThresholdHz
                            )
                            || (textGender == GenderMale && pitchHz < GenderPitchThresholdHz);

                        confidence = agrees ? 0.98 : 0.90;
                        source = agrees
                            ? $"Acoustic & Text ({textGender})"
                            : $"Text Cues ({textGender})";
                    }
                    else
                    {
                        confidence = 0.88;
                        source = $"Text Cues ({textGender})";
                    }
                }
                // 2. If no text cues, classify by fundamental acoustic pitch (F0)
                else if (pitchHz >= ChildPitchThresholdHz)
                {
                    finalGender = GenderChild;
                    confidence = Math.Min(
                        0.96,
                        0.80 + ((pitchHz - ChildPitchThresholdHz) / 100.0) * 0.16
                    );
                    source = "Acoustic Pitch (Child/High)";
                }
                else if (pitchHz >= GenderPitchThresholdHz)
                {
                    finalGender = GenderFemale;
                    confidence = Math.Min(
                        0.96,
                        0.75 + ((pitchHz - GenderPitchThresholdHz) / 100.0) * 0.20
                    );
                    source = "Acoustic Pitch (Female)";
                }
                else if (pitchHz > 0)
                {
                    finalGender = GenderMale;
                    confidence = Math.Min(
                        0.96,
                        0.75 + ((GenderPitchThresholdHz - pitchHz) / 80.0) * 0.20
                    );
                    source = "Acoustic Pitch (Male)";
                }
                else
                {
                    finalGender = GenderMale;
                    confidence = 0.50;
                    source = "Default";
                }

                results[seg.Index] = new GenderDetectionResult(
                    finalGender,
                    pitchHz,
                    confidence,
                    source
                );

                processedSegCount++;
                if (processedSegCount % 5 == 0 || processedSegCount == segments.Count)
                {
                    progressCallback?.Invoke(
                        processedSegCount,
                        segments.Count,
                        $"Analyzing voice pitch {processedSegCount}/{segments.Count}..."
                    );
                }
            }

            // Pass 2: Temporal Continuity Smoothing
            // Prevents rapid 1-line voice flickering on consecutive speech turns by the same speaker
            for (int i = 1; i < segments.Count; i++)
            {
                var seg = segments[i];
                var prevSeg = segments[i - 1];

                if (
                    !results.TryGetValue(seg.Index, out var currentRes)
                    || !results.TryGetValue(prevSeg.Index, out var prevRes)
                )
                {
                    continue;
                }

                double gapSec = (seg.StartTime - prevSeg.EndTime).TotalSeconds;
                string currentText = (
                    seg.OriginalText ?? seg.KhmerText ?? string.Empty
                ).TrimStart();
                bool startsWithDialogueDash =
                    currentText.StartsWith("-") || currentText.StartsWith("—");

                // If gap is short (< 1.2s), no dialogue dash, and current segment had weak acoustic pitch (pitch == 0 or low confidence)
                if (gapSec >= 0 && gapSec < 1.2 && !startsWithDialogueDash)
                {
                    if (currentRes.MedianPitchHz == 0 && currentRes.Confidence <= 0.65)
                    {
                        results[seg.Index] = new GenderDetectionResult(
                            prevRes.Gender,
                            currentRes.MedianPitchHz,
                            Math.Max(0.70, prevRes.Confidence * 0.90),
                            "Temporal Continuity"
                        );
                    }
                }
            }
        }
        catch
        {
            // Fallback for any unexpected processing errors
            foreach (var seg in segments)
            {
                if (!results.ContainsKey(seg.Index))
                {
                    var textGender = DetectGenderFromText(seg.OriginalText, seg.KhmerText);
                    results[seg.Index] = new GenderDetectionResult(
                        textGender ?? GenderMale,
                        0,
                        textGender != null ? 0.80 : 0.50,
                        textGender != null ? "Text Cues" : "Default"
                    );
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }

        return results;
    }
}
