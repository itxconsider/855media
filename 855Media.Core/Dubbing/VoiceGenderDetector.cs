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
    string Gender, // "Male" or "Female"
    double MedianPitchHz,
    double Confidence, // 0.0 to 1.0
    string Source // "Acoustic Pitch", "Text Cues", or "Default"
);

public static class VoiceGenderDetector
{
    public const string GenderMale = "Male";
    public const string GenderFemale = "Female";
    public const string GenderChild = "Child";

    // Standard human pitch thresholds (Hz)
    // Adult Male: typically 85 Hz - 160 Hz (average ~120 Hz)
    // Adult Female: typically 165 Hz - 265 Hz (average ~210 Hz)
    // Child / High Voice: typically 265 Hz - 450+ Hz (average ~330 Hz)
    public const double GenderPitchThresholdHz = 165.0;
    public const double ChildPitchThresholdHz = 265.0;

    private static readonly Regex ChildTextRegex = new(
        @"\b(child|kid|kids|baby|toddler|little\s+girl|little\s+boy|daughter|son|childhood)\b|\[(?:child|kid|baby|girl|boy)\]|\((?:child|kid|baby|girl|boy)\)|ក្មេង|កូន|កូនស្រី|កូនប្រុស|កុមារ|កុមារី|ទារក|ក្មេងស្រី|ក្មេងប្រុស|ចៅ",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex FemaleTextRegex = new(
        @"\b(she|her|hers|herself|woman|women|girl|girls|lady|ladies|mother|mom|mama|sister|sisters|wife|queen|miss|mrs|ms|madam|madame|aunt|princess|actress|mary|elena|sarah|lisa|anna|sreymom|female|heroine|honey|darling)\b|\[(?:female|heroine|woman|actor\s*b)\]|\((?:female|heroine|woman|actor\s*b)\)|actor\s*b|នាង|កញ្ញា|លោកស្រី|អ្នកស្រី|អ្នកមីង|យាយ|ម៉ែ|ម្តាយ|បងស្រី|អូនស្រី|អូនសម្លាញ់",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex MaleTextRegex = new(
        @"\b(he|him|his|himself|man|men|boy|boys|guy|guys|gentleman|gentlemen|father|dad|papa|brother|brothers|husband|king|mr|sir|uncle|prince|actor|john|david|michael|jack|piseth|male|hero)\b|\[(?:male|hero|man|actor\s*a)\]|\((?:male|hero|man|actor\s*a)\)|actor\s*a|លោក|លោកពូ|តា|ឪពុក|ពុក|បងប្រុស|អូនប្រុស|បុរស",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// Scans dialogue text for gender and child cues.
    /// </summary>
    public static string? DetectGenderFromText(string? originalText, string? khmerText)
    {
        var text = $"{originalText ?? string.Empty} {khmerText ?? string.Empty}";
        if (string.IsNullOrWhiteSpace(text))
            return null;

        int childHits = ChildTextRegex.Matches(text).Count;
        int femaleHits = FemaleTextRegex.Matches(text).Count;
        int maleHits = MaleTextRegex.Matches(text).Count;

        if (childHits > femaleHits && childHits > maleHits)
            return GenderChild;
        if (femaleHits > maleHits)
            return GenderFemale;
        if (maleHits > femaleHits)
            return GenderMale;

        return null;
    }

    /// <summary>
    /// Calculates fundamental frequency (F0) pitch of 16kHz 16-bit mono PCM audio using normalized autocorrelation.
    /// </summary>
    public static double CalculateMedianPitchHz(short[] samples, int sampleRate = 16000)
    {
        if (samples.Length < sampleRate * 0.1) // Need at least 100ms
            return 0;

        int frameSize = (int)(sampleRate * 0.04); // 40ms frame
        int hopSize = (int)(sampleRate * 0.02); // 20ms hop
        int minLag = (int)(sampleRate / 480.0); // ~480 Hz upper limit for child speech (~33 samples)
        int maxLag = (int)(sampleRate / 70.0); // ~70 Hz lower limit for deep male speech (~228 samples)

        var pitchValues = new List<double>();

        for (int offset = 0; offset + frameSize < samples.Length; offset += hopSize)
        {
            // Energy check to skip silence / unvoiced background
            double energy = 0;
            for (int i = 0; i < frameSize; i++)
            {
                energy += samples[offset + i] * samples[offset + i];
            }
            if (energy < frameSize * 50.0 * 50.0) // Low energy silence
                continue;

            double bestCorr = 0;
            int bestLag = -1;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0;
                double sumSq1 = 0;
                double sumSq2 = 0;

                for (int i = 0; i < frameSize - lag; i++)
                {
                    double s1 = samples[offset + i];
                    double s2 = samples[offset + i + lag];
                    sum += s1 * s2;
                    sumSq1 += s1 * s1;
                    sumSq2 += s2 * s2;
                }

                double denom = Math.Sqrt(sumSq1 * sumSq2);
                if (denom > 1e-6)
                {
                    double normCorr = sum / denom;
                    if (normCorr > bestCorr)
                    {
                        bestCorr = normCorr;
                        bestLag = lag;
                    }
                }
            }

            // Strong periodicity indicates voiced human speech
            if (bestCorr > 0.40 && bestLag > 0)
            {
                double pitch = (double)sampleRate / bestLag;
                if (pitch >= 75 && pitch <= 350)
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
    /// Analyzes an entire media file and classifies male/female speaking turns across all segments.
    /// Fast: extracts a 16kHz mono scratch WAV once, then seeks directly in memory for sub-second classification.
    /// </summary>
    public static async Task<Dictionary<int, GenderDetectionResult>> DetectGendersForSegmentsAsync(
        string ffmpegPath,
        string mediaFilePath,
        IReadOnlyList<SubtitleSegment> segments,
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
            // Intelligent two-pass contextual + conversational turn-taking analysis
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
                    // Alternate conversation turns: if last was Male, this is Female
                    chosen = (lastGender == GenderMale) ? GenderFemale : GenderMale;
                    source = "Dialogue Turn";
                    conf = 0.70;
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
            // 1. Extract audio downsampled to 16kHz mono 16-bit PCM for pitch analysis
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(mediaFilePath);
            psi.ArgumentList.Add("-vn");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("16000");
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("pcm_s16le");
            psi.ArgumentList.Add(tempPcmWav);

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                ChildProcessTracker.Track(proc);
                await proc.WaitForExitWithCancellationAsync(cancellationToken);
            }

            if (!File.Exists(tempPcmWav) || new FileInfo(tempPcmWav).Length < 44)
            {
                // Fallback to text-only analysis if audio extraction failed
                foreach (var seg in segments)
                {
                    var textGender = DetectGenderFromText(seg.OriginalText, seg.KhmerText);
                    results[seg.Index] = new GenderDetectionResult(
                        textGender ?? GenderMale,
                        0,
                        textGender != null ? 0.8 : 0.5,
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
            long maxDataBytes = fs.Length - dataStartOffset;

            foreach (var seg in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double startSec = Math.Max(0, seg.StartTime.TotalSeconds);
                double durSec = Math.Min(seg.Duration.TotalSeconds, 4.0); // Analyze up to 4s of the segment

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

                if (pitchHz >= ChildPitchThresholdHz)
                {
                    finalGender = GenderChild;
                    confidence = Math.Min(
                        0.95,
                        0.75 + ((pitchHz - ChildPitchThresholdHz) / 100.0) * 0.20
                    );
                    source = "Acoustic Pitch (Child/High)";
                }
                else if (pitchHz >= GenderPitchThresholdHz)
                {
                    finalGender = GenderFemale;
                    confidence = Math.Min(
                        0.95,
                        0.70 + ((pitchHz - GenderPitchThresholdHz) / 100.0) * 0.25
                    );
                    source = "Acoustic Pitch (Female)";
                }
                else if (pitchHz > 0)
                {
                    finalGender = GenderMale;
                    confidence = Math.Min(
                        0.95,
                        0.70 + ((GenderPitchThresholdHz - pitchHz) / 80.0) * 0.25
                    );
                    source = "Acoustic Pitch (Male)";
                }
                else if (!string.IsNullOrWhiteSpace(textGender))
                {
                    finalGender = textGender;
                    confidence = 0.80;
                    source = "Text Cues";
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
            }
        }
        catch
        {
            // Fallback for any exception
            foreach (var seg in segments)
            {
                if (!results.ContainsKey(seg.Index))
                {
                    var textGender = DetectGenderFromText(seg.OriginalText, seg.KhmerText);
                    results[seg.Index] = new GenderDetectionResult(
                        textGender ?? GenderMale,
                        0,
                        0.5,
                        "Fallback"
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
