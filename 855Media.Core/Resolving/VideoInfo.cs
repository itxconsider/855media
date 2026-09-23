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
    string? AuthorAvatarUrl = null
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
            null,
            video.Duration,
            video.Thumbnails.OrderByDescending(t => t.Resolution.Area).Select(t => t.Url).ToArray(),
            video
        );
}
