using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

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

public record ActorToneArchetype(
    string Name,
    string DisplayName,
    string Icon,
    string Color,
    int DefaultPitchShift,
    string DefaultRate,
    double DefaultWarmth,
    double DefaultClarity,
    string DefaultEmotion,
    string Description,
    string SampleLine
);

public static class ActorEmotionEngine
{
    // Emotion constants
    public const string EmotionNormal = "Normal";
    public const string EmotionCrying = "Crying";
    public const string EmotionLaughing = "Laughing";
    public const string EmotionSad = "Sad";
    public const string EmotionHappy = "Happy";
    public const string EmotionAngry = "Angry";
    public const string EmotionWhisper = "Whisper";
    public const string EmotionFear = "Fear";
    public const string EmotionVillain = "Villain";
    public const string EmotionNarrator = "Narrator";
    public const string EmotionElder = "Elder";
    public const string EmotionAction = "Action";
    public const string EmotionRomantic = "Romantic";
    public const string EmotionYouth = "Youth";
    public const string EmotionSarcastic = "Sarcastic";
    public const string EmotionScream = "Scream";
    public const string EmotionFrustrated = "Frustrated";
    public const string EmotionStrong = "Strong";
    public const string EmotionSoft = "Soft";

    private static readonly Dictionary<string, ActorEmotionConfig> Configs = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        [EmotionNormal] = new(
            EmotionNormal,
            "Normal Dialogue",
            "AccountVoice",
            "#64748B", // Slate Gray
            "+0%",
            "+0Hz",
            "+0%",
            0,
            string.Empty,
            "Standard natural dialogue cadence & clear presence"
        ),
        [EmotionCrying] = new(
            EmotionCrying,
            "Crying & Sobbing",
            "EmoticonCryOutline",
            "#8B5CF6", // Violet
            "-10%",
            "-12Hz",
            "-10%",
            -2,
            // Vocal cord quiver + dynamic breathy volume fluctuations + softening
            "vibrato=f=4.8:d=0.32,tremolo=f=3.2:d=0.22,lowpass=f=7500,volume=0.92",
            "Emotional sobbing quiver, tremor & tearful drops"
        ),
        [EmotionLaughing] = new(
            EmotionLaughing,
            "Laughing & Smiling",
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
            "Sad & Melancholy",
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
            "Happy & Joyful",
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
            "Angry & Shouting",
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
            "Whisper & Secretive",
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
            "Fear & Terrified",
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
        [EmotionVillain] = new(
            EmotionVillain,
            "Villain & Menacing",
            "SkullOutline",
            "#9333EA", // Deep Purple
            "-5%",
            "-20Hz",
            "+12%",
            -3,
            // Deep sinister chest rumble & cold menacing presence
            "equalizer=f=120:t=q:w=1.0:g=4.0,equalizer=f=2400:t=q:w=1.5:g=-2.0,volume=1.12",
            "Deep sinister chest rumble, dark weight & deliberate pace"
        ),
        [EmotionNarrator] = new(
            EmotionNarrator,
            "Movie Trailer Narrator",
            "MicrophoneVariant",
            "#D97706", // Amber Bronze
            "+5%",
            "-15Hz",
            "+18%",
            -2,
            // Deep proximity bass boost + broadcast warmth & authoritative compression
            "equalizer=f=110:t=q:w=0.8:g=4.5,equalizer=f=3400:t=q:w=1.2:g=2.8,compand=attacks=0.02:decays=0.1:points=-60/-60|-18/-8|0/0,volume=1.15",
            "Deep movie trailer voice with broadcast chest warmth & authority"
        ),
        [EmotionElder] = new(
            EmotionElder,
            "Wise Elder / Mentor",
            "AccountTie",
            "#0D9488", // Teal
            "-8%",
            "-16Hz",
            "-5%",
            -2,
            // Warm weathered low-mids + relaxed patient cadence
            "equalizer=f=260:t=q:w=1.1:g=3.2,lowpass=f=7000,volume=0.96",
            "Warm weathered low-mids, dignified grace & patient cadence"
        ),
        [EmotionAction] = new(
            EmotionAction,
            "Action Hero / Grit",
            "Flash",
            "#DC2626", // Crimson
            "+22%",
            "+8Hz",
            "+20%",
            +1,
            // Strained throat tension, high grit and aggressive forward projection
            "compand=attacks=0.01:decays=0.06:points=-50/-50|-12/-4|0/0,treble=g=2.5,volume=1.18",
            "Combat strained throat grit, forward punch & fast attack"
        ),
        [EmotionRomantic] = new(
            EmotionRomantic,
            "Romantic & Intimate",
            "HeartOutline",
            "#EC4899", // Rose Pink
            "+0%",
            "-8Hz",
            "-10%",
            -1,
            // Gentle proximity warmth, soft breathy air and close mic presence
            "highpass=f=180,equalizer=f=300:t=q:w=1.2:g=2.0,equalizer=f=4500:t=q:w=1.5:g=3.0,volume=0.92",
            "Soft intimate warmth, breathy air and close proximity feeling"
        ),
        [EmotionYouth] = new(
            EmotionYouth,
            "Youth & Child",
            "Baby",
            "#06B6D4", // Cyan
            "+20%",
            "+32Hz",
            "+5%",
            +4,
            // High register agility, bright energetic sparkle
            "highpass=f=260,equalizer=f=3600:t=q:w=1.2:g=4.0,volume=1.02",
            "Bright high pitch register, nimble energetic cadence & cheer"
        ),
        [EmotionSarcastic] = new(
            EmotionSarcastic,
            "Sarcastic & Mocking",
            "EmoticonWinkOutline",
            "#84CC16", // Lime
            "+8%",
            "+10Hz",
            "+0%",
            +1,
            // Playful dynamic inflection with slight smirk filter
            "equalizer=f=1200:t=q:w=1.5:g=2.5,treble=g=2,volume=1.0",
            "Playful mocking drawl, dynamic inflection & smug presence"
        ),
        [EmotionScream] = new(
            EmotionScream,
            "Scream & Agony",
            "VolumeHigh",
            "#E11D48", // Vivid Crimson Rose
            "+26%",
            "+30Hz",
            "+35%",
            +3,
            // Visceral scream projection: high presence bite, rapid compand attack & soft saturation limiter
            "compand=attacks=0.005:decays=0.05:points=-50/-50|-14/-3|0/0,equalizer=f=2800:t=q:w=1.2:g=5.5,treble=g=4.0,alimiter=limit=0.98,volume=1.28",
            "Visceral blood-curdling scream, battle cry, peak projection & intense agony"
        ),
        [EmotionFrustrated] = new(
            EmotionFrustrated,
            "Frustrated & Agony",
            "EmoticonConfusedOutline",
            "#EA580C", // Burnt Orange
            "+12%",
            "+6Hz",
            "+10%",
            +1,
            // Clenched teeth throat constriction & exasperated sighs
            "equalizer=f=850:t=q:w=1.4:g=3.8,equalizer=f=2200:t=q:w=1.2:g=2.8,compand=attacks=0.01:decays=0.08:points=-45/-45|-16/-6|0/0,volume=1.08",
            "Constricted throat tension, exasperated sighs & teeth-gritting frustration"
        ),
        [EmotionStrong] = new(
            EmotionStrong,
            "Strong & Powerful",
            "ArmFlex",
            "#1D4ED8", // Bold Royal Blue
            "+5%",
            "+4Hz",
            "+20%",
            +1,
            // Solid chest fundamental boost at 180Hz + presence bite at 2600Hz + optical compand + ceiling
            "compand=attacks=0.01:decays=0.08:points=-50/-50|-14/-4|0/0,equalizer=f=180:t=q:w=1.0:g=3.8,equalizer=f=2600:t=q:w=1.2:g=3.2,volume=1.20",
            "Authoritative, deep chest resonance, bold forward projection & commanding power"
        ),
        [EmotionSoft] = new(
            EmotionSoft,
            "Soft & Gentle",
            "Feather",
            "#14B8A6", // Soft Teal
            "-6%",
            "-6Hz",
            "-20%",
            -1,
            // Warm low-pass smoothing + gentle body warmth + soft mid scoop
            "lowpass=f=6800,equalizer=f=250:t=q:w=1.1:g=2.2,equalizer=f=2400:t=q:w=1.3:g=-2.5,volume=0.82",
            "Delicate, soothing, calm, tender intimacy & gentle acoustic cushioning"
        ),
    };

    // Archetype definitions
    private static readonly Dictionary<string, ActorToneArchetype> Archetypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Hero"] = new(
            "Hero",
            "Hero / Leading Actor",
            "ShieldAccount",
            "#3B82F6",
            0,
            "+15%",
            0.25,
            0.35,
            EmotionNormal,
            "Confident, grounded, heroic clarity and presence",
            "កុំបារម្ភអី! ខ្ញុំនឹងនៅទីនេះដើម្បីការពារអ្នកទាំងអស់គ្នា!"
        ),
        ["Villain"] = new(
            "Villain",
            "Villain / Antagonist",
            "SkullOutline",
            "#9333EA",
            -3,
            "+5%",
            0.80,
            -0.10,
            EmotionVillain,
            "Sinister, deep menacing chest resonance and deliberate pace",
            "ឯងគិតថាឯងអាចគេចផុតពីកណ្តាប់ដៃខ្ញុំបានឬ? មិនងាយទេ!"
        ),
        ["Elder"] = new(
            "Elder",
            "Wise Elder / Mentor",
            "AccountTie",
            "#0D9488",
            -2,
            "+0%",
            0.70,
            -0.20,
            EmotionElder,
            "Warm, dignified, weathered chest resonance and patient cadence",
            "ចូរចាំថា ការអត់ធ្មត់ និងប្រាជ្ញា គឺជាកម្លាំងដ៏អស្ចារ្យបំផុតក្នុងជីវិត។"
        ),
        ["Action"] = new(
            "Action",
            "Action Hero / Warrior",
            "Flash",
            "#DC2626",
            0,
            "+22%",
            0.40,
            0.50,
            EmotionAction,
            "High-energy grit, punchy compression and fast attack",
            "ប្រយ័ត្នទាំងអស់គ្នា! សត្រូវកំពុងសម្រុកចូលមកហើយ!"
        ),
        ["Narrator"] = new(
            "Narrator",
            "Cinematic Narrator / Trailer",
            "MicrophoneVariant",
            "#D97706",
            -2,
            "+8%",
            0.85,
            0.45,
            EmotionNarrator,
            "Deep movie trailer voice, broadcast warmth and authoritative punch",
            "នៅក្នុងពិភពលោកដ៏អាថ៌កំបាំងមួយ រឿងរ៉ាវដ៏អស្ចារ្យទើបតែបានចាប់ផ្តើម..."
        ),
        ["Romantic"] = new(
            "Romantic",
            "Romantic / Intimate Lead",
            "HeartOutline",
            "#EC4899",
            -1,
            "+5%",
            0.45,
            0.30,
            EmotionRomantic,
            "Soft intimate warmth, breathy air and gentle delivery",
            "ទោះបីជាមានរឿងអ្វីកើតឡើងក៏ដោយ បេះដូងខ្ញុំនៅតែជារបស់អ្នកជានិច្ច។"
        ),
        ["Youth"] = new(
            "Youth",
            "Youth / Child Actor",
            "Baby",
            "#06B6D4",
            +4,
            "+20%",
            -0.40,
            0.60,
            EmotionYouth,
            "Bright high pitch, nimble cadence, cheerful energy",
            "បងប្រុស! មើលចុះ! ទីនេះមានរបស់ប្លែកៗជាច្រើនគួរឱ្យសប្បាយណាស់!"
        ),
        ["Comic"] = new(
            "Comic",
            "Comic Relief / Animated",
            "EmoticonLolOutline",
            "#F59E0B",
            +1,
            "+22%",
            0.10,
            0.55,
            EmotionLaughing,
            "Buoyant smiling bounce, playful dynamic inflection",
            "ហាហា! ខ្ញុំប្រាប់ហើយថាកុំធ្វើបែបហ្នឹង ឥឡូវត្រូវមែន!"
        ),
        ["AntiHero"] = new(
            "AntiHero",
            "Anti-Hero / Tormented",
            "Flash",
            "#F97316",
            -1,
            "+14%",
            0.35,
            0.45,
            EmotionFrustrated,
            "Gritty, frustrated, brooding tension, suppressed rage & sharp edge",
            "ខ្ញុំធុញទ្រាន់នឹងរឿងទាំងអស់នេះណាស់! ឈប់មករំខានខ្ញុំទៀតទៅ!"
        ),
        ["Leader"] = new(
            "Leader",
            "Strong Leader / Commander",
            "Crown",
            "#1E40AF",
            0,
            "+10%",
            0.70,
            0.50,
            EmotionStrong,
            "Commanding authority, forward chest resonance, decisive presence and bold projection",
            "ស្តាប់បញ្ជាទាំងអស់គ្នា! យើងត្រូវតែឈ្នះក្នុងសមរភូមិនេះ!"
        ),
        ["Gentle"] = new(
            "Gentle",
            "Soft & Gentle / Caregiver",
            "Feather",
            "#0D9488",
            -1,
            "-5%",
            0.35,
            -0.30,
            EmotionSoft,
            "Soft, comforting, velvety smooth delivery, gentle breathing and warm presence",
            "កុំបារម្ភអី សម្រាកឱ្យស្រួលចុះ ខ្ញុំនៅក្បែរអ្នកជានិច្ច។"
        ),
    };

    public static IReadOnlyList<ActorEmotionConfig> AllEmotions => Configs.Values.ToList();

    public static IReadOnlyList<string> EmotionNames => Configs.Keys.ToList();

    public static IReadOnlyList<ActorToneArchetype> AllArchetypes => Archetypes.Values.ToList();

    public static IReadOnlyList<string> ArchetypeNames => Archetypes.Keys.ToList();

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

    public static ActorToneArchetype GetArchetype(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (Archetypes.TryGetValue(name, out var match))
                return match;

            foreach (var kvp in Archetypes)
            {
                if (name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }
        return Archetypes["Hero"];
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
    /// Computes the effective Hz pitch parameter combining the character's pitch shift (semitones)
    /// and the acting emotion's pitch offset.
    /// Each semitone corresponds to ~7.5Hz for human vocal fundamental frequency (F0).
    /// </summary>
    public static string ComputeEffectiveTtsPitch(
        int characterPitchShift,
        string? emotionTtsPitch = null,
        int emotionPitchShiftOffset = 0
    )
    {
        // Calculate semitone pitch in Hz (~7.5 Hz per semitone)
        double totalPitchHz = (characterPitchShift + emotionPitchShiftOffset) * 7.5;

        // Parse any explicit Hz offset from the emotion configuration
        if (!string.IsNullOrWhiteSpace(emotionTtsPitch))
        {
            var clean = emotionTtsPitch.Replace("Hz", "").Replace("+", "").Trim();
            if (int.TryParse(clean, out int parsedHz))
            {
                totalPitchHz += parsedHz;
            }
        }

        int clamped = Math.Clamp((int)Math.Round(totalPitchHz), -85, 85);
        return clamped >= 0 ? $"+{clamped}Hz" : $"{clamped}Hz";
    }

    /// <summary>
    /// Constructs a multi-stage FFmpeg audio filter chain tailored to the character's tone
    /// warmth (chest resonance), clarity (consonant air), archetype, and acting emotion.
    /// </summary>
    public static string BuildActorAcousticFilter(
        string? emotionName,
        double warmth = 0.0,
        double clarity = 0.0,
        string? archetype = null
    )
    {
        var filters = new List<string>();

        // 1. Character Tone Warmth (Chest resonance at 180Hz)
        if (Math.Abs(warmth) > 0.05)
        {
            double gainDb = Math.Clamp(warmth * 5.0, -5.0, 5.0);
            filters.Add($"equalizer=f=180:t=q:w=1.2:g={gainDb:F1}");
        }

        // 2. Character Tone Clarity & Air (Presence at 3800Hz)
        if (Math.Abs(clarity) > 0.05)
        {
            double gainDb = Math.Clamp(clarity * 4.5, -4.5, 4.5);
            filters.Add($"equalizer=f=3800:t=q:w=1.4:g={gainDb:F1}");
        }

        // 3. Emotion-Specific Acoustic Filter (Vibrato, tremolo, compand, lowpass, etc.)
        var emotionCfg = GetConfig(emotionName);
        if (!string.IsNullOrWhiteSpace(emotionCfg.FfmpegFilter))
        {
            filters.Add(emotionCfg.FfmpegFilter);
        }

        return string.Join(",", filters);
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

        // 1. Scream & Agony cues (Visceral terror screams, battle cries, shrieks, peak agony)
        if (
            Regex.IsMatch(
                combined,
                @"\b(screaming|screams|scream|screeched|screeching|screech|shrieking|shriek|yelling|yell|roaring|roar|agony|nooo+|argh+|aah+|waaa+|arrgh)\b|\[scream|\[screaming|\(scream|\(screaming|ស្រែកខ្លាំង|ស្រែកយំ|ឈឺចាប់ខ្លាំង|ស្រែករក|សម្រែក"
            )
            || combined.Contains("!!!")
            || combined.Contains("!?!")
        )
            return EmotionScream;

        // 2. Frustrated & Exasperated cues (Groans, sighs, exasperation, teeth-gritting)
        if (
            Regex.IsMatch(
                combined,
                @"\b(frustrated|frustrating|frustration|exasperated|annoyed|irritated|groan|groaning|fed up|cant take it|can't take it|sick of|give up|damn it|dammit|ugh)\b|\[groan|\[sigh|\(groan|\(sigh|មួម៉ៅ|ធុញថប់|ធុញ|អស់សង្ឃឹម|មិនអាចទ្រាំបាន|ទ្រាំមិនបាន|ពិបាកចិត្ត|ហត់ចិត្ត"
            )
        )
            return EmotionFrustrated;

        // 3. Crying cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(crying|cries|cry|sobbing|sob|weeping|weep|tears|huhu|sniffles?|wailing)\b|\[cry|\[sob|\(cry|\(sob|យំ|ខ្សឹក|អួលដើមក|ស្រក់ទឹកភ្នែក"
            )
        )
            return EmotionCrying;

        // 4. Laughing cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(laughing|laughs|laugh|chuckles?|giggles?|haha|hehe|5555|lmao|lol)\b|\[laugh|\[chuckle|\(laugh|\(chuckle|សើច|កំប្លែង|ក្អាកក្អាយ"
            )
        )
            return EmotionLaughing;

        // 5. Whisper cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(whispering|whispers|whisper|psst|shh|quietly|secret)\b|\[whisper|\(whisper|ខ្សឹប|តិចៗ"
            )
        )
            return EmotionWhisper;

        // 6. Angry & Shouting cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(angry|shouting|shouts|furious|damn|shut up|hate|rage|bastard|idiot)\b|\[angry|\(angry|ខឹង|គំហក|ឈ្លោះ|ស្អប់"
            )
            || combined.Contains("!?")
            || combined.Contains("?!")
            || combined.Count(c => c == '!') >= 2
        )
            return EmotionAngry;

        // 7. Strong & Powerful cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(strong|strongly|powerful|power|commanding|command|authoritative|firm|firmly|decisive|intense|general|warrior|unyielding|force)\b|\[strong|\(strong|រឹងមាំ|អំណាច|បញ្ជា|ខ្លាំង|ម៉ឺងម៉ាត់|ដាច់ខាត|មេដឹកនាំ|មេបញ្ជាការ|កម្លាំង"
            )
        )
            return EmotionStrong;

        // 8. Soft & Gentle cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(soft|softly|gentle|gently|tender|tenderly|calm|calmly|soothing|peaceful|comforting|sweet|lullaby|delicate)\b|\[soft|\(soft|\[gentle|\(gentle|ស្រទន់|ស្រាល|ទន់ភ្លន់|ថ្នមៗ|ថ្នម|រម្យទម|លួងលោម|ស្ងប់ស្ងាត់|សុភាព"
            )
        )
            return EmotionSoft;

        // 9. Fear cues
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

        // 8. Villain cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(destroy|die|kill|fool|kneel|obey|punish|darkness|evil)\b|កម្ទេច|សម្លាប់|លុតជង្គង់|អាក្រក់"
            )
        )
            return EmotionVillain;

        // 9. Romantic cues
        if (
            Regex.IsMatch(
                combined,
                @"\b(darling|honey|sweetheart|forever|kiss|my love)\b|បងសម្លាញ់|អូនសម្លាញ់|ថើប|ស្រឡាញ់"
            )
        )
            return EmotionRomantic;

        return EmotionNormal;
    }

    /// <summary>
    /// Applies acoustic tone and emotion DSP filters using FFmpeg to sculpt the actor's vocal character.
    /// </summary>
    public static async Task<bool> ApplyActorAcousticFilterAsync(
        string ffmpegPath,
        string inputAudioPath,
        string outputAudioPath,
        string emotion,
        double warmth = 0.0,
        double clarity = 0.0,
        string? archetype = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(inputAudioPath) || !File.Exists(ffmpegPath))
            return false;

        var filter = BuildActorAcousticFilter(emotion, warmth, clarity, archetype);
        if (string.IsNullOrWhiteSpace(filter))
        {
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
            psi.ArgumentList.Add(filter);
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("pcm_s16le");
            psi.ArgumentList.Add(tempOutput);

            using var proc = Process.Start(psi);
            if (proc == null)
                return false;

            ChildProcessTracker.Track(proc);
            await proc.WaitForExitWithCancellationAsync(cancellationToken);

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

    /// <summary>
    /// Legacy wrapper for emotion-only acoustic filter.
    /// </summary>
    public static Task<bool> ApplyEmotionAcousticFilterAsync(
        string ffmpegPath,
        string inputAudioPath,
        string outputAudioPath,
        string emotion,
        CancellationToken cancellationToken = default
    )
    {
        return ApplyActorAcousticFilterAsync(
            ffmpegPath,
            inputAudioPath,
            outputAudioPath,
            emotion,
            0.0,
            0.0,
            null,
            cancellationToken
        );
    }
}
