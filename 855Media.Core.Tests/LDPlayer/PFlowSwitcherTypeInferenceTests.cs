using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSwitcherTypeInferenceTests
{
    private static XDocument Sheet() =>
        XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld55-switcher-selected-page-captured.xml"
            )
        );

    private static XElement Row(XDocument doc, string name) =>
        doc.Descendants("node").Single(n => (string?)n.Attribute("content-desc") == name);

    private static XElement List(XDocument doc) =>
        doc.Descendants("node")
            .Single(n =>
                (string?)n.Attribute("class") == "androidx.recyclerview.widget.RecyclerView"
            );

    private static void Select(XDocument doc, string name)
    {
        foreach (var n in doc.Descendants("node"))
            n.SetAttributeValue("selected", "false");
        Row(doc, name).SetAttributeValue("selected", "true");
    }

    private static void Badge(XElement row) =>
        row.Add(
            new XElement(
                "node",
                new XAttribute("text", "Page"),
                new XAttribute("package", "com.facebook.katana")
            )
        );

    private static void RemoveFooter(XDocument doc) =>
        doc.Descendants("node")
            .Where(n => (string?)n.Attribute("content-desc") == "Go to Meta Account settings")
            .Remove();

    [Theory]
    [InlineData("valid")]
    [InlineData("no-footer")]
    [InlineData("disabled-footer")]
    [InlineData("two-selected")]
    [InlineData("hidden-row")]
    [InlineData("wrong-child")]
    public void CapturedCompactAccountSwitcherRequiresCompleteUsableRows(string change)
    {
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld9-compact-switcher-captured.xml"
            )
        );
        var footer = doc.Descendants("node")
            .Single(n =>
                (string?)n.Attribute("content-desc") == "Go to Accounts Center"
                && (string?)n.Attribute("clickable") == "true"
            );
        var personal = Row(doc, "Fixture Personal");
        if (change == "no-footer")
            footer.Remove();
        if (change == "disabled-footer")
            footer.SetAttributeValue("enabled", "false");
        if (change == "two-selected")
            personal.SetAttributeValue("selected", "true");
        if (change == "hidden-row")
            personal.SetAttributeValue("visible-to-user", "false");
        if (change == "wrong-child")
            personal
                .Descendants("node")
                .Where(n => (string?)n.Attribute("text") == "Fixture Personal")
                .ToList()
                .ForEach(n => n.SetAttributeValue("text", "Unrelated"));
        var actors = PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString());
        if (change == "valid")
        {
            Assert.Equal(2, actors.Length);
            Assert.Single(
                actors,
                a => a.IsSelected && a.Type == "Page" && a.Name == "Fixture Page"
            );
            Assert.Single(
                actors,
                a => !a.IsSelected && a.Type == "Profile" && a.Name == "Fixture Personal"
            );
        }
        else
            Assert.Empty(actors);
    }

    private static XDocument PageCountSheet(int pages, bool personalActive)
    {
        var doc = Sheet();
        Row(doc, "HDPlumbers+, 1 notification").Remove();
        if (pages == 0)
            Row(doc, "Bushcraft improvisation").Remove();
        else
        {
            if (personalActive)
                Badge(Row(doc, "Bushcraft improvisation"));
            for (var i = 1; i < pages; i++)
            {
                var page = new XElement(Row(doc, "Bushcraft improvisation"));
                page.SetAttributeValue("content-desc", "Extra Page " + i);
                foreach (var n in page.Descendants("node"))
                {
                    n.SetAttributeValue("selected", "false");
                    if ((string?)n.Attribute("text") == "Bushcraft improvisation")
                        n.SetAttributeValue("text", "Extra Page " + i);
                }
                page.SetAttributeValue("selected", "false");
                Badge(page);
                List(doc).Add(page);
            }
        }
        Select(doc, personalActive ? "Personal Owner" : "Bushcraft improvisation");
        return doc;
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(7, true)]
    [InlineData(7, false)]
    public async Task CompleteListsSupportZeroOneAndManyPages(int pages, bool personalActive)
    {
        var xml = PageCountSheet(pages, personalActive).ToString();
        var actors = PFlowPersonaSwitcherService.InferNativeSwitcherActors(xml);
        Assert.Equal(pages + 1, actors.Length);
        Assert.Equal(pages, actors.Count(a => a.Type == "Page"));
        Assert.Single(actors, a => a.Type == "Profile" && a.Name == "Personal Owner");
        Assert.Single(actors, a => a.IsSelected && a.Type == (personalActive ? "Profile" : "Page"));
        var binding = new PFlowAccountBinding(
            "fixture",
            900756,
            "fixture-LD",
            "com.facebook.katana",
            "100000000000001"
        );
        var targets = actors
            .Select(
                (a, i) =>
                    new FacebookPageTarget
                    {
                        PageName = a.Name,
                        Type = a.Type,
                        UserUid = binding.OwnerUid,
                        AccountBinding = binding,
                        PageId = a.Type == "Profile" ? binding.OwnerUid : "20000000000000" + i,
                    }
            )
            .ToArray();
        var selectedTarget = targets.Single(t =>
            actors.Any(a => a.IsSelected && a.Name == t.PageName)
        );
        Assert.NotNull(
            PFlowNativeIdentityScope.FromVerifiedName(binding, "123", selectedTarget, xml, targets)
        );
        var (service, fake) = Harness(xml);
        Assert.Equal(actors, await service.ScanNativeSwitcherActorsAsync(900756, null, default));
        Assert.Equal(0, fake.Swipes);
        Assert.Equal(0, fake.Taps);
        Assert.DoesNotContain("ExecuteAdbAsync", fake.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FixedFooterSupportsZeroPagesAndOneSelectedPage(int pages)
    {
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld64-switcher-static-footer-captured.xml"
            )
        );
        if (pages == 0)
            Row(doc, "Mattea Page+").Remove();
        else
            Select(doc, "Mattea Page+");
        var actors = PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString());
        Assert.Equal(pages + 1, actors.Length);
        Assert.Equal(pages, actors.Count(a => a.Type == "Page"));
        Assert.Single(actors, a => a.Type == "Profile" && a.Name == "Personal Owner");
        Assert.Single(actors, a => a.IsSelected && a.Type == (pages == 0 ? "Profile" : "Page"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void SmallListsStillRequireCompleteUsableEvidence(int pages, bool personalActive)
    {
        var doc = PageCountSheet(pages, personalActive);
        RemoveFooter(doc);
        Assert.Empty(PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString()));
        doc = PageCountSheet(pages, personalActive);
        Row(doc, "Personal Owner").SetAttributeValue("content-desc", "Personal�");
        Assert.Empty(PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString()));
    }

    [Fact]
    public void TwoUnbadgedRowsIdentifySelectedPageAndPersonalWithoutSavedTargets()
    {
        var xml = Sheet().ToString();
        var actors = PFlowPersonaSwitcherService.InferNativeSwitcherActors(xml);
        Assert.Equal(3, actors.Length);
        Assert.Contains(
            actors,
            a => a.Name == "Bushcraft improvisation" && a.Type == "Page" && a.IsSelected
        );
        Assert.Contains(
            actors,
            a => a.Name == "Personal Owner" && a.Type == "Profile" && !a.IsSelected
        );
        Assert.Contains(actors, a => a.Name == "HDPlumbers+" && a.Type == "Page" && !a.IsSelected);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Bushcraft improvisation",
                "Profile"
            )
        );
        Assert.Equal(
            new[] { "Bushcraft improvisation", "HDPlumbers+" },
            PFlowPersonaSwitcherService.DiscoveryPageNames(xml)
        );
        Assert.NotNull(PFlowPersonaSwitcherService.FindPersonalProfileNodeInSwitcher(xml));
        Assert.NotNull(LDPlayerService.FindPersonalProfileNodeInSwitcher(xml));
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        Assert.Equal((360, 820), AtxAgentClient.FindPersonalProfileCoordsInBloks(screen)!.Value);
        Assert.False(AtxAgentClient.IsPersonalProfileAlreadySelectedInBloks(screen));
        Assert.Null(
            PFlowPersonaSwitcherService.FindPersonalProfileNodeInSwitcher(
                xml,
                "Bushcraft improvisation"
            )
        );
    }

    [Fact]
    public void OneUnbadgedSelectedRowIdentifiesPersonalWhenAllInactivePagesAreBadged()
    {
        var doc = Sheet();
        Select(doc, "Personal Owner");
        Badge(Row(doc, "Bushcraft improvisation"));
        var xml = doc.ToString();
        var actors = PFlowPersonaSwitcherService.InferNativeSwitcherActors(xml);
        Assert.Single(
            actors,
            a => a.Type == "Profile" && a.IsSelected && a.Name == "Personal Owner"
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "Personal Owner", "Page")
        );
        Assert.Null(PFlowPersonaSwitcherService.FindPersonalProfileNodeInSwitcher(xml));
        Assert.True(
            AtxAgentClient.IsPersonalProfileAlreadySelectedInBloks(
                AtxAgentClient.ParseBloksHierarchy(xml)
            )
        );
    }

    [Theory]
    [InlineData("three-unbadged")]
    [InlineData("untyped")]
    [InlineData("no-footer")]
    [InlineData("clipped")]
    [InlineData("truncated")]
    [InlineData("duplicate")]
    [InlineData("two-selected")]
    [InlineData("foreign")]
    [InlineData("scrollable")]
    [InlineData("disabled-personal")]
    [InlineData("hidden-personal")]
    public void IncompleteOrAmbiguousEvidenceDoesNotInferTypes(string change)
    {
        var doc = Sheet();
        var selected = Row(doc, "Bushcraft improvisation");
        var personal = Row(doc, "Personal Owner");
        if (change == "three-unbadged")
        {
            var extra = new XElement(personal);
            extra.SetAttributeValue("content-desc", "Another Owner");
            List(doc).Add(extra);
        }
        if (change == "untyped")
            doc.Descendants("node").Where(n => (string?)n.Attribute("text") == "Page").Remove();
        if (change == "no-footer")
            RemoveFooter(doc);
        if (change == "clipped")
            selected.SetAttributeValue("bounds", "[32,628][688,680]");
        if (change == "truncated")
            selected.SetAttributeValue("content-desc", "Bushcraft…");
        if (change == "duplicate")
            personal.SetAttributeValue("content-desc", "Bushcraftimprovisation");
        if (change == "two-selected")
            personal.SetAttributeValue("selected", "true");
        if (change == "foreign")
            foreach (var n in doc.Descendants("node"))
                n.SetAttributeValue("package", "foreign.app");
        if (change == "scrollable")
            List(doc).SetAttributeValue("scrollable", "true");
        if (change == "disabled-personal")
            personal.SetAttributeValue("enabled", "false");
        if (change == "hidden-personal")
            personal.SetAttributeValue("visible-to-user", "false");
        Assert.Empty(PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString()));
    }

    [Fact]
    public void InstagramIdentityIsExcludedFromUnbadgedCount()
    {
        var doc = Sheet();
        var ig = new XElement(Row(doc, "Personal Owner"));
        ig.SetAttributeValue("content-desc", "linked_instagram");
        ig.Add(
            new XElement(
                "node",
                new XAttribute("class", "android.widget.ImageView"),
                new XAttribute("content-desc", "Instagram")
            )
        );
        List(doc).Add(ig);
        Assert.Equal(
            3,
            PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString()).Length
        );
    }

    [Fact]
    public void CompleteFacebookSublistCanEndBeforeScrollableInstagramSection()
    {
        var doc = Sheet();
        Select(doc, "Personal Owner");
        Badge(Row(doc, "Bushcraft improvisation"));
        // Move the selected actor to the explicit first list slot as captured on LD-55.
        var personal = Row(doc, "Personal Owner");
        void Shift(XElement row, int dy)
        {
            foreach (var node in row.DescendantsAndSelf("node"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    (string?)node.Attribute("bounds") ?? "",
                    @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
                );
                if (match.Success)
                    node.SetAttributeValue(
                        "bounds",
                        $"[{match.Groups[1].Value},{int.Parse(match.Groups[2].Value) + dy}][{match.Groups[3].Value},{int.Parse(match.Groups[4].Value) + dy}]"
                    );
            }
        }
        Shift(personal, -128);
        personal.Parent!.Remove();
        List(doc).AddFirst(personal);
        Shift(Row(doc, "Bushcraft improvisation"), 128);
        List(doc).SetAttributeValue("scrollable", "true");
        List(doc)
            .Add(
                new XElement(
                    "node",
                    new XAttribute("class", "android.widget.Button"),
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("content-desc", "Create Facebook Page"),
                    new XAttribute("clickable", "true"),
                    new XAttribute("bounds", "[32,1064][688,1168]")
                )
            );
        Assert.Single(
            PFlowPersonaSwitcherService.InferNativeSwitcherActors(doc.ToString()),
            a => a.Type == "Profile" && a.IsSelected
        );
    }

    private static (string Top, string Bottom) ScrolledList()
    {
        var top = Sheet();
        List(top).SetAttributeValue("scrollable", "true");
        RemoveFooter(top);
        var bottom = Sheet();
        List(bottom).SetAttributeValue("scrollable", "true");
        Row(bottom, "Bushcraft improvisation").Remove();
        Row(bottom, "Personal Owner").Remove();
        var extra = new XElement(Row(bottom, "HDPlumbers+, 1 notification"));
        extra.SetAttributeValue("content-desc", "Another Page");
        List(bottom).Add(extra);
        return (top.ToString(), bottom.ToString());
    }

    private static (PFlowPersonaSwitcherService Service, TypeDevice Device) Harness(
        params string[] screens
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, TypeDevice>();
        var fake = (TypeDevice)(object)ld;
        fake.Screens = new Queue<string>(screens);
        fake.Last = screens.Last();
        var now = DateTime.UtcNow;
        return (
            new PFlowPersonaSwitcherService(
                ld,
                null,
                (delay, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    now += delay;
                    return Task.CompletedTask;
                },
                () => now
            ),
            fake
        );
    }

    [Fact]
    public async Task CompleteScrollScanMergesRowsWithoutDatabaseGraphOrActorTaps()
    {
        var (top, bottom) = ScrolledList();
        var (service, fake) = Harness(top, bottom);
        var actors = await service.ScanNativeSwitcherActorsAsync(55, null, default);
        Assert.Equal(4, actors.Length);
        Assert.Single(actors, a => a.Type == "Profile" && a.Name == "Personal Owner");
        Assert.Single(actors, a => a.IsSelected && a.Type == "Page");
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
        Assert.All(
            fake.Calls,
            name => Assert.Contains(name, new[] { "DumpBloksScreenAsync", "SwipeAsync" })
        );
    }

    [Fact]
    public async Task CapturedLd3ListStartsAtSelectedOwnerAndMergesClippedBottomPage()
    {
        var top = FacebookLayoutRegistryTests.Fixture("ld3-switcher-scroll-top");
        var bottom = FacebookLayoutRegistryTests.Fixture("ld3-switcher-scroll-bottom");
        var (service, fake) = Harness(top, bottom);
        var actors = await service.ScanNativeSwitcherActorsAsync(3, null, default);
        Assert.Equal(6, actors.Length);
        Assert.Single(actors, a => a.Name == "Harch Jing" && a.Type == "Profile" && a.IsSelected);
        Assert.Single(actors, a => a.Name == "YeeYee's Life" && a.Type == "Page");
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedNotificationRowsAtEndReuseOnlyPreviouslyCompleteUpperRow(bool unseen)
    {
        var top = FacebookLayoutRegistryTests.Fixture("ld3-switcher-notifications-top");
        var bottom = FacebookLayoutRegistryTests.Fixture("ld3-switcher-notifications-bottom");
        if (unseen)
            bottom = bottom.Replace("Jungle Bushcraft", "Unobserved Actor");
        var (service, fake) = Harness(top, bottom);
        var actors = await service.ScanNativeSwitcherActorsAsync(3, null, default);
        Assert.Equal(unseen ? 0 : 6, actors.Length);
        if (!unseen)
        {
            Assert.Single(
                actors,
                a => a.Name == "Harch Jing" && a.Type == "Profile" && a.IsSelected
            );
            Assert.Single(actors, a => a.Name == "Jungle Bushcraft" && a.Type == "Page");
        }
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task SelectedPageWithNotificationRowAtViewportBottomStillResolvesOwner()
    {
        var (service, fake) = Harness(
            FacebookLayoutRegistryTests.Fixture("ld3-selected-yeeyee-notifications"),
            FacebookLayoutRegistryTests.Fixture("ld3-selected-yeeyee-end")
        );
        var actors = await service.ScanNativeSwitcherActorsAsync(3, null, default);
        Assert.Equal(6, actors.Length);
        Assert.Single(actors, a => a.Name == "Harch Jing" && a.Type == "Profile");
        Assert.Single(actors, a => a.Name == "YeeYee's Life" && a.Type == "Page" && a.IsSelected);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task ScrollStallWithoutFooterRemainsUnknown()
    {
        var (top, _) = ScrolledList();
        var (service, fake) = Harness(top);
        Assert.Empty(await service.ScanNativeSwitcherActorsAsync(55, null, default));
        Assert.InRange(fake.Swipes, 1, 4);
    }

    [Fact]
    public async Task ChangedSelectedActorOrBadgeDuringScanIsRejected()
    {
        var (top, _) = ScrolledList();
        var changed = Sheet();
        List(changed).SetAttributeValue("scrollable", "true");
        Select(changed, "Personal Owner");
        Badge(Row(changed, "Bushcraft improvisation"));
        var (service, _) = Harness(top, top, top, changed.ToString());
        Assert.Empty(await service.ScanNativeSwitcherActorsAsync(55, null, default));
    }

    [Fact]
    public async Task PersonalQueryUsesInferenceWithoutAnExpectedName()
    {
        var (service, fake) = Harness(Sheet().ToString());
        Assert.Equal((360, 820), (await service.QueryPersonalProfileInSwitcherAsync(55))!.Value);
        Assert.Equal(0, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task CancelledScanDoesNotScrollOrTap()
    {
        var (top, _) = ScrolledList();
        var (service, fake) = Harness(top);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ScanNativeSwitcherActorsAsync(55, null, cts.Token)
        );
        Assert.Equal(0, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    public class TypeDevice : DispatchProxy
    {
        public Queue<string> Screens = new();
        public string Last = "";
        public int Swipes,
            Taps;
        public List<string> Calls = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            if (args?.LastOrDefault() is CancellationToken ct)
                ct.ThrowIfCancellationRequested();
            if (method.Name == "DumpBloksScreenAsync")
            {
                if (Screens.Count > 0)
                    Last = Screens.Dequeue();
                return Task.FromResult<BloksScreenInfo?>(AtxAgentClient.ParseBloksHierarchy(Last));
            }
            if (method.Name == "SwipeAsync")
            {
                Swipes++;
                return Task.FromResult(true);
            }
            if (method.Name == "TapAsync")
            {
                Taps++;
                throw new Exception("Type scan must not select an actor");
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
