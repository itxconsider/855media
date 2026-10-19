using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowCreatorDashboardWelcomeTests
{
    // Synthetic hierarchy based on the supplied screenshot; not a captured device dump.
    private static string Screen(bool welcome = true) =>
        new XDocument(
            new XElement(
                "hierarchy",
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("bounds", "[0,0][325,580]"),
                    Node("Creator Dashboard", "[50,15][250,45]"),
                    Node("Analytics", "[10,55][90,95]", true),
                    Node("Content", "[95,55][165,95]", true),
                    Node("Community", "[170,55][260,95]", true),
                    welcome
                        ? new XElement(
                            "node",
                            new XAttribute("package", "com.facebook.katana"),
                            new XAttribute("bounds", "[0,229][325,580]"),
                            Node("Welcome to the new Creator\nDashboard", "[30,390][295,440]"),
                            Node("Start with challenges", "[10,510][163,546]", true),
                            Node("Explore on your own", "[169,510][313,546]", true)
                        )
                        : null
                )
            )
        ).ToString();

    private static XElement Node(string text, string bounds, bool clickable = false) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("text", text),
            new XAttribute("bounds", bounds),
            new XAttribute("enabled", "true"),
            new XAttribute("visible-to-user", "true"),
            new XAttribute("clickable", clickable ? "true" : "false")
        );

    [Fact]
    public void RegistryRecognizesWelcomeAndBlocksBackgroundAnalyticsUntilDismissed()
    {
        var match = FacebookLayoutRegistry.Match(
            Screen(),
            FacebookScreenKind.CreatorDashboardWelcome
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("dashboard.creator-welcome.v1", match.VariantId);
        var control = match.Control(FacebookControlRole.Dismiss)!;
        Assert.Equal((241, 528), (control.X, control.Y));
        Assert.False(FacebookLayoutRegistry.Match(Screen(), FacebookScreenKind.Dashboard).IsMatch);
        var dashboard = FacebookLayoutRegistry.Match(Screen(false), FacebookScreenKind.Dashboard);
        Assert.True(dashboard.IsMatch, dashboard.Diagnostic);
        Assert.NotNull(dashboard.Control(FacebookControlRole.Analytics));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("title")]
    [InlineData("other-choice")]
    [InlineData("header")]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("duplicate")]
    [InlineData("outside")]
    public void UnsafeOrUnrelatedControlsAreNeverTapped(string change)
    {
        var doc = XDocument.Parse(Screen());
        var button = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("text") == "Explore on your own");
        switch (change)
        {
            case "foreign":
                foreach (var n in doc.Descendants("node"))
                    n.SetAttributeValue("package", "com.other.app");
                break;
            case "title":
                doc.Descendants("node")
                    .Single(n => ((string?)n.Attribute("text"))?.StartsWith("Welcome") == true)
                    .SetAttributeValue("text", "Different popup");
                break;
            case "header":
                doc.Descendants("node")
                    .Single(n => (string?)n.Attribute("text") == "Creator Dashboard")
                    .Remove();
                break;
            case "other-choice":
                doc.Descendants("node")
                    .Single(n => (string?)n.Attribute("text") == "Start with challenges")
                    .Remove();
                break;
            case "hidden":
                button.SetAttributeValue("visible-to-user", "false");
                break;
            case "disabled":
                button.SetAttributeValue("enabled", "false");
                break;
            case "duplicate":
                var duplicate = new XElement(button);
                duplicate.SetAttributeValue("bounds", "[169,550][313,578]");
                button.Parent!.Add(duplicate);
                break;
            case "outside":
                button.Remove();
                doc.Root!.Element("node")!.Add(button);
                break;
        }
        Assert.Null(PFlowCreatorDashboardWelcome.DismissControl(doc.ToString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(11)]
    public async Task DismissalTapsOnlyExploreOnceAndWaitsForTheSheetToClose(int remaining)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, WelcomeDevice>();
        var device = (WelcomeDevice)(object)ld;
        device.Remaining = remaining;
        Task<BloksScreenInfo?> Run() =>
            PFlowCreatorDashboardWelcome.DismissAsync(
                ld,
                968,
                AtxAgentClient.ParseBloksHierarchy(Screen()),
                null,
                default,
                (_, _) => Task.CompletedTask
            );
        if (remaining > 10)
            await Assert.ThrowsAsync<InvalidOperationException>(Run);
        else
        {
            var after = await Run();
            Assert.Null(PFlowCreatorDashboardWelcome.Sheet(after!.RawXml));
            Assert.True(
                FacebookLayoutRegistry.MatchScreen(after, FacebookScreenKind.Dashboard).IsMatch
            );
        }
        Assert.Equal(new[] { (241, 528) }, device.Taps);
    }

    [Fact]
    public async Task NormalDashboardIsUntouchedAndCancellationDoesNotTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, WelcomeDevice>();
        var device = (WelcomeDevice)(object)ld;
        var screen = AtxAgentClient.ParseBloksHierarchy(Screen(false));
        Assert.Same(
            screen,
            await PFlowCreatorDashboardWelcome.DismissAsync(ld, 968, screen, null, default)
        );
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowCreatorDashboardWelcome.DismissAsync(
                ld,
                968,
                AtxAgentClient.ParseBloksHierarchy(Screen()),
                null,
                cancel.Token
            )
        );
        Assert.Empty(device.Taps);
    }

    public class WelcomeDevice : DispatchProxy
    {
        public int Remaining;
        public List<(int, int)> Taps = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "TapAsync":
                    Taps.Add(((int)args![1]!, (int)args[2]!));
                    return Task.FromResult(true);
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Screen(Remaining-- > 0))
                    );
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
