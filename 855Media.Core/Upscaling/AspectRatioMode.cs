using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace _855Media.Core.Upscaling;

public enum AspectRatioMode
{
    [Display(Name = "Original (Match Source)")]
    Original = 0,

    [Display(Name = "Vertical 9:16 (Crop Fill)")]
    Vertical916Crop = 1,

    [Display(Name = "Vertical 9:16 (Fit Full + Blur)")]
    Vertical916BlurredCanvas = 2,

    [Display(Name = "Square 1:1 (Crop Fill)")]
    Square11 = 3,

    [Display(Name = "Cinematic 21:9 (Widescreen)")]
    Cinematic219 = 4,

    [Display(Name = "Vertical 9:16 (1:1 Crop + Blur)")]
    Vertical916SquareBlur = 5,

    [Display(Name = "Square 1:1 (Blurred Canvas)")]
    Square11BlurredCanvas = 6,
}

public static class AspectRatioFilterBuilder
{
    public static string? BuildFilter(
        AspectRatioMode mode,
        UpscaleTargetResolution targetResolution
    ) => BuildFilter(mode, targetResolution, SmartTrackingMode.StaticCenter, 0.5, 0.5, 0.0);

    public static string? BuildFilter(
        AspectRatioMode mode,
        UpscaleTargetResolution targetResolution,
        SmartTrackingMode trackingMode,
        double actionCentroidX = 0.5,
        double actionCentroidY = 0.5,
        double zoomPercent = 0.0
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

        int proxyW = (Math.Max(2, targetW / 4) / 2) * 2;
        int proxyH = (Math.Max(2, targetH / 4) / 2) * 2;

        bool hasZoom = zoomPercent > 0.0;
        double factor = hasZoom ? Math.Clamp(1.0 - (zoomPercent / 100.0), 0.70, 0.99) : 1.0;
        string factorStr = factor.ToString("0.###", CultureInfo.InvariantCulture);

        return mode switch
        {
            AspectRatioMode.Vertical916Crop => isSmart
                ? (
                    hasZoom
                        ? $"crop=w='min(iw,ih*9/16)*{factorStr}':h='min(ih,iw*16/9)*{factorStr}':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={targetW}:{targetH}:flags=lanczos,setsar=1"
                        : $"crop=w='min(iw,ih*9/16)':h='min(ih,iw*16/9)':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='(ih-out_h)/2',scale={targetW}:{targetH}:flags=lanczos,setsar=1"
                )
                : (
                    hasZoom
                        ? $"crop=w='min(iw,ih*9/16)*{factorStr}':h='min(ih,iw*16/9)*{factorStr}',scale={targetW}:{targetH}:flags=lanczos,setsar=1"
                        : $"crop=w='min(iw,ih*9/16)':h='min(ih,iw*16/9)',scale={targetW}:{targetH}:flags=lanczos,setsar=1"
                ),

            AspectRatioMode.Vertical916BlurredCanvas =>
                $"format=yuv420p,split=2[bg][fg];[bg]scale={targetW}:{targetH}:force_original_aspect_ratio=increase:flags=bilinear,crop={targetW}:{targetH},scale={proxyW}:{proxyH},boxblur=10:2,scale={targetW}:{targetH}:flags=bilinear[blurred];[fg]scale={targetW}:-2:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2,setsar=1",

            AspectRatioMode.Vertical916SquareBlur => isSmart
                ? (
                    hasZoom
                        ? $"format=yuv420p,split=2[bg][fg];[bg]scale={targetW}:{targetH}:force_original_aspect_ratio=increase:flags=bilinear,crop={targetW}:{targetH},scale={proxyW}:{proxyH},boxblur=10:2,scale={targetW}:{targetH}:flags=bilinear[blurred];[fg]crop=w='min(iw,ih)*{factorStr}':h='min(iw,ih)*{factorStr}':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={targetW}:{targetW}:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2,setsar=1"
                        : $"format=yuv420p,split=2[bg][fg];[bg]scale={targetW}:{targetH}:force_original_aspect_ratio=increase:flags=bilinear,crop={targetW}:{targetH},scale={proxyW}:{proxyH},boxblur=10:2,scale={targetW}:{targetH}:flags=bilinear[blurred];[fg]crop=w='min(iw,ih)':h='min(iw,ih)':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={targetW}:{targetW}:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2,setsar=1"
                )
                : (
                    hasZoom
                        ? $"format=yuv420p,split=2[bg][fg];[bg]scale={targetW}:{targetH}:force_original_aspect_ratio=increase:flags=bilinear,crop={targetW}:{targetH},scale={proxyW}:{proxyH},boxblur=10:2,scale={targetW}:{targetH}:flags=bilinear[blurred];[fg]crop=w='min(iw,ih)*{factorStr}':h='min(iw,ih)*{factorStr}',scale={targetW}:{targetW}:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2,setsar=1"
                        : $"format=yuv420p,split=2[bg][fg];[bg]scale={targetW}:{targetH}:force_original_aspect_ratio=increase:flags=bilinear,crop={targetW}:{targetH},scale={proxyW}:{proxyH},boxblur=10:2,scale={targetW}:{targetH}:flags=bilinear[blurred];[fg]crop=w='min(iw,ih)':h='min(iw,ih)',scale={targetW}:{targetW}:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2,setsar=1"
                ),

            AspectRatioMode.Square11 => isSmart
                ? (
                    hasZoom
                        ? $"crop=w='min(iw,ih)*{factorStr}':h='min(iw,ih)*{factorStr}':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={targetW}:{targetW}:flags=lanczos,setsar=1"
                        : $"crop=w='min(iw,ih)':h='min(iw,ih)':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={targetW}:{targetW}:flags=lanczos,setsar=1"
                )
                : (
                    hasZoom
                        ? $"crop=w='min(iw,ih)*{factorStr}':h='min(iw,ih)*{factorStr}',scale={targetW}:{targetW}:flags=lanczos,setsar=1"
                        : $"crop=w='min(iw,ih)':h='min(iw,ih)',scale={targetW}:{targetW}:flags=lanczos,setsar=1"
                ),

            AspectRatioMode.Square11BlurredCanvas =>
                $"format=yuv420p,split=2[bg][fg];[bg]scale={targetW}:{targetH}:force_original_aspect_ratio=increase:flags=bilinear,crop={targetW}:{targetH},scale={proxyW}:{proxyW},boxblur=10:2,scale={targetW}:{targetH}:flags=bilinear[blurred];[fg]scale={targetW}:{targetW}:force_original_aspect_ratio=decrease:flags=lanczos[scaled];[blurred][scaled]overlay=(W-w)/2:(H-h)/2,setsar=1",

            AspectRatioMode.Cinematic219 => isSmart
                ? (
                    hasZoom
                        ? $"crop=w='iw*{factorStr}':h='min(ih,iw*9/21)*{factorStr}':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={(is4k ? 3840 : 2560)}:{(is4k ? 1640 : 1080)}:flags=lanczos,setsar=1"
                        : $"crop=w=iw:h='min(ih,iw*9/21)':x=0:y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))',scale={(is4k ? 3840 : 2560)}:{(is4k ? 1640 : 1080)}:flags=lanczos,setsar=1"
                )
                : (
                    hasZoom
                        ? $"crop=w='iw*{factorStr}':h='min(ih,iw*9/21)*{factorStr}',scale={(is4k ? 3840 : 2560)}:{(is4k ? 1640 : 1080)}:flags=lanczos,setsar=1"
                        : $"crop=w=iw:h='min(ih,iw*9/21)',scale={(is4k ? 3840 : 2560)}:{(is4k ? 1640 : 1080)}:flags=lanczos,setsar=1"
                ),

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

        double factor = Math.Clamp(1.0 - (zoomPercent / 100.0), 0.70, 0.99);
        string factorStr = factor.ToString("0.###", CultureInfo.InvariantCulture);
        string xStr = Math.Clamp(actionCentroidX, 0.05, 0.95)
            .ToString("0.000", CultureInfo.InvariantCulture);
        string yStr = Math.Clamp(actionCentroidY, 0.05, 0.95)
            .ToString("0.000", CultureInfo.InvariantCulture);

        return zoomMode switch
        {
            SmartZoomMode.CenterCrop => $"crop=w='iw*{factorStr}':h='ih*{factorStr}'",

            SmartZoomMode.ActionAnchored =>
                $"crop=w='iw*{factorStr}':h='ih*{factorStr}':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2))'",

            SmartZoomMode.CinematicPushIn =>
                $"crop=w='iw*{factorStr}':h='ih*{factorStr}':x='max(0,min(iw-out_w,iw*{xStr}-out_w/2+sin(t/10)*15))':y='max(0,min(ih-out_h,ih*{yStr}-out_h/2+cos(t/10)*15))'",

            _ => $"crop=w='iw*{factorStr}':h='ih*{factorStr}'",
        };
    }
}
