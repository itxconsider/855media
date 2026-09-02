using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Gress;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;

namespace _855Media.Core.Downloading;

public class VideoDownloader(IReadOnlyList<Cookie>? initialCookies = null) : IDisposable
{
    private readonly YoutubeClient _youtube = new(Http.Client, initialCookies ?? []);

    public async Task<IReadOnlyList<VideoDownloadOption>> GetDownloadOptionsAsync(
        VideoId videoId,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(
                videoId,
                cancellationToken
            );
            var options = VideoDownloadOption.ResolveAll(
                manifest,
                includeLanguageSpecificAudioStreams
            );
            if (options.Count > 0)
                return options;
        }
        catch
        {
            // Fall back to default option if YoutubeExplode manifest parsing fails (HTTP 400 Bad Request / 403)
        }

        return [new VideoDownloadOption(YoutubeExplode.Videos.Streams.Container.Mp4, false, [])];
    }

    public async Task<VideoDownloadOption> GetBestDownloadOptionAsync(
        VideoId videoId,
        VideoDownloadPreference preference,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        var options = await GetDownloadOptionsAsync(
            videoId,
            includeLanguageSpecificAudioStreams,
            cancellationToken
        );

        return preference.TryGetBestOption(options)
            ?? new VideoDownloadOption(YoutubeExplode.Videos.Streams.Container.Mp4, false, []);
    }

    public async Task DownloadVideoAsync(
        string filePath,
        IVideo video,
        VideoDownloadOption downloadOption,
        bool includeSubtitles = true,
        string? ffmpegPath = null,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        // If fallback option without stream infos or YoutubeExplode fails, use YtDlp
        if (downloadOption.StreamInfos.Count == 0)
        {
            var arguments = new List<string>
            {
                "--no-playlist",
                "--format",
                downloadOption.Container.IsAudioOnly
                    ? "bestaudio/best"
                    : "bestvideo+bestaudio/best",
                "-o",
                filePath,
                $"https://www.youtube.com/watch?v={video.Id}",
            };

            var ffmpegCliPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
            if (!string.IsNullOrWhiteSpace(ffmpegCliPath))
            {
                arguments.Add("--ffmpeg-location");
                arguments.Add(ffmpegCliPath);
            }

            await YtDlp.RunAsync(arguments, progress, cancellationToken);
            return;
        }

        // Include subtitles in the output container
        var trackInfos = new List<ClosedCaptionTrackInfo>();
        if (includeSubtitles && !downloadOption.Container.IsAudioOnly)
        {
            try
            {
                var manifest = await _youtube.Videos.ClosedCaptions.GetManifestAsync(
                    video.Id,
                    cancellationToken
                );
                trackInfos.AddRange(manifest.Tracks);
            }
            catch
            {
                // Subtitles missing or unavailable
            }
        }

        try
        {
            await _youtube.Videos.DownloadAsync(
                downloadOption.StreamInfos,
                trackInfos,
                new ConversionRequestBuilder(filePath)
                    .SetFFmpegPath(ffmpegPath ?? FFmpeg.TryGetCliFilePath() ?? "ffmpeg")
                    .SetContainer(downloadOption.Container)
                    .SetPreset(ConversionPreset.Medium)
                    .Build(),
                progress?.ToDoubleBased(),
                cancellationToken
            );
        }
        catch
        {
            // Fallback to YtDlp if YoutubeExplode download fails midway
            var arguments = new List<string>
            {
                "--no-playlist",
                "--format",
                downloadOption.Container.IsAudioOnly
                    ? "bestaudio/best"
                    : "bestvideo+bestaudio/best",
                "-o",
                filePath,
                $"https://www.youtube.com/watch?v={video.Id}",
            };

            var ffmpegCliPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
            if (!string.IsNullOrWhiteSpace(ffmpegCliPath))
            {
                arguments.Add("--ffmpeg-location");
                arguments.Add(ffmpegCliPath);
            }

            await YtDlp.RunAsync(arguments, progress, cancellationToken);
        }
    }

    public void Dispose() => _youtube.Dispose();
}
