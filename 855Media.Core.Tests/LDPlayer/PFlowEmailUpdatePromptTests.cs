using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowEmailUpdatePromptTests
{
    private static string Fixture() =>
        FacebookLayoutRegistryTests.Fixture("feed-email-update-screenshot");

    [Fact]
    public void ReminderIsAnExplicitComponentWithNotNowPreferredOverScopedClose()
    {
        var xml = Fixture();
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.EmailUpdatePrompt);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("feed.email-update.v1", match.VariantId);
        Assert.Equal(
            (158, 271),
            (
                match.Control(FacebookControlRole.Dismiss)!.X,
                match.Control(FacebookControlRole.Dismiss)!.Y
            )
        );
        Assert.NotEqual(
            FacebookScreenKind.EmailUpdatePrompt,
            FacebookLayoutRegistry.Match(xml).Screen
        );
        Assert.Equal((304, 80, "close"), PFlowEmailUpdatePrompt.DismissControl(xml, true));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("duplicate")]
    public void UnusableNotNowFallsBackToTheCardsOwnClose(string state)
    {
        var doc = XDocument.Parse(Fixture());
        var notNow = doc.Descendants("node").Single(n => (string?)n.Attribute("text") == "Not now");
        switch (state)
        {
            case "missing":
                notNow.Remove();
                break;
            case "disabled":
                notNow.SetAttributeValue("enabled", "false");
                break;
            case "hidden":
                notNow.SetAttributeValue("visible-to-user", "false");
                break;
            case "duplicate":
                var duplicate = new XElement(notNow);
                duplicate.SetAttributeValue("bounds", "[2,289][315,304]");
                notNow.Parent!.Add(duplicate);
                break;
        }
        Assert.Equal((304, 80, "close"), PFlowEmailUpdatePrompt.DismissControl(doc.ToString()));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("cta")]
    [InlineData("brand")]
    [InlineData("foreign")]
    [InlineData("input")]
    public void UnrelatedAndEditableScreensCannotSupplyDismissal(string change)
    {
        var xml = Fixture();
        xml = change switch
        {
            "title" => xml.Replace("Update your email", "Different reminder"),
            "cta" => xml.Replace("Update in Accounts Center", "Different action"),
            "brand" => xml.Replace("text=\"Meta\"", "text=\"Another author\""),
            "foreign" => xml.Replace("com.facebook.katana", "com.other.app"),
            _ => xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
            ),
        };
        Assert.Null(PFlowEmailUpdatePrompt.Card(xml));
        Assert.Null(PFlowEmailUpdatePrompt.DismissControl(xml));
    }

    [Fact]
    public void OutsideCloseIsNotUsedWhenTheCardHasNoUsableDismissal()
    {
        var doc = XDocument.Parse(Fixture());
        foreach (
            var n in PFlowEmailUpdatePrompt
                .Card(doc.ToString())!
                .Descendants("node")
                .Where(n =>
                    (string?)n.Attribute("text") == "Not now"
                    || (string?)n.Attribute("content-desc") == "Close"
                )
        )
        {
            // Mutate the source tree, not the independently parsed cached card.
            doc.Descendants("node")
                .Single(d => (string?)d.Attribute("bounds") == (string?)n.Attribute("bounds"))
                .SetAttributeValue("enabled", "false");
        }
        Assert.Null(PFlowEmailUpdatePrompt.DismissControl(doc.ToString()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DismissalReobservesAndUsesCloseAtMostOnceIfNotNowDidNotWork(
        bool ignoreNotNow,
        bool ignoreClose
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReminderDevice>();
        var device = (ReminderDevice)(object)ld;
        device.IgnoreNotNow = ignoreNotNow;
        device.IgnoreClose = ignoreClose;
        async Task<string?> Dismiss() =>
            await PFlowEmailUpdatePrompt.DismissAsync(
                ld,
                967,
                device.Xml,
                null,
                default,
                (_, _) => Task.CompletedTask
            );
        if (ignoreClose)
            await Assert.ThrowsAsync<InvalidOperationException>(Dismiss);
        else
            Assert.Null(PFlowEmailUpdatePrompt.Card(await Dismiss()));
        Assert.Equal(
            ignoreNotNow ? new[] { (158, 271), (304, 80) } : new[] { (158, 271) },
            device.Taps
        );
    }

    [Fact]
    public async Task OwnProfileNavigationDismissesReminderBeforeCheckingTarget()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReminderDevice>();
        var device = (ReminderDevice)(object)ld;
        Assert.True(
            await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                967,
                "Target",
                null,
                s => PFlowOwnProfileNavigation.IsOwnPageProfile(s, "Target"),
                default,
                delay: (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(new[] { (158, 271) }, device.Taps);
    }

    [Fact]
    public async Task SharedSwitcherDismissesReminderAndFreshlyConfirmsTheTarget()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReminderDevice>();
        var device = (ReminderDevice)(object)ld;
        var switcher = new PFlowPersonaSwitcherService(
            ld,
            null,
            (_, _) => Task.CompletedTask,
            () => DateTime.UtcNow
        );
        Assert.True(await switcher.SwitchToPageByIdAsync(967, "Target", "123"));
        Assert.Equal(new[] { (158, 271) }, device.Taps);
    }

    [Fact]
    public async Task CloseOnlyFailureDoesNotKeepTappingAndCancellationMakesNoTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReminderDevice>();
        var device = (ReminderDevice)(object)ld;
        var doc = XDocument.Parse(device.Xml);
        doc.Descendants("node").Single(n => (string?)n.Attribute("text") == "Not now").Remove();
        device.Xml = doc.ToString();
        device.IgnoreClose = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowEmailUpdatePrompt.DismissAsync(
                ld,
                967,
                device.Xml,
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(new[] { (304, 80) }, device.Taps);
        device.Taps.Clear();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowEmailUpdatePrompt.DismissAsync(ld, 967, device.Xml, null, ct.Token)
        );
        Assert.Empty(device.Taps);
    }

    public class ReminderDevice : DispatchProxy
    {
        public string Xml = Fixture();
        public bool IgnoreNotNow,
            IgnoreClose;
        public List<(int X, int Y)> Taps = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    Assert.True(
                        point is (158, 271) or (304, 80),
                        "Only reminder decline controls may be tapped"
                    );
                    Taps.Add(point);
                    if (point == (158, 271) ? !IgnoreNotNow : !IgnoreClose)
                        Xml = PFlowPostTargetSessionTests.Own(
                            new PFlowJob { TargetName = "Target" }
                        );
                    return Task.FromResult(true);
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml);
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml)
                    );
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
