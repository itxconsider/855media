using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAndroidTextInputTests
{
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
            PFlowAndroidTextInput.EnsureAsync(ld, 4, "ld-folder", default)
        );
        Assert.Contains("no post submitted", error.Message);
        if (failure != "activation")
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
        public string Failure = "";
        public List<string> Calls = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            Assert.Equal(4, args![0]);
            switch (method.Name)
            {
                case "IsAdbKeyboardInstalledAsync":
                    return Task.FromResult(Installed);
                case "InstallAdbKeyboardAsync":
                    Installed = Failure is not ("install" or "verification");
                    return Task.FromResult(Failure != "install");
                case "EnableAdbKeyboardAsync":
                    return Task.FromResult(true);
                case "IsAdbKeyboardActiveAsync":
                    return Task.FromResult(Failure != "activation");
                default:
                    throw new NotSupportedException("Unexpected UI action: " + method.Name);
            }
        }
    }
}
