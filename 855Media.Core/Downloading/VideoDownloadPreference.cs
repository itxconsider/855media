using System;
using System.Collections.Generic;
using System.Linq;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Downloading;

public record VideoDownloadPreference(
    Container PreferredContainer,
    VideoQualityPreference PreferredVideoQuality,
    bool TranslateCaptionsToEnglish = false,
    bool TranslateTitleToEnglish = false
)
{
    public VideoDownloadOption? TryGetBestOption(IReadOnlyList<VideoDownloadOption> options)
    {
        // Short-circuit for audio-only formats
        if (PreferredContainer.IsAudioOnly)
            return options.FirstOrDefault(o => o.Container == PreferredContainer && o.IsAudioOnly);

        var preferredContainer = Container.Mp4;
        var orderedOptions = options
            .Where(o => !o.IsAudioOnly)
            .OrderBy(o => o.VideoQuality)
            .ToArray();

        var preferredOption = PreferredVideoQuality switch
        {
            VideoQualityPreference.Highest => orderedOptions.LastOrDefault(o =>
                o.Container == preferredContainer
            ),

            VideoQualityPreference.UpTo2160p => orderedOptions
                .Where(o => o.VideoQuality?.MaxHeight <= 2160)
                .LastOrDefault(o => o.Container == preferredContainer),

            VideoQualityPreference.UpTo1440p => orderedOptions
                .Where(o => o.VideoQuality?.MaxHeight <= 1440)
                .LastOrDefault(o => o.Container == preferredContainer),

            VideoQualityPreference.UpTo1080p => orderedOptions
                .Where(o => o.VideoQuality?.MaxHeight <= 1080)
                .LastOrDefault(o => o.Container == preferredContainer),

            VideoQualityPreference.UpTo720p => orderedOptions
                .Where(o => o.VideoQuality?.MaxHeight <= 720)
                .LastOrDefault(o => o.Container == preferredContainer),

            VideoQualityPreference.UpTo480p => orderedOptions
                .Where(o => o.VideoQuality?.MaxHeight <= 480)
                .LastOrDefault(o => o.Container == preferredContainer),

            VideoQualityPreference.UpTo360p => orderedOptions
                .Where(o => o.VideoQuality?.MaxHeight <= 360)
                .LastOrDefault(o => o.Container == preferredContainer),

            VideoQualityPreference.Lowest => orderedOptions.FirstOrDefault(o =>
                o.Container == preferredContainer
            ),

            _ => throw new InvalidOperationException(
                $"Unknown video quality preference '{PreferredVideoQuality}'."
            ),
        };

        return preferredOption
            ?? orderedOptions.FirstOrDefault(o => o.Container == preferredContainer);
    }
}
