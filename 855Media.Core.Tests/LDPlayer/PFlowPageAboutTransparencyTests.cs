using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

[Collection("Layout configuration")]
public class PFlowPageAboutTransparencyTests
{
    private const string Name = "Mattea Page+";

    private static string Fixture(string stage) =>
        FacebookLayoutRegistryTests.Fixture("ld64-page-about-" + stage);

    private static Task NoDelay(TimeSpan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CapturedLd64ReadsDateThroughRegisteredAboutAndScopedSeeAll()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            64,
            Name,
            false,
            "fixture",
            default,
            NoDelay
        );
        Assert.Equal("September 27, 2026", result.CreationDate);
        Assert.Equal(2, fake.Points.Count);
        Assert.Equal(1, fake.Backs);
        Assert.Equal(0, fake.Swipes);
    }

    [Fact]
    public async Task AboutSectionScrollsBeforeRebindingSeeAll()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = "offscreen";
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            64,
            Name,
            false,
            "fixture",
            default,
            NoDelay
        );
        Assert.Equal("September 27, 2026", result.CreationDate);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(2, fake.Points.Count);
    }

    [Theory]
    [InlineData("wrong-title")]
    [InlineData("disabled-see-all")]
    [InlineData("duplicate-see-all")]
    public async Task UnrelatedOrUnusableSeeAllDoesNotGetTapped(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            64,
            Name,
            false,
            "fixture",
            default,
            NoDelay
        );
        Assert.Empty(result.CreationDate);
        Assert.Single(fake.Points); // Only About.
    }

    [Fact]
    public async Task WrongTransparencyOwnerNeverSuppliesDate()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = "wrong-owner";
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            64,
            Name,
            false,
            "fixture",
            default,
            NoDelay
        );
        Assert.Empty(result.CreationDate);
        Assert.Equal(2, fake.Points.Count);
        Assert.Equal(0, fake.Backs);
    }

    [Theory]
    [InlineData("pid-change")]
    [InlineData("foreign")]
    [InlineData("input")]
    public async Task ChangedSessionStopsBeforeSeeAll(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowProfileDetailsReader.ReadOwnProfileAsync(
                ld,
                64,
                Name,
                false,
                "fixture",
                default,
                NoDelay
            )
        );
        Assert.Single(fake.Points);
        Assert.Equal(0, fake.Backs);
    }

    [Fact]
    public async Task CancellationStopsWithoutCleanupTaps()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowProfileDetailsReader.ReadOwnProfileAsync(
                ld,
                64,
                Name,
                false,
                "fixture",
                stop.Token,
                (_, token) =>
                {
                    stop.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Single(fake.Points);
        Assert.Equal(0, fake.Backs);
    }

    [Fact]
    public async Task DisabledAboutNeverFallsBackToTappingTheName()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = "disabled-about";
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            64,
            Name,
            false,
            "fixture",
            default,
            NoDelay
        );
        Assert.Empty(result.CreationDate);
        Assert.Empty(fake.Points);
    }

    [Fact]
    public async Task ActualReaderRespectsDisabledRegistryAboutEntry()
    {
        var folder = Path.Combine(Path.GetTempPath(), "page-about-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "layouts.json");
        try
        {
            File.WriteAllText(
                file,
                """
                {"formatVersion":1,"variants":[{"id":"profile.page-about-entry.v1","baseId":"profile.page-about-entry.v1","enabled":false}]}
                """
            );
            Assert.True(FacebookLayoutRegistry.ReloadConfiguration(file).Applied);
            var ld = DispatchProxy.Create<ILDPlayerService, Device>();
            var fake = (Device)(object)ld;
            var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
                ld,
                64,
                Name,
                false,
                "fixture",
                default,
                NoDelay
            );
            Assert.Empty(result.CreationDate);
            Assert.Empty(fake.Points);
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            File.Delete(file);
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void RegistryRequiresTargetContextAndPreservesControlGuards()
    {
        var profile = Fixture("profile");
        Assert.Null(
            FacebookLayoutRegistry
                .Match(profile, FacebookScreenKind.PageAboutEntry)
                .Control(FacebookControlRole.About)
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(profile, FacebookScreenKind.PageAboutEntry, new("Other Page"))
                .Control(FacebookControlRole.About)
        );
        Assert.NotNull(
            FacebookLayoutRegistry
                .Match(profile, FacebookScreenKind.PageAboutEntry, new(Name))
                .Control(FacebookControlRole.About)
        );
        var about = Fixture("selected");
        Assert.NotNull(
            FacebookLayoutRegistry
                .Match(about, FacebookScreenKind.PageAbout, new(Name))
                .Control(FacebookControlRole.PageTransparency)
        );
        var disabled = FacebookLayoutCatalog.Parse(
            """
            {"formatVersion":1,"variants":[{"id":"profile.page-about-transparency.v1","baseId":"profile.page-about-transparency.v1","enabled":false}]}
            """
        );
        Assert.Null(
            disabled
                .Match(about, FacebookScreenKind.PageAbout, new(Name))
                .Control(FacebookControlRole.PageTransparency)
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(
                    about.Replace("com.facebook.katana", "foreign.app"),
                    FacebookScreenKind.PageAbout,
                    new(Name)
                )
                .Control(FacebookControlRole.PageTransparency)
        );
    }

    public class Device : DispatchProxy
    {
        public string Mode = "ok";
        public int Backs,
            Swipes;
        public List<(int X, int Y)> Points = [];
        private int Stage;

        private string Current()
        {
            if (Stage == 0)
            {
                if (Mode != "disabled-about")
                    return Fixture("profile");
                var profile = XDocument.Parse(Fixture("profile"));
                profile
                    .Descendants("node")
                    .First(n => n.Attribute("text")?.Value == "About")
                    .AncestorsAndSelf("node")
                    .First(n => n.Attribute("clickable")?.Value == "true")
                    .SetAttributeValue("enabled", "false");
                return profile.ToString();
            }
            if (Stage >= 2)
                return Mode == "wrong-owner"
                    ? Fixture("transparency").Replace(Name, "Other Page")
                    : Fixture("transparency");
            var doc = XDocument.Parse(Fixture("selected"));
            if (Mode == "wrong-title")
                return doc.ToString().Replace("Page transparency", "Photos");
            if (Mode == "foreign")
                return doc.ToString().Replace("com.facebook.katana", "foreign.app");
            if (Mode == "input")
                doc.Root!.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("class", "android.widget.EditText")
                    )
                );
            var seeAll = doc.Descendants("node")
                .First(n => n.Attribute("text")?.Value == "See all")
                .AncestorsAndSelf("node")
                .First(n => n.Attribute("clickable")?.Value == "true");
            if (Mode == "disabled-see-all")
                seeAll.SetAttributeValue("enabled", "false");
            if (Mode == "duplicate-see-all")
            {
                var copy = new XElement(seeAll);
                copy.SetAttributeValue("bounds", "[650,1120][710,1150]");
                seeAll.AddAfterSelf(copy);
            }
            if (Mode == "offscreen" && Swipes == 0)
            {
                foreach (
                    var n in doc.Descendants("node")
                        .Where(n =>
                            n.Attribute("text")?.Value == "Page transparency"
                            || n.Attribute("text")?.Value == "See all"
                            || n.Attribute("content-desc")?.Value == "See all"
                        )
                )
                    n.SetAttributeValue("bounds", "[30,1400][690,1450]");
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
                    return Task.FromResult<string?>(Current());
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "ExecuteAdbAsync":
                    Assert.Equal("shell pidof com.facebook.katana", args![1]);
                    return Task.FromResult(
                        Mode == "pid-change" && Points.Count > 0 ? "456" : "123"
                    );
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    var expected =
                        Stage == 0
                            ? PFlowPageAboutTransparency.AboutEntry(Current(), Name)
                            : PFlowPageAboutTransparency.TransparencyEntry(Current());
                    Assert.Equal(expected, point);
                    Points.Add(point);
                    Stage++;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Swipes++;
                    return Task.FromResult(true);
                case "PressBackAsync":
                    Backs++;
                    Stage--;
                    return Task.FromResult(true);
                default:
                    throw new Exception("Unexpected action: " + method.Name);
            }
        }
    }
}
