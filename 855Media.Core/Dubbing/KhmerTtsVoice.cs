namespace _855Media.Core.Dubbing;

/// <summary>
/// Represents a Microsoft Edge-TTS neural base voice for Khmer (km-KH).
/// In 855Media, both Piseth and Sreymom are dynamically shaped into "Strong &amp; Powerful"
/// or "Soft &amp; Gentle" voices using the ActorEmotionEngine, Parametric EQ (Warmth/Clarity),
/// and RVC voice models.
/// </summary>
public record KhmerTtsVoice(string Id, string DisplayName, string Gender, string Description)
{
    public static readonly KhmerTtsVoice PisethMale = new(
        "km-KH-PisethNeural",
        "Piseth (បុរស / Male)",
        "Male",
        "Microsoft Khmer Neural Male Voice"
    );

    public static readonly KhmerTtsVoice SreymomFemale = new(
        "km-KH-SreymomNeural",
        "Sreymom (នារី / Female)",
        "Female",
        "Microsoft Khmer Neural Female Voice"
    );

    public static readonly KhmerTtsVoice GoogleFemale = new(
        "km-KH-GoogleNeural",
        "Google Khmer (នារី / Female)",
        "Female",
        "Google Speech Translation Voice"
    );

    public static readonly KhmerTtsVoice[] All = [PisethMale, SreymomFemale, GoogleFemale];

    /// <summary>
    /// Delivery intensity styles available in the Dubbing Actor Emotion Engine.
    /// </summary>
    public static class DeliveryStyles
    {
        public const string Strong = "Strong";
        public const string Soft = "Soft";
        public const string Normal = "Normal";
        public const string Whisper = "Whisper";
        public const string Scream = "Scream";
    }
}
