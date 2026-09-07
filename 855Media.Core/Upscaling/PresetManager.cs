using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.Upscaling;

public record UpscalePreset(
    string Name,
    string Description,
    bool EnableDenoise,
    bool EnableDeinterlace,
    double Brightness,
    double Contrast,
    double Saturation,
    bool AutoNormalize,
    string? PreferredModelName = null
)
{
    public string? RecommendedModel => PreferredModelName;
}

public static class PresetManager
{
    public static readonly UpscalePreset DefaultPreset = new(
        "Default / Neutral",
        "Standard upscaling with neutral color balance and no pre-filtering.",
        EnableDenoise: false,
        EnableDeinterlace: false,
        Brightness: 0.0,
        Contrast: 1.0,
        Saturation: 1.0,
        AutoNormalize: false
    );

    public static readonly UpscalePreset VhsHomeVideo = new(
        "VHS & Old Home Video",
        "Scrubs tape hiss/analog noise (hqdn3d), deinterlaces combed frames (yadif), and dynamically lifts contrast.",
        EnableDenoise: true,
        EnableDeinterlace: true,
        Brightness: 0.0,
        Contrast: 1.15,
        Saturation: 1.10,
        AutoNormalize: true
    );

    public static readonly UpscalePreset AnimeCartoon = new(
        "Anime / Cartoon Sharp",
        "Scrubs compression artifacts and elevates color vibrancy using the anime neural model.",
        EnableDenoise: true,
        EnableDeinterlace: false,
        Brightness: 0.02,
        Contrast: 1.10,
        Saturation: 1.25,
        AutoNormalize: false,
        PreferredModelName: "realesr-animevideov3"
    );

    public static readonly UpscalePreset CinematicMaster = new(
        "Cinematic Master",
        "Applies dynamic range normalization with rich contrast and warm highlight rendering.",
        EnableDenoise: false,
        EnableDeinterlace: false,
        Brightness: 0.0,
        Contrast: 1.08,
        Saturation: 1.05,
        AutoNormalize: true
    );

    public static IReadOnlyList<UpscalePreset> AllPresets { get; } =
    [DefaultPreset, VhsHomeVideo, AnimeCartoon, CinematicMaster];

    public static IReadOnlyList<string> PresetNames { get; } =
        AllPresets.Select(p => p.Name).ToList();

    public static UpscalePreset? FindPreset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return AllPresets.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static UpscalePreset? GetPreset(string? name) => FindPreset(name);

    public static void ApplyPreset(UpscaleJob job, string presetName)
    {
        var preset = FindPreset(presetName) ?? DefaultPreset;
        job.EnableDenoise = preset.EnableDenoise;
        job.EnableDeinterlace = preset.EnableDeinterlace;
        job.ActivePresetName = preset.Name;

        job.ColorGrading.Brightness = preset.Brightness;
        job.ColorGrading.Contrast = preset.Contrast;
        job.ColorGrading.Saturation = preset.Saturation;
        job.ColorGrading.AutoNormalize = preset.AutoNormalize;
    }

    public static void ApplyPreset(
        ColorGradingSettings grading,
        out bool denoise,
        out bool deinterlace,
        string presetName
    )
    {
        var preset = FindPreset(presetName) ?? DefaultPreset;
        denoise = preset.EnableDenoise;
        deinterlace = preset.EnableDeinterlace;

        grading.Brightness = preset.Brightness;
        grading.Contrast = preset.Contrast;
        grading.Saturation = preset.Saturation;
        grading.AutoNormalize = preset.AutoNormalize;
    }
}
