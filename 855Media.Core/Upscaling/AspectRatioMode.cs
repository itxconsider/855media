using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace _855Media.Core.Upscaling;

public enum AspectRatioMode
{
    [Display(Name = "Original (Match Source)")]
    Original,

    [Display(Name = "Vertical 9:16 (Shorts / Reels / TikTok) - Crop Fill")]
    Vertical916Crop,

    [Display(Name = "Vertical 9:16 (Shorts / Reels / TikTok) - Blurred Canvas")]
    Vertical916BlurredCanvas,

    [Display(Name = "Square 1:1 (Instagram Feed)")]
    Square11,

    [Display(Name = "Cinematic 21:9 (Widescreen)")]
    Cinematic219,
}

public static class AspectRatioFilterBuilder
{
    public static string? BuildFilter(
        AspectRatioMode mode,
        UpscaleTargetResolution targetResolution
    ) => BuildFilter(mode, targetResolution, SmartTrackingMode.StaticCenter, 0.5, 0.5);

    public static string? BuildFilter(
        AspectRatioMode mode,
        UpscaleTargetResolution targetResolution,
        SmartTrackingMode trackingMode,
        double actionCentroidX = 0.5,
        double actionCentroidY = 0.5
    )
    {
        bool is4k = targetResolution == UpscaleTargetResolution.Uhd4k;
        int targetW = is4k ? 2160 : 1080;
        int targetH = is4k ? 3840 : 1920;

        actionCentroidX = Math.Clamp(actionCentroidX, 0.05, 0.95);
        actionCentroidY = Math.Clamp(actionCentroidY, 0.05, 0.95);

        string xStr = actionCentroidX.ToString("0.000", CultureInfo.InvariantCulture);
        string yStr = actionCentroidY.ToString("0.000", CultureInfo.InvariantCulture);

        bool isSmart = trackingMode != SmartTrackingMode.StaticCenter;

        return mode switch
        {
            AspectRatioMode.Vertical916Crop => isSmart
                ? $"crop=w='min(iw,ih*9/16)':h='min(ih,iw*16/9)':x='clamp(iw*{xStr}-w/2,0,iw-w)':y='(ih-h)/2',scale={targetW}:{targetH}:flags=lanczos"
                : $"crop=w='min(iw,ih*9/16)':h='min(ih,iw*16/9)',scale={targetW}:{targetH}:flags=lanczos",

            AspectRatioMode.Vertical916BlurredCanvas =>
                $"split=2[bg][fg];[bg]scale={targetW}:{targetH}:flags=lanczos,boxblur=25:5[blurred];[fg]scale={targetW}:-2:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2",

            AspectRatioMode.Square11 => isSmart
                ? $"crop=w='min(iw,ih)':h='min(iw,ih)':x='clamp(iw*{xStr}-w/2,0,iw-w)':y='clamp(ih*{yStr}-h/2,0,ih-h)',scale={targetW}:{targetW}:flags=lanczos"
                : $"crop=w='min(iw,ih)':h='min(iw,ih)',scale={targetW}:{targetW}:flags=lanczos",

            AspectRatioMode.Cinematic219 => isSmart
                ? $"crop=w=iw:h='min(ih,iw*9/21)':x=0:y='clamp(ih*{yStr}-h/2,0,ih-h)',scale={(is4k ? 3840 : 2560)}:{(is4k ? 1640 : 1080)}:flags=lanczos"
                : $"crop=w=iw:h='min(ih,iw*9/21)',scale={(is4k ? 3840 : 2560)}:{(is4k ? 1640 : 1080)}:flags=lanczos",

            _ => null,
        };
    }

    public static string? BuildMicroZoomFilter(
        double zoomPercent,
        SmartZoomMode zoomMode = SmartZoomMode.ActionAnchored,
        double actionCentroidX = 0.5,
        double actionCentroidY = 0.5
    )
    {
        if (zoomPercent <= 0.0)
            return null;

        double factor = Math.Clamp(1.0 - (zoomPercent / 100.0), 0.90, 0.99);
        string factorStr = factor.ToString("0.##", CultureInfo.InvariantCulture);
        string xStr = Math.Clamp(actionCentroidX, 0.05, 0.95)
            .ToString("0.000", CultureInfo.InvariantCulture);
        string yStr = Math.Clamp(actionCentroidY, 0.05, 0.95)
            .ToString("0.000", CultureInfo.InvariantCulture);

        return zoomMode switch
        {
            SmartZoomMode.CenterCrop => $"crop=w='iw*{factorStr}':h='ih*{factorStr}'",

            SmartZoomMode.ActionAnchored =>
                $"crop=w='iw*{factorStr}':h='ih*{factorStr}':x='clamp(iw*{xStr}-w/2,0,iw-w)':y='clamp(ih*{yStr}-h/2,0,ih-h)'",

            SmartZoomMode.CinematicPushIn =>
                $"crop=w='iw*min(1.0,1.0-({zoomPercent.ToString("0.#", CultureInfo.InvariantCulture)}/100)*(t/60))':h='ih*min(1.0,1.0-({zoomPercent.ToString("0.#", CultureInfo.InvariantCulture)}/100)*(t/60))':x='clamp(iw*{xStr}-w/2,0,iw-w)':y='clamp(ih*{yStr}-h/2,0,ih-h)'",

            _ => $"crop=w='iw*{factorStr}':h='ih*{factorStr}'",
        };
    }
}
