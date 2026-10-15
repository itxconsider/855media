using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookPageFetchStateMachineTests
{
    private static FacebookLayoutMatch Match(string fixture) =>
        FacebookLayoutRegistry.Match(
            FacebookLayoutRegistryTests.Fixture(fixture),
            context: new("W500 PH")
        );

    private static FacebookPageFetchStateMachine Selected()
    {
        var flow = new FacebookPageFetchStateMachine("12345", "W500 PH");
        Assert.True(flow.VerifyOwnerUid("12345").Allowed);
        Assert.True(flow.ConfirmPageSelection(true).Allowed);
        return flow;
    }

    [Fact]
    public void RecognizedNameCannotBypassLiveOwnerUidAndSelection()
    {
        var flow = new FacebookPageFetchStateMachine("12345", "W500 PH");
        Assert.False(flow.ConfirmPageSelection(true).Allowed);
        Assert.False(
            flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, Match("own-profile")).Allowed
        );
        Assert.False(flow.VerifyOwnerUid("99999").Allowed);
        Assert.Equal(FacebookPageFetchState.Started, flow.State);
        Assert.True(flow.VerifyOwnerUid("12345").Allowed);
        Assert.False(flow.ConfirmPageSelection(false).Allowed);
    }

    [Fact]
    public void SupportedRouteRequiresEachTransitionAndCannotBeReused()
    {
        var flow = Selected();
        Assert.False(
            flow.Advance(FacebookPageFetchAction.ReadAnalytics, Match("analytics-summary")).Allowed
        );
        var profile = Match("own-profile");
        Assert.True(flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, profile).Allowed);
        Assert.True(flow.Advance(FacebookPageFetchAction.OpenDashboard, profile).Allowed);
        var dashboard = Match("dashboard");
        Assert.True(flow.Advance(FacebookPageFetchAction.ReadDashboard, dashboard).Allowed);
        Assert.True(flow.Advance(FacebookPageFetchAction.OpenAnalytics, dashboard).Allowed);
        Assert.True(
            flow.Advance(FacebookPageFetchAction.ReadAnalytics, Match("analytics-summary")).Allowed
        );
        flow.Complete();
        Assert.False(
            flow.Advance(FacebookPageFetchAction.ReadAnalytics, Match("analytics-summary")).Allowed
        );
        Assert.False(flow.VerifyOwnerUid("12345").Allowed);
    }

    [Fact]
    public void UnrecognizedOrMissingControlsDoNotAdvance()
    {
        var flow = Selected();
        var profile = Match("own-profile");
        Assert.True(flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, profile).Allowed);
        Assert.False(
            flow.Advance(
                FacebookPageFetchAction.OpenDashboard,
                profile with
                {
                    Controls = [],
                }
            ).Allowed
        );
        Assert.Equal(FacebookPageFetchState.OwnProfileConfirmed, flow.State);
        Assert.False(
            flow.Advance(
                FacebookPageFetchAction.OpenDashboard,
                FacebookLayoutRegistry.Match("<hierarchy/>")
            ).Allowed
        );
        Assert.Equal(FacebookPageFetchState.OwnProfileConfirmed, flow.State);
    }

    [Fact]
    public void NavigationRequiresNewObservation()
    {
        var flow = Selected();
        var profile = Match("own-profile");
        flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, profile);
        flow.Advance(FacebookPageFetchAction.OpenDashboard, profile);
        var reused = Match("dashboard") with { ObservationId = profile.ObservationId };
        Assert.Contains(
            "fresh-observation-required",
            flow.Advance(FacebookPageFetchAction.ReadDashboard, reused).MissingEvidence
        );
        Assert.Equal(FacebookPageFetchState.DashboardRequested, flow.State);
    }

    [Fact]
    public void BlockedObservationStopsOnlyItsOwnFetch()
    {
        var flow = Selected();
        var other = Selected();
        var blocked = FacebookLayoutRegistry.Match(
            "<hierarchy><node package='other.app' /></hierarchy>"
        );
        Assert.False(flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, blocked).Allowed);
        Assert.Equal(FacebookPageFetchState.Stopped, flow.State);
        Assert.Throws<InvalidOperationException>(flow.Complete);
        Assert.False(
            flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, Match("own-profile")).Allowed
        );
        Assert.Equal(FacebookPageFetchState.PageSelected, other.State);
    }

    [Fact]
    public void WrongPageContextAndPrematureCompletionAreRejected()
    {
        var flow = Selected();
        Assert.False(
            flow.Advance(
                FacebookPageFetchAction.ConfirmOwnProfile,
                Match("own-profile") with
                {
                    Context = new("Other Page"),
                }
            ).Allowed
        );
        Assert.Throws<InvalidOperationException>(flow.Complete);
    }
}
