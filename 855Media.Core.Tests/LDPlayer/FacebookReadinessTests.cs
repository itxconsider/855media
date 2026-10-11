using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookReadinessTests
{
    [Theory]
    [InlineData("WIFI")]
    [InlineData("ETHERNET")]
    [InlineData("CELLULAR")]
    [InlineData("VPN")]
    public void InternetCheck_AcceptsValidatedInternetWithoutVpnOwner(string transport)
    {
        var dump =
            $"Active default network: 134\nCurrent Networks:\n  NetworkAgentInfo{{ network{{134}} ni{{[type: {transport}[], state: CONNECTED/CONNECTED]}} nc{{[ Transports: {transport} Capabilities: INTERNET&VALIDATED]}} }}";
        Assert.True(LDPlayerService.IsAndroidInternetConnected(dump));
    }

    [Theory]
    [InlineData("CONNECTED/CONNECTED", "INTERNET")]
    [InlineData("CONNECTED/CONNECTED", "VALIDATED")]
    [InlineData("CONNECTED/CONNECTED", "INTERNET&CAPTIVE_PORTAL")]
    [InlineData("DISCONNECTED/DISCONNECTED", "INTERNET&VALIDATED")]
    [InlineData("CONNECTED/CONNECTED", "INTERNET&UNVALIDATED")]
    public void InternetCheck_RejectsOfflineUnvalidatedOrCaptiveNetworks(string state, string flags)
    {
        var dump =
            $"Active default network: 134\nCurrent Networks:\n  NetworkAgentInfo{{ network{{134}} ni{{[state: {state}]}} nc{{[ Capabilities: {flags}]}} }}";
        Assert.False(LDPlayerService.IsAndroidInternetConnected(dump));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Active default network: 999\n")]
    public void InternetCheck_RequiresTheValidatedNetworkToBeTheActiveDefault(string defaultNetwork)
    {
        var dump =
            defaultNetwork
            + "Current Networks:\n  NetworkAgentInfo{ network{134} ni{[state: CONNECTED/CONNECTED]} nc{[ Capabilities: INTERNET&VALIDATED]} }";
        Assert.False(LDPlayerService.IsAndroidInternetConnected(dump));
    }

    [Fact]
    public void InternetCheck_RejectsHistoricalValidationAndUnwantedFlags()
    {
        var invalid =
            "Current Networks:\n  NetworkAgentInfo{ ni{[state: CONNECTED/CONNECTED]} nc{[ Capabilities: INTERNET Unwanted: VALIDATED]} }\n";
        Assert.False(LDPlayerService.IsAndroidInternetConnected(invalid));
        Assert.False(
            LDPlayerService.IsAndroidInternetConnected(
                invalid
                    + "Network event history:\n  NetworkAgentInfo{ ni{[state: CONNECTED/CONNECTED]} nc{[ Capabilities: INTERNET&VALIDATED]} }"
            )
        );
    }

    [Theory]
    [InlineData("su 0 id", false)]
    [InlineData("su -c cat /data/data/com.facebook.katana/preferences", false)]
    [InlineData("  su\t0 ls /data/data", false)]
    [InlineData("su", false)]
    [InlineData("run-as com.facebook.katana ls", false)]
    [InlineData("input tap 664 644", false)]
    [InlineData("dumpsys connectivity", true)]
    [InlineData("pm list packages -U com.surfshark.vpnclient.android", true)]
    public void ShellRouting_UsesRealAdbForRootAndInputCommands(string command, bool useAtx)
    {
        Assert.Equal(useAtx, LDPlayerService.CanExecuteThroughAtxShell(command));
    }

    [Fact]
    public void FocusParser_RejectsShellErrors()
    {
        Assert.Null(AtxAgentClient.ParseFocusedActivity("/system/bin/sh: grep: bad argument"));
    }

    [Fact]
    public void FocusParser_PrefersCurrentWindowAndAcceptsRelativeActivity()
    {
        var output =
            "mFocusedApp=Token{u0 com.old.app/.Old}\n"
            + "mCurrentFocus=Window{u0 com.facebook.katana/.ProfileActivity}";
        Assert.Equal(
            "com.facebook.katana/.ProfileActivity",
            AtxAgentClient.ParseFocusedActivity(output)
        );
    }

    [Fact]
    public void VpnCheck_RequiresValidatedTunnelOwnedBySurfshark()
    {
        var dump =
            "Current Networks:\n  NetworkAgentInfo{ ni{[type: VPN[], state: CONNECTED/CONNECTED]} "
            + "nc{[ Transports: VPN Capabilities: INTERNET&TRUSTED&VALIDATED EstablishingAppUid: 10065]} }";
        Assert.True(LDPlayerService.IsSurfsharkVpnConnected(dump, 10065));
        Assert.False(LDPlayerService.IsSurfsharkVpnConnected(dump, 10066));
        Assert.False(
            LDPlayerService.IsSurfsharkVpnConnected(dump.Replace("&VALIDATED", ""), 10065)
        );
    }

    [Fact]
    public void VpnCheck_RejectsWifiAndHistoricalVpnRequests()
    {
        var dump =
            "Current Networks:\n  NetworkAgentInfo{ ni{[type: WIFI[], state: CONNECTED/CONNECTED]} "
            + "nc{[ Transports: WIFI Capabilities: INTERNET&NOT_VPN&VALIDATED]} }\n"
            + "NetworkRequest [Transports: VPN EstablishingAppUid: 10065]";
        Assert.False(LDPlayerService.IsSurfsharkVpnConnected(dump, 10065));
    }

    [Fact]
    public void VpnCheck_RejectsPreviouslyConnectedAgentInHistorySection()
    {
        var dump =
            "Current Networks:\n"
            + "  NetworkAgentInfo{ ni{[type: WIFI[], state: CONNECTED/CONNECTED]} "
            + "nc{[ Transports: WIFI Capabilities: INTERNET&VALIDATED]} }\n"
            + "Network event history:\n"
            + "  NetworkAgentInfo{ ni{[type: VPN[], state: CONNECTED/CONNECTED]} "
            + "nc{[ Transports: VPN Capabilities: INTERNET&VALIDATED EstablishingAppUid: 10065]} }";
        Assert.False(LDPlayerService.IsSurfsharkVpnConnected(dump, 10065));
    }

    [Fact]
    public void VpnCheck_RequiresConnectedStateInsideSameAgentRecord()
    {
        var dump =
            "Current Networks:\n"
            + "  NetworkAgentInfo{ ni{[type: VPN[], state: DISCONNECTED/DISCONNECTED]} "
            + "nc{[ Transports: VPN Capabilities: INTERNET&VALIDATED EstablishingAppUid: 10065]} }\n"
            + "    historical state: CONNECTED/CONNECTED\n";
        Assert.False(LDPlayerService.IsSurfsharkVpnConnected(dump, 10065));
    }

    [Fact]
    public void VpnCheck_HandlesNestedLinkAndUidBracesInCurrentAgent()
    {
        var dump =
            "Current Networks:\r\r\n"
            + "  NetworkAgentInfo{ ni{[type: VPN[], state: CONNECTED/CONNECTED]} "
            + "lp{{InterfaceName: tun1 Routes: [0.0.0.0/0 -> tun1]}} "
            + "nc{[ Transports: VPN Capabilities: INTERNET&VALIDATED "
            + "Uids: <{0-99999}> OwnerUid: 10065]} everValidated{true} }\r\r\n"
            + "Network Requests:\r\r\n";
        Assert.True(LDPlayerService.IsSurfsharkVpnConnected(dump, 10065));
    }

    [Fact]
    public void VpnCheck_RejectsUnwantedValidatedCapability()
    {
        var dump =
            "Current Networks:\n"
            + "  NetworkAgentInfo{ ni{[type: VPN[], state: CONNECTED/CONNECTED]} "
            + "nc{[ Transports: VPN Capabilities: INTERNET Unwanted: VALIDATED OwnerUid: 10065]} }";
        Assert.False(LDPlayerService.IsSurfsharkVpnConnected(dump, 10065));
    }
}
