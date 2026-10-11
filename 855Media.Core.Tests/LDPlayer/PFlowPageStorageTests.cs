using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowPageStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "855Media_PageStorageTests_" + Guid.NewGuid().ToString("N")
    );

    private string FilePath => Path.Combine(_directory, "facebook_pages.json");

    public PFlowPageStorageTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task SaveAndLoad_PreservesDatesFacebookStatisticsAndPostingConfiguration()
    {
        var profile = new FacebookPageTarget
        {
            PageId = "profile-1",
            PersonaUid = "profile-1",
            UserUid = "profile-1",
            Type = "Profile",
            DeviceName = "LD-6",
            Birthdate = "March 23, 1999",
            CreationDate = "Joined Facebook September 2026",
        };
        var page = new FacebookPageTarget
        {
            PageId = "page-1",
            PersonaUid = "persona-1",
            UserUid = profile.UserUid,
            CreationDate = "Joined Facebook November 2022",
            Followers = 37,
            ReelsCount = 9,
            VideosCount = 4,
            PictureCount = 6,
            IsSelected = false,
            TitleVideoReel = "Saved title",
            CustomMaxDailyPosts = 2,
            PostsTodayCount = 1,
            LastPostDate = DateTime.Today.ToString("yyyy-MM-dd"),
        };

        await PFlowPageStorage.SavePagesAsync([profile, page], FilePath);
        var loaded = await PFlowPageStorage.LoadPagesAsync(FilePath);
        Assert.Equal(profile.Birthdate, loaded[0].Birthdate);
        Assert.Equal(profile.CreationDate, loaded[0].CreationDate);
        Assert.Equal(page.CreationDate, loaded[1].CreationDate);
        Assert.Equal(37, loaded[1].Followers);
        Assert.Equal(9, loaded[1].ReelsCount);
        Assert.Equal(4, loaded[1].VideosCount);
        Assert.Equal(6, loaded[1].PictureCount);
        Assert.False(loaded[1].IsSelected);
        Assert.Equal("Saved title", loaded[1].TitleVideoReel);
        Assert.Equal(2, loaded[1].CustomMaxDailyPosts);
        Assert.Equal(1, loaded[1].GetPostsToday());
    }

    [Fact]
    public async Task Load_DamagedMainFile_RecoversPreviousCompleteSave()
    {
        await PFlowPageStorage.SavePagesAsync(
            [new FacebookPageTarget { PageId = "previous", CreationDate = "November 2022" }],
            FilePath
        );
        await PFlowPageStorage.SavePagesAsync(
            [new FacebookPageTarget { PageId = "current" }],
            FilePath
        );
        await File.WriteAllTextAsync(FilePath, "[{\"PageId\":");

        var recovered = await PFlowPageStorage.LoadPagesAsync(FilePath);
        Assert.Equal("previous", Assert.Single(recovered).PageId);
        Assert.Equal("November 2022", recovered[0].CreationDate);
        await PFlowPageStorage.SavePagesAsync(recovered, FilePath);
        await File.WriteAllTextAsync(FilePath, "damaged again");
        Assert.Equal(
            "previous",
            Assert.Single(await PFlowPageStorage.LoadPagesAsync(FilePath)).PageId
        );
    }

    [Fact]
    public async Task SaveAndLoad_PreservesObservedZeroSeparatelyFromUnknownStatistics()
    {
        await PFlowPageStorage.SavePagesAsync(
            [
                new FacebookPageTarget
                {
                    PageId = "observed-zero",
                    Followers = 0,
                    FollowersKnown = true,
                },
                new FacebookPageTarget
                {
                    PageId = "not-read",
                    Followers = 0,
                    FollowersKnown = false,
                },
            ],
            FilePath
        );
        var loaded = await PFlowPageStorage.LoadPagesAsync(FilePath);
        Assert.Equal(0, loaded[0].Followers);
        Assert.True(loaded[0].FollowersKnown);
        Assert.Equal(0, loaded[1].Followers);
        Assert.False(loaded[1].FollowersKnown);
    }

    [Fact]
    public async Task Load_DamagedFileWithoutBackup_ReportsErrorInsteadOfReturningEmptyRows()
    {
        await File.WriteAllTextAsync(FilePath, "incomplete json");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PFlowPageStorage.LoadPagesAsync(FilePath)
        );
    }

    [Fact]
    public async Task ConcurrentSaves_AlwaysProduceOneCompleteSnapshot()
    {
        await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(batch =>
                    PFlowPageStorage.SavePagesAsync(
                        Enumerable
                            .Range(0, 20)
                            .Select(row => new FacebookPageTarget
                            {
                                PageId = $"{batch}-{row}",
                                DeviceName = $"LD-{batch}",
                                CreationDate = "November 2022",
                            }),
                        FilePath
                    )
                )
        );

        var rows = await PFlowPageStorage.LoadPagesAsync(FilePath);
        Assert.Equal(20, rows.Count);
        Assert.Single(rows.Select(row => row.DeviceName).Distinct());
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Save_BothCopiesUnreadable_PreservesOriginalFilesAndReportsError()
    {
        await File.WriteAllTextAsync(FilePath, "damaged main");
        await File.WriteAllTextAsync(FilePath + ".bak", "damaged backup");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PFlowPageStorage.SavePagesAsync(
                [new FacebookPageTarget { PageId = "new-data" }],
                FilePath
            )
        );
        Assert.Equal("damaged main", await File.ReadAllTextAsync(FilePath));
        Assert.Equal("damaged backup", await File.ReadAllTextAsync(FilePath + ".bak"));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Save_EmptyRows_RequiresSuccessfulLoadAndSupportsIntentionalDeletion()
    {
        await PFlowPageStorage.SavePagesAsync(
            [new FacebookPageTarget { PageId = "saved" }],
            FilePath
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PFlowPageStorage.SavePagesAsync([], FilePath)
        );
        Assert.Single(await PFlowPageStorage.LoadPagesAsync(FilePath));
        await PFlowPageStorage.SavePagesAsync([], FilePath);
        Assert.Empty(await PFlowPageStorage.LoadPagesAsync(FilePath));
    }

    [Fact]
    public async Task Save_EmptyFirstConfiguration_AfterSuccessfulMissingFileLoad_IsAllowed()
    {
        Assert.Empty(await PFlowPageStorage.LoadPagesAsync(FilePath));
        await PFlowPageStorage.SavePagesAsync([], FilePath);
        Assert.Empty(await PFlowPageStorage.LoadPagesAsync(FilePath));
    }
}
