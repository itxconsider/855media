using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchProcessGuardTests
{
    [Theory]
    [InlineData("other.app/Main", "123", "no longer in the foreground")]
    [InlineData("com.facebook.katana/Main", "", "not running")]
    [InlineData("com.facebook.katana/Main", "unreadable", "not running")]
    public async Task FailureMessageDescribesObservedCondition(
        string activity,
        string pid,
        string message
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Activity = activity;
        device.Pid = pid;
        using var scope = new PFlowNativeFetchScope(ld, 900944, "fixture");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetchDevice(
                ld,
                900944,
                "fixture",
                _ => Task.CompletedTask
            ).ReadNumericIdAsync(new("Owner", "Profile"), default)
        );
        Assert.Contains(message, error.Message);
        Assert.DoesNotContain("another app opened", error.Message);
    }

    [Fact]
    public async Task MissingHierarchyCannotLaunchOrRestartDuringPinnedFetch()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using var scope = new PFlowNativeFetchScope(ld, 900944, "fixture");
        Assert.False(
            await new PFlowPersonaSwitcherService(ld).SelectNativeTargetAsync(
                900944,
                new() { PageName = "Owner", Type = "Profile" },
                "fixture",
                default
            )
        );
    }

    public class Device : DispatchProxy
    {
        public string Activity = "com.facebook.katana/Main",
            Pid = "123";

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name switch
            {
                "GetCurrentFocusedActivityAsync" => Task.FromResult<string?>(Activity),
                "ExecuteAdbAsync" when (string)args![1]! == "shell pidof com.facebook.katana" =>
                    Task.FromResult(Pid),
                "DumpBloksScreenAsync" => Task.FromResult<BloksScreenInfo?>(null),
                _ => throw new InvalidOperationException(
                    "Unexpected device action: " + method.Name
                ),
            };
    }
}
