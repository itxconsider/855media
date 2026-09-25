using System;
using System.Collections.Generic;
using System.Linq;
using YoutubeExplode.Videos;

namespace _855Media.Core.Resolving;

public record VideoInfo(
    VideoSource Source,
    string Id,
    string Url,
    string Title,
    string AuthorTitle,
    string? AuthorUrl,
    long? ViewCount,
    TimeSpan? Duration,
    IReadOnlyList<string> ThumbnailUrls,
    IVideo? YoutubeVideo = null,
    string? AuthorAvatarUrl = null,
    long? LikeCount = null
)
{
    public static VideoInfo FromYoutube(IVideo video) =>
        new(
            VideoSource.YouTube,
            video.Id,
            video.Url,
            video.Title,
            video.Author.ChannelTitle,
            video.Author.ChannelUrl,
            video is Video fullVideo ? fullVideo.Engagement.ViewCount : null,
            video.Duration,
            video.Thumbnails.OrderByDescending(t => t.Resolution.Area).Select(t => t.Url).ToArray(),
            video
        );

    public string? FormattedViewCount =>
        ViewCount switch
        {
            >= 1_000_000_000 => $"{(ViewCount.Value / 1_000_000_000.0):0.#}B views",
            >= 1_000_000 => $"{(ViewCount.Value / 1_000_000.0):0.#}M views",
            >= 1_000 => $"{(ViewCount.Value / 1_000.0):0.#}K views",
            >= 0 => $"{ViewCount.Value:N0} views",
            _ => null,
        };

    public bool HasViewCount => ViewCount is >= 0;
}
