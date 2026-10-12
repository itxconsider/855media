using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookReelSourcesTests
{
    [Fact]
    public void MissingSourceIsDifferentFromEmptySourceAndRecoversWhenItReturns()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "855Media-missing-source-" + Guid.NewGuid().ToString("N")
        );
        var target = new FacebookPageTarget { VideoReelPath = folder };
        target.RefreshMediaCounts();
        Assert.Equal(0, target.UploadReelsCount);
        Assert.Contains("missing or inaccessible", target.ReelFilesError);
        Directory.CreateDirectory(folder);
        try
        {
            target.RefreshMediaCounts();
            Assert.Equal(0, target.UploadReelsCount);
            Assert.Empty(target.ReelFilesError);
            File.WriteAllText(Path.Combine(folder, "recovered.mp4"), "synthetic");
            target.RefreshMediaCounts();
            Assert.Equal(1, target.UploadReelsCount);
            Assert.Empty(target.ReelFilesError);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void PartialSourceAvailabilityIsReportedWithoutLosingKnownFiles()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "855Media-partial-source-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "clip.mp4"), "synthetic");
            var target = new FacebookPageTarget
            {
                VideoReelPath = folder,
                VideoLongPath = Path.Combine(folder, "missing"),
            };
            var snapshot = target.ReadUploadMediaSnapshot("Reel");
            Assert.Single(snapshot.Files);
            Assert.NotEmpty(snapshot.Error);
            target.RefreshMediaCounts();
            Assert.NotEmpty(target.ReelFilesError);
            Assert.Empty(target.PhotoFilesError);
            Assert.Empty(target.GroupFilesError);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void NoConfiguredSourceIsAValidZeroCount()
    {
        var target = new FacebookPageTarget();
        target.RefreshMediaCounts();
        Assert.Equal(0, target.UploadReelsCount);
        Assert.Empty(target.ReelFilesError);
        Assert.Empty(target.PhotoFilesError);
        Assert.Empty(target.GroupFilesError);
    }

    [Fact]
    public void CountsAndQueueSourcesAgreeAndDeduplicateFiles()
    {
        var source = Path.Combine(
            Path.GetTempPath(),
            "855Media-reel-sources-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        try
        {
            var reel = Path.Combine(source, "clip.MKV");
            File.WriteAllText(reel, "synthetic");
            File.WriteAllText(Path.Combine(source, "caption.txt"), "synthetic");
            File.WriteAllText(Path.Combine(source, "nested", "hidden.mp4"), "synthetic");
            var target = new FacebookPageTarget
            {
                VideoReelPath = source,
                VideoLongPath = reel,
                GroupReelPath = source,
                GroupVideoPath = reel,
                TitleVideoReel = "Primary caption",
                TitleVideoLong = "Additional caption",
            };
            target.RefreshMediaCounts();
            Assert.Equal(1, target.UploadReelsCount);
            Assert.Equal("1", target.FileInGroup);
            Assert.Equal(reel, Assert.Single(target.GetUploadMediaFiles("Reel")));
            Assert.Equal("Additional caption", target.GetReelCaptionForFile(reel));
            File.WriteAllText(Path.Combine(source, "next.mp4"), "synthetic");
            target.RefreshMediaCounts();
            Assert.Equal(2, target.UploadReelsCount);
            Assert.Equal("2", target.FileInGroup);
        }
        finally
        {
            Directory.Delete(source, true);
        }
    }

    [Fact]
    public void LegacyVideoOnlyConfigurationBecomesReelsWithoutLosingCaptionOrGroupSource()
    {
        var target = new FacebookPageTarget
        {
            VideoLongPath = "saved-video-folder",
            TitleVideoLong = "Saved caption",
            GroupVideoPath = "saved-group-folder",
        };
        target.NormalizeLegacyVideoSources();
        target.NormalizeLegacyVideoSources();
        Assert.Equal("saved-video-folder", target.VideoReelPath);
        Assert.Equal("Saved caption", target.TitleVideoReel);
        Assert.Equal("saved-group-folder", target.GroupReelPath);
        Assert.Empty(target.VideoLongPath);
        Assert.Empty(target.GroupVideoPath);
    }

    [Fact]
    public void DistinctLegacyVideoSourceIsRetainedAlongsideExistingReels()
    {
        var target = new FacebookPageTarget
        {
            VideoReelPath = "primary",
            VideoLongPath = "additional",
        };
        target.NormalizeLegacyVideoSources();
        Assert.Equal("primary", target.VideoReelPath);
        Assert.Equal("additional", target.VideoLongPath);
        Assert.Equal(2, target.GetUploadMediaPaths("Reel").Count);
    }
}
