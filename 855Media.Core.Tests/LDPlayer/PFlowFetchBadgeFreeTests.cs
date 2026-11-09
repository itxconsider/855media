using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchBadgeFreeTests
{
    private static PFlowPersonaSwitcherService.NativeSwitcherActor[] Ld3Actors() =>
        [
            new("Harch Jing", "Profile", false),
            new("KC Waterheaters", "Page", false),
            new("Jungle Bushcraft", "Page", false),
            new("Breastfeeding Journey", "Page", false),
            new("SAM Outdoor", "Page", false),
            new("YeeYee's Life", "Page", true),
        ];

    [Fact]
    public void FetchAndPostingMatchSameCapturedSelectedPageIgnoringNotifications()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld3-selected-yeeyee-notifications");
        var actors = Ld3Actors();
        Assert.Equal("YeeYee's Life", PFlowPersonaSwitcherService.FetchSelectedRow(xml, actors));
        Assert.True(
            PFlowPersonaSwitcherService
                .FindNativeTargetInSwitcher(
                    xml,
                    "YeeYee's Life",
                    "Page",
                    actors,
                    requireResolvedType: true
                )
                ?.IsSelected
        );
        var doc = XDocument.Parse(xml);
        doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "YeeYee's Life")
            .SetAttributeValue("content-desc", "YeeYee's Life, 99 notifications");
        Assert.Equal(
            "YeeYee's Life",
            PFlowPersonaSwitcherService.FetchSelectedRow(doc.ToString(), actors)
        );
        Assert.True(
            PFlowPersonaSwitcherService
                .FindNativeTargetInSwitcher(
                    doc.ToString(),
                    "YeeYee's Life",
                    "Page",
                    actors,
                    requireResolvedType: true
                )
                ?.IsSelected
        );
    }

    [Theory]
    [InlineData("two-selected")]
    [InlineData("duplicate")]
    [InlineData("disabled")]
    [InlineData("foreign")]
    [InlineData("unknown-member")]
    public void FreshSelectedRowRejectsUnsafeOrAmbiguousProof(string change)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld3-selected-yeeyee-notifications")
        );
        var row = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "YeeYee's Life");
        if (change == "two-selected")
            doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == "Harch Jing")
                .SetAttributeValue("selected", "true");
        if (change == "duplicate")
            row.AddAfterSelf(new XElement(row));
        if (change == "disabled")
            row.SetAttributeValue("enabled", "false");
        if (change == "foreign")
            foreach (var node in doc.Descendants("node"))
                node.SetAttributeValue("package", "foreign.app");
        var actors = Ld3Actors();
        if (change == "unknown-member")
            actors = actors.Where(a => a.Name != "YeeYee's Life").ToArray();
        Assert.Null(PFlowPersonaSwitcherService.FetchSelectedRow(doc.ToString(), actors));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedBadgeEvidenceResolvesSelectedOwnerOrPage(bool pageSelected)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, NoDevice>();
        var service = new PFlowPersonaSwitcherService(ld);
        PFlowPersonaSwitcherService.NativeSwitcherActor[] raw =
        [
            new("Harch Jing", "Unknown", !pageSelected),
            new("KC Waterheaters", pageSelected ? "Unknown" : "Page", pageSelected),
            new("Jungle Bushcraft", "Page", false),
            new("Breastfeeding Journey", "Page", false),
            new("SAM Outdoor", "Page", false),
            new("YeeYee's Life", "Page", false),
        ];
        var actors = service.ResolveCompletedNativeActors(raw);
        Assert.Equal(6, actors.Length);
        Assert.Equal("Profile", actors.Single(a => a.Name == "Harch Jing").Type);
        Assert.All(actors.Where(a => a.Name != "Harch Jing"), a => Assert.Equal("Page", a.Type));
        raw[2] = new("Jungle Bushcraft", "Unknown", false);
        if (pageSelected)
            Assert.Empty(service.ResolveCompletedNativeActors(raw));
        raw[1] = raw[1] with { IsSelected = !pageSelected };
        Assert.Empty(service.ResolveCompletedNativeActors(raw));
    }

    public class NoDevice : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new Exception("Completed-list inference must not call the device.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedRolesFollowFreshSelectionWithoutRepeatingTypeInference(bool pageSelected)
    {
        PFlowFetchActor[] known =
        [
            new("Personal Owner", "Profile", !pageSelected),
            new("Mattea Page+", "Page", pageSelected),
        ];
        PFlowPersonaSwitcherService.NativeSwitcherActor[] raw =
        [
            new("Personal Owner", "Unknown", !pageSelected),
            new("Mattea Page+", "Unknown", pageSelected),
        ];
        var rebound = PFlowNativeFetchDevice.RebindVerifiedActors(known, raw);
        Assert.Equal(
            pageSelected ? "Page" : "Profile",
            Assert.Single(rebound, a => a.Selected).Type
        );
        Assert.Equal("Profile", rebound[0].Type);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("renamed")]
    [InlineData("wrong-badge")]
    [InlineData("two-selected")]
    public void CachedRolesRejectChangedOrAmbiguousFreshLists(string change)
    {
        PFlowFetchActor[] known = [new("Owner", "Profile", true), new("Page", "Page")];
        PFlowPersonaSwitcherService.NativeSwitcherActor[] raw =
        [
            new("Owner", change == "wrong-badge" ? "Page" : "Unknown", true),
            new(change == "renamed" ? "Different" : "Page", "Unknown", change == "two-selected"),
        ];
        if (change == "missing")
            raw = [raw[0]];
        Assert.Throws<InvalidOperationException>(() =>
            PFlowNativeFetchDevice.RebindVerifiedActors(known, raw)
        );
    }

    [Fact]
    public void UnresolvedSiblingStaysUnknownWithoutReprobingItsSettings()
    {
        PFlowFetchActor[] known =
        [
            new("Owner", "Profile", true),
            new("Unresolved sibling", "Unknown"),
        ];
        PFlowPersonaSwitcherService.NativeSwitcherActor[] raw =
        [
            new("Owner", "Unknown", true),
            new("Unresolved sibling", "Unknown", false),
        ];
        var rebound = PFlowNativeFetchDevice.RebindVerifiedActors(known, raw);
        Assert.Equal("Unknown", rebound[1].Type);
        Assert.Equal("Profile", Assert.Single(rebound, a => a.Selected).Type);
    }

    [Fact]
    public async Task DelayedSelectedRowSettlesWithExactlyOneSelectionTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SelectionDevice>();
        var d = (SelectionDevice)(object)ld;
        d.StaleReads = 10;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            () => DateTime.UtcNow
        );
        Assert.True(await service.SelectFetchRowAsync(901, "Mattea Page+", "fixture", default));
        Assert.Equal(1, d.SelectionTaps);
    }

    [Fact]
    public async Task SlowFreshObservationsStillAllowTwoMatchingSelectionFrames()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SelectionDevice>();
        var d = (SelectionDevice)(object)ld;
        var now = DateTime.UtcNow;
        d.OnRead = () => now += TimeSpan.FromSeconds(6);
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                now += delay;
                return Task.CompletedTask;
            },
            () => now
        );
        Assert.True(await service.SelectFetchRowAsync(901, "Mattea Page+", "fixture", default));
        Assert.Equal(1, d.SelectionTaps);
    }

    [Fact]
    public async Task AdapterSelectionReusesVerifiedTypesWithoutSettingsNavigation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SelectionDevice>();
        var d = (SelectionDevice)(object)ld;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var progress = new List<string>();
        var adapter = new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            m =>
            {
                progress.Add(m);
                return Task.CompletedTask;
            }
        );
        typeof(PFlowNativeFetchDevice)
            .GetMethod("RememberActors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(
                adapter,
                [
                    new PFlowFetchActor[]
                    {
                        new("Personal Owner", "Profile", true),
                        new("Mattea Page+", "Page"),
                    },
                ]
            );
        Assert.True(await adapter.SelectAsync(new("Mattea Page+", "Page"), default));
        Assert.Equal(1, d.SelectionTaps);
        Assert.DoesNotContain(progress, m => m.Contains("Reading native actor type"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSettingsProbeUsesOwnProfileAndReturnsWithoutRoot(bool page)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.UiDevice>();
        var d = (PFlowNativeFetchDeviceTests.UiDevice)(object)ld;
        d.Page = page;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        var method = typeof(PFlowNativeFetchDevice).GetMethod(
            "ReadActorTypeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var type = await (Task<string>)
            method.Invoke(adapter, ["Fixture Owner", CancellationToken.None])!;
        Assert.Equal(page ? "Page" : "Profile", type);
        Assert.Equal(1, d.SettingsTaps);
        Assert.False(d.Settings);
        Assert.Equal(0, d.Forbidden);
    }

    [Fact]
    public async Task TypeProbeCanReachOwnLinkBelowTwoSettingsScrolls()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.UiDevice>();
        var d = (PFlowNativeFetchDeviceTests.UiDevice)(object)ld;
        d.RevealAfter = 3;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        var method = typeof(PFlowNativeFetchDevice).GetMethod(
            "ReadActorTypeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.Equal(
            "Profile",
            await (Task<string>)method.Invoke(adapter, ["Fixture Owner", CancellationToken.None])!
        );
        Assert.Equal(3, d.Swipes);
        Assert.False(d.Settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedDiscoverySelectionChecksFreshSelectedName(bool wrongDestination)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SelectionDevice>();
        var d = (SelectionDevice)(object)ld;
        d.WrongDestination = wrongDestination;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        Assert.Equal(
            !wrongDestination,
            await new PFlowPersonaSwitcherService(ld).SelectFetchRowAsync(
                901,
                "Mattea Page+",
                "fixture",
                default
            )
        );
        Assert.Equal(1, d.SelectionTaps);
    }

    [Theory]
    [InlineData("Page Settings", "Page link", "Page")]
    [InlineData("Page Settings", "Share Page", "Page")]
    [InlineData("Profile Settings", "Your profile link", "Profile")]
    [InlineData("Profile Settings", "Share profile", "Profile")]
    [InlineData("Creator Dashboard", "Digital creator", "Unknown")]
    [InlineData("Profile Settings", "Page link", "Unknown")]
    [InlineData("Page Settings", "Your profile link", "Unknown")]
    public void ExplicitSettingsEvidenceOnly(string header, string link, string expected)
    {
        string Node(string text) =>
            $"<node package='com.facebook.katana' text='{text}' enabled='true'/>";
        Assert.Equal(
            expected,
            PFlowNativeFetchDevice.SettingsActorType(
                "<hierarchy>" + Node(header) + Node(link) + "</hierarchy>"
            )
        );
    }

    [Fact]
    public void ConflictingSettingsRemainUnknown()
    {
        var xml =
            "<hierarchy><node package='com.facebook.katana' text='Profile Settings'/><node package='com.facebook.katana' text='Your profile link'/><node package='com.facebook.katana' text='Page Settings'/><node package='com.facebook.katana' text='Page link'/></hierarchy>";
        Assert.Equal("Unknown", PFlowNativeFetchDevice.SettingsActorType(xml));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedReaderEnumeratesBadgeFreeRowsWithoutInventingTypes(bool pageSelected)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SheetDevice>();
        var d = (SheetDevice)(object)ld;
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld64-switcher-static-footer-captured.xml"
            )
        );
        foreach (var n in doc.Descendants("node"))
            n.SetAttributeValue("selected", "false");
        doc.Descendants("node")
            .Single(n =>
                n.Attribute("content-desc")?.Value
                == (pageSelected ? "Mattea Page+" : "Personal Owner")
            )
            .SetAttributeValue("selected", "true");
        d.Xml = doc.ToString();
        var reader = new PFlowPersonaSwitcherService(ld);
        var rows = await reader.ScanNativeSwitcherActorsAsync(
            901,
            "fixture",
            default,
            includeUnknown: true
        );
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Equal("Unknown", row.Type));
        Assert.Single(rows, r => r.IsSelected);
        Assert.Equal(0, d.Mutations);
    }

    public class SheetDevice : DispatchProxy
    {
        public string Xml = "";
        public int Mutations;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "DumpBloksScreenAsync")
                return Task.FromResult<BloksScreenInfo?>(AtxAgentClient.ParseBloksHierarchy(Xml));
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(Xml);
            Mutations++;
            throw new NotSupportedException(method.Name);
        }
    }

    public class SelectionDevice : DispatchProxy
    {
        public Action? OnRead;
        public int StaleReads;
        public bool WrongDestination;
        public int SelectionTaps;
        private bool _sheet = true;
        private bool _page;

        string Xml()
        {
            OnRead?.Invoke();
            if (!_sheet)
                return FacebookLayoutRegistryTests.Fixture("ld10-posting-menu-captured");
            var doc = XDocument.Load(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures/PFlowScreenTime/ld64-switcher-static-footer-captured.xml"
                )
            );
            foreach (var n in doc.Descendants("node"))
                n.SetAttributeValue("selected", "false");
            var visiblePage = _page && StaleReads-- <= 0;
            doc.Descendants("node")
                .Single(n =>
                    n.Attribute("content-desc")?.Value
                    == (visiblePage ? "Mattea Page+" : "Personal Owner")
                )
                .SetAttributeValue("selected", "true");
            return doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "ExecuteAdbAsync" when (string)args![1]! == "shell pidof com.facebook.katana":
                    return Task.FromResult("123");
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml())
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "TapAsync":
                    if (_sheet)
                    {
                        SelectionTaps++;
                        _page = !WrongDestination;
                    }
                    _sheet = !_sheet;
                    return Task.FromResult(true);
                case "PressBackAsync":
                    _sheet = false;
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
