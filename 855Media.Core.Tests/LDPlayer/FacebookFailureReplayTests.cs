using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookFailureReplayTests
{
    [Fact]
    public void CapturedVideoSelfieIsAReadOnlyCheckpointWithoutAutomaticAction()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("audit-video-selfie-checkpoint-ld21");
        Assert.Equal(PFlowLoginScreenKind.Checkpoint, PFlowLoginScreen.Classify(xml));
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.Authentication, match.Screen);
        Assert.Empty(match.Controls);
        Assert.False(PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"));
        Assert.NotEqual(
            PFlowLoginScreenKind.Checkpoint,
            PFlowLoginScreen.Classify(
                xml.Replace("We need more info to make sure you're human.", "Other message")
            )
        );
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        foreach (
            var node in doc.Descendants("node")
                .Where(n =>
                    n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("content-desc")?.Value == "Start video selfie"
                )
        )
            node.SetAttributeValue("enabled", "false");
        Assert.NotEqual(PFlowLoginScreenKind.Checkpoint, PFlowLoginScreen.Classify(doc.ToString()));
    }

    [Theory]
    [InlineData(
        "audit-menu-dashboard-overlap-ld5",
        FacebookScreenKind.Menu,
        FacebookScreenKind.Dashboard
    )]
    [InlineData(
        "audit-tabbed-album-list-overlap",
        FacebookScreenKind.StoryAlbums,
        FacebookScreenKind.ReelPicker
    )]
    public void CapturedOverlaysHaveOneDestination(
        string fixture,
        FacebookScreenKind expected,
        FacebookScreenKind rejected
    )
    {
        var xml = FacebookLayoutRegistryTests.Fixture(fixture);
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(expected, match.Screen);
        Assert.False(FacebookLayoutRegistry.Match(xml, rejected).IsMatch);
    }

    [Fact]
    public void CapturedReelPickerCannotAlsoAuthorizeReelsBrowsing()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("audit-reel-picker-ld69");
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.ReelPicker, match.Screen);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelsFeed).IsMatch);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch);
        Assert.False(PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"));
    }

    [Fact]
    public void CapturedFacebookSystemAlbumDoesNotHideTheOwnedJobAlbum()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("audit-reel-albums-facebook-minus-one-ld23");
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.StoryAlbums, match.Screen);
        Assert.NotNull(PFlowStoryLayout.Album(xml, "Reel_2cba9a17f9be40c4b2dfeea7f5d07c93"));
        Assert.Null(PFlowStoryLayout.Album(xml, "Reel_not_this_job"));
        Assert.Null(
            PFlowStoryLayout.Album(
                xml.Replace("Facebook, -1", "Unknown, -1"),
                "Reel_2cba9a17f9be40c4b2dfeea7f5d07c93"
            )
        );
    }

    [Theory]
    [InlineData("audit-media-permission-ld45")]
    [InlineData("audit-storage-permission-ld5")]
    public void SupportedMediaPromptsAreNonActionableRegistryScreens(string fixture)
    {
        var xml = FacebookLayoutRegistryTests.Fixture(fixture);
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.MediaPermissionPrompt, match.Screen);
        Assert.Empty(match.Controls);
        Assert.False(PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"));
        Assert.False(
            FacebookLayoutRegistry
                .Match(
                    xml.Replace("Facebook", "Other App"),
                    FacebookScreenKind.MediaPermissionPrompt
                )
                .IsMatch
        );
    }

    [Fact]
    public void PageIdentityPromptCannotGrantContinuationOrPublish()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("audit-page-identity-restriction");
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.PageIdentityRestriction, match.Screen);
        Assert.Empty(match.Controls);
        Assert.False(PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"));
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelComposer).IsMatch);
    }

    [Fact]
    public void BundledRevisionKeepsOldConfigurationAndDisabledFamiliesCompatible()
    {
        var json = FacebookLayoutCatalog.CreateDefaultJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("formatVersion").GetInt32());
        var home = doc
            .RootElement.GetProperty("variants")
            .EnumerateArray()
            .Single(v => v.GetProperty("baseId").GetString() == "feed.home.v1");
        Assert.Equal("feed.home.v2", home.GetProperty("id").GetString());
        Assert.Equal(2, home.GetProperty("version").GetInt32());
        var legacy = FacebookLayoutCatalog.Parse(
            """
            {"formatVersion":1,"variants":[{"id":"feed.home.v1","baseId":"feed.home.v1","version":1,"enabled":false}]}
            """
        );
        Assert.False(
            legacy
                .Match(
                    FacebookLayoutRegistryTests.Fixture("feed-home"),
                    FacebookScreenKind.NewsFeed
                )
                .IsMatch
        );
    }
}
