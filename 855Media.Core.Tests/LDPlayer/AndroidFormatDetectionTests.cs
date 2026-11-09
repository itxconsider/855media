using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AndroidFormatDetectionTests
{
    [Theory]
    [InlineData(45)]
    [InlineData(46)]
    public void CapturedAndroid14ActiveNetworkAndResumedActivity(int index)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures/PFlowPlatform");
        var net = File.ReadAllText(Path.Combine(folder, $"ld14-{index}-connectivity.txt"));
        Assert.True(LDPlayerService.IsAndroidInternetConnected(net));
        Assert.False(
            LDPlayerService.IsAndroidInternetConnected(
                net.Replace("Active default network: 100", "Active default network: 999")
            )
        );
        Assert.False(
            LDPlayerService.IsAndroidInternetConnected(net.Replace("&VALIDATED", "&UNVALIDATED"))
        );
        var activities = File.ReadAllText(Path.Combine(folder, $"ld14-{index}-activity.txt"));
        var result = AtxAgentClient.ParseFocusedActivity(
            "ACTIVITY com.ldmnq.launcher3/.Launcher\n" + activities
        );
        Assert.StartsWith("com.facebook.katana/", result);
    }

    [Theory]
    [InlineData("WIFI CONNECTED extra:", true)]
    [InlineData("VPN CONNECTED extra:", true)]
    [InlineData("ETHERNET CONNECTED extra:", true)]
    [InlineData("WIFI DISCONNECTED extra:", false)]
    [InlineData("WIFI CONNECTING extra:", false)]
    public void DetectsCompactNetworkStateWithoutLooseningValidation(string ni, bool connected)
    {
        var text =
            $"Active default network: 100\nCurrent Networks:\n  NetworkAgentInfo{{network{{100}} ni{{{ni}}} nc{{[ Capabilities: INTERNET&VALIDATED]}} }}";
        Assert.Equal(connected, LDPlayerService.IsAndroidInternetConnected(text));
    }

    [Fact]
    public void ForeignFocusWinsAndAmbiguousOrHistoricalActivitiesCannotAuthorize()
    {
        Assert.Equal(
            "com.android.settings/.Settings",
            AtxAgentClient.ParseFocusedActivity(
                "mCurrentFocus=Window{u0 com.android.settings/.Settings}\ntopResumedActivity=ActivityRecord{u0 com.facebook.katana/.Main}"
            )
        );
        Assert.Null(
            AtxAgentClient.ParseFocusedActivity(
                "ACTIVITY com.ldmnq.launcher3/.Launcher\nACTIVITY com.facebook.katana/.Main"
            )
        );
        Assert.Null(
            AtxAgentClient.ParseFocusedActivity(
                "topResumedActivity=ActivityRecord{u0 com.facebook.katana/.Main}\nResumedActivity: ActivityRecord{u0 com.android.settings/.Settings}"
            )
        );
    }

    [Fact]
    public void Android14VpnTransportCombinationStillRequiresExactOwnerAndValidation()
    {
        var text =
            "Current Networks:\n  NetworkAgentInfo{network{102} ni{VPN CONNECTED extra: } nc{[ Transports: WIFI|VPN Capabilities: INTERNET&VALIDATED OwnerUid: 10079]} }";
        Assert.True(LDPlayerService.IsSurfsharkVpnConnected(text, 10079));
        Assert.False(LDPlayerService.IsSurfsharkVpnConnected(text, 10080));
        Assert.False(
            LDPlayerService.IsSurfsharkVpnConnected(
                text.Replace("&VALIDATED", "&UNVALIDATED"),
                10079
            )
        );
    }
}
