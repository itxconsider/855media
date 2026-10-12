using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public sealed record FacebookMediaSnapshot(IReadOnlyList<string> Files, string Error);

public partial class FacebookPageTarget
{
    public static readonly string[] ReelExtensions = [".mp4", ".mov", ".webm", ".mkv"];
    public static readonly string[] PhotoExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    [System.Text.Json.Serialization.JsonIgnore]
    public string ReelFilesError { get; private set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string PhotoFilesError { get; private set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string GroupFilesError { get; private set; } = "";

    public void ApplyMediaSnapshot(FacebookPageTarget snapshot)
    {
        ApplyMediaCounts(
            snapshot.UploadReelsCount,
            snapshot.UploadPictureCount,
            snapshot.FileInGroup
        );
        ReelFilesError = snapshot.ReelFilesError;
        PhotoFilesError = snapshot.PhotoFilesError;
        GroupFilesError = snapshot.GroupFilesError;
    }

    private void RefreshMediaSnapshot()
    {
        var reels = ReadUploadMediaSnapshot("Reel");
        var photos = ReadUploadMediaSnapshot("Picture");
        var groupReels = ReadMediaFiles([GroupReelPath, GroupVideoPath], ReelExtensions);
        var groupPhotos = ReadMediaFiles([GroupPicturePath], PhotoExtensions);
        ApplyMediaCounts(
            reels.Files.Count,
            photos.Files.Count,
            groupReels
                .Files.Concat(groupPhotos.Files)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString()
        );
        ReelFilesError = reels.Error;
        PhotoFilesError = photos.Error;
        GroupFilesError = string.Join(
            "; ",
            new[] { groupReels.Error, groupPhotos.Error }.Where(e => e.Length > 0)
        );
    }

    public void ApplyMediaCounts(int reels, int photos, string groupFiles)
    {
        UploadReelsCount = reels;
        UploadVideosCount = 0;
        UploadPictureCount = photos;
        FileInGroup = groupFiles;
    }

    public void NormalizeLegacyVideoSources()
    {
        if (string.IsNullOrWhiteSpace(VideoReelPath) && !string.IsNullOrWhiteSpace(VideoLongPath))
        {
            VideoReelPath = VideoLongPath;
            VideoLongPath = string.Empty;
            if (string.IsNullOrWhiteSpace(TitleVideoReel))
            {
                TitleVideoReel = TitleVideoLong;
                TitleVideoReelMode = TitleVideoLongMode;
            }
        }
        if (string.IsNullOrWhiteSpace(GroupReelPath) && !string.IsNullOrWhiteSpace(GroupVideoPath))
        {
            GroupReelPath = GroupVideoPath;
            GroupVideoPath = string.Empty;
        }
    }

    // Keep a distinct legacy source available as an additional Reel source.
    public IReadOnlyList<string> GetUploadMediaPaths(string type) =>
        (type == "Picture" ? new[] { ImagePath } : new[] { VideoReelPath, VideoLongPath })
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Trim('"', '\''))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<string> GetUploadMediaFiles(string type) =>
        ReadUploadMediaSnapshot(type).Files;

    public FacebookMediaSnapshot ReadUploadMediaSnapshot(string type) =>
        ReadMediaFiles(
            GetUploadMediaPaths(type),
            type == "Picture" ? PhotoExtensions : ReelExtensions
        );

    public string GetReelCaptionForFile(string file)
    {
        try
        {
            var source = VideoLongPath.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(TitleVideoLong))
            {
                var fullFile = Path.GetFullPath(file);
                var fullSource = Path.GetFullPath(source);
                if (
                    string.Equals(fullFile, fullSource, StringComparison.OrdinalIgnoreCase)
                    || fullFile.StartsWith(
                        fullSource.TrimEnd(Path.DirectorySeparatorChar)
                            + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return TitleVideoLong;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        { }
        return TitleVideoReel;
    }

    private static FacebookMediaSnapshot ReadMediaFiles(
        IEnumerable<string> paths,
        string[] extensions
    )
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var source in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var path = source.Trim().Trim('"', '\'');
            try
            {
                IEnumerable<string> files;
                if (File.Exists(path))
                    files = [path];
                else if (Directory.Exists(path))
                    files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
                else
                {
                    errors.Add("Source missing or inaccessible: " + path);
                    continue;
                }
                foreach (var file in files)
                    if (
                        extensions.Contains(
                            Path.GetExtension(file),
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                        result.Add(Path.GetFullPath(file));
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                )
            {
                errors.Add("Source unavailable: " + path + " (" + ex.Message + ")");
            }
        }
        return new(
            result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(),
            string.Join("; ", errors)
        );
    }
}
