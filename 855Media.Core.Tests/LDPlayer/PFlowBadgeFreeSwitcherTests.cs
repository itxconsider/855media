using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowBadgeFreeSwitcherTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        900764,
        "LD-fixture",
        "com.facebook.katana",
        "100000000000001"
    );

    private static FacebookPageTarget[] Saved() =>
        [
            new()
            {
                AccountBinding = Binding,
                DeviceName = Binding.DeviceName,
                UserUid = Binding.OwnerUid,
                PageId = Binding.OwnerUid,
                PersonaUid = Binding.OwnerUid,
                PageName = "Personal Owner",
                Type = "Profile",
            },
            new()
            {
                AccountBinding = Binding,
                DeviceName = Binding.DeviceName,
                UserUid = Binding.OwnerUid,
                PageId = "200000000000001",
                PersonaUid = "300000000000001",
                PageName = "Mattea Page+",
                Type = "Page",
            },
        ];

    private static XDocument Sheet(bool pageSelected = false, bool extra = true)
    {
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld64-switcher-static-footer-captured.xml"
            )
        );
        var owner = Row(doc, "Personal Owner");
        var page = Row(doc, "Mattea Page+");
        foreach (var n in doc.Descendants("node"))
            n.SetAttributeValue("selected", "false");
        owner.SetAttributeValue("selected", !pageSelected ? "true" : "false");
        page.SetAttributeValue("selected", pageSelected ? "true" : "false");
        if (extra)
        {
            var other = new XElement(page);
            other.SetAttributeValue("content-desc", "Bros Cooking");
            other.SetAttributeValue("selected", "false");
            other.SetAttributeValue("bounds", "[40,698][680,810]");
            foreach (var n in other.Descendants("node"))
            {
                n.SetAttributeValue("selected", "false");
                if (n.Attribute("text")?.Value == "Mattea Page+")
                    n.SetAttributeValue("text", "Bros Cooking");
                if (n.Attribute("bounds") != null)
                    n.SetAttributeValue("bounds", "[176,730][420,770]");
            }
            page.AddAfterSelf(other);
            var create = doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == "Create Facebook Page");
            create.SetAttributeValue("bounds", "[40,812][680,924]");
        }
        return doc;
    }

    private static XElement Row(XDocument doc, string name) =>
        doc.Descendants("node").Single(n => n.Attribute("content-desc")?.Value == name);

    private static (PFlowPersonaSwitcherService Service, Device Fake) Harness(
        string xml,
        FacebookPageTarget[]? saved = null
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Xml = xml;
        var now = DateTime.UtcNow;
        return (
            new(
                ld,
                null,
                (duration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    now += duration;
                    return Task.CompletedTask;
                },
                () => now,
                Binding,
                saved ?? Saved()
            ),
            fake
        );
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SavedTypesResolveBothSelectedStatesWithUnknownSibling(
        bool pageActive,
        bool extra
    )
    {
        var xml = Sheet(pageActive, extra).ToString();
        var (service, fake) = Harness(xml);
        var actors = await service.ScanNativeSwitcherActorsAsync(
            Binding.DeviceIndex,
            null,
            default
        );
        Assert.Single(
            actors,
            a => a.Name == "Personal Owner" && a.Type == "Profile" && a.IsSelected == !pageActive
        );
        Assert.Single(
            actors,
            a => a.Name == "Mattea Page+" && a.Type == "Page" && a.IsSelected == pageActive
        );
        if (extra)
            Assert.Single(actors, a => a.Name == "Bros Cooking" && a.Type == "Unknown");
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Mattea Page+",
                "Page",
                actors,
                true
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Bros Cooking",
                "Page",
                actors,
                true
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Personal Owner",
                "Page",
                actors,
                true
            )
        );
        Assert.Equal(0, fake.Taps + fake.Swipes + fake.Forbidden);
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("wrong-binding")]
    [InlineData("wrong-owner")]
    [InlineData("duplicate-owner")]
    [InlineData("conflicting-owner-type")]
    public void BadSavedContextCannotFallBackToOldTwoRowGuess(string change)
    {
        var saved = Saved().ToList();
        if (change == "missing-owner")
            saved.RemoveAt(0);
        if (change == "wrong-binding")
            saved.ForEach(t => t.AccountBinding = Binding with { DeviceIndex = 1 });
        if (change == "wrong-owner")
            saved.ForEach(t => t.UserUid = "999999999999999");
        if (change == "duplicate-owner")
            saved.Add(saved[0].CreateSnapshot());
        if (change == "conflicting-owner-type")
        {
            var conflict = saved[0].CreateSnapshot();
            conflict.Type = "Page";
            saved.Add(conflict);
        }
        var xml = Sheet(extra: false).ToString();
        var (service, _) = Harness(xml, saved.ToArray());
        var actors = service.ResolveNativeSwitcherActors(xml);
        Assert.Empty(actors);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Mattea Page+",
                "Page",
                actors,
                true
            )
        );
    }

    [Fact]
    public void AmbiguousPageRemainsUnknownWithoutBlockingOwner()
    {
        var saved = Saved().ToList();
        var conflict = saved[1].CreateSnapshot();
        conflict.PageId = "888888888888888";
        saved.Add(conflict);
        var xml = Sheet().ToString();
        var (service, _) = Harness(xml, saved.ToArray());
        var actors = service.ResolveNativeSwitcherActors(xml);
        Assert.Single(actors, a => a.Name == "Personal Owner" && a.Type == "Profile");
        Assert.Single(actors, a => a.Name == "Mattea Page+" && a.Type == "Unknown");
    }

    [Fact]
    public void PageFromAnotherAccountStaysUnknownAndDoesNotBlockMappedOwner()
    {
        var saved = Saved();
        saved[1].AccountBinding = Binding with { OwnerUid = "999999999999999" };
        saved[1].UserUid = "999999999999999";
        var xml = Sheet().ToString();
        var (service, _) = Harness(xml, saved);
        var actors = service.ResolveNativeSwitcherActors(xml);
        Assert.Single(actors, a => a.Name == "Personal Owner" && a.Type == "Profile");
        Assert.Single(actors, a => a.Name == "Mattea Page+" && a.Type == "Unknown");
    }

    [Theory]
    [InlineData("Альона Лисенко")]
    [InlineData("Dara Aba Rol Sa")]
    public void CompleteUnicodeAndMultipleWordOwnerNamesAreSupported(string name)
    {
        var xml = Sheet().ToString().Replace("Personal Owner", name);
        var saved = Saved();
        saved[0].PageName = name;
        var (service, _) = Harness(xml, saved);
        Assert.Single(
            service.ResolveNativeSwitcherActors(xml),
            a => a.Name == name && a.Type == "Profile" && a.IsSelected
        );
    }

    [Fact]
    public void ZeroPageAccountKeepsItsSavedPersonalRole()
    {
        var doc = Sheet(extra: false);
        Row(doc, "Mattea Page+").Remove();
        var xml = doc.ToString();
        var (service, _) = Harness(xml, [Saved()[0]]);
        var actor = Assert.Single(service.ResolveNativeSwitcherActors(xml));
        Assert.Equal("Profile", actor.Type);
        Assert.True(actor.IsSelected);
    }

    [Fact]
    public void ExplicitBadgeLayoutKeepsExistingInferenceEvenWhenSavedTypeConflicts()
    {
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld55-switcher-selected-page-captured.xml"
            )
        );
        var xml = doc.ToString();
        var (service, _) = Harness(xml);
        Assert.Equal(
            PFlowPersonaSwitcherService.InferNativeSwitcherActors(xml),
            service.ResolveNativeSwitcherActors(xml)
        );
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("duplicate-name")]
    [InlineData("two-selected")]
    [InlineData("missing-footer")]
    [InlineData("truncated")]
    [InlineData("foreign")]
    public void UnusableRowsNeverGainAuthorityFromDatabase(string change)
    {
        var doc = Sheet();
        var page = Row(doc, "Mattea Page+");
        if (change == "hidden")
            page.SetAttributeValue("visible-to-user", "false");
        if (change == "disabled")
            page.SetAttributeValue("enabled", "false");
        if (change == "two-selected")
            page.SetAttributeValue("selected", "true");
        if (change == "duplicate-name")
        {
            var other = Row(doc, "Bros Cooking");
            other.SetAttributeValue("content-desc", "Mattea Page+");
            foreach (var n in other.Descendants("node").Where(n => n.Attribute("text") != null))
                n.SetAttributeValue("text", "Mattea Page+");
        }
        if (change == "missing-footer")
            doc.Descendants("node")
                .Where(n =>
                    n.Attribute("content-desc")?.Value
                        is "Create Facebook Page"
                            or "Go to Accounts Center"
                    || n.Attribute("text")?.Value == "Your Instagram profile"
                )
                .Remove();
        if (change == "truncated")
            page.SetAttributeValue("content-desc", "Mattea...");
        if (change == "foreign")
            doc.Root!.Add(new XElement("node", new XAttribute("package", "foreign.app")));
        var xml = doc.ToString();
        var (service, _) = Harness(xml);
        var actors = service.ResolveNativeSwitcherActors(xml);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Mattea Page+",
                "Page",
                actors,
                true
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectorUsesMappedRoleAndDismissesSelectedTargetWithoutRestart(
        bool pageActive
    )
    {
        var (service, fake) = Harness(Sheet(pageActive).ToString());
        Assert.True(
            await service.SelectNativeTargetAsync(
                Binding.DeviceIndex,
                Saved()[1],
                null,
                default,
                postingNameOnly: true
            )
        );
        Assert.Equal(pageActive ? 0 : 1, fake.Taps);
        Assert.Equal(0, fake.Forbidden + fake.Swipes);
    }

    [Fact]
    public async Task UnknownTargetAndWrongLdAreRejectedWithoutInteraction()
    {
        var (service, fake) = Harness(Sheet().ToString());
        var unknown = Saved()[1];
        unknown.PageName = "Bros Cooking";
        Assert.False(
            await service.SelectNativeTargetAsync(
                Binding.DeviceIndex,
                unknown,
                null,
                default,
                postingNameOnly: true
            )
        );
        Assert.Empty(await service.ScanNativeSwitcherActorsAsync(1, null, default));
        Assert.Equal(0, fake.Taps + fake.Swipes + fake.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountPreparationReadsKnownOwnerAndSelectedRoleWithoutRestarts(
        bool pageActive
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Xml = Sheet(pageActive).ToString();
        var actor = await PFlowPostingNameVerification.ObserveAccountAsync(
            ld,
            Binding,
            "Personal Owner",
            null,
            default,
            Saved()
        );
        Assert.NotNull(actor);
        Assert.Equal(pageActive ? "Mattea Page+" : "Personal Owner", actor.Name);
        Assert.Equal(pageActive ? "Page" : "Profile", actor.Type);
        Assert.Equal(0, fake.Forbidden + fake.Taps + fake.Swipes);
    }

    [Fact]
    public async Task ActivationSwitchesKnownPageWithUnknownSiblingAndFreshNameProof()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Xml = Sheet().ToString();
        var proof = await PFlowPostingNameVerification.ActivateAsync(
            ld,
            Binding,
            Saved()[1],
            Saved(),
            null,
            default
        );
        Assert.True(proof.UsesDatabaseName);
        Assert.Equal(1, fake.Taps);
        Assert.Equal(0, fake.Forbidden + fake.Swipes);
    }

    public class Device : DispatchProxy
    {
        public string Xml = "";
        public bool Closed;
        public int Taps,
            Swipes,
            Forbidden;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            var xml = Closed
                ? "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node package='com.facebook.katana' bounds='[48,108][405,188]' content-desc='Mattea Page+, see your profile'/></node></hierarchy>"
                : Xml;
            switch (method!.Name)
            {
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(xml)
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(xml);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "ExecuteAdbAsync" when (string)args![1]! == "shell pidof com.facebook.katana":
                    return Task.FromResult("123");
                case "PressBackAsync":
                    Closed = true;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    Closed = true;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Swipes++;
                    throw new Exception("Complete sheet should not scroll");
            }
            Forbidden++;
            throw new Exception("Unexpected device call: " + method.Name);
        }
    }
}
