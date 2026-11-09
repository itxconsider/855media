using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

[Collection("Layout configuration")]
public class PFlowProfileDetailsRoutingTests
{
    private const string Name = "Mattea Page+";

    [Fact]
    public void NestedNativeHeaderUsesTheExactLeafButton()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld14-tap-profile");
        var result = PFlowProfileDetailsRouting.Decide(xml, "TAP Plumbing And Heating", false);
        Assert.Equal(PFlowProfileDetailsFlow.NameSheet, result.Flow);
        Assert.Equal((364, 640), (result.Control!.X, result.Control.Y));
    }

    private static string Fixture(string stage) =>
        FacebookLayoutRegistryTests.Fixture("ld64-page-about-" + stage);

    private static string Classic()
    {
        var doc = XDocument.Parse(Fixture("profile"));
        foreach (
            var node in doc.Descendants("node")
                .Where(n =>
                    n.Attribute("content-desc")?.Value
                        is "Posts, 1 of 4"
                            or "About, 2 of 4"
                            or "Photos, 3 of 4"
                )
                .ToArray()
        )
            node.Remove();
        return doc.ToString();
    }

    [Fact]
    public void SamePageChoosesByLayoutInsteadOfRememberingItsLastRoute()
    {
        Assert.Equal(
            PFlowProfileDetailsFlow.About,
            PFlowProfileDetailsRouting.Decide(Fixture("profile"), Name, false).Flow
        );
        var classic = PFlowProfileDetailsRouting.Decide(Classic(), Name, false);
        Assert.Equal(PFlowProfileDetailsFlow.NameSheet, classic.Flow);
        Assert.Equal("profile.details-entry.v1", classic.Variant);
        Assert.Equal(
            PFlowProfileDetailsFlow.About,
            PFlowProfileDetailsRouting.Decide(Fixture("profile"), Name, false).Flow
        );
    }

    [Fact]
    public void ScatteredBodyLabelsAreNotAnAboutTabStrip()
    {
        var doc = XDocument.Parse(Classic());
        foreach (var (label, y) in new[] { ("Posts", 700), ("About", 800), ("Photos", 900) })
            doc.Root!.Add(Node(label, y));
        Assert.False(PFlowPageAboutTransparency.HasAboutStrip(doc.ToString()));
        Assert.Equal(
            PFlowProfileDetailsFlow.NameSheet,
            PFlowProfileDetailsRouting.Decide(doc.ToString(), Name, false).Flow
        );
    }

    [Theory]
    [InlineData("wrong-name")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("disabled-header")]
    [InlineData("hidden-header")]
    [InlineData("duplicate-header")]
    public async Task UnsupportedOrUnsafeClassicLayoutNeverNavigates(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        var result = await Read(ld);
        Assert.Empty(result.CreationDate);
        Assert.Empty(fake.Taps);
        Assert.Equal(0, fake.Backs);
        Assert.Equal(0, fake.Swipes);
    }

    [Theory]
    [InlineData("classic", 2, 2)]
    [InlineData("name-noop", 3, 1)]
    [InlineData("missing-date", 3, 2)]
    [InlineData("already-about", 1, 1)]
    public async Task ReaderChoosesAndConfirmsDestinationBeforeAcceptingDate(
        string mode,
        int taps,
        int backs
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        var result = await Read(ld);
        Assert.Equal("September 27, 2026", result.CreationDate);
        Assert.Equal(taps, fake.Taps.Count);
        Assert.Equal(backs, fake.Backs);
        Assert.Equal(0, fake.Swipes);
        // The failed name route is attempted once only; About never retaps the name.
        Assert.Equal(mode == "already-about" ? 0 : 1, fake.Taps.Count(t => t == "name"));
    }

    [Theory]
    [InlineData("changed-process")]
    [InlineData("foreign-after-name")]
    public async Task ChangedSessionCannotAuthorizeAlternateRoute(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Read(ld));
        Assert.Equal(new[] { "name" }, fake.Taps);
        Assert.Equal(0, fake.Backs);
    }

    [Fact]
    public async Task DisabledClassicRegistryEntryIsRespectedByActualReader()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(
                file,
                """
                {"formatVersion":1,"variants":[{"id":"profile.details-entry.v1","baseId":"profile.details-entry.v1","enabled":false}]}
                """
            );
            Assert.True(FacebookLayoutRegistry.ReloadConfiguration(file).Applied);
            var ld = DispatchProxy.Create<ILDPlayerService, Device>();
            Assert.Empty((await Read(ld)).CreationDate);
            Assert.Empty(((Device)(object)ld).Taps);
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassicRegistryEntryRequiresExactNameAndTypeContext()
    {
        Assert.False(
            FacebookLayoutRegistry.Match(Classic(), FacebookScreenKind.ProfileDetailsEntry).IsMatch
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(
                    Classic(),
                    FacebookScreenKind.ProfileDetailsEntry,
                    new(TargetName: "Other Page", PostType: "Page")
                )
                .Control(FacebookControlRole.OpenProfileDetails)
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(Classic(), FacebookScreenKind.ProfileDetailsEntry, new(TargetName: Name))
                .Control(FacebookControlRole.OpenProfileDetails)
        );
        Assert.NotEqual(
            FacebookScreenKind.ProfileDetailsEntry,
            FacebookLayoutRegistry.Match(Classic(), context: new(Name)).Screen
        );
    }

    [Fact]
    public async Task FailedNameTapWithoutSupportedAlternateNeverRetriesOrPressesBack()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = "no-alternate";
        Assert.Empty((await Read(ld)).CreationDate);
        Assert.Equal(new[] { "name" }, fake.Taps);
        Assert.Equal(0, fake.Backs);
        Assert.Equal(0, fake.Swipes);
    }

    [Fact]
    public async Task CancellationAfterNameTapCannotStartAboutFallback()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = "name-noop";
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowProfileDetailsReader.ReadOwnProfileAsync(
                ld,
                64,
                Name,
                false,
                "fixture",
                stop.Token,
                (_, ct) =>
                {
                    stop.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(new[] { "name" }, fake.Taps);
        Assert.Equal(0, fake.Backs);
    }

    private static XElement Node(string label, int y, bool clickable = true) =>
        new(
            "node",
            new XAttribute("text", label),
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", clickable),
            new XAttribute("bounds", $"[20,{y}][400,{y + 40}]")
        );

    private static Task<FacebookAccountDetails> Read(ILDPlayerService ld) =>
        PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            64,
            Name,
            false,
            "fixture",
            default,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );

    public class Device : DispatchProxy
    {
        public string Mode = "classic";
        public List<string> Taps = [];
        public int Backs,
            Swipes;
        private int Dumps;
        private string State = "profile";

        private string Current()
        {
            if (State == "transparency")
                return Fixture("transparency");
            if (State == "about")
                return Fixture("selected");
            if (State == "sheet")
                return new XElement(
                    "hierarchy",
                    Node(Name, 60),
                    Node("About " + Name, 110),
                    Node(
                        "To keep Facebook safe, we're showing information about people and their profiles.",
                        150,
                        false
                    ),
                    Node("Joined Facebook September 2026", 220),
                    Mode == "missing-date" ? null : Node("Transparency and privacy policy", 300)
                ).ToString();
            var doc = XDocument.Parse(
                State == "fallback" && Mode != "no-alternate" || Mode == "already-about"
                    ? Fixture("profile")
                    : Classic()
            );
            if (Mode == "already-about")
                doc.Descendants("node")
                    .First(n => n.Attribute("text")?.Value == "About")
                    .Ancestors("node")
                    .First(n => n.Attribute("clickable")?.Value == "true")
                    .SetAttributeValue("selected", "true");
            var name = doc.Descendants("node").First(n => n.Attribute("text")?.Value == Name);
            if (Mode == "wrong-name")
                return doc.ToString().Replace(Name, "Other Page");
            if (Mode == "foreign" || Mode == "foreign-after-name" && Taps.Count > 0)
                return doc.ToString().Replace("com.facebook.katana", "foreign.app");
            if (Mode == "input")
                doc.Root!.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("class", "android.widget.EditText")
                    )
                );
            if (Mode == "disabled-header")
                name.SetAttributeValue("enabled", "false");
            if (Mode == "hidden-header")
                name.SetAttributeValue("visible-to-user", "false");
            if (Mode == "duplicate-header")
            {
                var duplicate = new XElement(name);
                duplicate.SetAttributeValue("bounds", "[30,704][320,750]");
                name.AddAfterSelf(duplicate);
            }
            return doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    Dumps++;
                    if (Mode == "already-about" && Dumps >= 4 && State == "profile")
                        State = "about";
                    return Task.FromResult<string?>(Current());
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "ExecuteAdbAsync":
                    Assert.Equal("shell pidof com.facebook.katana", args![1]);
                    return Task.FromResult(
                        Mode == "changed-process" && Taps.Count > 0 ? "456" : "123"
                    );
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    if (State == "profile" && Mode != "already-about")
                    {
                        Assert.Equal((174, 674), point);
                        Taps.Add("name");
                        State = Mode
                            is "name-noop"
                                or "changed-process"
                                or "foreign-after-name"
                                or "no-alternate"
                            ? "fallback"
                            : "sheet";
                    }
                    else if (State == "fallback")
                    {
                        Assert.Equal(PFlowPageAboutTransparency.AboutEntry(Current(), Name), point);
                        Taps.Add("about");
                        State = "about";
                    }
                    else
                    {
                        var expected =
                            State == "sheet"
                                ? (210, 320)
                                : PFlowPageAboutTransparency.TransparencyEntry(
                                    Mode == "already-about" && State == "profile"
                                        ? Fixture("selected")
                                        : Current()
                                );
                        // An already-selected About keeps the profile header in the first
                        // observation; its next frame exposes the actual section.
                        Assert.Equal(expected, point);
                        Taps.Add("transparency");
                        State = "transparency";
                    }
                    return Task.FromResult(true);
                case "PressBackAsync":
                    Backs++;
                    State =
                        State == "transparency"
                            ? (
                                Taps.Contains("about") || Mode == "already-about"
                                    ? "about"
                                    : "sheet"
                            )
                        : Mode == "missing-date" ? "fallback"
                        : "profile";
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Swipes++;
                    return Task.FromResult(true);
                default:
                    throw new Exception("Unexpected action: " + method.Name);
            }
        }
    }
}
