using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class LDPlayerUiAutomationServiceTests
{
    private class FakeLdPlayerService : LDPlayerService
    {
        public List<string> ExecutedCommands { get; } = new();
        public string AdbResponse { get; set; } = "OK";

        public override Task<string> ExecuteAdbAsync(
            int index,
            string adbCommand,
            string? customPath = null,
            CancellationToken cancellationToken = default
        )
        {
            ExecutedCommands.Add(adbCommand);
            return Task.FromResult(AdbResponse);
        }
    }

    [Fact]
    public void Constructor_NullLDPlayer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LDPlayerUiAutomationService(null!));
    }

    [Fact]
    public async Task SwipeAsync_SendsInputSwipeCommand()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.SwipeAsync(0, 100, 200, 300, 400, 500);

        Assert.True(result);
        Assert.Contains(
            fake.ExecutedCommands,
            cmd => cmd.Contains("input swipe 100 200 300 400 500")
        );
    }

    [Fact]
    public async Task TapAsync_SendsInputTapCommand()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.TapAsync(0, 350, 720);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input tap 350 720"));
    }

    [Fact]
    public async Task SendKeyAsync_SendsKeyeventCommand()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.SendKeyAsync(0, 66);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input keyevent 66"));
    }

    [Fact]
    public async Task PressHomeAsync_SendsKeyevent3()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.PressHomeAsync(0);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input keyevent 3"));
    }

    [Fact]
    public async Task PressBackAsync_SendsKeyevent4()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.PressBackAsync(0);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input keyevent 4"));
    }

    [Fact]
    public async Task PressRecentAppsAsync_SendsKeyevent187()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.PressRecentAppsAsync(0);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input keyevent 187"));
    }

    [Fact]
    public async Task DumpUiHierarchyAsync_ReturnsHierarchyXml()
    {
        var fake = new FakeLdPlayerService
        {
            AdbResponse = "<hierarchy rotation=\"0\"><node text=\"Home\" /></hierarchy>",
        };
        var service = new LDPlayerUiAutomationService(fake);

        var xml = await service.DumpUiHierarchyAsync(0);

        Assert.NotNull(xml);
        Assert.Contains("<hierarchy", xml);
    }

    [Fact]
    public async Task ClickElementByTextAsync_FindsNodeAndTapsCenter()
    {
        var fake = new FakeLdPlayerService
        {
            AdbResponse =
                "<hierarchy rotation=\"0\"><node text=\"Log In\" bounds=\"[100,200][300,400]\" /></hierarchy>",
        };
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.ClickElementByTextAsync(0, "Log In");

        Assert.True(result);
        // Center of [100,200][300,400] is X: (100+300)/2 = 200, Y: (200+400)/2 = 300
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input tap 200 300"));
    }

    [Fact]
    public async Task ClickElementByResourceIdAsync_FindsNodeAndTapsCenter()
    {
        var fake = new FakeLdPlayerService
        {
            AdbResponse =
                "<hierarchy rotation=\"0\"><node resource-id=\"com.facebook.katana:id/login_btn\" bounds=\"[100,500][500,600]\" /></hierarchy>",
        };
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.ClickElementByResourceIdAsync(0, "login_btn");

        Assert.True(result);
        // Center of [100,500][500,600] is X: (100+500)/2 = 300, Y: (500+600)/2 = 550
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("input tap 300 550"));
    }

    [Fact]
    public async Task InputTextAsync_EscapesCharactersAndSendsShellInput()
    {
        var fake = new FakeLdPlayerService();
        var service = new LDPlayerUiAutomationService(fake);

        var result = await service.InputTextAsync(0, "Hello World! & Special #1");

        Assert.True(result);
        Assert.Contains(
            fake.ExecutedCommands,
            cmd => cmd.Contains("shell input text \"Hello%sWorld!%s\\&%sSpecial%s\\#1\"")
        );
    }
}
