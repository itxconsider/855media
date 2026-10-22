using System.Net;
using System.Reflection;
using System.Text;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowFetchGraphSessionTests
{
    private static (ILDPlayerService Ld, PFlowNativeIdentityScopeTests.Device Device) Fake()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeIdentityScopeTests.Device>();
        return (ld, (PFlowNativeIdentityScopeTests.Device)(object)ld);
    }

    private sealed class Http : HttpMessageHandler
    {
        public string Id = "100000000000001";
        public int Calls;
        public Action? DuringRead;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            Assert.Equal("?fields=id,name,picture", request.RequestUri!.Query);
            DuringRead?.Invoke();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                id = Id,
                                name = "Fixture",
                                picture = new
                                {
                                    data = new { url = "https://example.test/image.jpg" },
                                },
                            }
                        ),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    [Fact]
    public async Task OneAccountThreePagesSharesGraphAcrossRootWarmAndDetailsStages()
    {
        var (ld, device) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        var reader = new PFlowGraphIdentityReader(client);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var token = device.AuthToken;
        for (var pass = 0; pass < 2; pass++)
        for (var actor = 0; actor < 4; actor++)
        {
            device.AuthToken = token + "actor" + actor;
            http.Id = (100000000000001L + actor).ToString();
            var identity = await reader.TryReadUidAsync(ld, 12, null, default);
            Assert.Equal(http.Id, identity!.Id);
            Assert.NotNull(identity.PictureUrl); // UID-first stages retain later detail fields.
            Assert.Equal(identity, await reader.TryReadAsync(ld, 12, null, default));
        }
        Assert.Equal(4, http.Calls);
        Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("token")]
    public async Task ChangedProcessOrTokenRequiresFreshGraph(string change)
    {
        var (ld, device) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var reader = new PFlowGraphIdentityReader(client);
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        if (change == "process")
            device.Pid = "999";
        else
            device.AuthToken += "changed";
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(2, http.Calls);
    }

    [Fact]
    public async Task ForeignForegroundInvalidatesProofWithoutCallingGraph()
    {
        var (ld, device) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var reader = new PFlowGraphIdentityReader(client);
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        device.Activity = "other.app/Activity";
        Assert.Null(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(1, http.Calls);
        device.Activity = "com.facebook.katana/Activity";
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(2, http.Calls);
    }

    [Fact]
    public async Task ProcessChangingDuringGraphCannotSeedProof()
    {
        var (ld, device) = Fake();
        using var http = new Http { DuringRead = () => device.Pid = "999" };
        using var client = new HttpClient(http);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var reader = new PFlowGraphIdentityReader(client);
        Assert.Null(await reader.TryReadAsync(ld, 12, null, default));
        http.DuringRead = null;
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(2, http.Calls);
    }

    [Fact]
    public async Task DisposalAndNewInvocationCannotReuseProof()
    {
        var (ld, _) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        var reader = new PFlowGraphIdentityReader(client);
        using (new PFlowFetchGraphSession(ld, 12, null))
            Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        using (new PFlowFetchGraphSession(ld, 12, null))
            Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(3, http.Calls);
    }

    [Fact]
    public async Task OwnerRotationAndReturnRequireNewVerification()
    {
        var (ld, device) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var reader = new PFlowGraphIdentityReader(client);
        var owner = device.OwnerUid;
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        device.OwnerUid = "100000000000099";
        http.Id = device.OwnerUid;
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        device.OwnerUid = owner;
        http.Id = owner;
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(3, http.Calls);
    }

    [Fact]
    public async Task CancellationDuringFreshReadCannotSeedCache()
    {
        var (ld, _) = Fake();
        using var cts = new CancellationTokenSource();
        using var http = new Http { DuringRead = () => cts.Cancel() };
        using var client = new HttpClient(http);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var reader = new PFlowGraphIdentityReader(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.TryReadAsync(ld, 12, null, cts.Token)
        );
        http.DuringRead = null;
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.Equal(2, http.Calls);
    }

    [Fact]
    public async Task DifferentDeviceServiceAndPathCannotUseScopedCache()
    {
        var (ld, _) = Fake();
        var (other, _) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        using var session = new PFlowFetchGraphSession(ld, 12, null);
        var reader = new PFlowGraphIdentityReader(client);
        Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        Assert.NotNull(await reader.TryReadAsync(ld, 13, null, default));
        Assert.NotNull(await reader.TryReadAsync(other, 12, null, default));
        Assert.NotNull(await reader.TryReadAsync(ld, 12, "different-install", default));
        Assert.Equal(4, http.Calls);
    }

    [Fact]
    public async Task ParallelFetchContextsDoNotShareProof()
    {
        var (ld, _) = Fake();
        using var http = new Http();
        using var client = new HttpClient(http);
        var reader = new PFlowGraphIdentityReader(client);
        async Task Fetch()
        {
            using var session = new PFlowFetchGraphSession(ld, 12, null);
            Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
            Assert.NotNull(await reader.TryReadAsync(ld, 12, null, default));
        }
        await Task.WhenAll(Fetch(), Fetch());
        Assert.Equal(2, http.Calls);
    }
}
