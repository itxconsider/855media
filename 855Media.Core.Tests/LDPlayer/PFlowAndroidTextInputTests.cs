using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAndroidTextInputTests
{
    private static Task NoDelay(TimeSpan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FreshInstallWaitsForPackageRegistrationAndActivation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.DelayedRegistrationReads = 2;
        fake.DelayedActivationReads = 2;
        await PFlowAndroidTextInput.EnsureAsync(ld, 4, "ld-folder", default, NoDelay);
        Assert.Equal(1, fake.Calls.Count(c => c == "InstallAdbKeyboardAsync"));
        Assert.Equal(3, fake.Calls.Count(c => c == "EnableAdbKeyboardAsync"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingKeyboardInstallsAndExistingKeyboardIsReused(bool installed)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Installed = installed;
        await PFlowAndroidTextInput.EnsureAsync(ld, 4, "ld-folder", default);
        Assert.Equal(installed ? 0 : 1, fake.Calls.Count(c => c == "InstallAdbKeyboardAsync"));
        Assert.Equal("IsAdbKeyboardInstalledAsync", fake.Calls[0]);
        Assert.Equal("IsAdbKeyboardActiveAsync", fake.Calls[^1]);
    }

    [Theory]
    [InlineData("install")]
    [InlineData("verification")]
    [InlineData("activation")]
    public async Task SetupFailuresStopBeforeTextOrShare(string failure)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Failure = failure;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowAndroidTextInput.EnsureAsync(ld, 4, "ld-folder", default, NoDelay)
        );
        Assert.Contains("no post submitted", error.Message);
        if (failure != "activation")
            Assert.DoesNotContain("EnableAdbKeyboardAsync", fake.Calls);
    }

    [Fact]
    public async Task EnableFailureRetriesButNeverAcceptsAnUnverifiedDefault()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Installed = true;
        fake.Failure = "enable";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowAndroidTextInput.EnsureAsync(ld, 4, "ld-folder", default, NoDelay)
        );
        Assert.Equal(10, fake.Calls.Count(c => c == "EnableAdbKeyboardAsync"));
        Assert.DoesNotContain("IsAdbKeyboardActiveAsync", fake.Calls);
    }

    [Fact]
    public async Task CancellationDuringRegistrationWaitStopsBeforeActivation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.DelayedRegistrationReads = 20;
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowAndroidTextInput.EnsureAsync(
                ld,
                4,
                "ld-folder",
                stop.Token,
                (_, token) =>
                {
                    stop.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(1, fake.Calls.Count(c => c == "InstallAdbKeyboardAsync"));
        Assert.DoesNotContain("EnableAdbKeyboardAsync", fake.Calls);
    }

    [Fact]
    public async Task CancellationPerformsNoSetupActions()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowAndroidTextInput.EnsureAsync(ld, 4, "ld-folder", ct.Token)
        );
        Assert.Empty(((Device)(object)ld).Calls);
    }

    public class Device : DispatchProxy
    {
        public bool Installed;
        public int DelayedRegistrationReads;
        public int DelayedActivationReads;
        public string Failure = "";
        public List<string> Calls = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            Assert.Equal(4, args![0]);
            switch (method.Name)
            {
                case "IsAdbKeyboardInstalledAsync":
                    return Task.FromResult(Installed && DelayedRegistrationReads-- <= 0);
                case "InstallAdbKeyboardAsync":
                    Installed = Failure is not ("install" or "verification");
                    return Task.FromResult(Failure != "install");
                case "EnableAdbKeyboardAsync":
                    return Task.FromResult(Failure != "enable");
                case "IsAdbKeyboardActiveAsync":
                    return Task.FromResult(
                        Failure != "activation" && DelayedActivationReads-- <= 0
                    );
                default:
                    throw new NotSupportedException("Unexpected UI action: " + method.Name);
            }
        }
    }
}
