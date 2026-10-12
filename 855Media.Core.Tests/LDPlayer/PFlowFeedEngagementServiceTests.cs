using System;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFeedEngagementServiceTests
{
    [Fact]
    public void Constructor_NullLDPlayer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PFlowFeedEngagementService(null!));
    }

    [Fact]
    public void FindLikeButtonCoordinates_ValidXml_ReturnsCoordinates()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node content-desc=""Like button"" bounds=""[40,500][160,560]"" />
            </node>
        </hierarchy>";

        var coords = PFlowFeedEngagementService.FindLikeButtonCoordinates(xml);
        Assert.NotNull(coords);
        Assert.Equal(100, coords?.X);
        Assert.Equal(530, coords?.Y);
    }

    [Fact]
    public void FindLikeButtonCoordinates_AlreadyLiked_ReturnsNull()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node content-desc=""Remove reaction"" bounds=""[40,500][160,560]"" />
            </node>
        </hierarchy>";

        var coords = PFlowFeedEngagementService.FindLikeButtonCoordinates(xml);
        Assert.Null(coords);
    }

    [Fact]
    public void FindLikeButtonCoordinates_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(PFlowFeedEngagementService.FindLikeButtonCoordinates(null));
        Assert.Null(PFlowFeedEngagementService.FindLikeButtonCoordinates(string.Empty));
        Assert.Null(PFlowFeedEngagementService.FindLikeButtonCoordinates("   "));
    }

    [Fact]
    public void FindVideoLikeButtonCoordinates_ValidXml_ReturnsCoordinates()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node content-desc=""Like this reel"" bounds=""[620,600][700,680]"" />
            </node>
        </hierarchy>";

        var coords = PFlowFeedEngagementService.FindVideoLikeButtonCoordinates(xml);
        Assert.NotNull(coords);
        Assert.Equal(660, coords?.X);
        Assert.Equal(640, coords?.Y);
    }

    [Fact]
    public void FindVideoLikeButtonCoordinates_AlreadyLiked_ReturnsNull()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node content-desc=""Unlike"" bounds=""[620,600][700,680]"" />
            </node>
        </hierarchy>";

        var coords = PFlowFeedEngagementService.FindVideoLikeButtonCoordinates(xml);
        Assert.Null(coords);
    }

    [Fact]
    public void FindVideoLikeButtonCoordinates_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(PFlowFeedEngagementService.FindVideoLikeButtonCoordinates(null));
        Assert.Null(PFlowFeedEngagementService.FindVideoLikeButtonCoordinates(string.Empty));
    }

    [Fact]
    public async Task EnsureOnFeedAsync_EmptyXml_ReturnsTrue()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);
        var engagement = new PFlowFeedEngagementService(ldPlayer);

        var result = await engagement.EnsureOnFeedAsync(0);
        Assert.True(result);
    }

    [Fact]
    public async Task LDPlayerService_DelegationToFeedEngagementService_MatchesOutput()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);

        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node content-desc=""Like button"" bounds=""[40,500][160,560]"" />
            </node>
        </hierarchy>";

        var serviceCoords = LDPlayerService.FindLikeButtonCoordinates(xml);
        var directCoords = PFlowFeedEngagementService.FindLikeButtonCoordinates(xml);

        Assert.Equal(serviceCoords, directCoords);
        Assert.True(await ldPlayer.EnsureOnFeedAsync(0));
    }
}
