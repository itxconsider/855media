using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowHelperHealthTests
{
    [Fact]
    public void ReadsFreshProviderResponseWithoutTreatingVpnPresenceAsSurfsharkOwnership()
    {
        var status = PFlowHelperHealth.Parse(
            "Row: 0 json={\"protocolVersion\":1,\"heartbeat\":true,\"sampleUtcMillis\":1790000000000,\"elapsedRealtimeMillis\":6500,\"internetValidated\":true,\"activeNetworkVpn\":true,\"adbKeyboardInstalled\":false}"
        );
        Assert.NotNull(status);
        Assert.True(status.InternetValidated);
        Assert.True(status.ActiveNetworkVpn);
        Assert.False(status.AdbKeyboardInstalled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("error: device offline")]
    [InlineData("SecurityException: Permission Denial")]
    [InlineData("Row: 0 json={")]
    [InlineData("Row: 0 json={\"protocolVersion\":2,\"heartbeat\":true,\"sampleUtcMillis\":1}")]
    [InlineData("Row: 0 json={\"protocolVersion\":1,\"heartbeat\":false,\"sampleUtcMillis\":1}")]
    [InlineData("Row: 0 json={\"protocolVersion\":1,\"heartbeat\":true}")]
    public void RejectsUnavailableOrIncompatibleResponses(string? output) =>
        Assert.Null(PFlowHelperHealth.Parse(output));

    [Fact]
    public void PartialReadErrorsRemainVisibleAndDoNotBecomeInternetReady()
    {
        var status = PFlowHelperHealth.Parse(
            "Row: 0 json={\"protocolVersion\":1,\"heartbeat\":true,\"sampleUtcMillis\":1,\"statusError\":\"SecurityException\"}"
        );
        Assert.NotNull(status);
        Assert.False(status.InternetValidated);
        Assert.Equal("SecurityException", status.StatusError);
    }
}
