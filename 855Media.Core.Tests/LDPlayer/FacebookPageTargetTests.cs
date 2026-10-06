using System;
using System.IO;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookPageTargetTests : IDisposable
{
    private readonly string _tempDir;

    public FacebookPageTargetTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "855Media_TargetTests_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void CountMediaFiles_NullOrEmptyPath_ReturnsZero(string? path)
    {
        var count = FacebookPageTarget.CountMediaFiles(path, [".mp4", ".mov"]);
        Assert.Equal(0, count);
    }

    [Fact]
    public void CountMediaFiles_NonExistentPath_ReturnsZero()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist_folder");
        var count = FacebookPageTarget.CountMediaFiles(nonExistent, [".mp4"]);
        Assert.Equal(0, count);
    }

    [Fact]
    public void CountMediaFiles_SingleFile_ReturnsOneIfValidExtension()
    {
        var filePath = Path.Combine(_tempDir, "video1.mp4");
        File.WriteAllText(filePath, "dummy");

        var count = FacebookPageTarget.CountMediaFiles(filePath, [".mp4", ".mov"]);
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountMediaFiles_SingleFile_ReturnsZeroIfInvalidExtension()
    {
        var filePath = Path.Combine(_tempDir, "video1.txt");
        File.WriteAllText(filePath, "dummy");

        var count = FacebookPageTarget.CountMediaFiles(filePath, [".mp4", ".mov"]);
        Assert.Equal(0, count);
    }

    [Fact]
    public void CountMediaFiles_QuotedPath_StripsQuotesAndCounts()
    {
        var filePath = Path.Combine(_tempDir, "photo1.jpg");
        File.WriteAllText(filePath, "dummy");

        var quoted = $"\"{filePath}\"";
        var count = FacebookPageTarget.CountMediaFiles(quoted, [".jpg", ".png"]);
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountMediaFiles_Directory_CountsOnlyValidFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.mp4"), "dummy");
        File.WriteAllText(Path.Combine(_tempDir, "b.MOV"), "dummy");
        File.WriteAllText(Path.Combine(_tempDir, "c.webm"), "dummy");
        File.WriteAllText(Path.Combine(_tempDir, "d.txt"), "dummy");
        File.WriteAllText(Path.Combine(_tempDir, "e.jpg"), "dummy");

        var count = FacebookPageTarget.CountMediaFiles(_tempDir, [".mp4", ".mov", ".webm"]);
        Assert.Equal(3, count);
    }

    [Fact]
    public void RefreshMediaCounts_ComputesAllCategoriesCorrectly()
    {
        var reelsDir = Path.Combine(_tempDir, "reels");
        var videosDir = Path.Combine(_tempDir, "videos");
        var picturesDir = Path.Combine(_tempDir, "pictures");
        var groupReelDir = Path.Combine(_tempDir, "group_reels");

        Directory.CreateDirectory(reelsDir);
        Directory.CreateDirectory(videosDir);
        Directory.CreateDirectory(picturesDir);
        Directory.CreateDirectory(groupReelDir);

        File.WriteAllText(Path.Combine(reelsDir, "reel1.mp4"), "content");
        File.WriteAllText(Path.Combine(reelsDir, "reel2.mov"), "content");

        File.WriteAllText(Path.Combine(videosDir, "vid1.mkv"), "content");

        File.WriteAllText(Path.Combine(picturesDir, "pic1.png"), "content");
        File.WriteAllText(Path.Combine(picturesDir, "pic2.jpg"), "content");
        File.WriteAllText(Path.Combine(picturesDir, "pic3.webp"), "content");

        File.WriteAllText(Path.Combine(groupReelDir, "greel1.mp4"), "content");

        var target = new FacebookPageTarget
        {
            VideoReelPath = reelsDir,
            VideoLongPath = videosDir,
            ImagePath = picturesDir,
            GroupReelPath = groupReelDir,
        };

        target.RefreshMediaCounts();

        Assert.Equal(2, target.ReelsCount);
        Assert.Equal(1, target.VideosCount);
        Assert.Equal(3, target.PictureCount);
        Assert.Equal("1", target.FileInGroup);
    }

    [Fact]
    public void RefreshMediaCounts_EmptyPaths_SetsZeros()
    {
        var target = new FacebookPageTarget();
        target.RefreshMediaCounts();

        Assert.Equal(0, target.ReelsCount);
        Assert.Equal(0, target.VideosCount);
        Assert.Equal(0, target.PictureCount);
        Assert.Equal("0", target.FileInGroup);
    }
}
