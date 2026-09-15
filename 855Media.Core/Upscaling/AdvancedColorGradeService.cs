using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace _855Media.Core.Upscaling;

public static class AdvancedColorGradeService
{
    public static readonly IReadOnlyList<string> AvailableToneCurves =
    [
        "None",
        "Deep S-Curve",
        "Faded Black",
        "Cross Process",
        "Vintage",
        "Kodak Portra 400",
        "Fuji Velvia",
        "Bleach Bypass",
        "Cyberpunk / Neon",
        "Golden Hour Warmth",
    ];

    /// <summary>
    /// Builds the combined FFmpeg video filter chain based on advanced color grading parameters.
    /// Returns null if all settings are at their default / neutral state.
    /// </summary>
    public static string? BuildFilterString(ColorGradingSettings settings)
    {
        if (!settings.HasActiveGrading)
            return null;

        var filters = new List<string>();

        // 1. Auto-normalization (balances dynamic range before color work)
        if (settings.AutoNormalize)
        {
            filters.Add("normalize");
        }

        // 2. Color Temperature (3500K to 9500K, default 6500K)
        if (
            settings.ColorTemperature != 6500
            && settings.ColorTemperature >= 1000
            && settings.ColorTemperature <= 40000
        )
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"colortemperature=temperature={settings.ColorTemperature}"
                )
            );
        }

        // 3. 3-Way Color Wheels: Shadow, Midtone & Highlight Split-Toning (colorbalance)
        bool hasShadowTint =
            Math.Abs(settings.ShadowRed) > 0.001
            || Math.Abs(settings.ShadowGreen) > 0.001
            || Math.Abs(settings.ShadowBlue) > 0.001;

        bool hasMidtoneTint =
            Math.Abs(settings.MidtoneRed) > 0.001
            || Math.Abs(settings.MidtoneGreen) > 0.001
            || Math.Abs(settings.MidtoneBlue) > 0.001;

        bool hasHighlightTint =
            Math.Abs(settings.HighlightRed) > 0.001
            || Math.Abs(settings.HighlightGreen) > 0.001
            || Math.Abs(settings.HighlightBlue) > 0.001;

        if (hasShadowTint || hasMidtoneTint || hasHighlightTint)
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"colorbalance=rs={settings.ShadowRed:F2}:gs={settings.ShadowGreen:F2}:bs={settings.ShadowBlue:F2}:rm={settings.MidtoneRed:F2}:gm={settings.MidtoneGreen:F2}:bm={settings.MidtoneBlue:F2}:rh={settings.HighlightRed:F2}:gh={settings.HighlightGreen:F2}:bh={settings.HighlightBlue:F2}"
                )
            );
        }

        // 4. Basic Exposure Adjustments (brightness, contrast, saturation, gamma)
        if (
            Math.Abs(settings.Brightness) > 0.001
            || Math.Abs(settings.Contrast - 1.0) > 0.001
            || Math.Abs(settings.Saturation - 1.0) > 0.001
            || Math.Abs(settings.Gamma - 1.0) > 0.001
        )
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"eq=brightness={settings.Brightness:F2}:contrast={settings.Contrast:F2}:saturation={settings.Saturation:F2}:gamma={settings.Gamma:F2}"
                )
            );
        }

        // 5. Smart Vibrance (selective saturation with skin protection)
        if (Math.Abs(settings.Vibrance) > 0.01)
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"vibrance=intensity={settings.Vibrance:F2}"
                )
            );
        }

        // 6. Matte Tone Curves
        var curveFilter = GetToneCurveFilter(settings.ToneCurve);
        if (!string.IsNullOrWhiteSpace(curveFilter))
        {
            filters.Add(curveFilter);
        }

        // 7. Organic 35mm Film Grain (0 to 20 intensity)
        if (settings.FilmGrain > 0)
        {
            int grainIntensity = Math.Clamp(settings.FilmGrain, 1, 20);
            filters.Add($"noise=alls={grainIntensity}:allf=t+u");
        }

        // 8. Vignette (0.0 to 1.0)
        if (settings.Vignette > 0.01)
        {
            double angle = Math.Clamp(settings.Vignette * 0.45, 0.05, 0.45);
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"vignette=PI*{angle:F3}"));
        }

        // 9. Custom 3D LUT (.cube file) with Opacity Blending
        if (!string.IsNullOrWhiteSpace(settings.LutPath) && File.Exists(settings.LutPath))
        {
            var escapedPath = settings.LutPath.Replace("\\", "/").Replace(":", "\\:");
            if (settings.LutOpacity >= 0.99)
            {
                filters.Add($"lut3d=file='{escapedPath}'");
            }
            else if (settings.LutOpacity > 0.01)
            {
                // Subgraph blend with opacity control
                double opacity = Math.Clamp(settings.LutOpacity, 0.01, 1.0);
                filters.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"split[lut_orig][lut_in];[lut_in]lut3d=file='{escapedPath}'[lut_out];[lut_orig][lut_out]blend=all_opacity={opacity:F2}"
                    )
                );
            }
        }

        return filters.Count > 0 ? string.Join(",", filters) : null;
    }

    public static string? GetToneCurveFilter(string? curveName)
    {
        if (
            string.IsNullOrWhiteSpace(curveName)
            || string.Equals(curveName, "None", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        return curveName.Trim() switch
        {
            "Deep S-Curve" => "curves=m='0/0 0.25/0.15 0.75/0.85 1/1'",
            "Faded Black" => "curves=m='0/0.06 0.25/0.28 0.75/0.75 1/0.96'",
            "Cross Process" => "curves=r='0/0 0.25/0.15 0.75/0.85 1/1':b='0/0.08 0.5/0.45 1/0.92'",
            "Vintage" => "curves=preset=vintage",
            "Kodak Portra 400" =>
                "curves=r='0/0 0.2/0.23 0.7/0.74 1/0.98':g='0/0 0.25/0.24 0.75/0.73 1/0.97':b='0/0.02 0.3/0.26 0.7/0.67 1/0.93'",
            "Fuji Velvia" =>
                "curves=r='0/0 0.25/0.18 0.75/0.82 1/1':g='0/0 0.25/0.2 0.75/0.84 1/1':b='0/0 0.25/0.22 0.75/0.8 1/1'",
            "Bleach Bypass" => "curves=m='0/0 0.2/0.1 0.5/0.52 0.8/0.9 1/1'",
            "Cyberpunk / Neon" =>
                "curves=r='0/0 0.3/0.18 0.7/0.82 1/1':b='0/0.06 0.3/0.4 0.7/0.85 1/1':g='0/0 0.5/0.42 1/0.95'",
            "Golden Hour Warmth" => "curves=r='0/0.02 0.5/0.58 1/1':b='0/0 0.5/0.4 1/0.92'",
            _ => null,
        };
    }
}
