using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using _855Media.Core.Utils.Extensions;
using PowerKit.Extensions;
using YoutubeExplode.Videos;

namespace _855Media.Core.Tagging;

public class MediaTagInjector
{
    private readonly MusicBrainzClient _musicBrainz = new();

    private void InjectMiscMetadata(MediaFile mediaFile, VideoInfo video)
    {
        if (!string.IsNullOrWhiteSpace(video.Title))
            mediaFile.SetTitle(video.Title);

        if (!string.IsNullOrWhiteSpace(video.AuthorTitle))
            mediaFile.SetArtist(video.AuthorTitle);

        var description = (video.YoutubeVideo as Video)?.Description;
        if (!string.IsNullOrWhiteSpace(description))
            mediaFile.SetDescription(description);

        mediaFile.SetComment(
            $"""
            Downloaded using 855Media (https://github.com/itxconsider/MediaTag)
            Video: {video.Title}
            Video URL: {video.Url}
            Channel: {video.AuthorTitle}
            Channel URL: {video.AuthorUrl}
            """
        );
    }

    private async Task InjectMusicMetadataAsync(
        MediaFile mediaFile,
        VideoInfo video,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var recordings = await _musicBrainz.SearchRecordingsAsync(
                video.Title,
                cancellationToken
            );

            var recording = recordings.FirstOrDefault(r =>
                // Recording title must be a part of the video title.
                // Recording artist must be a part of the video title or channel title.
                video.Title.Contains(r.Title, StringComparison.OrdinalIgnoreCase)
                && (
                    video.Title.Contains(r.Artist, StringComparison.OrdinalIgnoreCase)
                    || (
                        !string.IsNullOrWhiteSpace(video.AuthorTitle)
                        && video.AuthorTitle.Contains(r.Artist, StringComparison.OrdinalIgnoreCase)
                    )
                )
            );

            if (recording is null)
                return;

            mediaFile.SetArtist(recording.Artist);
            mediaFile.SetTitle(recording.Title);

            if (!string.IsNullOrWhiteSpace(recording.ArtistSort))
                mediaFile.SetArtistSort(recording.ArtistSort);

            if (!string.IsNullOrWhiteSpace(recording.Album))
                mediaFile.SetAlbum(recording.Album);
        }
        catch (Exception)
        {
            // Ignore MusicBrainz lookup failure so basic title, artist, and comment metadata are preserved
        }
    }

    private async Task InjectThumbnailAsync(
        MediaFile mediaFile,
        VideoInfo video,
        CancellationToken cancellationToken = default
    )
    {
        var thumbnailUrl =
            video
                .YoutubeVideo?.Thumbnails.Where(t =>
                    string.Equals(t.TryGetImageFormat(), "jpg", StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(t => t.Resolution.Area)
                .Select(t => t.Url)
                .FirstOrDefault()
            ?? video.ThumbnailUrls.FirstOrDefault()
            ?? (
                video.Source == VideoSource.YouTube
                    ? $"https://i.ytimg.com/vi/{video.Id}/hqdefault.jpg"
                    : null
            );

        if (thumbnailUrl is null)
            return;

        try
        {
            mediaFile.SetThumbnail(
                await Http.Client.GetByteArrayAsync(thumbnailUrl, cancellationToken)
            );
        }
        catch (Exception)
        {
            // Ignore thumbnail download errors so remaining metadata tags are still saved
        }
    }

    public async Task InjectTagsAsync(
        string filePath,
        VideoInfo video,
        CancellationToken cancellationToken = default
    )
    {
        using var mediaFile = MediaFile.Open(filePath);

        InjectMiscMetadata(mediaFile, video);
        await InjectMusicMetadataAsync(mediaFile, video, cancellationToken);
        await InjectThumbnailAsync(mediaFile, video, cancellationToken);

        mediaFile.Save();
    }
}
