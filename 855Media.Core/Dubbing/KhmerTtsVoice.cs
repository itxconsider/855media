namespace _855Media.Core.Dubbing;

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

    public static readonly KhmerTtsVoice[] All = [PisethMale, SreymomFemale];
}
