using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AndroidKeyboardActivatorTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Ld9AcknowledgedSelectionRequiresVerifiedFallback(bool readback, bool expected)
    {
        var changed = false;
        var ime = AdbKeyboardConstants.ImeComponent;
        var result = await AndroidKeyboardActivator.EnableAsync(
            (command, _) =>
            {
                var output = command switch
                {
                    "ime list -a -s" => "com.android.adbkeyboard/com.android.adbkeyboard.AdbIME",
                    "getprop ro.build.version.sdk" => "28",
                    "settings get secure default_input_method" => changed && readback
                        ? ime
                        : "normal/.IME",
                    _ when command.StartsWith("ime set") => $"Input method {ime}  selected\n",
                    _ => "",
                };
                if (command.StartsWith("settings put"))
                    changed = true;
                return Task.FromResult((true, output));
            },
            default
        );
        Assert.Equal(expected, result);
        Assert.True(changed);
    }

    [Theory]
    [InlineData(34, true, true, true)]
    [InlineData(28, true, true, false)]
    [InlineData(34, false, true, false)]
    [InlineData(34, true, false, false)]
    public async Task FallbackRequiresExactFailureSupportedAndroidAndReadback(
        int sdk,
        bool unknown,
        bool readback,
        bool expected
    )
    {
        var changed = false;
        var ime = AdbKeyboardConstants.ImeComponent;
        var result = await AndroidKeyboardActivator.EnableAsync(
            (command, _) =>
            {
                var value = command switch
                {
                    "ime list -a -s" => ime,
                    "getprop ro.build.version.sdk" => sdk.ToString(),
                    "settings get secure default_input_method" => changed && readback
                        ? ime
                        : "normal/.IME",
                    _ when command.StartsWith("ime set") => unknown
                        ? "Unknown input method"
                        : "Permission denied",
                    _ => "",
                };
                if (command.StartsWith("settings put"))
                    changed = true;
                return Task.FromResult((true, value));
            },
            default
        );
        Assert.Equal(expected, result);
        Assert.Equal(sdk >= 34 && unknown, changed);
    }

    [Fact]
    public async Task MissingExactInstalledComponentDoesNotChangeKeyboard()
    {
        var calls = 0;
        Assert.False(
            await AndroidKeyboardActivator.EnableAsync(
                (_, _) =>
                {
                    calls++;
                    return Task.FromResult((true, AdbKeyboardConstants.ImeComponent + "extra"));
                },
                default
            )
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationAfterListingDoesNotSelect()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AndroidKeyboardActivator.EnableAsync(
                (_, _) =>
                {
                    calls++;
                    stop.Cancel();
                    return Task.FromResult((true, AdbKeyboardConstants.ImeComponent));
                },
                stop.Token
            )
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NormalSelectionNeedsNoSettingsWrite()
    {
        var ime = AdbKeyboardConstants.ImeComponent;
        Assert.True(
            await AndroidKeyboardActivator.EnableAsync(
                (command, _) =>
                {
                    Assert.False(command.StartsWith("settings put"));
                    Assert.False(command.StartsWith("getprop"));
                    return Task.FromResult((true, ime));
                },
                default
            )
        );
    }
}
