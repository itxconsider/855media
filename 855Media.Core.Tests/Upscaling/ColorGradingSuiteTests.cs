using System;
using System.IO;
using System.Linq;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class ColorGradingSuiteTests
{
    [Fact]
    public void ColorGradingSettings_DefaultValues_AreNeutral()
    {
        var settings = new ColorGradingSettings();
        Assert.Equal(1.0, settings.Gamma);
        Assert.Equal(0.0, settings.Vibrance);
        Assert.Equal(0.0, settings.MidtoneRed);
        Assert.Equal(0.0, settings.MidtoneGreen);
        Assert.Equal(0.0, settings.MidtoneBlue);
        Assert.False(settings.HasActiveGrading);
    }

    [Theory]
    [InlineData(1.2, 0.0, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 0.3, 0.0, 0.0, 0.0)]
    [InlineData(1.0, 0.0, 0.1, 0.0, 0.0)]
    [InlineData(1.0, 0.0, 0.0, -0.1, 0.0)]
    [InlineData(1.0, 0.0, 0.0, 0.0, 0.15)]
    public void ColorGradingSettings_ModifiedParameters_HasActiveGradingIsTrue(
        double gamma,
        double vibrance,
        double midR,
        double midG,
        double midB
    )
    {
        var settings = new ColorGradingSettings
        {
            Gamma = gamma,
            Vibrance = vibrance,
            MidtoneRed = midR,
            MidtoneGreen = midG,
            MidtoneBlue = midB,
        };

        Assert.True(settings.HasActiveGrading);
    }

    [Fact]
    public void ColorGradingSettings_Reset_RestoresDefaults()
    {
        var settings = new ColorGradingSettings
        {
            Gamma = 1.4,
            Vibrance = 0.5,
            MidtoneRed = 0.2,
            MidtoneGreen = -0.1,
            MidtoneBlue = 0.3,
        };

        settings.Reset();

        Assert.Equal(1.0, settings.Gamma);
        Assert.Equal(0.0, settings.Vibrance);
        Assert.Equal(0.0, settings.MidtoneRed);
        Assert.Equal(0.0, settings.MidtoneGreen);
        Assert.Equal(0.0, settings.MidtoneBlue);
        Assert.False(settings.HasActiveGrading);
    }

    [Fact]
    public void AdvancedColorGradeService_Midtones_ProducesColorBalanceRmGmBm()
    {
        var settings = new ColorGradingSettings
        {
            MidtoneRed = 0.12,
            MidtoneGreen = -0.08,
            MidtoneBlue = 0.05,
        };

        var filter = AdvancedColorGradeService.BuildFilterString(settings);

        Assert.NotNull(filter);
        Assert.Contains("colorbalance=", filter);
        Assert.Contains("rm=0.12", filter);
        Assert.Contains("gm=-0.08", filter);
        Assert.Contains("bm=0.05", filter);
    }

    [Fact]
    public void AdvancedColorGradeService_Gamma_ProducesEqGamma()
    {
        var settings = new ColorGradingSettings { Gamma = 1.35 };

        var filter = AdvancedColorGradeService.BuildFilterString(settings);

        Assert.NotNull(filter);
        Assert.Contains("eq=", filter);
        Assert.Contains("gamma=1.35", filter);
    }

    [Fact]
    public void AdvancedColorGradeService_Vibrance_ProducesVibranceFilter()
    {
        var settings = new ColorGradingSettings { Vibrance = 0.45 };

        var filter = AdvancedColorGradeService.BuildFilterString(settings);

        Assert.NotNull(filter);
        Assert.Contains("vibrance=intensity=0.45", filter);
    }

    [Theory]
    [InlineData("Kodak Portra 400")]
    [InlineData("Fuji Velvia")]
    [InlineData("Bleach Bypass")]
    [InlineData("Cyberpunk / Neon")]
    [InlineData("Golden Hour Warmth")]
    public void AdvancedColorGradeService_NewToneCurves_GenerateCurvesFilter(string curveName)
    {
        var settings = new ColorGradingSettings { ToneCurve = curveName };

        var filter = AdvancedColorGradeService.BuildFilterString(settings);

        Assert.NotNull(filter);
        Assert.Contains("curves=", filter);
    }

    [Fact]
    public void ColorPresetManager_BuiltInPresets_ContainCinematicAdditions()
    {
        var presets = ColorPresetManager.GetAllPresets();

        Assert.Contains(presets, p => p.Name.Contains("Portra 400"));
        Assert.Contains(presets, p => p.Name.Contains("Velvia"));
        Assert.Contains(presets, p => p.Name.Contains("Bleach Bypass"));
        Assert.Contains(presets, p => p.Name.Contains("Cyberpunk"));
        Assert.Contains(presets, p => p.Name.Contains("Golden Hour"));
    }

    [Fact]
    public void ColorPresetManager_Portra400_AppliesVibranceAndGamma()
    {
        var preset = ColorPresetManager.GetPreset("Kodak Portra 400 (Soft Warmth)");
        Assert.NotNull(preset);

        var settings = new ColorGradingSettings();
        settings.ApplyPreset(preset);

        Assert.Equal(1.02, settings.Gamma);
        Assert.Equal(0.08, settings.Vibrance);
        Assert.Equal("Kodak Portra 400", settings.ToneCurve);
    }

    [Fact]
    public void HistogramCalculator_NullOrEmptyBytes_ReturnsNoData()
    {
        var resultNull = HistogramCalculator.CalculateFromBmp(null);
        Assert.False(resultNull.HasData);
        Assert.Empty(resultNull.RedPath);

        var resultEmpty = HistogramCalculator.CalculateFromBmp(Array.Empty<byte>());
        Assert.False(resultEmpty.HasData);
    }

    [Fact]
    public void HistogramCalculator_ValidBmp_CalculatesBinsAndSvgPaths()
    {
        // Generate a minimal valid 24-bit uncompressed BMP (4x4 pixels, 16 pixels total)
        int width = 4;
        int height = 4;
        int bytesPerPixel = 3;
        int rowStride = ((width * bytesPerPixel + 3) / 4) * 4; // 12 bytes per row rounded to 12
        int pixelDataSize = rowStride * height;
        int fileSize = 54 + pixelDataSize;

        byte[] bmp = new byte[fileSize];
        // 'BM'
        bmp[0] = 0x42;
        bmp[1] = 0x4D;
        // File size
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        // Offset to pixel data
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        // DIB Header size (40)
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        // Width & Height
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        // Planes = 1
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        // Bits per pixel = 24
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);

        // Fill pixel data with known colors (e.g. B=50, G=100, R=200)
        for (int y = 0; y < height; y++)
        {
            int rowStart = 54 + y * rowStride;
            for (int x = 0; x < width; x++)
            {
                int p = rowStart + x * 3;
                bmp[p] = 50; // Blue
                bmp[p + 1] = 100; // Green
                bmp[p + 2] = 200; // Red
            }
        }

        var result = HistogramCalculator.CalculateFromBmp(bmp, splitRatio: 0.0);

        Assert.True(result.HasData);
        Assert.Equal(16, result.RedBins[200]);
        Assert.Equal(16, result.GreenBins[100]);
        Assert.Equal(16, result.BlueBins[50]);

        Assert.NotEmpty(result.RedPath);
        Assert.StartsWith("M 0,", result.RedPath);
        Assert.EndsWith("Z", result.RedPath);

        Assert.NotEmpty(result.LumaPath);
        Assert.StartsWith("M 0,", result.LumaPath);
    }
}
