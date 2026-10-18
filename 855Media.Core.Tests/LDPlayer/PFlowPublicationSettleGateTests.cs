using System;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPublicationSettleGateTests
{
    private const string Clear =
        "<hierarchy><node package='com.facebook.katana' text='Home'/></hierarchy>";
    private const string Scan =
        "<hierarchy><node package='com.facebook.katana' text='Running copyright scan...'/></hierarchy>";

    [Fact]
    public void BrieflyMissingScanCannotAuthorizeFallback()
    {
        var gate = new PFlowPublicationSettleGate();
        Assert.False(gate.Observe(Scan, TimeSpan.Zero));
        Assert.False(gate.Observe(Clear, TimeSpan.FromSeconds(1)));
        Assert.False(gate.Observe(Clear, TimeSpan.FromSeconds(9)));
        Assert.False(gate.Observe(Scan, TimeSpan.FromSeconds(10)));
        Assert.False(gate.Observe(Clear, TimeSpan.FromSeconds(11)));
        Assert.False(gate.Observe(Clear, TimeSpan.FromSeconds(40)));
        Assert.True(gate.Observe(Clear, TimeSpan.FromSeconds(41)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<hierarchy/>")]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' class='android.widget.ProgressBar'/></hierarchy>"
    )]
    [InlineData("<hierarchy><node package='other.app' text='Home'/></hierarchy>")]
    public void MissingLoadingAndForeignScreensResetQuietPeriod(string transient)
    {
        var gate = new PFlowPublicationSettleGate();
        Assert.False(gate.Observe(Clear, TimeSpan.Zero));
        Assert.False(gate.Observe(transient, TimeSpan.FromSeconds(29)));
        Assert.False(gate.Observe(Clear, TimeSpan.FromSeconds(30)));
        Assert.True(gate.Observe(Clear, TimeSpan.FromSeconds(60)));
        gate.Reset();
        Assert.False(gate.Observe(Clear, TimeSpan.FromSeconds(61)));
    }
}
