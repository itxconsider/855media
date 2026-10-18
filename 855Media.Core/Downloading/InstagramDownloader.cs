using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using Gress;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Downloading;

public class InstagramDownloader(IReadOnlyList<Cookie>? initialCookies = null)
{
    public async Task DownloadVideoAsync(
        string filePath,
        VideoInfo video,
        Container container,
        VideoDownloadOption? option = null,
        string? ffmpegPath = null,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default,
        VideoDownloadPreference? preference = null
    )
    {
        filePath = FileUtils.SanitizeFilePath(filePath);
        var audio = container.IsAudioOnly || option?.IsAudioOnly == true;
        if (!audio)
            ffmpegPath = MediaCompatibility.RequireVideoFFmpeg(filePath, ffmpegPath);
        else
            ffmpegPath ??= FFmpeg.TryGetCliFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var cookies = InstagramQueryResolver.ResolveCookies(initialCookies);
        var (path, temporary) = await YtDlp.TryCreateCookieFileAsync(cookies, cancellationToken);
        try
        {
            await YtDlp.RunAsync(
                BuildArguments(filePath, video, container, option, ffmpegPath, path, preference),
                progress,
                cancellationToken,
                captureOutput: false
            );
            if (!audio)
                await MediaCompatibility.EnsureWindowsCompatibleAsync(
                    filePath,
                    ffmpegPath,
                    cancellationToken
                );
        }
        finally
        {
            if (temporary && path is not null)
                File.Delete(path);
        }
    }

    internal static List<string> BuildArguments(
        string filePath,
        VideoInfo video,
        Container container,
        VideoDownloadOption? option = null,
        string? ffmpegPath = null,
        string? cookiePath = null,
        VideoDownloadPreference? preference = null
    )
    {
        var audio = container.IsAudioOnly || option?.IsAudioOnly == true;
        var args = new List<string>
        {
            "--ignore-config",
            "--newline",
            "--force-overwrites",
            "--referer",
            "https://www.instagram.com/",
        };
        if (video.PlaylistIndex is { } index)
            args.AddRange([
                "--playlist-items",
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ]);
        else
            args.Add("--no-playlist");
        args.AddRange([
            "--output",
            audio ? filePath : Path.ChangeExtension(filePath, null) + ".%(ext)s",
        ]);
        if (cookiePath is not null)
            args.AddRange(["--cookies", cookiePath]);
        if (ffmpegPath is not null)
            args.AddRange(["--ffmpeg-location", ffmpegPath]);
        if (audio)
            args.AddRange([
                "--format",
                "bestaudio/best",
                "--extract-audio",
                "--audio-format",
                container.Name,
            ]);
        else
        {
            args.AddRange([
                "--format",
                "bestvideo*+bestaudio/best",
                "--merge-output-format",
                "mp4",
                "--remux-video",
                "mp4",
            ]);
            var height =
                option?.VideoQuality?.MaxHeight ?? preference?.PreferredVideoQuality.GetMaxHeight();
            if (height is > 0)
                args.AddRange(["--format-sort", $"res:{height}"]);
            else if (preference?.PreferredVideoQuality == VideoQualityPreference.Lowest)
                args.AddRange(["--format-sort", "+res"]);
        }
        args.Add(video.Url);
        return args;
    }
}
