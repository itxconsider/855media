using System.Globalization;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AssignedInventoryAndFetchOptionsTests
{
    private const string Valid = "6,LD-6,123,124,1,1000,1001,720,1280,320";
    private const string BadOther = "11,LD-11,bad-window,0,0,-1,-1,720,1280,320";

    [Fact]
    public void UnrelatedMalformedRowBlocksLaunchInventoryButNotExactAssignedRow()
    {
        var data = Valid + "\n" + BadOther;
        Assert.Throws<LDInventoryUnavailableException>(() => LDPlayerService.ParseInventory(data));
        var row = Assert.Single(LDPlayerService.ParseAssignedInventory(data, 6));
        Assert.Equal(6, row.Index);
        Assert.Equal("LD-6", row.Name);
        Assert.Equal(1000, row.Pid);
    }

    [Theory]
    [InlineData("6,LD-6,bad,0,1,100,101")]
    [InlineData("bad-index,LD-X,0,0,0,-1,-1")]
    [InlineData("6,LD-6,0,0,1,100,101")]
    [InlineData("11,LD-11,0,0,0,-1,-1\n11,LD-11,0,0,0,-1,-1")]
    public void MalformedTargetUnknownIndexAndDuplicatesNeverAuthorizeAssignedActions(
        string extra
    ) =>
        Assert.Throws<LDInventoryUnavailableException>(() =>
            LDPlayerService.ParseAssignedInventory(Valid + "\n" + extra, 6)
        );

    [Fact]
    public void MissingAssignedIndexCannotUseAnotherRowOrSavedData() =>
        Assert.Throws<LDInventoryUnavailableException>(() =>
            LDPlayerService.ParseAssignedInventory(Valid, 7)
        );

    [Fact]
    public void ProcessSentinelsUseInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var display = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            display.NumberFormat.NegativeSign = "−";
            CultureInfo.CurrentCulture = display;
            var rows = LDPlayerService.ParseInventory(
                Valid + "\n11,LD-11,0,0,0,-1,-1,720,1280,320"
            );
            Assert.Equal(-1, rows[1].Pid);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task FullAndAssignedConsumersShareRawCommandWithoutSharingTheirValidation()
    {
        var coordinator = new LDInventoryCommandCoordinator();
        var ready = new TaskCompletionSource<(int, string, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var commands = 0;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "ldconsole.exe");
        Task<(int, string, string)> Command(CancellationToken _)
        {
            Interlocked.Increment(ref commands);
            return ready.Task;
        }
        var a = coordinator.ReadAsync(path, Command, default);
        var b = coordinator.ReadAsync(path, Command, default);
        ready.SetResult((0, Valid + "\n" + BadOther, ""));
        var results = await Task.WhenAll(a, b);
        Assert.Equal(1, commands);
        Assert.Throws<LDInventoryUnavailableException>(() =>
            LDPlayerService.ParseInventory(results[0].StdOut)
        );
        Assert.Single(LDPlayerService.ParseAssignedInventory(results[1].StdOut, 6));
    }

    [Fact]
    public void FetchSettingsDefaultOffAndRoundTrip()
    {
        var defaults = new AutoPostConfig();
        Assert.False(
            defaults.GetPagesByApi
                || defaults.SkipVerifiedFetchAccounts
                || defaults.SkipVerifiedFetchPages
        );
        var copy = JsonSerializer.Deserialize<AutoPostConfig>(
            JsonSerializer.Serialize(
                new AutoPostConfig
                {
                    GetPagesByApi = true,
                    SkipVerifiedFetchAccounts = true,
                    SkipVerifiedFetchPages = true,
                }
            )
        )!;
        Assert.True(
            copy.GetPagesByApi && copy.SkipVerifiedFetchAccounts && copy.SkipVerifiedFetchPages
        );
    }

    private static PFlowAccountBinding Binding =>
        new("fixture", 6, "LD-6", "com.facebook.katana", "100000000000001");
    private static FacebookPageTarget Page =>
        new()
        {
            AccountBinding = Binding,
            UserUid = Binding.OwnerUid,
            PageId = "100000000000002",
            PersonaUid = "100000000000002",
            PageName = "Page One",
            Type = "Page",
            LivePageAccessVerified = true,
        };

    [Theory]
    [InlineData("pending")]
    [InlineData("cached")]
    [InlineData("blocked")]
    [InlineData("wrong-owner")]
    public void UnverifiedOrConflictingPagesCannotBeSkipped(string mode)
    {
        var page = Page;
        if (mode == "pending")
            page.FetchDetailsPending = true;
        if (mode == "cached")
            page.LivePageAccessVerified = null;
        if (mode == "blocked")
            page.PageAccessIssue = "unavailable";
        if (mode == "wrong-owner")
            page.UserUid = "100000000000009";
        Assert.False(PFlowFetchSkipPolicy.Page(page));
    }

    [Fact]
    public void OnlyUniqueBoundVerifiedPrimaryProfileCanSkipWholeAccount()
    {
        var account = new PFlowPersonalAccount { Binding = Binding, Name = "Owner" };
        var primary = new FacebookPageTarget
        {
            AccountBinding = Binding,
            UserUid = Binding.OwnerUid,
            PageId = Binding.OwnerUid,
            PageName = "Owner",
            Type = "Profile",
        };
        Assert.True(PFlowFetchSkipPolicy.Account(account, [primary]));
        Assert.False(PFlowFetchSkipPolicy.Account(account, [primary, primary]));
        primary.FetchDetailsPending = true;
        Assert.False(PFlowFetchSkipPolicy.Account(account, [primary]));
    }

    [Theory]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    public async Task NativeFetchSkipsOnlyVerifiedPagesAndExplicitSinglePageRefreshOverridesSkipping(
        bool skip,
        bool single,
        int expected
    )
    {
        var device = new Device();
        var saved = Page;
        var result = await new PFlowNativeFetch(device).RunAsync(
            [saved],
            _ => Task.CompletedTask,
            default,
            onlyPageName: single ? "Page One" : null,
            skipVerifiedPages: skip
        );
        Assert.Equal(expected, device.PageReads);
        Assert.Equal(skip && !single ? new[] { saved.PageId } : [], result.SkippedPageIds);
        Assert.True(saved.LivePageAccessVerified);
        Assert.False(saved.FetchDetailsPending);
        Assert.DoesNotContain(
            result.Details!.Pages,
            p => skip && !single && p.PageId == saved.PageId
        );
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task SkippingProfileDetailsStillDiscoversMissingPages(bool skipPages, int expected)
    {
        var device = new Device();
        var result = await new PFlowNativeFetch(device).RunAsync(
            [Page],
            _ => Task.CompletedTask,
            default,
            readPersonalDetails: false,
            skipVerifiedPages: skipPages
        );
        Assert.Equal(0, device.PersonalReads);
        Assert.Equal(expected, device.PageReads);
        Assert.Contains(result.Details!.Pages, p => p.PageName == "Page Two");
        Assert.Equal(Binding.OwnerUid, result.Details.Uid);
    }

    private sealed class Device : IPFlowNativeFetchDevice
    {
        public int PageReads;
        public int PersonalReads;

        public Task<PFlowFetchActor[]> ScanAsync(CancellationToken ct) =>
            Task.FromResult(
                new[]
                {
                    new PFlowFetchActor("Owner", "Profile", true),
                    new("Page One", "Page"),
                    new("Page Two", "Page"),
                }
            );

        public Task<bool> SelectAsync(PFlowFetchActor actor, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<string?> ReadNumericIdAsync(PFlowFetchActor actor, CancellationToken ct) =>
            Task.FromResult<string?>(
                actor.Type == "Profile" ? Binding.OwnerUid
                : actor.Name == "Page One" ? "100000000000002"
                : "100000000000003"
            );

        public Task<FacebookAccountDetails?> PersonalDetailsAsync(
            string id,
            string name,
            CancellationToken ct
        )
        {
            PersonalReads++;
            return Task.FromResult<FacebookAccountDetails?>(
                new()
                {
                    Uid = id,
                    Name = name,
                    SwitchedToProfile = true,
                }
            );
        }

        public Task<FacebookPageInfo?> PageDetailsAsync(
            FacebookPageInfo page,
            string owner,
            string ownerName,
            CancellationToken ct
        )
        {
            PageReads++;
            return Task.FromResult<FacebookPageInfo?>(page);
        }
    }
}
