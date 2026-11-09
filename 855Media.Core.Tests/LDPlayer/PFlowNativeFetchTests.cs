using System.Net;
using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNativeFetchTests
{
    const string Owner = "100000000000001";

    [Fact]
    public async Task CollectedNamesPrecedeOwnerLinkAndPageDetailsUpdates()
    {
        var d = new Device(2);
        var stages = new List<string>();
        await new PFlowNativeFetch(d).RunAsync(
            [],
            Progress,
            default,
            collected: update =>
            {
                if (update.OwnerId == null)
                {
                    Assert.Equal(0, d.PersonalReads + d.PageReads);
                    Assert.Equal(2, update.Actors.Count(a => a.Type == "Page"));
                    stages.Add("names");
                }
                else if (update.Page == null)
                    stages.Add("owner");
                else
                    stages.Add(
                        update.Page.PageName
                            + ":"
                            + (update.Page.LivePageAccessVerified == true ? "details" : "id")
                    );
                return Task.CompletedTask;
            }
        );
        Assert.Equal(
            new[]
            {
                "names",
                "owner",
                "Page 1:id",
                "Page 1:details",
                "Page 2:id",
                "Page 2:details",
            },
            stages
        );
    }

    [Fact]
    public async Task StoppingAfterCollectedPageIdKeepsEarlierUpdatesAndRestoresOwner()
    {
        var d = new Device(1);
        var collected = new List<string>();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new PFlowNativeFetch(d).RunAsync(
                [],
                Progress,
                default,
                collected: update =>
                {
                    collected.Add(update.Page?.PageName ?? "names");
                    if (update.Page != null)
                        throw new OperationCanceledException();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Contains("Page 1", collected);
        Assert.Equal(0, d.PageReads);
        Assert.Equal("Profile", d.Selected.Last().Type);
    }

    static Task Progress(string _) => Task.CompletedTask;

    [Fact]
    public async Task FetchProgressUsesPlainAccountAndPageMessages()
    {
        var messages = new List<string>();
        await new PFlowNativeFetch(new Device(1)).RunAsync(
            [],
            message =>
            {
                messages.Add(message);
                return Task.CompletedTask;
            },
            default
        );
        Assert.Contains("Account confirmed: Owner Name.", messages);
        Assert.Contains("Page details read: Page 1.", messages);
        Assert.All(
            messages,
            message =>
            {
                Assert.DoesNotContain("Graph", message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("root", message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UID source", message, StringComparison.OrdinalIgnoreCase);
            }
        );
    }

    static FacebookPageTarget[] Saved(int pages) =>
        new[]
        {
            new FacebookPageTarget
            {
                Type = "Profile",
                PageName = "Owner Name",
                UserUid = Owner,
                PageId = Owner,
                DeviceName = "LD-1",
            },
        }
            .Concat(
                Enumerable
                    .Range(1, pages)
                    .Select(i => new FacebookPageTarget
                    {
                        Type = "Page",
                        PageName = "Page " + i,
                        UserUid = Owner,
                        PageId = (200000000000000L + i).ToString(),
                        PersonaUid = (300000000000000L + i).ToString(),
                        DeviceName = "LD-1",
                    })
            )
            .ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public async Task NewAccountEnumeratesZeroOneManyPagesFromNativeNumericLinks(int pages)
    {
        var d = new Device(pages);
        var r = await new PFlowNativeFetch(d).RunAsync([], Progress, default);
        Assert.Equal(Owner, r.Details!.Uid);
        Assert.Equal(pages, r.Details.Pages.Count);
        Assert.All(r.Details.Pages, p => Assert.True(p.LivePageAccessVerified));
        Assert.Empty(r.Unresolved);
        Assert.Equal("Owner Name", d.Selected.Last().Name);
    }

    [Fact]
    public async Task UnresolvedSiblingIsReportedWithoutGuessingOrBlockingKnownPage()
    {
        var d = new Device(1);
        d.Actors = d.Actors.Append(new("Unresolved sibling", "Unknown")).ToArray();
        var r = await new PFlowNativeFetch(d).RunAsync([], Progress, default);
        Assert.Single(r.Details!.Pages);
        Assert.Equal(["Unresolved sibling"], r.Unresolved);
        Assert.DoesNotContain(d.Selected, a => a.Name == "Unresolved sibling");
    }

    [Fact]
    public async Task SinglePageRefreshReadsOnlyRequestedPageAndRestoresOwner()
    {
        var d = new Device(7);
        var r = await new PFlowNativeFetch(d).RunAsync(
            Saved(7),
            Progress,
            default,
            "Owner Name",
            Owner,
            onlyPageName: "Page 3",
            readPersonalDetails: false
        );
        Assert.Equal("Page 3", Assert.Single(r.Details!.Pages).PageName);
        Assert.Equal(0, d.PersonalReads);
        Assert.Equal(1, d.PageReads);
        Assert.Equal("Page 3", Assert.Single(d.Selected, a => a.Type == "Page").Name);
        Assert.Equal("Profile", d.Selected.Last().Type);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Owner Name")]
    public async Task SinglePageRefreshCannotSubstituteAnotherActor(string name)
    {
        var d = new Device(7);
        var r = await new PFlowNativeFetch(d).RunAsync(
            Saved(7),
            Progress,
            default,
            "Owner Name",
            Owner,
            onlyPageName: name,
            readPersonalDetails: false
        );
        Assert.Empty(r.Details!.Pages);
        Assert.Equal(name, Assert.Single(r.Unresolved));
        Assert.Equal(0, d.PageReads + d.PersonalReads);
        Assert.DoesNotContain(d.Selected, a => a.Type == "Page");
    }

    [Fact]
    public async Task SinglePageRefreshStillRejectsWrongOwnerBeforeReadingPages()
    {
        var d = new Device(7);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetch(d).RunAsync(
                Saved(7),
                Progress,
                default,
                "Other",
                Owner,
                onlyPageName: "Page 3",
                readPersonalDetails: false
            )
        );
        Assert.Equal(0, d.PageReads + d.PersonalReads);
    }

    [Fact]
    public async Task KnownIdsAreRoutingOnlyAndNeedTheSameUniqueOwnerAndPageNames()
    {
        var d = new Device(3) { NoIds = true };
        var logs = new List<string>();
        var r = await new PFlowNativeFetch(d).RunAsync(
            Saved(3),
            s =>
            {
                logs.Add(s);
                return Task.CompletedTask;
            },
            default,
            "OwnerName",
            Owner
        );
        Assert.Equal(3, r.Details!.Pages.Count);
        Assert.Contains(logs, s => s.Contains("using saved account details"));
        Assert.Equal(Saved(3)[1].PageId, r.Details.Pages[0].PageId);
    }

    [Fact]
    public async Task UnknownOwnerIdIsNotGeneratedFromNamesOrPositions()
    {
        var d = new Device(3) { NoIds = true };
        var r = await new PFlowNativeFetch(d).RunAsync([], Progress, default);
        Assert.Null(r.Details);
        Assert.Equal(4, r.Unresolved.Length);
        Assert.Equal(0, d.PersonalReads + d.PageReads);
    }

    [Fact]
    public async Task UnknownPageIdsStayUnresolvedWhileOwnerCanBeSaved()
    {
        var d = new Device(2) { NoPageIds = true };
        var r = await new PFlowNativeFetch(d).RunAsync([], Progress, default);
        Assert.Empty(r.Details!.Pages);
        Assert.Equal(2, r.Unresolved.Length);
        Assert.Equal(0, d.PageReads);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("two-profiles")]
    [InlineData("no-selection")]
    [InlineData("unknown-type")]
    [InlineData("empty")]
    public async Task IncompleteOrAmbiguousListCannotStartDiscovery(string mutation)
    {
        var d = new Device(1);
        d.Actors = mutation switch
        {
            "duplicate" => [new("Owner Name", "Profile", true), new("OwnerName", "Page")],
            "truncated" => [new("Owner Name", "Profile", true), new("Page…", "Page")],
            "two-profiles" => [new("Owner Name", "Profile", true), new("Other", "Profile")],
            "no-selection" => [new("Owner Name", "Profile")],
            "unknown-type" => [new("Owner Name", "Unknown", true)],
            _ => [],
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetch(d).RunAsync([], Progress, default)
        );
        Assert.Empty(d.Selected);
    }

    [Theory]
    [InlineData("owner-name")]
    [InlineData("owner-id")]
    [InlineData("personal-details")]
    public async Task WrongOwnerOrDetailsNeverSaveAnotherAccount(string mutation)
    {
        var d = new Device(1) { WrongPersonal = mutation == "personal-details" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetch(d).RunAsync(
                Saved(1),
                Progress,
                default,
                mutation == "owner-name" ? "Other" : null,
                mutation == "owner-id" ? "999999999999999" : null
            )
        );
        Assert.Equal(0, d.PageReads);
    }

    [Theory]
    [InlineData("native-id")]
    [InlineData("detail-uid")]
    [InlineData("detail-name")]
    [InlineData("switch")]
    public async Task ChangedPageEvidenceRetainsKnownCandidateUnverified(string mutation)
    {
        var d = new Device(1) { Failure = mutation };
        var r = await new PFlowNativeFetch(d).RunAsync(Saved(1), Progress, default);
        var page = Assert.Single(r.Details!.Pages);
        Assert.False(page.LivePageAccessVerified);
        Assert.Equal(Saved(1)[1].PageId, page.PageId);
        Assert.Single(r.Unresolved);
    }

    [Fact]
    public async Task DifferentOwnersSavedPagesCannotSupplyAnUnresolvedPageId()
    {
        var d = new Device(1) { NoPageIds = true };
        var rows = Saved(1);
        rows[1].UserUid = "999999999999999";
        var r = await new PFlowNativeFetch(d).RunAsync(rows, Progress, default);
        Assert.Empty(r.Details!.Pages);
        Assert.Single(r.Unresolved);
    }

    [Fact]
    public async Task CancelDuringPageSelectionRestoresPersonalActor()
    {
        using var cancel = new CancellationTokenSource();
        var d = new Device(1) { Cancel = cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowNativeFetch(d).RunAsync([], Progress, cancel.Token)
        );
        Assert.Equal("Profile", d.Selected.Last().Type);
    }

    [Fact]
    public async Task NativePolicySkipsBothGraphReadersWithoutAnyDeviceOrHttpRead()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DenyDevice>();
        var d = (DenyDevice)(object)ld;
        using var http = new DenyHttp();
        using var client = new HttpClient(http);
        var reader = new PFlowGraphIdentityReader(client);
        using (new PFlowNativeFetchScope(ld, 1, "installation"))
        {
            Assert.Null(await reader.TryReadAsync(ld, 1, "installation", default));
            Assert.Null(await reader.TryReadUidAsync(ld, 1, "installation", default));
            Assert.False(PFlowNativeFetchScope.Active(ld, 2, "installation"));
            Assert.False(PFlowNativeFetchScope.Active(ld, 1, "other"));
        }
        Assert.False(PFlowNativeFetchScope.Active(ld, 1, "installation"));
        Assert.Equal(0, d.Calls + http.Calls);
    }

    [Fact]
    public async Task NestedDevicePoliciesRetainOuterGuardsAndCancellationDoesNotReadGraph()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DenyDevice>();
        using var outer = new PFlowNativeFetchScope(ld, 1, "first");
        using (new PFlowNativeFetchScope(ld, 2, "second"))
        {
            Assert.True(PFlowNativeFetchScope.Active(ld, 1, "first"));
            Assert.True(PFlowNativeFetchScope.Active(ld, 2, "second"));
            Assert.Throws<InvalidOperationException>(() =>
                PFlowNativeFetchScope.RequireAllowed(ld, 1, "first", "shell su 0 id")
            );
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new PFlowGraphIdentityReader().TryReadAsync(ld, 2, "second", cancel.Token)
            );
        }
        Assert.True(PFlowNativeFetchScope.Active(ld, 1, "first"));
        Assert.False(PFlowNativeFetchScope.Active(ld, 2, "second"));
        Assert.Equal(0, ((DenyDevice)(object)ld).Calls);
    }

    [Theory]
    [InlineData("shell su 0 id")]
    [InlineData("shell run-as com.facebook.katana cat authentication")]
    [InlineData("shell cat /data/data/com.facebook.katana/x")]
    [InlineData("shell cat /data/user/0/com.facebook.katana/x")]
    public async Task RealAdbDispatcherBlocksRootOrPrivateStorageBeforeLaunchingAnyCommand(
        string command
    )
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        using var scope = new PFlowNativeFetchScope(ld, 999, "missing-installation");
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ld.ExecuteAdbAsync(999, command, "missing-installation")
        );
        PFlowNativeFetchScope.RequireAllowed(
            ld,
            999,
            "missing-installation",
            "shell pidof com.facebook.katana"
        );
    }

    public class DenyDevice : DispatchProxy
    {
        public int Calls;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new Exception("No device call allowed");
        }
    }

    sealed class DenyHttp : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            throw new Exception("No network call allowed");
        }
    }

    [Fact]
    public async Task NewTargetUsesOnlyPageUidAsItsRoutingKey()
    {
        var r = await new PFlowNativeFetch(new Device(1)).RunAsync([], Progress, default);
        var page = Assert.Single(r.Details!.Pages);
        Assert.Equal("300000000000001", page.PersonaUid);
        Assert.Equal(page.PersonaUid, page.PageId);
        Assert.True(page.LivePageAccessVerified);
    }

    [Fact]
    public async Task PageUidWorksWithoutCanonicalIdInSavedRow()
    {
        var saved = Saved(1);
        saved[1].PageId = "";
        var r = await new PFlowNativeFetch(new Device(1)).RunAsync(saved, Progress, default);
        var page = Assert.Single(r.Details!.Pages);
        Assert.Equal(saved[1].PersonaUid, page.PersonaUid);
        Assert.True(page.LivePageAccessVerified);
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public async Task NativePageUidFillsMissingUidWithoutReplacingLegacyHistoryKey()
    {
        var saved = Saved(1);
        saved[1].PersonaUid = "";
        var r = await new PFlowNativeFetch(new Device(1)).RunAsync(saved, Progress, default);
        var page = Assert.Single(r.Details!.Pages);
        Assert.Equal("300000000000001", page.PersonaUid);
        Assert.Equal(saved[1].PageId, page.PageId);
        Assert.True(page.LivePageAccessVerified);
    }

    [Fact]
    public async Task CanonicalIdAloneCannotSubstituteForUnavailablePageUid()
    {
        var saved = Saved(1);
        saved[1].PersonaUid = "";
        var d = new Device(1) { NoPageIds = true };
        var r = await new PFlowNativeFetch(d).RunAsync(saved, Progress, default);
        Assert.Single(r.Unresolved);
        Assert.False(Assert.Single(r.Details!.Pages).LivePageAccessVerified);
        Assert.Equal(0, d.PageReads);
    }

    [Fact]
    public async Task MatchingCanonicalNumberCannotOverrideContradictoryPageUid()
    {
        var saved = Saved(1);
        var d = new Device(1) { PageUidOverride = saved[1].PageId };
        var r = await new PFlowNativeFetch(d).RunAsync(saved, Progress, default);
        Assert.Single(r.Unresolved);
        Assert.Equal(0, d.PageReads);
    }

    [Fact]
    public async Task DetailCanonicalNumberIsIgnoredWhenPageUidMatches()
    {
        var saved = Saved(1);
        var r = await new PFlowNativeFetch(new Device(1) { Failure = "detail-key" }).RunAsync(
            saved,
            Progress,
            default
        );
        var page = Assert.Single(r.Details!.Pages);
        Assert.True(page.LivePageAccessVerified);
        Assert.Equal(saved[1].PageId, page.PageId);
        Assert.Equal(saved[1].PersonaUid, page.PersonaUid);
    }

    sealed class Device : IPFlowNativeFetchDevice
    {
        public PFlowFetchActor[] Actors;
        public List<PFlowFetchActor> Selected = [];
        public bool NoIds,
            NoPageIds,
            WrongPersonal;
        public string Failure = "";
        public string? PageUidOverride;
        public int PersonalReads,
            PageReads;
        public CancellationTokenSource? Cancel;

        public Device(int pages) =>
            Actors = new[] { new PFlowFetchActor("Owner Name", "Profile", true) }
                .Concat(
                    Enumerable.Range(1, pages).Select(i => new PFlowFetchActor("Page " + i, "Page"))
                )
                .ToArray();

        public Task<PFlowFetchActor[]> ScanAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Actors);
        }

        public Task<bool> SelectAsync(PFlowFetchActor actor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Selected.Add(actor);
            if (actor.Type == "Page" && Cancel != null)
            {
                Cancel.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return Task.FromResult(actor.Type != "Page" || Failure != "switch");
        }

        public Task<string?> ReadNumericIdAsync(PFlowFetchActor a, CancellationToken ct) =>
            Task.FromResult<string?>(
                NoIds || NoPageIds && a.Type == "Page" ? null
                : a.Type == "Profile" ? Owner
                : Failure == "native-id" ? "999999999999999"
                : PageUidOverride ?? (300000000000000L + int.Parse(a.Name[5..])).ToString()
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
                    Name = WrongPersonal ? "Other" : name,
                    SwitchedToProfile = true,
                }
            );
        }

        public Task<FacebookPageInfo?> PageDetailsAsync(
            FacebookPageInfo page,
            string ownerId,
            string ownerName,
            CancellationToken ct
        )
        {
            PageReads++;
            return Task.FromResult<FacebookPageInfo?>(
                new()
                {
                    PageId = Failure == "detail-key" ? "999999999999999" : page.PageId,
                    PageName = Failure == "detail-name" ? "Other" : page.PageName,
                    PersonaUid = Failure == "detail-uid" ? "999999999999999" : page.PersonaUid,
                    Followers = 123,
                }
            );
        }
    }
}
