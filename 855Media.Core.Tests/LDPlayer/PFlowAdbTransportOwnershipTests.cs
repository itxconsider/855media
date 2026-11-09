using System.Net;
using System.Net.Sockets;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAdbTransportOwnershipTests
{
    [Theory]
    [InlineData("C:/LD/14", "C:/LD/14/dnplayer.exe", 200, true)]
    [InlineData("C:/LD/14", "C:/LD/9/dnplayer.exe", 200, false)]
    [InlineData("C:/LD/14", "C:/LD/14/dnplayer.exe", 201, false)]
    [InlineData("C:/LD/14", "C:/LD/14/other.exe", 200, false)]
    public void SameIndexCannotAuthorizeAnotherInstallationOrVm(
        string folder,
        string processPath,
        int owner,
        bool expected
    )
    {
        var instance = new LDInstanceInfo
        {
            Index = 45,
            IsAndroidStarted = true,
            Pid = 100,
            VboxPid = 200,
        };
        Assert.Equal(
            expected,
            PFlowAdbTransportOwnership.Matches(instance, folder, processPath, [owner])
        );
        Assert.False(PFlowAdbTransportOwnership.Matches(instance, folder, processPath, []));
        Assert.False(PFlowAdbTransportOwnership.Matches(instance, folder, processPath, [200, 201]));
        instance.IsAndroidStarted = false;
        Assert.False(PFlowAdbTransportOwnership.Matches(instance, folder, processPath, [200]));
    }

    [Fact]
    public void NativeListenerReaderFindsThisProcessAndNoUnboundPort()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Equal([Environment.ProcessId], PFlowAdbTransportOwnership.ListeningOwners(port));
        listener.Stop();
        Assert.Empty(PFlowAdbTransportOwnership.ListeningOwners(port));
    }

    [Fact]
    public void MissingExplicitInstallationCannotFallBackToInstalledLd()
    {
        var folder = Path.Combine(Path.GetTempPath(), "missing-ld-" + Guid.NewGuid().ToString("N"));
        var service = new LDPlayerService();
        Assert.Equal(Path.Combine(folder, "ldconsole.exe"), service.ResolveLdConsolePath(folder));
        Assert.False(service.IsInstalled(folder));
    }
}
