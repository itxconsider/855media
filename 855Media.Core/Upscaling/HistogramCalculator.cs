using System;
using System.Globalization;
using System.Text;

namespace _855Media.Core.Upscaling;

public class ColorHistogramResult
{
    public int[] RedBins { get; set; } = new int[256];
    public int[] GreenBins { get; set; } = new int[256];
    public int[] BlueBins { get; set; } = new int[256];
    public int[] LumaBins { get; set; } = new int[256];
    public bool HasData { get; set; }

    public string RedPath { get; set; } = string.Empty;
    public string GreenPath { get; set; } = string.Empty;
    public string BluePath { get; set; } = string.Empty;
    public string LumaPath { get; set; } = string.Empty;
}

public static class HistogramCalculator
{
    public const int DefaultViewWidth = 256;
    public const int DefaultViewHeight = 60;

    /// <summary>
    /// Computes 256-bin RGB and Luminance histograms from a raw BMP byte array and generates
    /// SVG mini-language path data suitable for Avalonia Path controls.
    /// </summary>
    /// <param name="bmpBytes">Raw BMP file bytes.</param>
    /// <param name="splitRatio">Ratio for split-screen frame; if analyzing graded portion (right side), samples from splitRatio to 1.0.</param>
    /// <param name="viewWidth">Canvas width coordinate space (default 256).</param>
    /// <param name="viewHeight">Canvas height coordinate space (default 60).</param>
    /// <returns>Populated ColorHistogramResult.</returns>
    public static ColorHistogramResult CalculateFromBmp(
        byte[]? bmpBytes,
        double splitRatio = 0.5,
        int viewWidth = DefaultViewWidth,
        int viewHeight = DefaultViewHeight
    )
    {
        var result = new ColorHistogramResult();
        if (bmpBytes == null || bmpBytes.Length < 54)
            return result;

        // Verify BMP magic header 'BM'
        if (bmpBytes[0] != 0x42 || bmpBytes[1] != 0x4D)
            return result;

        int bfOffBits = BitConverter.ToInt32(bmpBytes, 10);
        int biWidth = BitConverter.ToInt32(bmpBytes, 18);
        int biHeight = BitConverter.ToInt32(bmpBytes, 22);
        short biBitCount = BitConverter.ToInt16(bmpBytes, 28);

        if (biWidth <= 0 || biHeight == 0 || (biBitCount != 24 && biBitCount != 32))
            return result;

        int absHeight = Math.Abs(biHeight);
        bool isTopDown = biHeight < 0;
        int bytesPerPixel = biBitCount / 8;
        int rowStride = ((biWidth * bytesPerPixel + 3) / 4) * 4;

        if (bfOffBits < 0 || bfOffBits + (long)rowStride * absHeight > bmpBytes.Length)
            return result;

        // If splitRatio is specified, sample the graded region (right side of the split screen)
        int startX = 0;
        if (splitRatio > 0.0 && splitRatio < 1.0)
        {
            startX = (int)(biWidth * splitRatio);
            if (biWidth - startX < 16)
            {
                // If graded portion is too narrow, sample the whole frame
                startX = 0;
            }
        }

        int[] rBins = result.RedBins;
        int[] gBins = result.GreenBins;
        int[] bBins = result.BlueBins;
        int[] lumaBins = result.LumaBins;

        for (int y = 0; y < absHeight; y++)
        {
            int rowIdx = isTopDown ? y : (absHeight - 1 - y);
            int rowOffset = bfOffBits + rowIdx * rowStride;

            for (int x = startX; x < biWidth; x++)
            {
                int pixelOffset = rowOffset + x * bytesPerPixel;
                byte b = bmpBytes[pixelOffset];
                byte g = bmpBytes[pixelOffset + 1];
                byte r = bmpBytes[pixelOffset + 2];

                rBins[r]++;
                gBins[g]++;
                bBins[b]++;

                int luma = (299 * r + 587 * g + 114 * b) / 1000;
                if (luma > 255)
                    luma = 255;
                lumaBins[luma]++;
            }
        }

        result.HasData = true;

        // Find peak across bins 1..254 to prevent edge spikes (e.g. letterbox black) from crushing the curve
        int maxCount = 1;
        for (int i = 1; i < 255; i++)
        {
            if (rBins[i] > maxCount)
                maxCount = rBins[i];
            if (gBins[i] > maxCount)
                maxCount = gBins[i];
            if (bBins[i] > maxCount)
                maxCount = bBins[i];
            if (lumaBins[i] > maxCount)
                maxCount = lumaBins[i];
        }

        double sqrtMax = Math.Sqrt(maxCount);
        if (sqrtMax < 1.0)
            sqrtMax = 1.0;

        double usableHeight = Math.Max(10.0, viewHeight - 4.0);

        result.RedPath = BuildFilledAreaPath(rBins, sqrtMax, viewWidth, viewHeight, usableHeight);
        result.GreenPath = BuildFilledAreaPath(gBins, sqrtMax, viewWidth, viewHeight, usableHeight);
        result.BluePath = BuildFilledAreaPath(bBins, sqrtMax, viewWidth, viewHeight, usableHeight);
        result.LumaPath = BuildStrokeCurvePath(
            lumaBins,
            sqrtMax,
            viewWidth,
            viewHeight,
            usableHeight
        );

        return result;
    }

    private static string BuildFilledAreaPath(
        int[] bins,
        double sqrtMax,
        int viewWidth,
        int viewHeight,
        double usableHeight
    )
    {
        var sb = new StringBuilder(2048);
        sb.Append(CultureInfo.InvariantCulture, $"M 0,{viewHeight} ");

        for (int i = 0; i < 256; i++)
        {
            double normalized = Math.Sqrt(bins[i]) / sqrtMax;
            if (normalized > 1.0)
                normalized = 1.0;
            double y = viewHeight - (normalized * usableHeight);
            sb.Append(CultureInfo.InvariantCulture, $"L {i},{y:0.0} ");
        }

        sb.Append(CultureInfo.InvariantCulture, $"L {viewWidth - 1},{viewHeight} Z");
        return sb.ToString();
    }

    private static string BuildStrokeCurvePath(
        int[] bins,
        double sqrtMax,
        int viewWidth,
        int viewHeight,
        double usableHeight
    )
    {
        var sb = new StringBuilder(2048);

        for (int i = 0; i < 256; i++)
        {
            double normalized = Math.Sqrt(bins[i]) / sqrtMax;
            if (normalized > 1.0)
                normalized = 1.0;
            double y = viewHeight - (normalized * usableHeight);

            if (i == 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"M 0,{y:0.0} ");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"L {i},{y:0.0} ");
            }
        }

        return sb.ToString();
    }
}
