using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.Editing;

internal static class GpuScalePolicy
{
    // Preserve effects and the RTX mode's sharpening rather than silently dropping them.
    internal static string? Filter(EditJob job)
    {
        if (
            job.ModelType != EditModelType.FastNative
            || job.Codec != EditVideoCodec.H264
            || job.TargetAspectRatio != AspectRatioMode.Original
            || job.EnableDeinterlace
            || job.EnableDenoise
            || job.EnableFacialClarity
            || (job.EnableMicroZoom && job.MicroZoomPercent > 0)
            || job.TargetFramerate != TargetFramerate.Original
            || Math.Abs(job.PlaybackSpeed - 1) >= 0.001
            || !string.IsNullOrWhiteSpace(job.ColorGrading?.BuildFilterString())
        )
            return null;

        var size = job.TargetResolution switch
        {
            EditTargetResolution.Hd1080p => "w=-2:h=1080",
            EditTargetResolution.Uhd4k => "w=-2:h=2160",
            EditTargetResolution.Scale2x => "w=iw*2:h=ih*2",
            EditTargetResolution.Scale4x => "w=iw*4:h=ih*4",
            _ => null,
        };
        return size is null ? null : $"scale_cuda={size}:interp_algo=bicubic:format=yuv420p";
    }

    internal static List<string>? BuildArguments(EditJob job, IReadOnlyList<string> original)
    {
        var filter = Filter(job);
        if (filter is null || !original.Contains("h264_nvenc") || !original.Contains("cuda"))
            return null;
        var result = new List<string>();
        for (var i = 0; i < original.Count; i++)
        {
            var arg = original[i];
            if (arg == "-pix_fmt")
            {
                i++;
                continue;
            } // Encoder consumes CUDA surfaces.
            result.Add(arg);
            if (arg == "-hwaccel")
            {
                result.Add(original[++i]);
                result.AddRange(["-hwaccel_output_format", "cuda"]);
            }
            else if (arg == "-vf")
            {
                i++;
                result.Add(filter);
            }
        }
        return result;
    }
}
