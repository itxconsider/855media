using System.Net;
using System.Net.Http;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowInstallationRoutingTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "855-routing-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void BlockedSavedPortIsReplacedWithoutChangingOtherAssignments()
    {
        var old = PFlowInstallationRouting.PortCore("blocked", 3, _root, (_, _) => true);
        var other = PFlowInstallationRouting.PortCore("other", 3, _root, (_, _) => true);
        var replacement = PFlowInstallationRouting.PortCore(
            "blocked",
            3,
            _root,
            (port, _) => port != old
        );
        Assert.NotEqual(old, replacement);
        Assert.NotEqual(other, replacement);
        Assert.Equal(
            replacement,
            PFlowInstallationRouting.PortCore("blocked", 3, _root, (_, _) => true)
        );
        Assert.Equal(other, PFlowInstallationRouting.PortCore("other", 3, _root, (_, _) => true));
    }

    [Fact]
    public void NewAllocationSkipsUnusableCandidatesAndOccupiedSavedPortIsPreserved()
    {
        var calls = 0;
        var allocated = PFlowInstallationRouting.PortCore(
            "candidate",
            3,
            _root,
            (_, existing) => existing || ++calls > 3
        );
        Assert.Equal(4, calls);
        Assert.Equal(
            allocated,
            PFlowInstallationRouting.PortCore("candidate", 3, _root, (_, existing) => existing)
        );
    }

    [Fact]
    public void PortsArePersistentAndInstallationScopedWithCanonicalPaths()
    {
        var nine = PFlowInstallationRouting.Port(@"C:\LDPlayer\LDPlayer9", 45, _root);
        var fourteen = PFlowInstallationRouting.Port(@"C:\LDPlayer\LDPlayer14", 45, _root);
        Assert.NotEqual(nine, fourteen);
        Assert.Equal(
            nine,
            PFlowInstallationRouting.Port(@"c:\ldplayer\ldplayer9\ldconsole.exe", 45, _root)
        );
        Assert.Equal(
            fourteen,
            PFlowInstallationRouting.Port(@"C:\LDPlayer\LDPlayer14\", 45, _root)
        );
        Assert.NotEqual(nine, PFlowInstallationRouting.Port(@"C:\LDPlayer\LDPlayer9", 46, _root));
    }

    [Fact]
    public async Task ConcurrentClientsAllocateUniquePortsAndDoNotCrossHttpRoutes()
    {
        var ports = await Task.WhenAll(
            Enumerable
                .Range(0, 25)
                .Select(i =>
                    Task.Run(() => PFlowInstallationRouting.Port("fixture-" + i, 45, _root))
                )
        );
        Assert.Equal(25, ports.Distinct().Count());
        var handler = new Capture();
        using var client = new AtxAgentClient(
            httpClient: new HttpClient(handler),
            routingDirectory: _root
        );
        var nine = client.ForInstallation(@"C:\LDPlayer\LDPlayer9");
        var fourteen = client.ForInstallation(@"C:\LDPlayer\LDPlayer14");
        Assert.Same(nine, client.ForInstallation(@"c:\ldplayer\ldplayer9\"));
        Assert.True(await nine.IsRunningAsync(45));
        Assert.True(await fourteen.IsRunningAsync(45));
        Assert.NotEqual(handler.Ports[0], handler.Ports[1]);
        nine.Dispose();
        Assert.True(await fourteen.IsRunningAsync(45));
        Assert.Equal(AtxAgentClient.DefaultBasePort + 45, client.GetDevicePort(45));
    }

    [Fact]
    public void CorruptRegistryDoesNotReuseUnverifiedPort()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "ports.json"), "{\"a\":25000,\"b\":25000}");
        Assert.Throws<InvalidDataException>(() =>
            PFlowInstallationRouting.Port("fixture", 45, _root)
        );
    }

    [Fact]
    public void ActiveSavedListenerKeepsItsAssignment()
    {
        var port = PFlowInstallationRouting.Port("active-forward", 3, _root);
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();
        try
        {
            Assert.Equal(port, PFlowInstallationRouting.Port("active-forward", 3, _root));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void HashCollisionUsesAnotherPersistedPort()
    {
        Directory.CreateDirectory(_root);
        var key = PFlowInstallationRouting.Key("fixture-collision", 45);
        var candidate = 20000 + (int)(Convert.ToUInt32(key[..8], 16) % 40000);
        File.WriteAllText(
            Path.Combine(_root, "ports.json"),
            "{\"existing-installation\":" + candidate + "}"
        );
        var port = PFlowInstallationRouting.Port("fixture-collision", 45, _root);
        Assert.NotEqual(candidate, port);
        Assert.Equal(port, PFlowInstallationRouting.Port("fixture-collision", 45, _root));
    }

    [Fact]
    public async Task LdServiceRoutesSameIndexThroughExplicitInstallationClients()
    {
        var handler = new Capture();
        using var atx = new AtxAgentClient(
            httpClient: new HttpClient(handler),
            routingDirectory: _root
        );
        var service = new LDPlayerService(atx);
        var first = Path.Combine(_root, "LD9");
        var second = Path.Combine(_root, "LD14");
        Assert.Equal(
            "com.facebook.katana/Main",
            await service.GetCurrentFocusedActivityAsync(45, first)
        );
        var firstPorts = handler.Ports.ToArray();
        handler.Ports.Clear();
        Assert.Equal(
            "com.facebook.katana/Main",
            await service.GetCurrentFocusedActivityAsync(45, second)
        );
        Assert.All(firstPorts, p => Assert.Equal(atx.ForInstallation(first).GetDevicePort(45), p));
        Assert.All(
            handler.Ports,
            p => Assert.Equal(atx.ForInstallation(second).GetDevicePort(45), p)
        );
        Assert.NotEqual(firstPorts[0], handler.Ports[0]);
    }

    [Fact]
    public async Task SameInstallationBlocksButOtherInstallationWithSameIndexCanProceed()
    {
        using var first = await PFlowDeviceLease.AcquireAsync(
            900045,
            default,
            @"C:\LDPlayer\LDPlayer9"
        );
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var second = await PFlowDeviceLease.AcquireAsync(
            900045,
            budget.Token,
            @"C:\LDPlayer\LDPlayer14"
        );
        using var blocked = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowDeviceLease.AcquireAsync(
                900045,
                blocked.Token,
                @"c:\ldplayer\ldplayer9\ldconsole.exe"
            )
        );
    }

    class Capture : HttpMessageHandler
    {
        public List<int> Ports = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Ports.Add(request.RequestUri!.Port);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        request.RequestUri.AbsolutePath == "/shell"
                            ? "{\"output\":\"mCurrentFocus=Window{123 u0 com.facebook.katana/Main}\"}"
                            : "0.10.1"
                    ),
                }
            );
        }
    }
}
