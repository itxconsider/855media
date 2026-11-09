using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowCachedOwnerRegressionTests
{
    [Fact]
    public async Task CachedMetadataCannotEnablePrivateOwnerExtraction()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowRootRemovalTests.DenyDevice>();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new PFlowAccountDiscoveryService(ld).ExtractFacebookAccountsDirectFromRootAdbAsync(2)
        );
        Assert.Equal(0, ((PFlowRootRemovalTests.DenyDevice)(object)ld).Reads);
    }
}
