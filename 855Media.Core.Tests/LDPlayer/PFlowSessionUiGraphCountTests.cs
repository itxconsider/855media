using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

// Name-session posting integration is covered in PFlowPostingNameTests. Old
// device Graph activation must stay unavailable rather than silently reuse a UID.
public class PFlowSessionUiGraphCountTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task RepeatedLegacyActivationCannotScrapeTokensOrSeedGraphProof(int targets)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowRootRemovalTests.DenyDevice>();
        using var handler = new PFlowRootRemovalTests.DenyHttp();
        using var http = new HttpClient(handler);
        for (var target = 0; target < targets; target++)
            Assert.Null(
                await new PFlowGraphIdentityReader(http).TryReadUidAsync(ld, 12, null, default)
            );
        Assert.Equal(0, ((PFlowRootRemovalTests.DenyDevice)(object)ld).Reads + handler.Reads);
    }
}
