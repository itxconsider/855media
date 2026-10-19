using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeReelObservationTests
{
    [Fact]
    public void CapturedLd14PreferenceCardAllowsBrowsingButNotInteractions()
    {
        var xml = Fixture("ld14-reels-interest-card");
        Assert.True(ScreenTimeNavigation.HasInlineCardDismissal(xml, "Reels"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(xml, "Reels"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Reels"));
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelsFeed);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("reels.tabbed.v1", match.VariantId);
        Assert.False(match.Browse!.InteractionReady);
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(xml));
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("title")]
    [InlineData("choice")]
    [InlineData("input")]
    public void PreferenceCardExceptionCannotHideAnUnrelatedOverlay(string change)
    {
        var doc = XDocument.Parse(Fixture("ld14-reels-interest-card"));
        var close = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Close");
        switch (change)
        {
            case "outside":
                close.Remove();
                doc.Root!.Add(close);
                break;
            case "title":
                foreach (
                    var n in doc.Descendants("node")
                        .Where(n =>
                            ((string?)n.Attribute("text"))?.StartsWith("Do you want") == true
                            || ((string?)n.Attribute("content-desc"))?.StartsWith("Do you want")
                                == true
                        )
                )
                {
                    n.SetAttributeValue("text", "Different dialog");
                    n.SetAttributeValue("content-desc", "Different dialog");
                }
                break;
            case "choice":
                foreach (
                    var n in doc.Descendants("node")
                        .Where(n => (string?)n.Attribute("content-desc") == "Not Interested")
                        .ToArray()
                )
                    n.Remove();
                break;
            case "input":
                doc.Root!.Add(
                    new XElement("node", new XAttribute("class", "android.widget.EditText"))
                );
                break;
        }
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Reels"));
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelsFeed).IsMatch
        );
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\u00a0")]
    [InlineData("\u202f\u200b")]
    public void FullscreenReelLabelsTolerateWhitespaceCaseAndUnicodePunctuation(string separator)
    {
        var doc = XDocument.Parse(Fixture("ld9-reels-fullscreen-multiline-creator"));
        foreach (var n in doc.Descendants("node"))
        foreach (
            var attribute in n.Attributes()
                .Where(a => a.Name is { LocalName: "text" or "content-desc" })
        )
            attribute.Value = attribute
                .Value.Replace(" ", separator)
                .Replace("'", "’")
                .ToUpperInvariant();
        var xml = doc.ToString();
        Assert.True(ScreenTimeNavigation.IsFullscreenReels(xml));
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelsFeed).IsMatch);
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(xml));
    }

    [Fact]
    public void ChangedCreatorWordingCanUseIndependentFullscreenActionRail()
    {
        var doc = ChangedCreator();
        Assert.True(ScreenTimeNavigation.IsFullscreenReels(doc.ToString()));
        Assert.True(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelsFeed).IsMatch
        );
        Assert.NotNull(ScreenTimeNavigation.ReelContentKey(doc.ToString()));
        Assert.True(
            ScreenTimeNavigation.ReelContentChanged(
                doc.ToString(),
                doc.ToString().Replace("Working through", "Different caption for")
            )
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("outside")]
    [InlineData("duplicate")]
    [InlineData("wrong-order")]
    public void StructuralFallbackRequiresUsableUnambiguousOrderedActionRail(string change)
    {
        var doc = ChangedCreator();
        var save = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Save");
        switch (change)
        {
            case "missing":
                save.Remove();
                break;
            case "hidden":
                save.SetAttributeValue("visible-to-user", "false");
                break;
            case "disabled":
                save.SetAttributeValue("enabled", "false");
                break;
            case "outside":
                save.SetAttributeValue("bounds", "[0,959][90,1082]");
                break;
            case "wrong-order":
                save.SetAttributeValue("bounds", "[624,100][720,180]");
                break;
            case "duplicate":
                var duplicate = new XElement(save);
                duplicate.SetAttributeValue("bounds", "[624,100][720,180]");
                save.Parent!.Add(duplicate);
                break;
        }
        Assert.False(ScreenTimeNavigation.IsFullscreenReels(doc.ToString()));
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelsFeed).IsMatch
        );
    }

    [Fact]
    public void StructuralFallbackStillRejectsEditableOrForeignScreens()
    {
        var xml = ChangedCreator().ToString();
        Assert.False(
            FacebookLayoutRegistry
                .Match(
                    xml.Replace("com.facebook.katana", "com.other.app"),
                    FacebookScreenKind.ReelsFeed
                )
                .IsMatch
        );
        var doc = XDocument.Parse(xml);
        doc.Descendants("node")
            .First(n => (string?)n.Attribute("content-desc") == "Add a comment")
            .SetAttributeValue("class", "android.widget.EditText");
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelsFeed).IsMatch
        );
    }

    [Fact]
    public void BrowseFailuresDescribeEvidenceInsteadOfClaimingDestinationWasLeft()
    {
        Assert.Contains(
            "screen data unavailable",
            ScreenTimeNavigation.BrowseFailureMessage("<hierarchy/>", "Reels")
        );
        const string unknown =
            "<hierarchy><node package='com.facebook.katana' text='Unrecognized screen'/></hierarchy>";
        Assert.Contains(
            "layout not recognized",
            ScreenTimeNavigation.BrowseFailureMessage(unknown, "Reels")
        );
        Assert.DoesNotContain(
            "left Reels",
            ScreenTimeNavigation.BrowseFailureMessage(unknown, "Reels")
        );
        Assert.Contains(
            "blocked",
            ScreenTimeNavigation.BrowseFailureMessage(
                unknown.Replace("text='Unrecognized screen'", "class='android.widget.EditText'"),
                "Reels"
            )
        );
        Assert.Contains(
            "different navigation tab",
            ScreenTimeNavigation.BrowseFailureMessage(
                unknown.Replace(
                    "text='Unrecognized screen'",
                    "content-desc='Home, tab 1 of 6' selected='true'"
                ),
                "Reels"
            )
        );
    }

    private static XDocument ChangedCreator()
    {
        var doc = XDocument.Parse(Fixture("ld9-reels-fullscreen-multiline-creator"));
        foreach (
            var n in doc.Descendants("node")
                .Where(n => ((string?)n.Attribute("content-desc"))?.StartsWith("View ") == true)
        )
            n.SetAttributeValue("content-desc", "Open creator profile");
        return doc;
    }

    [Fact]
    public void CapturedFullscreenReelWithMultilineCreatorMatchesRegisteredLayout()
    {
        var xml = Fixture("ld9-reels-fullscreen-multiline-creator");
        Assert.True(ScreenTimeNavigation.IsFullscreenReels(xml));
        Assert.True(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(xml, "Reels"));
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelsFeed);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("reels.fullscreen.v1", match.VariantId);
        Assert.NotNull(ScreenTimeNavigation.ReelContentKey(xml));
        Assert.False(ScreenTimeNavigation.ReelContentChanged(xml, xml));
    }

    [Theory]
    [InlineData("Comment")]
    [InlineData("Share")]
    [InlineData("Reel details")]
    [InlineData("Tap to show video controls")]
    [InlineData("Navigate to your Reels profile")]
    public void MultilineCreatorDoesNotBypassRequiredReelEvidence(string missing)
    {
        var doc = XDocument.Parse(Fixture("ld9-reels-fullscreen-multiline-creator"));
        foreach (
            var node in doc.Descendants("node")
                .Where(n => (string?)n.Attribute("content-desc") == missing)
                .ToArray()
        )
            node.Remove();
        Assert.False(ScreenTimeNavigation.IsFullscreenReels(doc.ToString()));
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelsFeed).IsMatch
        );
    }

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
            new ScreenTimeGesture(0, 450, true),
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
