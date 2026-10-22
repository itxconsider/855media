using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace _855Media.Core.Editing;

// A checkpoint certifies a successfully completed stage, never merely the presence of output.
internal static class EditCheckpoint
{
    private sealed record FileStamp(string Name, long Length, long LastWriteTicks);

    private sealed record Completion(string Key, FileStamp[] Files, long FrameCount = 0);

    internal static string Key(params object?[] values) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)))
        );

    internal static string SourceKey(string path)
    {
        var file = new FileInfo(path);
        return Key(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
    }

    internal static string RenderKey(EditJob job) =>
        Key(
            "source-limited-native-export-v1",
            SourceKey(job.FilePath),
            job.TargetResolution,
            job.TargetAspectRatio,
            job.Codec,
            job.HardwareAcceleration,
            job.ColorGrading?.BuildFilterString(),
            job.CameraMetadata,
            job.EnableDenoise,
            job.EnableDeinterlace,
            job.EnableMicroZoom,
            job.MicroZoomPercent,
            job.TrackingMode,
            job.ZoomMode,
            job.SpeedMode,
            job.PlaybackSpeed,
            job.TargetFramerate,
            job.AudioMode,
            job.ActivePresetName,
            job.ModelType,
            job.EnableFacialClarity,
            job.ActionCentroidX,
            job.ActionCentroidY
        );

    internal static bool IsFileComplete(string path, string key) =>
        IsComplete(path + ".complete.json", key, [path]);

    internal static long GetCompletedFileFrames(string path, string key) =>
        ReadCompletion(path + ".complete.json", key, [path])?.FrameCount ?? 0;

    internal static void CompleteFile(string path, string key, long frameCount = 0) =>
        Complete(path + ".complete.json", key, [path], frameCount);

    private static FileStamp Stamp(string path)
    {
        var file = new FileInfo(path);
        return new FileStamp(file.Name, file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private static bool IsComplete(string marker, string key, string[] paths) =>
        ReadCompletion(marker, key, paths) is not null;

    private static Completion? ReadCompletion(string marker, string key, string[] paths)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Completion>(File.ReadAllText(marker));
            return
                saved?.Key == key
                && saved.Files is not null
                && paths.Length > 0
                && paths.All(path => File.Exists(path) && new FileInfo(path).Length > 0)
                && saved.Files.SequenceEqual(paths.Select(Stamp))
                ? saved
                : null;
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or ArgumentException
            )
        {
            return null;
        }
    }

    private static void Complete(string marker, string key, string[] paths, long frameCount = 0)
    {
        var stamps = paths.Select(Stamp).ToArray();
        if (stamps.Length == 0 || stamps.Any(file => file.Length == 0))
            throw new InvalidOperationException("Cannot checkpoint empty output.");
        var tempPath = marker + ".tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(new Completion(key, stamps, frameCount))
        );
        File.Move(tempPath, marker, overwrite: true);
    }
}
