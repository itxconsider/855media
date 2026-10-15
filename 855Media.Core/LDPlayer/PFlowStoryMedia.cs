using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

internal sealed record PFlowStoryMedia(
    long MediaId,
    string Path,
    string Album,
    string Mime,
    long Size,
    long DurationMs
)
{
    internal bool Video => Mime.StartsWith("video/", StringComparison.Ordinal);

    internal static string AlbumFor(PFlowJob job) => "Story_" + job.JobId;

    internal static string Canonical(string path) =>
        path.StartsWith("/sdcard/", StringComparison.Ordinal)
            ? "/storage/emulated/0/" + path[8..]
            : path;

    internal static string Query(PFlowJob job)
    {
        ValidateJob(job);
        return "content query --uri content://media/external/file --projection _id:_data:_display_name:_size:mime_type:bucket_display_name:media_type:duration --where "
            + PFlowAutoPostService.ShellQuote("bucket_display_name='" + AlbumFor(job) + "'");
    }

    internal static void ValidateJob(PFlowJob job)
    {
        if (
            job.PostType != "Story"
            || !Guid.TryParseExact(job.JobId, "N", out _)
            || !Regex.IsMatch(job.Sha256, "^[0-9a-fA-F]{64}$")
            || job.Size <= 0
            || PFlowMediaIntent.MimeType(job.SourcePath) is not { } mime
            || !(
                mime.StartsWith("image/", StringComparison.Ordinal)
                || mime.StartsWith("video/", StringComparison.Ordinal)
            )
        )
            throw new InvalidOperationException(
                "Story staging requires a valid job and a fingerprinted photo/video."
            );
    }

    internal static PFlowStoryMedia? Read(string output, PFlowJob job)
    {
        ValidateJob(job);
        var rows = Regex.Matches(output, @"(?m)^Row:\s*\d+\s+([^\r\n]*)");
        if (rows.Count != 1)
            return null;
        var parts = rows[0]
            .Groups[1]
            .Value.Split(", ", StringSplitOptions.None)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToArray();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts)
            if (!fields.TryAdd(part[0].Trim(), part[1].Trim()))
                return null;
        string Field(string name) => fields.GetValueOrDefault(name, "");
        if (
            !long.TryParse(Field("_id"), out var id)
            || id <= 0
            || Canonical(Field("_data")) != Canonical(job.RemotePath)
            || Field("_display_name") != System.IO.Path.GetFileName(job.RemotePath)
            || Field("bucket_display_name") != AlbumFor(job)
            || Field("mime_type") != PFlowMediaIntent.MimeType(job.SourcePath)
            || !long.TryParse(Field("_size"), out var size)
            || size != job.Size
            || Field("media_type")
                != (Field("mime_type").StartsWith("video/", StringComparison.Ordinal) ? "3" : "1")
        )
            return null;
        _ = long.TryParse(Field("duration"), out var duration);
        return new(
            id,
            Canonical(job.RemotePath),
            AlbumFor(job),
            Field("mime_type"),
            size,
            duration
        );
    }
}
