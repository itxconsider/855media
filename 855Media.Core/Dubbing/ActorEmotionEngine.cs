using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Dubbing;

public record ActorEmotionConfig(
    string Name,
    string DisplayName,
    string Icon,
    string Color,
    string TtsRateOffset,
    string TtsPitch,
    string TtsVolume,
    int PitchShiftOffset,
    string FfmpegFilter,
    string Description
);

public static class ActorEmotionEngine
{
    public const string EmotionNormal = "Normal";
    public const string EmotionCrying = "Crying";
    public const string EmotionLaughing = "Laughing";
    public const string EmotionSad = "Sad";
    public const string EmotionHappy = "Happy";
    public const string EmotionAngry = "Angry";
    public const string EmotionWhisper = "Whisper";
    public const string EmotionFear = "Fear";

    private static readonly Dictionary<string, ActorEmotionConfig> Configs = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        [EmotionNormal] = new(
            EmotionNormal,
            "Normal",
            "AccountVoice",
            "#64748B", // Slate Gray
            "+0%",
            "+0Hz",
            "+0%",
            0,
            string.Empty,
            "Standard natural cadence"
        ),
        [EmotionCrying] = new(
            EmotionCrying,
            "Crying",
            "EmoticonCryOutline",
            "#8B5CF6", // Purple / Violet
            "-10%",
            "-12Hz",
            "-10%",
            -2,
            // Vocal cord quiver + dynamic breathy volume fluctuations + softening
            "vibrato=f=4.8:d=0.32,tremolo=f=3.2:d=0.22,lowpass=f=7500,volume=0.92",
            "Emotional sobbing quiver & vocal tremor"
        ),
        [EmotionLaughing] = new(
            EmotionLaughing,
            "Laughing",
            "EmoticonLolOutline",
            "#EAB308", // Golden Yellow
            "+16%",
            "+18Hz",
            "+5%",
            +1,
            // Rhythmic laughter breath bounce & bright treble presence
            "tremolo=f=7.5:d=0.42,treble=g=3,volume=1.05",
            "Buoyant chuckle flutter & smiling bounce"
        ),
        [EmotionSad] = new(
            EmotionSad,
            "Sad",
            "EmoticonSadOutline",
            "#38BDF8", // Sky Blue
            "-12%",
            "-15Hz",
            "-15%",
            -2,
            // Mellow warmth, softened attack, subdued presence
            "equalizer=f=320:t=q:w=1:g=2.5,lowpass=f=6200,volume=0.88",
            "Melancholic slow tempo & subdued tone"
        ),
        [EmotionHappy] = new(
            EmotionHappy,
            "Happy",
            "EmoticonHappyOutline",
            "#10B981", // Emerald Green
            "+14%",
            "+15Hz",
            "+10%",
            +2,
            // Crisp brightness, energetic presence
            "treble=g=3.5,equalizer=f=2500:t=q:w=1.2:g=2,volume=1.05",
            "Bright, joyful & upbeat dynamic range"
        ),
        [EmotionAngry] = new(
            EmotionAngry,
            "Angry",
            "EmoticonAngryOutline",
            "#EF4444", // Red
            "+20%",
            "+12Hz",
            "+25%",
            +3,
            // Heavy compressed punch & high acoustic energy
            "compand=attacks=0.01:decays=0.08:points=-60/-60|-20/-8|0/0,volume=1.22",
            "Punchy compression, fast & aggressive attack"
        ),
        [EmotionWhisper] = new(
            EmotionWhisper,
            "Whisper",
            "VolumeOff",
            "#6366F1", // Indigo
            "-6%",
            "-6Hz",
            "-35%",
            -1,
            // High-pass filter + breathy airy vocal resonance
            "highpass=f=350,equalizer=f=4000:t=q:w=1.5:g=6,volume=0.72",
            "Intimate breathy air & low body resonance"
        ),
        [EmotionFear] = new(
            EmotionFear,
            "Fear",
            "ShieldAlertOutline",
            "#F97316", // Orange
            "+10%",
            "+10Hz",
            "-5%",
            +1,
            // Rapid shivering tremor & nervous tension
            "vibrato=f=6.8:d=0.45,volume=0.95",
            "Rapid nervous shivering tremor & tension"
        ),
    };

    public static IReadOnlyList<ActorEmotionConfig> AllEmotions => Configs.Values.ToList();

    public static IReadOnlyList<string> EmotionNames =>
        [
            EmotionNormal,
            EmotionCrying,
            EmotionLaughing,
            EmotionSad,
            EmotionHappy,
            EmotionAngry,
            EmotionWhisper,
            EmotionFear,
        ];

    public static ActorEmotionConfig GetConfig(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var kvp in Configs)
            {
                if (
                    string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return kvp.Value;
                }
            }
        }
        return Configs[EmotionNormal];
    }

    public static string ComputeEffectiveRate(string? baseRate, string emotionRateOffset)
    {
        int baseVal = 15;
        if (!string.IsNullOrWhiteSpace(baseRate))
        {
            var clean = baseRate.Replace("%", "").Replace("+", "").Trim();
            if (int.TryParse(clean, out int parsed))
                baseVal = parsed;
        }

        int offsetVal = 0;
        if (!string.IsNullOrWhiteSpace(emotionRateOffset))
        {
            var clean = emotionRateOffset.Replace("%", "").Replace("+", "").Trim();
            if (int.TryParse(clean, out int parsed))
                offsetVal = parsed;
        }

        int finalVal = Math.Clamp(baseVal + offsetVal, -40, 60);
        return finalVal >= 0 ? $"+{finalVal}%" : $"{finalVal}%";
    }

    /// <summary>
    /// Scans dialogue cues (in both original and Khmer text) to automatically detect the acting emotion.
    /// </summary>
    public static string DetectEmotion(string? originalText, string? khmerText)
    {
        var combined =
            $"{originalText ?? string.Empty} {khmerText ?? string.Empty}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(combined))
            return EmotionNormal;

        // 1. Crying cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(crying|cries|cry|sobbing|sob|weeping|weep|tears|huhu|sniffles?|wailing)\b|\[cry|\[sob|\(cry|\(sob|យំ|ខ្សឹក|អួលដើមក|ស្រក់ទឹកភ្នែក"
            )
        )
            return EmotionCrying;

        // 2. Laughing cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(laughing|laughs|laugh|chuckles?|giggles?|haha|hehe|5555|lmao|lol)\b|\[laugh|\[chuckle|\(laugh|\(chuckle|សើច|កំប្លែង|ក្អាកក្អាយ"
            )
        )
            return EmotionLaughing;

        // 3. Whisper cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(whispering|whispers|whisper|psst|shh|quietly|secret)\b|\[whisper|\(whisper|ខ្សឹប|តិចៗ"
            )
        )
            return EmotionWhisper;

        // 4. Angry cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(angry|shouting|shouts|screaming|screams|furious|damn|shut up|hate|rage)\b|\[angry|\[scream|\(angry|\(scream|ខឹង|ស្រែក|គំហក|ឈ្លោះ|ស្អប់"
            )
            || combined.Contains("!?")
            || combined.Contains("?!")
            || combined.Count(c => c == '!') >= 2
        )
            return EmotionAngry;

        // 5. Fear cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(scared|terrified|fear|afraid|horror|monster|run away|help me|trembling)\b|\[fear|\[scared|\(fear|ភ័យ|ខ្លាច|រត់ទៅ|ជួយផង|ញ័រ"
            )
        )
            return EmotionFear;

        // 6. Sad cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(sad|sorrow|depressed|grief|heartbroken|lonely|painful|regret|sigh)\b|\[sad|\(sad|កំសត់|ស្រងូត|ឈឺចាប់|ឯកោ|សោកសៅ|ស្តាយក្រោយ"
            )
        )
            return EmotionSad;

        // 7. Happy cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(happy|yay|hurray|awesome|excited|wonderful|delighted|love you|celebrate)\b|\[happy|\(happy|សប្បាយ|រំភើប|អស្ចារ្យ|ស្រឡាញ់|អបអរ"
            )
        )
            return EmotionHappy;

        return EmotionNormal;
    }

    /// <summary>
    /// Applies acoustic emotion DSP filters using FFmpeg to sculpt the voice's acting expression.
    /// </summary>
    public static async Task<bool> ApplyEmotionAcousticFilterAsync(
        string ffmpegPath,
        string inputAudioPath,
        string outputAudioPath,
        string emotion,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(inputAudioPath) || !File.Exists(ffmpegPath))
            return false;

        var config = GetConfig(emotion);
        if (string.IsNullOrWhiteSpace(config.FfmpegFilter))
        {
            // Normal emotion requires no post-filter
            if (inputAudioPath != outputAudioPath)
            {
                File.Copy(inputAudioPath, outputAudioPath, overwrite: true);
            }
            return true;
        }

        try
        {
            var tempOutput = Path.Combine(
                Path.GetDirectoryName(outputAudioPath) ?? Path.GetTempPath(),
                $"dsp_{Guid.NewGuid():N}.wav"
            );

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
            psi.ArgumentList.Add(inputAudioPath);
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add(config.FfmpegFilter);
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("pcm_s16le");
            psi.ArgumentList.Add(tempOutput);

            using var proc = Process.Start(psi);
            if (proc == null)
                return false;

            await proc.WaitForExitAsync(cancellationToken);

            if (
                proc.ExitCode == 0
                && File.Exists(tempOutput)
                && new FileInfo(tempOutput).Length > 100
            )
            {
                if (File.Exists(outputAudioPath))
                    File.Delete(outputAudioPath);

                File.Move(tempOutput, outputAudioPath, overwrite: true);
                return true;
            }

            if (File.Exists(tempOutput))
                File.Delete(tempOutput);
        }
        catch
        {
            // Fallback: keep unfiltered audio
        }

        return false;
    }
}
