using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPersonalLookupRecoveryTests
{
    [Theory]
    [InlineData("ld67-menu-duplicate-profile-label", "Kian Pearce")]
    [InlineData("ld68-menu-duplicate-profile-label", "Alisha Armstrong")]
    public async Task RecordedDrawerReturnsToOwnProfileWithoutChoosingCreatePage(
        string fixture,
        string name
    )
    {
        var xml = FacebookLayoutRegistryTests.Fixture(fixture);
        var match = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.Menu,
            new(TargetName: name)
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        var header = match.Control(FacebookControlRole.ActiveProfile);
        Assert.NotNull(header);
        Assert.Equal(148, header!.Y);
        Assert.Null(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.Menu, new(TargetName: "Wrong actor"))
                .Control(FacebookControlRole.ActiveProfile)
        );
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Override = xml;
        device.Name = name;
        device.HeaderPoint = (header.X, header.Y);
        Assert.True(
            await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                973,
                name,
                null,
                s =>
                    !AtxAgentClient.IsMenuPageInBloks(s)
                    && PFlowProfileDates.MatchesName(s, name)
                    && s.AllElements.Any(e => e.Matches("Edit profile", false)),
                default,
                delay: (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(new[] { "Profile header" }, device.Actions);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(0, true)]
    public async Task DelayedOrIgnoredDeepLinkStillReadsBirthdayAndReturnsOwnProfile(
        int frames,
        bool ignored
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Loading = frames;
        device.IgnoreLink = ignored;
        Assert.Equal(
            "March 14, 1992",
            await PFlowPersonalBirthdayReader.ReadAsync(
                ld,
                973,
                "Target",
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal("own", device.State);
        Assert.Contains("Personal details", device.Actions);
        if (ignored)
            Assert.Contains("Settings", device.Actions);
        Assert.DoesNotContain("Save", device.Actions);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("editable")]
    [InlineData("foreign")]
    public async Task UnavailableOrEditableBirthdaysRemainEmpty(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = mode;
        Assert.Empty(
            await PFlowPersonalBirthdayReader.ReadAsync(
                ld,
                973,
                "Target",
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.DoesNotContain("Save", device.Actions);
        Assert.True(device.Swipes <= 3);
    }

    [Fact]
    public void BirthdayValueMustBelongToAnExplicitCompactField()
    {
        var xml = Device.Xml(
            ("Personal details", 40, false),
            ("Birthday, March 14, 1992", 220, false)
        );
        Assert.Equal("March 14, 1992", PFlowPersonalBirthdayReader.ReadDate(xml, true));
        Assert.Empty(PFlowPersonalBirthdayReader.ReadDate(xml, false));
        Assert.Empty(
            PFlowPersonalBirthdayReader.ReadDate(
                xml.Replace("Birthday, March 14, 1992", "Profile created, March 14, 1992"),
                true
            )
        );
        Assert.Empty(
            PFlowPersonalBirthdayReader.ReadDate(
                xml.Replace("Birthday, March 14, 1992", "Birthday")
                    .Replace(
                        "</hierarchy>",
                        "<node text='January 23, 2024' package='com.facebook.katana' bounds='[20,800][680,860]'/></hierarchy>"
                    ),
                true
            )
        );
    }

    [Fact]
    public void PairedBirthdayRowsRejectMultipleDatesAndAccountChoosers()
    {
        var row =
            "<node package='com.facebook.katana' bounds='[20,200][680,320]'>"
            + "<node package='com.facebook.katana' text='Birthday' bounds='[30,210][200,240]'/><node package='com.facebook.katana' text='March 14, 1992' bounds='[30,250][400,280]'/></node>";
        var xml =
            "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'>"
            + row
            + "</node></hierarchy>";
        Assert.Equal("March 14, 1992", PFlowPersonalBirthdayReader.ReadDate(xml, true));
        Assert.Empty(
            PFlowPersonalBirthdayReader.ReadDate(
                xml.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' text='Choose a profile'/></hierarchy>"
                ),
                true
            )
        );
        Assert.Empty(
            PFlowPersonalBirthdayReader.ReadDate(
                xml.Replace(row, row + row.Replace("March 14, 1992", "July 7, 1999")),
                true
            )
        );
    }

    [Fact]
    public async Task ReadBirthdaySurvivesReturnFailureAndCancellationDoesNotNavigate()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = "return-failed";
        Assert.Equal(
            "March 14, 1992",
            await PFlowPersonalBirthdayReader.ReadAsync(
                ld,
                973,
                "Target",
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        device.Actions.Clear();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowPersonalBirthdayReader.ReadAsync(ld, 973, "Target", null, ct.Token)
        );
        Assert.Empty(device.Actions);
    }

    [Fact]
    public async Task HiddenSettingsSectionIsRevealedBeforeAccountsCenterLookup()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = "menu-hidden";
        device.IgnoreLink = true;
        Assert.Equal(
            "March 14, 1992",
            await PFlowPersonalBirthdayReader.ReadAsync(
                ld,
                973,
                "Target",
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(1, device.Swipes);
        Assert.Equal("own", device.State);
    }

    [Fact]
    public void RecordedSettingsControlAllowsOnlyItsPassiveListWrapper()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld67-settings-passive-list");
        var doc = XDocument.Parse(xml);
        var button = doc.Descendants("node")
            .Single(n =>
                ((string?)n.Attribute("content-desc"))?.StartsWith("Settings and privacy,") == true
            );
        Assert.Null(FacebookLayoutRegistry.UsableCenter(button));
        Assert.Equal((316, 963), FacebookLayoutRegistry.UsableCenter(button, readOnlyList: true));
        button.SetAttributeValue("enabled", "false");
        Assert.Null(FacebookLayoutRegistry.UsableCenter(button, readOnlyList: true));
    }

    [Fact]
    public async Task ScrolledDrawerRevealsItsHeaderBeforeReturningToOwnProfile()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Name = "Kian Pearce";
        device.Override = FacebookLayoutRegistryTests.Fixture("ld67-settings-passive-list");
        device.RevealMenuXml = FacebookLayoutRegistryTests.Fixture(
            "ld67-menu-duplicate-profile-label"
        );
        var header = FacebookLayoutRegistry
            .Match(device.RevealMenuXml, FacebookScreenKind.Menu, new(TargetName: device.Name))
            .Control(FacebookControlRole.ActiveProfile)!;
        device.HeaderPoint = (header.X, header.Y);
        Assert.True(
            await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                973,
                device.Name,
                null,
                s =>
                    !AtxAgentClient.IsMenuPageInBloks(s)
                    && PFlowProfileDates.MatchesName(s, device.Name)
                    && s.AllElements.Any(e => e.Matches("Edit profile", false)),
                default,
                delay: (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(1, device.Swipes);
        Assert.Equal(new[] { "Profile header" }, device.Actions);
    }

    [Theory]
    [InlineData("feed-hidden")]
    [InlineData("tap-delay")]
    public async Task FallbackRevealsHiddenNavigationAndRechecksUnchangedSettings(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = mode;
        device.IgnoreLink = true;
        Assert.Equal(
            "March 14, 1992",
            await PFlowPersonalBirthdayReader.ReadAsync(
                ld,
                973,
                "Target",
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal("own", device.State);
        if (mode == "feed-hidden")
            Assert.Equal(1, device.Swipes);
        else
            Assert.Equal(2, device.Actions.Count(a => a == "Settings & privacy"));
    }

    [Fact]
    public void CapturedSettingsOrdinalRowResolvesItsEnabledParentButton()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld67-settings-ordinal-row");
        Assert.Equal((316, 961), PFlowPersonalBirthdayReader.Control(xml, "Settings"));
        var doc = XDocument.Parse(xml);
        doc.Descendants("node")
            .Single(n => (string?)n.Attribute("clickable") == "true")
            .SetAttributeValue("enabled", "false");
        Assert.Null(PFlowPersonalBirthdayReader.Control(doc.ToString(), "Settings"));
    }

    [Fact]
    public async Task NewProfilesAndPersonalDetailsLandingReadsThePersonalSectionWithoutOpeningProfileEdits()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = "profiles-layout";
        Assert.Equal(
            "March 14, 1992",
            await PFlowPersonalBirthdayReader.ReadAsync(
                ld,
                973,
                "Target",
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Contains("Profiles and personal details", device.Actions);
        Assert.DoesNotContain("Target, Facebook", device.Actions);
        Assert.Equal("own", device.State);
    }

    [Fact]
    public void ProfileChooserRejectsOtherActorsAndNonFacebookRows()
    {
        var xml = Device.Xml(("Target, Facebook", 200, true));
        Assert.NotNull(PFlowPersonalBirthdayReader.ProfileControl(xml, "Target"));
        Assert.Null(PFlowPersonalBirthdayReader.ProfileControl(xml, "Other actor"));
        Assert.Null(
            PFlowPersonalBirthdayReader.ProfileControl(
                xml.Replace("Facebook", "Instagram"),
                "Target"
            )
        );
        Assert.Null(
            PFlowPersonalBirthdayReader.ProfileControl(
                xml.Replace("enabled=\"true\"", "enabled=\"false\""),
                "Target"
            )
        );
    }

    public class Device : DispatchProxy
    {
        public string Name = "Target",
            State = "own",
            Mode = "normal";
        public string? Override;
        public string? RevealMenuXml;
        public (int X, int Y) HeaderPoint;
        public int Loading,
            Swipes;
        public bool IgnoreLink;
        public List<string> Actions = [];

        public static string Xml(params (string Label, int Y, bool Click)[] nodes) =>
            new XElement(
                "hierarchy",
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("bounds", "[0,0][720,1280]"),
                    nodes.Select(n => new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("text", n.Label),
                        new XAttribute("enabled", "true"),
                        new XAttribute("clickable", n.Click ? "true" : "false"),
                        new XAttribute("bounds", $"[20,{n.Y}][680,{n.Y + 60}]")
                    ))
                )
            ).ToString();

        private string Screen()
        {
            if (Override != null)
                return Override;
            if (State == "loading" && Loading-- <= 0)
                State = "center";
            return State switch
            {
                "feed" when Swipes == 0 => Xml(("Story tray", 150, false)),
                "feed" => Xml((Name, 120, false), ("Edit profile", 230, true), ("Menu", 40, true)),
                "own" => Xml((Name, 120, false), ("Edit profile", 230, true), ("Menu", 40, true)),
                "loading" => Xml(("Loading", 200, false)),
                "center" when Mode == "profiles-layout" => Xml(
                    (
                        "Manage your connected experiences and account settings across Meta technologies. Learn more",
                        100,
                        false
                    ),
                    ("Profiles and personal details", 230, true),
                    ("Back", 20, true)
                ),
                "profiles" => Xml(
                    ("Profiles and personal details", 100, false),
                    ("Profiles", 180, false),
                    (Name + ", Facebook", 230, true),
                    ("Personal details", 400, false),
                    ("Birthday, March 14, 1992", 500, false),
                    ("Back", 20, true)
                ),
                "center" => Xml(
                    ("Accounts Center", 100, false),
                    ("Personal details", 230, true),
                    ("Back", 20, true)
                ),
                "menu" when Mode == "menu-hidden" && Swipes == 0 => Xml(
                    ("Your shortcuts", 100, false),
                    ("Back", 20, true)
                ),
                "menu" when Mode == "menu-hidden" => Xml(
                    (
                        "Settings and privacy, header. Section is collapsed. Double-tap to expand the section.",
                        230,
                        true
                    ),
                    ("Close menu.", 700, true),
                    ("Back", 20, true)
                ),
                "menu" => Xml(
                    ("Your shortcuts", 100, false),
                    ("Settings & privacy", 230, true),
                    ("Back", 20, true)
                ),
                "expanded" => Xml(
                    ("Your shortcuts", 100, false),
                    ("Settings", 230, true),
                    ("Back", 20, true)
                ),
                "settings" => Xml(
                    ("Settings & privacy", 100, false),
                    ("Accounts Center", 230, true),
                    ("Back", 20, true)
                ),
                "personal" when Mode == "editable" => Xml(
                        ("Personal details", 100, false),
                        ("Back", 20, true)
                    )
                    .Replace(
                        "</hierarchy>",
                        "<node class='android.widget.EditText' package='com.facebook.katana'/></hierarchy>"
                    ),
                "personal" when Mode == "foreign" => Xml(("Personal details", 100, false))
                    .Replace("com.facebook.katana", "com.other.app"),
                "personal" => Xml(
                    ("Personal details", 100, false),
                    (Mode == "hidden" ? "Contact info" : "Birthday, March 14, 1992", 230, false),
                    ("Back", 20, true)
                ),
                _ => throw new Exception(State),
            };
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Screen());
                case "OpenDeepLinkAsync":
                    State =
                        Mode == "feed-hidden" ? "feed"
                        : IgnoreLink ? "own"
                        : Loading > 0 ? "loading"
                        : "center";
                    return Task.FromResult(!IgnoreLink);
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    if (Override != null)
                    {
                        Assert.Equal(HeaderPoint, point);
                        Override = null;
                        State = "own";
                        Actions.Add("Profile header");
                        return Task.FromResult(true);
                    }
                    var doc = XDocument.Parse(Screen());
                    var label = doc.Descendants("node")
                        .Single(n =>
                            (string?)n.Attribute("clickable") == "true"
                            && ScreenTimeHierarchy.Center(n) == point
                        )
                        .Attribute("text")!
                        .Value;
                    Actions.Add(label);
                    if (
                        Mode == "tap-delay"
                        && label == "Settings & privacy"
                        && Actions.Count(a => a == label) == 1
                    )
                        return Task.FromResult(true);
                    State = label switch
                    {
                        "Menu" => "menu",
                        "Settings & privacy" => "expanded",
                        "Settings and privacy, header. Section is collapsed. Double-tap to expand the section." =>
                            "expanded",
                        "Settings" => "settings",
                        "Accounts Center" => "center",
                        "Profiles and personal details" => "profiles",
                        "Personal details" => "personal",
                        _ => throw new Exception("Unexpected tap " + label),
                    };
                    return Task.FromResult(true);
                case "PressBackAsync":
                    if (Mode == "return-failed")
                        throw new InvalidOperationException("Simulated return failure");
                    State = "own";
                    return Task.FromResult(true);
                case "SwipeAsync":
                    if (State == "feed")
                        Assert.True((int)args![2]! < (int)args[4]!);
                    Swipes++;
                    if (RevealMenuXml != null)
                    {
                        Assert.True((int)args![2]! < (int)args[4]!);
                        Override = RevealMenuXml;
                        RevealMenuXml = null;
                    }
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
