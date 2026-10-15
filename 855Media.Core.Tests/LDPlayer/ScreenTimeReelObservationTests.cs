using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeReelObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedTopNavigationUsesContinuousSwipeWhetherPausedOrPlaying(bool playing)
    {
        var doc = XDocument.Parse(Fixture("ld7-reels-top-navigation-paused"));
        if (playing)
            doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == "Play")
                .Remove();
        Assert.True(ScreenTimeNavigation.ReelsHasTopNavigation(doc.ToString(), 1280));
        Assert.Equal(
            new ScreenTimeGesture(0, 350, true),
            ScreenTimeGesture.ReelsScrollAttempt(0, true)
        );
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(
                ScreenTimeGesture.ReelsAttempt(attempt),
                ScreenTimeGesture.ReelsScrollAttempt(attempt + 1, true)
            );
        Assert.False(
            ScreenTimeNavigation.ReelsHasTopNavigation(Fixture("ld1-reels-interest"), 1280)
        );
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("unselected")]
    [InlineData("clipped")]
    [InlineData("bottom")]
    public void TopNavigationMustBeVisibleSelectedAndHaveUsableBounds(string mutation)
    {
        var doc = XDocument.Parse(Fixture("ld7-reels-top-navigation-paused"));
        var tab = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Reels, tab 2 of 6");
        switch (mutation)
        {
            case "hidden":
                tab.SetAttributeValue("visible-to-user", "false");
                break;
            case "disabled":
                tab.SetAttributeValue("enabled", "false");
                break;
            case "unselected":
                tab.SetAttributeValue("selected", "false");
                break;
            case "clipped":
                tab.SetAttributeValue("bounds", "[120,48][240,56]");
                break;
            case "bottom":
                tab.SetAttributeValue("bounds", "[120,1168][240,1280]");
                break;
        }
        Assert.False(ScreenTimeNavigation.ReelsHasTopNavigation(doc.ToString(), 1280));
    }

    [Fact]
    public void CapturedFullscreenInterestCardRecognizesReelsWithCountedActions()
    {
        var xml = Fixture("ld9-reels-fullscreen-interest");
        Assert.True(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.True(ScreenTimeNavigation.IsFullscreenReels(xml));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Reels"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(xml, "Reels"));
        Assert.NotNull(ScreenTimeNavigation.ReelContentKey(xml));
        Assert.False(
            ScreenTimeNavigation.ReelContentChanged(
                xml,
                xml.Replace("198 comments", "199 comments").Replace("39 shares", "40 shares")
            )
        );
        Assert.False(
            ScreenTimeService.IsScreen(xml.Replace("Reel details", "Other details"), "Reels")
        );
        var origin = ScreenTimeNavigation.ReelsSwipeStart(xml, 720, 1280);
        foreach (
            var control in ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    n.Attribute("content-desc")?.Value
                        is "Close"
                            or "Interested"
                            or "Not Interested"
                )
        )
        {
            var bounds = System.Text.RegularExpressions.Regex.Match(
                control.Attribute("bounds")?.Value ?? "",
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
            );
            Assert.True(bounds.Success);
            Assert.False(
                origin.X >= int.Parse(bounds.Groups[1].Value)
                    && origin.X <= int.Parse(bounds.Groups[3].Value)
                    && origin.Y >= int.Parse(bounds.Groups[2].Value)
                    && origin.Y <= int.Parse(bounds.Groups[4].Value)
            );
        }
    }

    [Fact]
    public void MenuShortcutCannotBeMistakenForReels()
    {
        var xml = Fixture("ld9-menu-over-notifications");
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Feed"));
    }

    [Fact]
    public void InlineStoryInFeedAllowsOnlyScrollingAboveReplyControls()
    {
        var xml = Fixture("ld8-feed-inline-story");
        Assert.True(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.Equal((360, 830), ScreenTimeNavigation.FeedSwipeStart(xml, 720, 1280));
        var doc = XDocument.Parse(xml);
        var input = doc.Descendants("node")
            .Single(n => n.Attribute("class")?.Value == "android.widget.EditText");
        input.SetAttributeValue("focused", "true");
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
        input.SetAttributeValue("focused", "false");
        input.SetAttributeValue("text", "Draft reply");
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
        input.SetAttributeValue("text", "");
        doc.Descendants("node")
            .First(n => n.Attribute("content-desc")?.Value == "Story 4 of 4")
            .Remove();
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
        Assert.True(
            ScreenTimeNavigation.IsBlockingScreen(
                xml.Replace("android:id/list", "other:list"),
                "Feed"
            )
        );
    }

    [Fact]
    public void CapturedFullscreenViewerIsReelsAndHasContentIdentity()
    {
        var xml = Fixture("ld9-reels-fullscreen");
        Assert.True(ScreenTimeNavigation.IsFullscreenReels(xml));
        Assert.True(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.NotNull(ScreenTimeNavigation.ReelContentKey(xml));
        Assert.True(
            ScreenTimeNavigation.ReelContentChanged(
                xml,
                xml.Replace("Example reel caption for viewer variant", "Another reel caption")
            )
        );
        Assert.False(
            ScreenTimeNavigation.ReelContentChanged(
                xml,
                xml.Replace("250 reactions", "251 reactions")
            )
        );
        Assert.False(
            ScreenTimeService.IsScreen(xml.Replace("Reel details", "Other details"), "Reels")
        );
        Assert.False(
            ScreenTimeService.IsScreen(xml.Replace("com.facebook.katana", "other.app"), "Reels")
        );
        var composer = XDocument.Parse(xml);
        composer
            .Descendants("node")
            .First(n => n.Attribute("content-desc")?.Value == "Add a comment")
            .SetAttributeValue("class", "android.widget.EditText");
        Assert.False(ScreenTimeService.IsScreen(composer.ToString(), "Reels"));
    }

    [Fact]
    public void CapturedFinishedFeedVideoIsScrollOnly()
    {
        var xml = Fixture("ld1-feed-finished-video-product");
        Assert.True(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
    }

    [Theory]
    [InlineData(0, 300, 100)]
    [InlineData(1, 200, 50)]
    [InlineData(2, 100, 75)]
    public void ReelsFallbackProfilesUseRequestedOrder(int attempt, int hold, int move) =>
        Assert.Equal(new ScreenTimeGesture(hold, move), ScreenTimeGesture.ReelsAttempt(attempt));

    [Fact]
    public void FinishedVideoWithProductDismissalInFeedIsBrowseOnly()
    {
        const string xml = """
            <hierarchy><node package="com.facebook.katana" class="androidx.viewpager.widget.ViewPager" resource-id="facebook:pager" bounds="[0,0][336,580]">
              <node package="com.facebook.katana" class="androidx.recyclerview.widget.RecyclerView" resource-id="android:id/list" scrollable="true">
                <node package="com.facebook.katana" text="Go to Reels"/>
                <node package="com.facebook.katana" text="Watch again"/>
                <node package="com.facebook.katana" class="android.view.ViewGroup" bounds="[15,394][317,501]">
                  <node package="com.facebook.katana" text="Etsy"/>
                  <node package="com.facebook.katana" content-desc="Close" clickable="true" bounds="[285,400][310,425]"/>
                </node>
              </node>
            </node></hierarchy>
            """;
        Assert.True(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(xml, "Feed"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.False(
            ScreenTimeService.IsScreen(xml.Replace("android:id/list", "other:list"), "Feed")
        );
    }

    [Theory]
    [InlineData("Go to Reels", "Watch again")]
    [InlineData("Confirm", "Delete")]
    public void ReelsInterruptionsRequireRecoveryBeforeBrowsing(string first, string second)
    {
        var xml =
            $"<hierarchy><node package='com.facebook.katana' text='Reels'/><node package='com.facebook.katana' text='{first}'/><node package='com.facebook.katana' text='{second}'/></hierarchy>";
        Assert.True(ScreenTimeNavigation.IsReelsInterruption(xml));
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.False(ScreenTimeNavigation.IsReelsInterruption(xml.Replace(second, "Other")));
        Assert.False(
            ScreenTimeNavigation.IsReelsInterruption(
                xml.Replace("com.facebook.katana", "other.app")
            )
        );
    }

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    [Fact]
    public void GroupReelHasAnIdentityAndCanAdvanceFromNormalReel()
    {
        var group = Fixture("ld2-reels-group-rats");
        Assert.True(ScreenTimeService.IsScreen(group, "Reels"));
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(group));
        Assert.True(ScreenTimeNavigation.ReelContentChanged(Fixture("ld1-reels-interest"), group));
        Assert.False(
            ScreenTimeNavigation.ReelContentChanged(
                group,
                group.Replace("88 reactions", "89 reactions")
            )
        );
        Assert.True(
            ScreenTimeNavigation.ReelContentChanged(
                group,
                group.Replace("Anthony Halstead", "Another author")
            )
        );
    }

    [Fact]
    public void CreatorLineBreakIsNormalized()
    {
        var xml = Fixture("ld2-reels-creator-linebreak");
        var normalized = xml.Replace("&#13;", " ")
            .Replace("&#xD;", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(xml));
        Assert.False(ScreenTimeNavigation.ReelContentChanged(xml, normalized));
        Assert.True(ScreenTimeNavigation.ReelContentChanged(Fixture("ld1-reels-interest"), xml));
    }

    [Fact]
    public void MissingCaptionAloneIsNotAnAdvanceButChangedCreatorIs()
    {
        var xml = Fixture("ld1-reels-interest");
        var doc = XDocument.Parse(xml);
        var details = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Reels tab details");
        foreach (var ancestor in details.Ancestors("node"))
            ancestor.SetAttributeValue("content-desc", "");
        var missing = doc.ToString();
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(missing));
        Assert.False(ScreenTimeNavigation.ReelContentChanged(xml, missing));
        Assert.False(ScreenTimeNavigation.ReelContentChanged(missing, xml));
        var creator = doc.Descendants("node")
            .First(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith("View ")
                && (n.Attribute("content-desc")?.Value ?? "").EndsWith("reels")
            );
        creator.SetAttributeValue("content-desc", "View Another creator's reels");
        Assert.True(ScreenTimeNavigation.ReelContentChanged(missing, doc.ToString()));
    }

    [Fact]
    public void SponsoredLabelAdWithoutOrdinaryCaptionUsesMetadata()
    {
        const string ad =
            "<hierarchy><node package='com.facebook.katana' content-desc='Reel. Swipe up to see more.'><node content-desc='Meta text attributing label for sponsored content (ads)'/><node text='Advertiser A'/><node text='An offer for this product'/><node text='100 reactions'/></node></hierarchy>";
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(ad));
        Assert.False(
            ScreenTimeNavigation.ReelContentChanged(
                ad,
                ad.Replace("100 reactions", "101 reactions")
            )
        );
        Assert.True(
            ScreenTimeNavigation.ReelContentChanged(ad, ad.Replace("Advertiser A", "Advertiser B"))
        );
        Assert.True(ScreenTimeNavigation.ReelContentChanged(Fixture("ld1-reels-interest"), ad));
        Assert.True(ScreenTimeNavigation.ReelContentChanged(ad, Fixture("ld1-reels-interest")));
    }

    [Theory]
    [InlineData(0, 100, 300)]
    [InlineData(1, 300, 100)]
    public void FeedProfilesAreExactlyTheRequestedPairs(int choice, int hold, int move) =>
        Assert.Equal(new ScreenTimeGesture(hold, move), ScreenTimeGesture.NewsFeed(choice));
}
