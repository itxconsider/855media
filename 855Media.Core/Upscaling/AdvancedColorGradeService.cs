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

        // 3. Shadow & Highlight Color Wheels / Split-Toning (colorbalance)
        bool hasShadowTint =
            Math.Abs(settings.ShadowRed) > 0.001
            || Math.Abs(settings.ShadowGreen) > 0.001
            || Math.Abs(settings.ShadowBlue) > 0.001;

        bool hasHighlightTint =
            Math.Abs(settings.HighlightRed) > 0.001
            || Math.Abs(settings.HighlightGreen) > 0.001
            || Math.Abs(settings.HighlightBlue) > 0.001;

        if (hasShadowTint || hasHighlightTint)
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"colorbalance=rs={settings.ShadowRed:F2}:gs={settings.ShadowGreen:F2}:bs={settings.ShadowBlue:F2}:rh={settings.HighlightRed:F2}:gh={settings.HighlightGreen:F2}:bh={settings.HighlightBlue:F2}"
                )
            );
        }

        // 4. Basic Exposure Adjustments (brightness, contrast, saturation)
        if (
            Math.Abs(settings.Brightness) > 0.001
            || Math.Abs(settings.Contrast - 1.0) > 0.001
            || Math.Abs(settings.Saturation - 1.0) > 0.001
        )
        {
            filters.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"eq=brightness={settings.Brightness:F2}:contrast={settings.Contrast:F2}:saturation={settings.Saturation:F2}"
                )
            );
        }

        // 5. Matte Tone Curves
        var curveFilter = GetToneCurveFilter(settings.ToneCurve);
        if (!string.IsNullOrWhiteSpace(curveFilter))
        {
            filters.Add(curveFilter);
        }

        // 6. Organic 35mm Film Grain (0 to 20 intensity)
        if (settings.FilmGrain > 0)
        {
            int grainIntensity = Math.Clamp(settings.FilmGrain, 1, 20);
            filters.Add($"noise=alls={grainIntensity}:allf=t+u");
        }

        // 7. Vignette (0.0 to 1.0)
        if (settings.Vignette > 0.01)
        {
            double angle = Math.Clamp(settings.Vignette * 0.45, 0.05, 0.45);
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"vignette=PI*{angle:F3}"));
        }

        // 8. Custom 3D LUT (.cube file) with Opacity Blending
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
            _ => null,
        };
    }
}
