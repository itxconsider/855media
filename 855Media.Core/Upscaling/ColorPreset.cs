using System;

namespace _855Media.Core.Upscaling;

public class ColorPreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }

    // Basic Exposure & Temperature
    public double Brightness { get; set; } = 0.0;
    public double Contrast { get; set; } = 1.0;
    public double Saturation { get; set; } = 1.0;
    public double Gamma { get; set; } = 1.0;
    public double Vibrance { get; set; } = 0.0;
    public bool AutoNormalize { get; set; }
    public int ColorTemperature { get; set; } = 6500;

    // Color Wheels & Tint (3-Way Lift, Gamma, Gain)
    public double ShadowRed { get; set; } = 0.0;
    public double ShadowGreen { get; set; } = 0.0;
    public double ShadowBlue { get; set; } = 0.0;

    public double MidtoneRed { get; set; } = 0.0;
    public double MidtoneGreen { get; set; } = 0.0;
    public double MidtoneBlue { get; set; } = 0.0;

    public double HighlightRed { get; set; } = 0.0;
    public double HighlightGreen { get; set; } = 0.0;
    public double HighlightBlue { get; set; } = 0.0;

    // Matte Tone Curves: "None", "Deep S-Curve", "Faded Black", "Cross Process"
    public string ToneCurve { get; set; } = "None";

    // Film Emulation & FX
    public int FilmGrain { get; set; } = 0; // 0 to 20
    public double Vignette { get; set; } = 0.0; // 0.0 to 1.0
    public string? LutPath { get; set; }
    public double LutOpacity { get; set; } = 1.0; // 0.0 to 1.0
}
