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

    [Fact]
    public void AutoPostTaskItem_StatusChange_FiresPropertyChanged()
    {
        var task = new AutoPostTaskItem { Status = "Queued" };
        string? changedProp = null;
        task.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        task.Status = "In Progress";

        Assert.Equal("Status", changedProp);
        Assert.Equal("In Progress", task.Status);
    }

    [Theory]
    [InlineData("Joined Facebook in October 2021", 2021, 10, 1)]
    [InlineData("Account created: January 15, 2024", 2024, 1, 15)]
    [InlineData("Member since July 2023", 2023, 7, 1)]
    [InlineData("Joined 2020", 2020, 1, 1)]
    [InlineData("2024-05-10", 2024, 5, 10)]
    public void TryParseCreationDate_ValidFormats_ReturnsCorrectDate(
        string input,
        int expectedYear,
        int expectedMonth,
        int expectedDay
    )
    {
        var parsed = FacebookPageTarget.TryParseCreationDate(input);
        Assert.NotNull(parsed);
        Assert.Equal(expectedYear, parsed.Value.Year);
        Assert.Equal(expectedMonth, parsed.Value.Month);
        Assert.Equal(expectedDay, parsed.Value.Day);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not Set")]
    [InlineData("Not Discovered")]
    [InlineData("N/A")]
    public void TryParseCreationDate_InvalidOrUnset_ReturnsNull(string? input)
    {
        var parsed = FacebookPageTarget.TryParseCreationDate(input);
        Assert.Null(parsed);
    }

    [Fact]
    public void GetPageMaturity_AgedAccount_ReturnsMature()
    {
        var target = new FacebookPageTarget
        {
            CreationDate = DateTime.Now.AddDays(-120).ToString("yyyy-MM-dd"),
        };
        Assert.Equal(PageMaturityLevel.Mature, target.GetPageMaturity());
        Assert.Equal(10, target.GetMaxDailyPostsLimit());
    }

    [Fact]
    public void GetPageMaturity_DevelopingAccount_ReturnsGrowing()
    {
        var target = new FacebookPageTarget
        {
            CreationDate = DateTime.Now.AddDays(-50).ToString("yyyy-MM-dd"),
        };
        Assert.Equal(PageMaturityLevel.Growing, target.GetPageMaturity());
        Assert.Equal(5, target.GetMaxDailyPostsLimit());
    }

    [Fact]
    public void GetPageMaturity_BrandNewAccount_ReturnsNew()
    {
        var target = new FacebookPageTarget
        {
            CreationDate = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd"),
        };
        Assert.Equal(PageMaturityLevel.New, target.GetPageMaturity());
        Assert.Equal(3, target.GetMaxDailyPostsLimit());
    }

    [Fact]
    public void GetPageMaturity_MissingCreationDate_FallsBackToFollowersAndContent()
    {
        var matureByFollowers = new FacebookPageTarget
        {
            CreationDate = "Not Set",
            Followers = 600,
        };
        Assert.Equal(PageMaturityLevel.Mature, matureByFollowers.GetPageMaturity());

        var growingByPosts = new FacebookPageTarget
        {
            CreationDate = "",
            ReelsCount = 8,
            VideosCount = 4,
        };
        Assert.Equal(PageMaturityLevel.Growing, growingByPosts.GetPageMaturity());

        var brandNew = new FacebookPageTarget
        {
            CreationDate = "",
            Followers = 5,
            ReelsCount = 1,
        };
        Assert.Equal(PageMaturityLevel.New, brandNew.GetPageMaturity());
    }

    [Fact]
    public void CustomMaxDailyPosts_OverridesMaturityLimit()
    {
        var target = new FacebookPageTarget
        {
            CreationDate = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd"), // New -> default 3
            CustomMaxDailyPosts = 12,
        };
        Assert.Equal(12, target.GetMaxDailyPostsLimit());
    }

    [Fact]
    public void DailyPostsTracking_RecordsAndCalculatesLimitsCorrectly()
    {
        var target = new FacebookPageTarget
        {
            CreationDate = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd"), // New -> limit 3
        };

        Assert.Equal(0, target.GetPostsToday());
        Assert.False(target.HasReachedDailyLimit());

        target.RecordPostToday();
        target.RecordPostToday();
        Assert.Equal(2, target.GetPostsToday());
        Assert.False(target.HasReachedDailyLimit());

        target.RecordPostToday();
        Assert.Equal(3, target.GetPostsToday());
        Assert.True(target.HasReachedDailyLimit());

        // Yesterday's posts shouldn't count today
        target.LastPostDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
        Assert.Equal(0, target.GetPostsToday());
        Assert.False(target.HasReachedDailyLimit());
    }
}
