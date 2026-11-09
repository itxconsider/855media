using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AdbKeyboardServiceTests
{
    [Theory]
    [InlineData("com.android.adbkeyboard/com.android.adbkeyboard.AdbIME", true)]
    [InlineData("com.android.adbkeyboard/.AdbIME.extra", false)]
    public async Task ActiveKeyboardRequiresExactCanonicalComponent(string response, bool expected)
    {
        var service = new AdbKeyboardService(new FakeLdPlayerService { AdbResponse = response });
        Assert.Equal(expected, await service.IsAdbKeyboardActiveAsync(11));
    }

    private class FakeLdPlayerService : LDPlayerService
    {
        public List<string> ExecutedCommands { get; } = new();
        public string AdbResponse { get; set; } = "OK";
        public Func<string, string>? ResponseForCommand { get; set; }

        public override Task<string> ExecuteAdbAsync(
            int index,
            string adbCommand,
            string? customPath = null,
            CancellationToken cancellationToken = default
        )
        {
            ExecutedCommands.Add(adbCommand);
            return Task.FromResult(ResponseForCommand?.Invoke(adbCommand) ?? AdbResponse);
        }
    }

    [Fact]
    public void Constructor_NullLDPlayer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AdbKeyboardService(null!));
    }

    [Fact]
    public async Task ClearTextAdbKeyboardAsync_ExecutesClearBroadcast()
    {
        var fake = new FakeLdPlayerService();
        var service = new AdbKeyboardService(fake);

        var result = await service.ClearTextAdbKeyboardAsync(0);

        Assert.True(result);
        Assert.Contains(
            fake.ExecutedCommands,
            cmd => cmd.Contains(AdbKeyboardConstants.ActionClearText)
        );
    }

    [Fact]
    public async Task SendAdbKeyboardEditorActionAsync_ExecutesEditorCodeBroadcast()
    {
        var fake = new FakeLdPlayerService();
        var service = new AdbKeyboardService(fake);

        var result = await service.SendAdbKeyboardEditorActionAsync(0, AdbEditorAction.Done);

        Assert.True(result);
        Assert.Contains(
            fake.ExecutedCommands,
            cmd =>
                cmd.Contains(AdbKeyboardConstants.ActionEditorCode)
                && cmd.Contains(((int)AdbEditorAction.Done).ToString())
        );
    }

    [Fact]
    public async Task SendAdbKeyboardCodeAsync_ExecutesKeyCodeBroadcast()
    {
        var fake = new FakeLdPlayerService();
        var service = new AdbKeyboardService(fake);

        var result = await service.SendAdbKeyboardCodeAsync(0, 66);

        Assert.True(result);
        Assert.Contains(
            fake.ExecutedCommands,
            cmd => cmd.Contains(AdbKeyboardConstants.ActionInputCode) && cmd.Contains("66")
        );
    }

    [Fact]
    public async Task InputTextAdbKeyboardAsync_EncodesBase64AndSendsBroadcast()
    {
        var fake = new FakeLdPlayerService();
        var service = new AdbKeyboardService(fake);

        var result = await service.InputTextAdbKeyboardAsync(0, "Hello World");

        Assert.True(result);
        var expectedB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello World"));
        Assert.Contains(
            fake.ExecutedCommands,
            cmd => cmd.Contains(AdbKeyboardConstants.ActionInputB64) && cmd.Contains(expectedB64)
        );
    }

    [Fact]
    public async Task EnableAdbKeyboardAsync_EnablesAndSetsIme()
    {
        var fake = new FakeLdPlayerService
        {
            ResponseForCommand = command =>
                command switch
                {
                    "shell ime list -a -s" => AdbKeyboardConstants.ImeComponent,
                    "shell settings get secure default_input_method" =>
                        AdbKeyboardConstants.ImeComponent,
                    _ => "OK",
                },
        };
        var service = new AdbKeyboardService(fake);

        var result = await service.EnableAdbKeyboardAsync(0);

        Assert.True(result);
        Assert.Equal("shell ime list -a -s", fake.ExecutedCommands[0]);
        Assert.Equal("shell settings get secure default_input_method", fake.ExecutedCommands[^1]);
        Assert.Contains(
            "shell ime set " + AdbKeyboardConstants.ImeComponent,
            fake.ExecutedCommands
        );
        Assert.Contains(
            fake.ExecutedCommands,
            cmd => cmd.Contains("ime enable") && cmd.Contains(AdbKeyboardConstants.ImeComponent)
        );
    }

    [Fact]
    public async Task IsAdbKeyboardInstalledAsync_ReturnsTrue_WhenPackageMatches()
    {
        var fake = new FakeLdPlayerService
        {
            AdbResponse = $"package:{AdbKeyboardConstants.PackageName}",
        };
        var service = new AdbKeyboardService(fake);

        var result = await service.IsAdbKeyboardInstalledAsync(0);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("pm list packages"));
    }

    [Fact]
    public async Task IsAdbKeyboardActiveAsync_ReturnsTrue_WhenImeMatches()
    {
        var fake = new FakeLdPlayerService { AdbResponse = AdbKeyboardConstants.ImeComponent };
        var service = new AdbKeyboardService(fake);

        var result = await service.IsAdbKeyboardActiveAsync(0);

        Assert.True(result);
        Assert.Contains(fake.ExecutedCommands, cmd => cmd.Contains("default_input_method"));
    }
}
