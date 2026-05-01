using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gress;
using MediaTag.Core.Resolving;
using YoutubeExplode.Videos.Streams;

namespace MediaTag.Core.Downloading;

public class TikTokDownloader
{
    public async Task DownloadVideoAsync(
        string filePath,
        VideoInfo video,
        Container container,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = new List<string>
        {
            "--newline",
            "--force-overwrites",
            "--no-playlist",
            "--paths",
            "temp:.tmp",
            "--output",
            filePath,
        };

        if (container.IsAudioOnly)
        {
            arguments.Add("--extract-audio");
            arguments.Add("--audio-format");
            arguments.Add(container == Container.Mp3 ? "mp3" : "vorbis");
        }
        else
        {
            arguments.Add("--format");
            arguments.Add("bestvideo*+bestaudio/best");
            arguments.Add("--recode-video");
            arguments.Add(container.Name);
        }

        arguments.Add(video.Url);

        await YtDlp.RunAsync(arguments, progress, cancellationToken);
    }
}
