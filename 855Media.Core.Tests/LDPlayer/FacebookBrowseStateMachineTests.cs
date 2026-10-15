using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookBrowseStateMachineTests
{
    private static string Captured(string name) =>
        FacebookLayoutRegistryTests.Fixture(name, "PFlowScreenTime");

    private static string Home => FacebookLayoutRegistryTests.Fixture("feed-home");

    private static string Continuity()
    {
        var doc = XDocument.Parse(Home);
        foreach (
            var node in doc.Descendants("node")
                .Where(n =>
                    n.Attribute("selected") != null
                    || n.Attribute("text")?.Value == "What's on your mind?"
                )
                .ToArray()
        )
            node.Remove();
        return doc.ToString();
    }

    [Theory]
    [InlineData("feed-home", "PFlowLayouts", FacebookScreenKind.NewsFeed, "feed.home.v1")]
    [InlineData(
        "ld1-feed-scrolled",
        "PFlowScreenTime",
        FacebookScreenKind.NewsFeed,
        "feed.scrolled.v1"
    )]
    [InlineData(
        "ld1-reels-interest",
        "PFlowScreenTime",
        FacebookScreenKind.ReelsFeed,
        "reels.tabbed.v1"
    )]
    [InlineData(
        "ld9-reels-fullscreen",
        "PFlowScreenTime",
        FacebookScreenKind.ReelsFeed,
        "reels.fullscreen.v1"
    )]
    public void SavedLayoutsHaveExactlyOneBrowsingVariant(
        string file,
        string folder,
        FacebookScreenKind screen,
        string variant
    )
    {
        var match = FacebookLayoutRegistry.Match(FacebookLayoutRegistryTests.Fixture(file, folder));
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(screen, match.Screen);
        Assert.Equal(variant, match.VariantId);
        Assert.NotNull(match.Browse);
        Assert.Null(match.Control(FacebookControlRole.Publish));
    }

    [Theory]
    [InlineData("ld8-feed-inline-story", FacebookScreenKind.NewsFeed)]
    [InlineData("ld1-feed-finished-video-product", FacebookScreenKind.NewsFeed)]
    [InlineData("ld9-reels-fullscreen-interest", FacebookScreenKind.ReelsFeed)]
    [InlineData("ld1-reels-interest", FacebookScreenKind.ReelsFeed)]
    public void SpecialCardsCanScrollWithoutAuthorizingInteraction(
        string fixture,
        FacebookScreenKind kind
    )
    {
        var flow = new FacebookBrowseStateMachine(kind);
        var decision = flow.Observe(new(Captured(fixture)));
        Assert.True(decision.Allowed, decision.Diagnostic);
        Assert.False(decision.InteractionReady);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Interact));
        Assert.True(flow.TryBeginInput(FacebookBrowseAction.Scroll));
    }

    [Theory]
    [InlineData(FacebookBrowseAction.Scroll)]
    [InlineData(FacebookBrowseAction.Interact)]
    public void EveryInputRequiresAnUnusedFreshObservation(FacebookBrowseAction action)
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        Assert.False(flow.TryBeginInput(action));
        var first = new FacebookLayoutObservation(Home);
        Assert.True(flow.Observe(first).Allowed);
        var older = new FacebookLayoutObservation(Home);
        Assert.True(flow.Observe(older).Allowed);
        Assert.True(flow.TryBeginInput(action));
        Assert.False(flow.TryBeginInput(action));
        Assert.False(flow.Observe(first).Allowed);
        Assert.False(flow.Observe(older).Allowed);
        Assert.True(flow.Observe(new(Home)).Allowed); // Same content, genuinely new capture.
        Assert.True(flow.TryBeginInput(action));
    }

    [Fact]
    public void ArbitraryListCannotEstablishFeedButVerifiedContinuityCanScroll()
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        var unknown = Continuity();
        Assert.False(flow.Observe(new(unknown)).Allowed);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        Assert.True(flow.Observe(new(Home)).Allowed);
        Assert.True(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        var continuity = flow.Observe(new(unknown));
        Assert.True(continuity.Allowed, continuity.Diagnostic);
        Assert.False(continuity.Match.IsMatch);
        Assert.False(continuity.InteractionReady);
        Assert.Equal("verified-feed-continuity-scroll-only", continuity.Reason);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Interact));
        Assert.True(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        Assert.False(flow.Observe(new(unknown.Replace("facebook:pager", "other:pager"))).Allowed);
    }

    [Fact]
    public void InteractionInvalidatesContainerContinuity()
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        Assert.True(flow.Observe(new(Home)).Allowed);
        Assert.True(flow.TryBeginInput(FacebookBrowseAction.Interact));
        Assert.False(flow.Observe(new(Continuity())).Allowed);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
    }

    [Fact]
    public void RecoveryInvalidatesEarlierRecognitionAndRequiresFreshDestination()
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        var prior = new FacebookLayoutObservation(Home);
        flow.Observe(prior);
        flow.BeginRecovery();
        Assert.Equal(FacebookBrowseState.Recovering, flow.State);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        Assert.False(flow.Observe(prior).Allowed);
        Assert.False(flow.Observe(new(Continuity())).Allowed);
        Assert.True(flow.Observe(new(Home)).Allowed);
        Assert.True(flow.TryBeginInput(FacebookBrowseAction.Scroll));
    }

    [Theory]
    [InlineData("login")]
    [InlineData("human-checkpoint")]
    [InlineData("approval")]
    public void AuthenticationStopsBrowsingEvenWithFeedBehindIt(string prompt)
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        flow.Observe(new(Home));
        var doc = XDocument.Parse(Home);
        var overlay = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld74-facebook-581-" + prompt, "PFlowLogin")
        );
        doc.Root!.Add(overlay.Root!.Elements().Select(n => new XElement(n)));
        var decision = flow.Observe(new(doc.ToString()));
        Assert.False(decision.Allowed);
        Assert.Equal(FacebookBrowseState.Stopped, flow.State);
        flow.BeginRecovery();
        Assert.False(flow.Observe(new(Home)).Allowed);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
    }

    [Theory]
    [InlineData("ld9-menu-over-notifications")]
    [InlineData("ld2-personal-professional")]
    [InlineData("ld56-switcher-untyped-top")]
    public void OtherScreensCannotBorrowFeedOrReelsLabels(string fixture)
    {
        var xml = Captured(fixture);
        foreach (var kind in new[] { FacebookScreenKind.NewsFeed, FacebookScreenKind.ReelsFeed })
        {
            var flow = new FacebookBrowseStateMachine(kind);
            Assert.False(flow.Observe(new(xml)).Allowed);
            Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        }
    }

    [Fact]
    public void SpinnerWithoutFeedContentCannotAuthorizeScrolling()
    {
        var doc = XDocument.Parse(Home);
        doc.Root!.Add(new XElement("node", new XAttribute("class", "android.widget.ProgressBar")));
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        var decision = flow.Observe(new(doc.ToString()));
        Assert.False(decision.Allowed, decision.Diagnostic);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        Assert.Contains(decision.Match.Candidates, c => c.ExcludedBy.Contains("browse-loading"));
    }

    [Fact]
    public void ForeignAppAndExplicitStopCannotResumeOrAffectAnotherDevice()
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        var other = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        Assert.False(flow.Observe(new(Home.Replace("com.facebook.katana", "other.app"))).Allowed);
        Assert.Equal(FacebookBrowseState.Stopped, flow.State);
        Assert.True(other.Observe(new(Home)).Allowed);
        other.Stop();
        Assert.False(other.TryBeginInput(FacebookBrowseAction.Scroll));
        Assert.False(other.Observe(new(Home)).Allowed);
    }

    [Fact]
    public void WrongDestinationAndConflictingTabsRequireRecognitionAgain()
    {
        var flow = new FacebookBrowseStateMachine(FacebookScreenKind.NewsFeed);
        Assert.True(flow.Observe(new(Home)).Allowed);
        var conflict = XDocument.Parse(Continuity());
        conflict.Root!.Add(
            new XElement(
                "node",
                new XAttribute("selected", "true"),
                new XAttribute("content-desc", "Reels, tab 2 of 6")
            )
        );
        Assert.False(flow.Observe(new(conflict.ToString())).Allowed);
        Assert.False(flow.TryBeginInput(FacebookBrowseAction.Scroll));
        Assert.False(flow.Observe(new(Captured("ld9-reels-fullscreen"))).Allowed);
    }
}
