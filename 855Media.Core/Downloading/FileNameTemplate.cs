using System;
using System.IO;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Downloading;

public static class FileNameTemplate
{
    public static string Apply(
        string template,
        VideoInfo video,
        Container container,
        string? number = null
    )
    {
        var raw = (string.IsNullOrWhiteSpace(template) ? "$title" : template)
            .Replace("$numc", number ?? "", StringComparison.Ordinal)
            .Replace("$num", number is not null ? $"[{number}]" : "", StringComparison.Ordinal)
            .Replace("$id", video.Id, StringComparison.Ordinal)
            .Replace("$title", video.Title, StringComparison.Ordinal)
            .Replace("$author", video.AuthorTitle, StringComparison.Ordinal)
            .Replace(
                "$uploadDate",
                (video.YoutubeVideo as Video)?.UploadDate.ToString("yyyy-MM-dd") ?? "",
                StringComparison.Ordinal
            );

        var safeBaseName = FileUtils.SanitizeFileName(
            raw,
            fallback: string.IsNullOrWhiteSpace(video.Id) ? "video" : video.Id
        );
        return $"{safeBaseName}.{container.Name}";
    }
}
