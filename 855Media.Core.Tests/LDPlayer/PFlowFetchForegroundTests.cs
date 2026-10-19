using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchForegroundTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LauncherIsRecoveredBeforePersonalLookup(int readyAfterLaunch)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.ReadyAfterLaunch = readyAfterLaunch;
        await PFlowSession.EnsureFetchFacebookForegroundAsync(
            ld,
            2,
            "test-ld",
            default,
            delay: (_, _) => Task.CompletedTask
        );
        Assert.Equal(readyAfterLaunch, fake.Launches);
        if (readyAfterLaunch > 0)
            Assert.Equal("com.facebook.katana", fake.Package);
        else
            Assert.Null(fake.Package);
    }

    [Fact]
    public async Task PersistentLauncherStopsBeforePersonalUiActions()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.ReadyAfterLaunch = 3;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowSession.EnsureFetchFacebookForegroundAsync(
                ld,
                2,
                null,
                default,
                delay: (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(2, fake.Launches);
        Assert.Contains("Root account data is saved", error.Message);
    }

    [Fact]
    public async Task CancellationDoesNotLaunchFacebook()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowSession.EnsureFetchFacebookForegroundAsync(ld, 2, null, ct.Token)
        );
        Assert.Equal(0, ((Device)(object)ld).Launches);
    }

    public class Device : DispatchProxy
    {
        public int ReadyAfterLaunch;
        public int Launches;
        public string? Package;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "GetCurrentFocusedActivityAsync")
                return Task.FromResult<string?>(
                    Launches >= ReadyAfterLaunch
                        ? "com.facebook.katana/com.facebook.FacebookActivity"
                        : "com.ldmnq.launcher3/com.android.launcher3.Launcher"
                );
            if (method.Name == "LaunchAppAsync")
            {
                Assert.Equal(2, args![0]);
                Launches++;
                Package = (string)args[1]!;
                return Task.FromResult(true);
            }
            throw new NotSupportedException("Unexpected device action: " + method.Name);
        }
    }
}
