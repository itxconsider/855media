using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.Upscaling;

internal static class NativeExportPolicy
{
    internal static void ReadSourceMetadata(
        UpscaleJob job,
        string probeOutput,
        double durationSeconds
    )
    {
        var video = Regex.Match(probeOutput, @"Stream[^\r\n]*Video:[^\r\n]*").Value;
        job.SourceVideoCodec = Regex.Match(video, @"Video:\s*(\w+)").Groups[1].Value;
        job.SourceVideoBitrate = ReadBitrate(video);
        var audioRates = Regex
            .Matches(probeOutput, @"Stream[^\r\n]*Audio:[^\r\n]*")
            .Select(match => ReadBitrate(match.Value))
            .ToArray();
        job.SourceAudioBitrate = audioRates.Where(rate => rate > 0).DefaultIfEmpty(0).Min();
        if (job.SourceVideoBitrate == 0 && video.Length > 0 && durationSeconds > 0)
        {
            // Some containers omit stream bitrate. Reserve known audio and container overhead.
            var audioBudget = audioRates.Sum(rate => rate > 0 ? rate : 128_000);
            job.SourceVideoBitrate = Math.Max(
                1_000,
                (long)(new FileInfo(job.FilePath).Length * 8.0 / durationSeconds * 0.98)
                    - audioBudget
            );
        }
    }

    private static long ReadBitrate(string stream)
    {
        var match = Regex.Match(stream, @"(?<rate>\d+(?:\.\d+)?)\s+kb/s");
        return
            match.Success
            && double.TryParse(
                match.Groups["rate"].Value,
                CultureInfo.InvariantCulture,
                out var rate
            )
            && rate > 0
            && rate < long.MaxValue / 1000.0
            ? (long)(rate * 1000)
            : 0;
    }

    internal static bool CanStreamCopy(UpscaleJob job) =>
        job.TargetResolution == UpscaleTargetResolution.Original1x
        && job.TargetAspectRatio == AspectRatioMode.Original
        && job.ModelType != UpscaleModelType.NvidiaRtx
        && job.TargetFramerate == TargetFramerate.Original
        && Math.Abs(job.PlaybackSpeed - 1.0) < 0.001
        && job.AudioMode == UpscaleAudioMode.CopyOriginal
        && !job.EnableDenoise
        && !job.EnableDeinterlace
        && !(job.EnableMicroZoom && job.MicroZoomPercent > 0)
        && !job.EnableFacialClarity
        && string.IsNullOrWhiteSpace(job.ColorGrading?.BuildFilterString())
        && job.SourceVideoCodec
            == (
                job.Codec switch
                {
                    UpscaleVideoCodec.H265 => "hevc",
                    UpscaleVideoCodec.Av1 => "av1",
                    _ => "h264",
                }
            );

    internal static string AudioBitrate(UpscaleJob job) =>
        Math.Min(job.SourceAudioBitrate > 0 ? job.SourceAudioBitrate : 128_000, 320_000)
            .ToString(CultureInfo.InvariantCulture);

    internal static string[] LimitVideoBitrate(string[] encoderArgs, long sourceBitrate)
    {
        if (sourceBitrate <= 0)
            return encoderArgs;
        var args = new List<string>();
        // Remove constant-quality modes before selecting constrained variable bitrate.
        var rateOptions = new HashSet<string>
        {
            "-crf",
            "-cq",
            "-global_quality",
            "-qp_i",
            "-qp_p",
            "-rc",
            "-b:v",
            "-maxrate",
            "-bufsize",
        };
        for (var i = 0; i < encoderArgs.Length; i++)
        {
            if (rateOptions.Contains(encoderArgs[i]))
            {
                i++;
                continue;
            }
            args.Add(encoderArgs[i]);
        }
        var encoderIndex = args.IndexOf("-c:v");
        var encoder = encoderIndex >= 0 ? args[encoderIndex + 1] : string.Empty;
        if (encoder.EndsWith("_nvenc", StringComparison.Ordinal))
            args.AddRange(["-rc", "vbr"]);
        else if (encoder.EndsWith("_amf", StringComparison.Ordinal))
            args.AddRange(["-rc", "vbr_peak"]);
        args.AddRange([
            "-b:v",
            sourceBitrate.ToString(CultureInfo.InvariantCulture),
            "-maxrate",
            sourceBitrate.ToString(CultureInfo.InvariantCulture),
            "-bufsize",
            (sourceBitrate * 2).ToString(CultureInfo.InvariantCulture),
        ]);
        return args.ToArray();
    }
}
