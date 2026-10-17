using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public partial class PFlowSharedSwitchTests
{
    [Fact]
    public void SwitchingLoaderIsRegisteredWithoutActorOrNavigationProof()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("identity-switching-screenshot");
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.IdentitySwitching, match.Screen);
        Assert.Empty(match.Controls);
        Assert.False(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml));
        Assert.Equal(
            PFlowPersonaSwitcherService.NativeRouteKind.WaitForSwitch,
            PFlowPersonaSwitcherService
                .PlanNativeRoute(AtxAgentClient.ParseBloksHierarchy(xml))
                .Kind
        );
        Assert.False(
            PFlowIdentitySwitchLayout.IsSwitching(
                xml.Replace("com.facebook.katana", "com.other.app")
            )
        );
        Assert.False(
            PFlowIdentitySwitchLayout.IsSwitching(
                xml.Replace("text=\"Switching to\"", "text=\"Something else\"")
            )
        );
        var doc = XDocument.Parse(xml);
        doc.Root!.Element("node")!
            .Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Like"),
                    new XAttribute("clickable", "true"),
                    new XAttribute("enabled", "true")
                )
            );
        Assert.False(PFlowIdentitySwitchLayout.IsSwitching(doc.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingLoaderThatResolvesDoesNotRestart(bool afterSelection)
    {
        var (service, device) = Harness(afterSelection ? "sheet" : "switching");
        device.PageActor = true;
        device.SwitchAfterSelection = afterSelection;
        device.SwitchReads = 4;
        Assert.True(
            await service.SwitchToPageByIdAsync(961, "KC Waterheaters", "1007497349108596")
        );
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.DeepLinks);
        Assert.Equal(0, device.ActionsWhileSwitching);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StuckSwitchRestartsOnceAndChecksFreshTarget(
        bool afterSelection,
        bool remainsStuck
    )
    {
        var waited = TimeSpan.Zero;
        var (service, device) = Harness(afterSelection ? "sheet" : "switching", d => waited += d);
        device.PageActor = true;
        device.SwitchAfterSelection = afterSelection;
        device.RepeatSwitchAfterRestart = remainsStuck;
        Assert.Equal(
            !remainsStuck,
            await service.SwitchToPageByIdAsync(962, "KC Waterheaters", "1007497349108596")
        );
        Assert.True(waited >= TimeSpan.FromSeconds(30));
        Assert.Equal(1, device.Stops);
        Assert.Equal(1, device.Launches);
        Assert.Equal(0, device.CacheClears);
        Assert.Equal(0, device.ActionsWhileSwitching);
        Assert.Equal(0, device.DeepLinks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartDoesNotAssumeTheSelectedActorOrBypassLogin(bool login)
    {
        var (service, device) = Harness("switching");
        device.ReopenState = login ? "input" : "own";
        device.WrongActor = !login;
        Assert.False(await service.EnsureSwitchedToPersonalProfileAsync(963, "Gerardo Lopez"));
        Assert.Equal(1, device.Stops);
        Assert.Equal(0, device.ActionsWhileSwitching);
        Assert.Equal(0, device.CacheClears);
    }

    [Fact]
    public async Task LoginDuringSwitchingAndCancellationStopWithoutRestart()
    {
        var (service, device) = Harness("switching");
        device.SwitchReads = 3;
        device.SwitchOutcome = "input";
        Assert.False(await service.EnsureSwitchedToPersonalProfileAsync(964, "Gerardo Lopez"));
        Assert.Equal(0, device.Stops);
        using var ct = new CancellationTokenSource();
        (service, device) = Harness("switching", _ => ct.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EnsureSwitchedToPersonalProfileAsync(
                964,
                "Gerardo Lopez",
                cancellationToken: ct.Token
            )
        );
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.ActionsWhileSwitching);
    }
}
