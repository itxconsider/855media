using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AndroidAppLauncherTests
{
    [Fact]
    public async Task CancelledResolutionCannotLaunchOrFallback()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AndroidAppLauncher.StartAsync(
                "com.facebook.katana",
                (_, _) =>
                {
                    calls++;
                    stop.Cancel();
                    return Task.FromResult((true, "com.facebook.katana/.LoginActivity"));
                },
                stop.Token
            )
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MultipleResolvedActivitiesCannotLaunch()
    {
        var calls = 0;
        Assert.False(
            await AndroidAppLauncher.StartAsync(
                "com.facebook.katana",
                (_, _) =>
                {
                    calls++;
                    return Task.FromResult(
                        (true, "com.facebook.katana/.One\ncom.facebook.katana/.Two")
                    );
                },
                default
            )
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ForeignResolvedActivityIsNeverStarted()
    {
        Assert.False(
            await AndroidAppLauncher.StartAsync(
                "com.facebook.katana",
                (c, _) =>
                {
                    Assert.False(c.StartsWith("am start"));
                    return Task.FromResult((true, "com.other/.Main"));
                },
                default
            )
        );
    }

    [Theory]
    [InlineData("cmd package")]
    [InlineData("pm")]
    public async Task ResolvesOwnLauncherWithoutMonkey(string resolver)
    {
        var calls = new List<string>();
        Assert.True(
            await AndroidAppLauncher.StartAsync(
                "com.facebook.katana",
                (command, _) =>
                {
                    calls.Add(command);
                    return Task.FromResult(
                        command.StartsWith(resolver)
                            ? (true, "com.facebook.katana/.LoginActivity\r\n")
                        : command.StartsWith("am start") ? (true, "Status: ok\r\nComplete\r\n")
                        : (false, "unknown command")
                    );
                },
                default
            )
        );
        Assert.DoesNotContain(calls, c => c.StartsWith("monkey"));
    }

    [Theory]
    [InlineData("Status: timeout")]
    [InlineData("Status: ok\nError: permission denied")]
    [InlineData("")]
    public async Task DoesNotTreatFailedActivityStartAsSuccess(string response)
    {
        Assert.False(
            await AndroidAppLauncher.StartAsync(
                "com.facebook.katana",
                (c, _) =>
                    Task.FromResult(
                        (
                            true,
                            c.StartsWith("cmd") ? "com.facebook.katana/.LoginActivity" : response
                        )
                    ),
                default
            )
        );
    }

    [Theory]
    [InlineData("Events injected: 1", true)]
    [InlineData("/system/bin/sh: monkey: inaccessible or not found", false)]
    public async Task SupportsLegacyFallbackOnlyWithEvidence(string output, bool expected)
    {
        Assert.Equal(
            expected,
            await AndroidAppLauncher.StartAsync(
                "com.facebook.katana",
                (c, _) =>
                    Task.FromResult((true, c.StartsWith("monkey") ? output : "unknown command")),
                default
            )
        );
    }

    [Fact]
    public async Task RejectsShellInjectionBeforeExecuting()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AndroidAppLauncher.StartAsync(
                "com.facebook.katana;id",
                (_, _) => throw new Exception("must not execute"),
                default
            )
        );
    }
}
