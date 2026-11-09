using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchGraphSessionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public async Task RetiredFetchGraphScopeNeverReadsDeviceCredentialsForAnyPageCount(int pages)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowRootRemovalTests.DenyDevice>();
        using var handler = new PFlowRootRemovalTests.DenyHttp();
        using var http = new HttpClient(handler);
        using (new PFlowFetchGraphSession(ld, 12, "fixture"))
            for (var pass = 0; pass < 2; pass++)
            for (var actor = 0; actor <= pages; actor++)
                Assert.Null(
                    await new PFlowGraphIdentityReader(http).TryReadAsync(
                        ld,
                        12,
                        "fixture",
                        default
                    )
                );
        Assert.Null(PFlowFetchGraphSession.Current);
        Assert.Equal(0, ((PFlowRootRemovalTests.DenyDevice)(object)ld).Reads + handler.Reads);
    }

    [Fact]
    public async Task RetiredScopeCannotInvokeAnOldFreshReaderAndDisposedScopeRejectsRead()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowRootRemovalTests.DenyDevice>();
        var scope = new PFlowFetchGraphSession(ld, 12, null);
        Assert.Null(
            await scope.ReadAsync(() => throw new Exception("Must not extract token"), default)
        );
        scope.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            scope.ReadAsync(() => Task.FromResult<PFlowGraphIdentity?>(null), default)
        );
    }
}
