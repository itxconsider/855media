using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNavigationRegressionTests
{
    [Fact]
    public void CapturedReelsProductCardDoesNotReceiveSwipeOrigin()
    {
        var xml = Fixture("ld2-reels-shop-interest");
        Assert.True(ScreenTimeService.IsScreen(xml, "Reels"));
        var start = ScreenTimeNavigation.ReelsSwipeStart(xml, 720, 1280);
        Assert.Equal((360, 960), start);
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        Assert.False(PFlowPersonaSwitcherService.CanRevealNativeNavigation(screen));
        Assert.Contains(
            screen.AllElements,
            e =>
                e.ContentDesc == "Menu"
                && PFlowPersonaSwitcherService.IsNativeNavigationTarget(e, screen)
        );
    }

    [Fact]
    public void ReelSwipeAvoidsAControlCoveringTheLongGestureOrigin()
    {
        var doc = XDocument.Parse(Fixture("ld2-reels-shop-interest"));
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("clickable", "true"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", "[24,920][592,990]")
            )
        );
        // The interest card also covers the next two candidate origins.
        Assert.Equal((360, 768), ScreenTimeNavigation.ReelsSwipeStart(doc.ToString(), 720, 1280));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReelsWaitsForObservedChangeBeforeRetryingSwipe(bool missingKeyDuringRender)
    {
        var (service, fake, _) = Harness();
        fake.StartFixture = "ld1-reels-interest";
        fake.ReadsBeforeReelChange = 2;
        fake.MissingKeyDuringRender = missingKeyDuringRender;
        var settings = Settings();
        settings.NewsFeed = false;
        settings.WatchVideo = true;
        settings.WatchVideoMin = settings.WatchVideoMax = 3;
        settings.Options.ReelDwellSeconds = 1;
        var result = await service.RunAsync(
            957,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, fake.ReadsBeforeReelChange);
    }

    [Fact]
    public void CapturedPlainMenuHeaderAndHiddenFeedNavigationAreSupported()
    {
        var visible = AtxAgentClient.ParseBloksHierarchy(Fixture("ld2-feed-menu-header"));
        Assert.NotNull(visible);
        var menu = visible!.AllElements.Single(e => e.ContentDesc == "Menu");
        Assert.True(PFlowPersonaSwitcherService.IsNativeNavigationTarget(menu, visible));
        var hidden = AtxAgentClient.ParseBloksHierarchy(Fixture("ld4-feed-hidden-menu"));
        Assert.NotNull(hidden);
        Assert.DoesNotContain(
            hidden!.AllElements,
            e => PFlowPersonaSwitcherService.IsNativeNavigationTarget(e, hidden)
        );
        Assert.True(PFlowPersonaSwitcherService.CanRevealNativeNavigation(hidden));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("body")]
    [InlineData("input")]
    public void PlainMenuNeedsVisibleHeaderAndConfirmedFeed(string mutation)
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(Fixture("ld2-feed-menu-header"))!;
        var menu = screen.AllElements.Single(e => e.ContentDesc == "Menu");
        if (mutation == "disabled")
            menu.Enabled = false;
        if (mutation == "body")
            menu.Bounds = (8, 500, 96, 588);
        if (mutation == "input")
        {
            var doc = XDocument.Parse(screen.RawXml!);
            doc.Root!.Add(new XElement("node", new XAttribute("class", "android.widget.EditText")));
            screen.RawXml = doc.ToString();
        }
        Assert.False(PFlowPersonaSwitcherService.IsNativeNavigationTarget(menu, screen));
    }

    [Fact]
    public async Task ReelsCanAdvanceIntoCapturedAdLayout()
    {
        var ad = Fixture("ld1-reels-ad-eero");
        Assert.True(ScreenTimeService.IsScreen(ad, "Reels"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(ad, "Reels"));
        var key = ScreenTimeNavigation.ReelContentKey(ad);
        Assert.NotNull(key);
        Assert.NotEqual(ScreenTimeNavigation.ReelContentKey(Fixture("ld1-reels-interest")), key);
        Assert.Equal(
            key,
            ScreenTimeNavigation.ReelContentKey(ad.Replace("544 reactions", "545 reactions"))
        );
        var (service, fake, _) = Harness();
        fake.StartFixture = "ld1-reels-interest";
        fake.AfterSwipeFixture = "ld1-reels-ad-eero";
        var settings = Settings();
        settings.NewsFeed = false;
        settings.WatchVideo = true;
        settings.WatchVideoMin = settings.WatchVideoMax = 2;
        settings.Options.ReelDwellSeconds = 1;
        var result = await service.RunAsync(
            938,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public void ReelContentKeyIgnoresReactionCounts()
    {
        var xml = Fixture("ld1-reels-interest");
        Assert.NotNull(ScreenTimeNavigation.ReelContentKey(xml));
        Assert.Equal(
            ScreenTimeNavigation.ReelContentKey(xml),
            ScreenTimeNavigation.ReelContentKey(xml.Replace("141 reactions", "142 reactions"))
        );
        Assert.NotEqual(
            ScreenTimeNavigation.ReelContentKey(xml),
            ScreenTimeNavigation.ReelContentKey(
                xml.Replace("Made with Fluidium App", "Different reel caption")
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReelsSwipeRequiresObservedContentChange(bool stuck)
    {
        var (service, fake, _) = Harness();
        fake.StartFixture = "ld1-reels-interest";
        fake.StuckReels = stuck;
        var settings = Settings();
        settings.NewsFeed = false;
        settings.WatchVideo = true;
        settings.WatchVideoMin = settings.WatchVideoMax = 10;
        settings.Options.ReelDwellSeconds = 1;
        if (stuck)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RunAsync(937, settings, _ => Task.FromResult(true), _ => Task.CompletedTask)
            );
            Assert.Contains("next reel after two swipes", ex.Message);
            Assert.Equal(2, fake.Swipes);
        }
        else
        {
            var result = await service.RunAsync(
                937,
                settings,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask
            );
            Assert.Equal(1, result.CompletedSteps);
            Assert.True(fake.Swipes > 1);
        }
        Assert.Equal(0, fake.Taps);
    }

    private static XDocument CarouselDocument()
    {
        var doc = XDocument.Parse(Fixture("ld4-feed-topics-carousel"));
        var module = doc.Descendants("node")
            .First(n => n.Attribute("text")?.Value == "Topics to Explore")
            .Parent!;
        var list = module
            .Ancestors("node")
            .First(n => n.Attribute("resource-id")?.Value == "android:id/list");
        module.Remove();
        list.RemoveNodes();
        list.Add(module);
        return doc;
    }

    [Theory]
    [InlineData("Topics to Explore")]
    [InlineData("Crohn's Disease & Ostomy Lifestyle")]
    [InlineData("Another group title")]
    public void CapturedCarouselStructureDoesNotDependOnItsTitle(string title)
    {
        var doc = CarouselDocument();
        var heading = doc.Descendants("node")
            .First(n => n.Attribute("text")?.Value == "Topics to Explore");
        heading.SetAttributeValue("text", title);
        heading.SetAttributeValue("content-desc", title);
        var hide = doc.Descendants("node").First(n => n.Attribute("content-desc")?.Value == "Hide");
        hide.SetAttributeValue("content-desc", "Close");
        Assert.True(ScreenTimeNavigation.HasFeedPreviewCarousel(doc.ToString()));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(doc.ToString(), "Feed"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
        Assert.NotNull(ScreenTimeNavigation.FeedViewportSignature(doc.ToString()));
        Assert.Null(ScreenTimeFeedTarget.Resolve(doc.ToString()));
        // The carousel alone never establishes initial Feed navigation.
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
    }

    [Theory]
    [InlineData("outside-feed")]
    [InlineData("other-package")]
    [InlineData("missing-options")]
    [InlineData("disabled-options")]
    [InlineData("hidden-carousel")]
    [InlineData("missing-title")]
    [InlineData("one-preview")]
    [InlineData("vertical-previews")]
    [InlineData("close-inside-preview")]
    public void UnsupportedCarouselCannotExemptClose(string mutation)
    {
        var doc = CarouselDocument();
        var heading = doc.Descendants("node")
            .First(n => n.Attribute("text")?.Value == "Topics to Explore");
        var module = heading.Parent!;
        var carousel = module
            .Elements("node")
            .First(n => n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView");
        var close = module
            .Descendants("node")
            .First(n => n.Attribute("content-desc")?.Value == "Hide");
        close.SetAttributeValue("content-desc", "Close");
        if (mutation == "outside-feed")
        {
            module.Remove();
            doc.Root!.Add(module);
        }
        if (mutation == "other-package")
            foreach (var n in module.DescendantsAndSelf("node"))
                n.SetAttributeValue("package", "other.app");
        if (mutation == "missing-options")
            module
                .Descendants("node")
                .First(n => n.Attribute("content-desc")?.Value == "See more options for this post")
                .Remove();
        if (mutation == "missing-title")
            heading.Remove();
        if (mutation == "disabled-options")
            module
                .Descendants("node")
                .First(n => n.Attribute("content-desc")?.Value == "See more options for this post")
                .SetAttributeValue("enabled", "false");
        if (mutation == "hidden-carousel")
            carousel.SetAttributeValue("visible-to-user", "false");
        if (mutation == "one-preview")
            foreach (var n in carousel.Elements("node").Skip(1).ToArray())
                n.Remove();
        if (mutation == "vertical-previews")
            foreach (var n in carousel.Elements("node"))
                n.SetAttributeValue("bounds", "[0,490][720,950]");
        if (mutation == "close-inside-preview")
        {
            close.Remove();
            close.SetAttributeValue("bounds", "[250,600][298,648]");
            carousel.Elements("node").First().Add(close);
        }
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
        Assert.Null(ScreenTimeNavigation.FeedViewportSignature(doc.ToString()));
    }

    [Fact]
    public async Task CapturedCarouselContinuesConfirmedFeedWithoutPreviewTaps()
    {
        var (service, fake, _) = Harness();
        fake.AfterSwipeXml = CarouselDocument().ToString();
        var settings = Settings();
        settings.Options.Reactions = true;
        var result = await service.RunAsync(
            936,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.True(fake.Swipes >= 2);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, result.Reactions);
        Assert.True(
            ScreenTimeNavigation.HasFeedPreviewCarousel(Fixture("ld4-feed-topics-carousel"))
        );
    }

    [Theory]
    [InlineData("ld2-feed-group-join")]
    [InlineData("ld5-feed-public-group")]
    public async Task CapturedGroupPostsStayInFeedAndSkipInteractions(string fixture)
    {
        var xml = Fixture(fixture);
        Assert.True(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed"));
        Assert.Null(ScreenTimeFeedTarget.Resolve(xml));
        var (service, fake, _) = Harness();
        fake.StartFixture = fixture;
        var settings = Settings();
        settings.Options.Reactions = true;
        var result = await service.RunAsync(
            935,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.True(fake.Swipes >= 2);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, result.Reactions);
    }

    [Theory]
    [InlineData("Back")]
    [InlineData("Go back")]
    public void GroupPostViewerCannotBorrowFeedBehindItsBackButton(string label)
    {
        var doc = XDocument.Parse(Fixture("ld5-feed-public-group"));
        doc.Root!.Add(new XElement("node", new XAttribute("content-desc", label)));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
        Assert.Null(ScreenTimeNavigation.FeedViewportSignature(doc.ToString()));
    }

    [Theory]
    [InlineData("Brand Survey")]
    [InlineData("Boost post")]
    [InlineData("Boost reel")]
    [InlineData("Create ad")]
    [InlineData("Join")]
    [InlineData("Sponsored")]
    [InlineData("Ad")]
    [InlineData("Popular in Miami")]
    public async Task SpecialFeedCardsContinueBrowsingWithoutEnabledInteractions(string label)
    {
        var xml = FeedWithCard(label);
        Assert.True(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed"));
        Assert.True(ScreenTimeHierarchy.IsUnsafe(xml));
        var (service, fake, _) = Harness();
        fake.AfterSwipeXml = xml;
        var settings = Settings();
        settings.OwnerUid = "test-owner";
        settings.OwnerName = "Test Owner";
        settings.Options.Reactions = true;
        settings.Options.RandomFeedReactions = true;
        settings.Options.RandomFeedComments = true;
        settings.Options.RandomCommentTexts = "An example comment";
        settings.Options.InteractionChancePercent = 100;
        var result = await service.RunAsync(
            934,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.True(fake.Swipes >= 2);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, result.Reactions);
        Assert.Equal(0, result.Comments);
    }

    private static string FeedWithCard(string label)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-top"));
        var list = doc.Descendants("node")
            .First(n => n.Attribute("resource-id")?.Value == "android:id/list");
        list.Add(new XElement("node", new XAttribute("text", label)));
        return doc.ToString();
    }

    [Fact]
    public void GroupPostHeaderCloseRequiresNativePostOptionsInsideFeed()
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-scrolled"));
        var list = doc.Descendants("node")
            .First(n => n.Attribute("resource-id")?.Value == "android:id/list");
        var options = new XElement(
            list.Descendants("node")
                .First(n =>
                    (n.Attribute("content-desc")?.Value ?? "").StartsWith("More options for ")
                )
        );
        options.SetAttributeValue("content-desc", "More options for Jeremy Dawkins’s post");
        options.SetAttributeValue("bounds", "[570,210][630,270]");
        var close = new XElement(options);
        close.SetAttributeValue("content-desc", "Close");
        close.SetAttributeValue("bounds", "[630,210][690,270]");
        list.RemoveNodes();
        list.Add(
            new XElement(
                "node",
                new XAttribute("class", "android.view.ViewGroup"),
                new XAttribute("bounds", "[0,200][720,350]"),
                options,
                close
            )
        );
        Assert.True(ScreenTimeNavigation.IsScrolledFeed(doc.ToString()));
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(doc.ToString(), "Feed"));
        options.Remove();
        Assert.False(ScreenTimeNavigation.IsScrolledFeed(doc.ToString()));
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
    }

    [Theory]
    [InlineData("Ad")]
    [InlineData("Peel · Ad")]
    [InlineData("Shop this reel")]
    public void ReelsOverlayCloseIsInlineOnlyInsideConfirmedReels(string label)
    {
        // Constructed compact overlays from the supplied screenshots.
        var doc = XDocument.Parse(Fixture("ld1-reels-interest"));
        var reels = doc.Descendants("node")
            .First(n => n.Attribute("content-desc")?.Value == "Reels tab details");
        var overlay = new XElement(
            "node",
            new XAttribute("bounds", "[24,700][560,860]"),
            new XElement("node", new XAttribute("text", label)),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Close"),
                new XAttribute("bounds", "[480,710][540,770]")
            )
        );
        reels.Add(overlay);
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Reels"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(doc.ToString(), "Reels"));
        overlay.Remove();
        doc.Root!.Add(overlay);
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Reels"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChapterRecoveryUsesOneBackAndRequiresFreshReels(bool succeeds)
    {
        var (service, fake, _) = Harness();
        fake.StartFixture = "ld1-reels-interest";
        fake.ChapterAfterSwipe = true;
        fake.ChapterRecoverySucceeds = succeeds;
        var settings = Settings();
        settings.NewsFeed = false;
        settings.WatchVideo = true;
        settings.WatchVideoMin = settings.WatchVideoMax = 10;
        settings.Options.ReelDwellSeconds = 1;
        if (succeeds)
        {
            var result = await service.RunAsync(
                932,
                settings,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask
            );
            Assert.Equal(1, result.CompletedSteps);
            Assert.True(fake.Swipes > 1);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RunAsync(933, settings, _ => Task.FromResult(true), _ => Task.CompletedTask)
            );
            Assert.Contains("left Reels", ex.Message);
            Assert.Equal(1, fake.Swipes);
        }
        Assert.Equal(1, fake.ChapterBacks);
        Assert.Equal(0, fake.Taps);
    }

    [Theory]
    [InlineData("Ad")]
    [InlineData("Learn more")]
    [InlineData("Shop this reel")]
    [InlineData("Peel · Ad")]
    [InlineData("Paid for by SLF PAC · AI info")]
    [InlineData("Threads you might like")]
    [InlineData("Open Threads")]
    [InlineData("You have friend requests waiting for you")]
    public void ReelsRecommendationsAreBrowsingOnly(string label)
    {
        var doc = XDocument.Parse(Fixture("ld1-reels-interest"));
        foreach (
            var node in doc.Descendants("node")
                .Where(n => n.Attribute("content-desc")?.Value == "Close")
                .ToArray()
        )
            node.Remove();
        doc.Root!.Add(new XElement("node", new XAttribute("text", label)));
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Reels"));
        Assert.True(ScreenTimeNavigation.IsBrowseOnlyCard(doc.ToString(), "Reels"));
    }

    [Fact]
    public void ChapterButtonIsContentButOpenChapterSheetBlocksBackgroundReels()
    {
        var doc = XDocument.Parse(Fixture("ld1-reels-interest"));
        doc.Root!.Add(new XElement("node", new XAttribute("text", "View chapters")));
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Reels"));
        doc.Root!.Add(
            new XElement("node", new XAttribute("text", "Chapters")),
            new XElement("node", new XAttribute("text", "Chapters are generated by Meta AI."))
        );
        Assert.True(ScreenTimeNavigation.IsReelsChaptersSheet(doc.ToString()));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Reels"));
    }

    [Fact]
    public void InstalledInterestCardsPermitBrowsingButNotInteraction()
    {
        var feed = Fixture("ld57-feed-interest");
        var reels = Fixture("ld1-reels-interest");
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(feed, "Feed"));
        Assert.NotNull(ScreenTimeNavigation.FeedViewportSignature(feed));
        Assert.True(ScreenTimeNavigation.HasInlineCardDismissal(feed, "Feed"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(reels, "Reels"));
        Assert.True(ScreenTimeService.IsScreen(reels, "Reels"));
        Assert.True(ScreenTimeNavigation.HasInlineCardDismissal(reels, "Reels"));
        Assert.False(ScreenTimeService.IsScreen(reels, "Feed"));
    }

    [Fact]
    public void UnrelatedCloseOverlayCannotBorrowInterestCardEvidence()
    {
        var doc = XDocument.Parse(Fixture("ld1-reels-interest"));
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Close"),
                new XAttribute("bounds", "[20,20][80,80]")
            )
        );
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Reels"));
    }

    [Theory]
    [InlineData("People you may know")]
    [InlineData("Reels")]
    [InlineData("Popular in Los Angeles")]
    [InlineData("Muse")]
    [InlineData("Ad")]
    [InlineData("Get a verified badge")]
    [InlineData("Popular in Miami")]
    [InlineData("Brand Survey")]
    [InlineData("Boost reel")]
    [InlineData("Create ad")]
    [InlineData("Join")]
    [InlineData("More options for Jeremy Dawkins’s post")]
    public void ScreenshotCardHeadersAreInlineOnlyInsideConfirmedFeed(string label)
    {
        // Constructed header variants from screenshots, not live device selectors.
        var doc = XDocument.Parse(Fixture("ld1-feed-top"));
        var list = doc.Descendants("node")
            .First(n => n.Attribute("resource-id")?.Value == "android:id/list");
        var card = new XElement(
            "node",
            new XAttribute("bounds", "[0,300][720,380]"),
            new XElement("node", new XAttribute("text", label)),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Close"),
                new XAttribute("bounds", "[630,300][690,360]")
            )
        );
        list.Add(card);
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
        Assert.True(ScreenTimeNavigation.HasInlineCardDismissal(doc.ToString(), "Feed"));
        card.Remove();
        doc.Root!.Add(card);
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Feed"));
    }

    [Theory]
    [InlineData("ld1-feed-reel-no-comment")]
    [InlineData("ld2-feed-text-actions")]
    public async Task ConfirmedFeedCanBrowseChangingCardsForFullDuration(string fixture)
    {
        var (service, fake, _) = Harness();
        fake.AfterSwipeFixture = fixture;
        var settings = Settings();
        settings.NewsFeedMin = settings.NewsFeedMax = 90;
        // No strict semantic Feed evidence in these captured viewports.
        Assert.False(ScreenTimeService.IsScreen(Fixture(fixture), "Feed"));
        Assert.Equal(
            ScreenTimeNavigation.FeedViewportSignature(Fixture("ld1-feed-top")),
            ScreenTimeNavigation.FeedViewportSignature(Fixture(fixture))
        );
        var result = await service.RunAsync(
            928,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.True(fake.Swipes >= 85);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task ContainerContinuityNeverEstablishesInitialFeed()
    {
        var (service, fake, _) = Harness();
        fake.StartFixture = "ld1-feed-reel-no-comment";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(929, Settings(), _ => Task.FromResult(true), _ => Task.CompletedTask)
        );
        Assert.Contains("Feed screen not recognized", ex.Message);
        Assert.Equal(0, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public void HomeReelsCarouselCannotConfirmReelsDestination()
    {
        var xml = Fixture("ld2-feed-text-actions");
        Assert.Contains("content-desc=\"Reels\"", xml);
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
    }

    [Fact]
    public async Task CancellationInBrowsingOnlyViewportStopsBeforeNextSwipe()
    {
        var (service, fake, _) = Harness();
        fake.AfterSwipeFixture = "ld1-feed-reel-no-comment";
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(
                932,
                Settings(),
                _ => Task.FromResult(true),
                p =>
                {
                    if (fake.Swipes > 0)
                        cancellation.Cancel();
                    return Task.CompletedTask;
                },
                ct: cancellation.Token
            )
        );
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("other-tab")]
    [InlineData("other-package")]
    [InlineData("other-pager")]
    [InlineData("missing-list")]
    [InlineData("two-lists")]
    [InlineData("empty-list")]
    [InlineData("back")]
    public void UnsupportedOrConflictingViewportCannotContinueFeed(string mutation)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-reel-no-comment"));
        var list = doc.Descendants("node")
            .First(n => n.Attribute("resource-id")?.Value == "android:id/list");
        if (mutation == "input")
            doc.Root!.Add(new XElement("node", new XAttribute("class", "android.widget.EditText")));
        if (mutation == "back")
            doc.Root!.Add(new XElement("node", new XAttribute("content-desc", "Back")));
        if (mutation == "other-tab")
            doc.Root!.Add(
                new XElement(
                    "node",
                    new XAttribute("content-desc", "Profile, tab 6 of 6"),
                    new XAttribute("selected", "true")
                )
            );
        if (mutation == "two-lists")
            list.AddAfterSelf(new XElement(list));
        if (mutation == "empty-list")
            list.RemoveNodes();
        if (mutation == "missing-list")
            list.Remove();
        foreach (var node in doc.Descendants("node"))
        {
            if (mutation == "other-package")
                node.SetAttributeValue("package", "other.app");
            if (
                mutation == "other-pager"
                && node.Attribute("class")?.Value == "androidx.viewpager.widget.ViewPager"
            )
                node.SetAttributeValue("class", "android.widget.FrameLayout");
        }
        Assert.Null(ScreenTimeNavigation.FeedViewportSignature(doc.ToString()));
    }

    [Fact]
    public async Task BrowsingOnlyViewportDoesNotAttemptEnabledReactions()
    {
        var (service, fake, _) = Harness();
        fake.AfterSwipeFixture = "ld2-feed-text-actions";
        var settings = Settings();
        settings.Options.Reactions = true;
        var result = await service.RunAsync(
            930,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.Skipped); // Initial strict Feed has no verified post; later viewports only scroll.
        Assert.Equal(0, result.Reactions);
        Assert.Equal(0, fake.Taps);
        Assert.True(fake.Swipes >= 2);
    }

    [Fact]
    public async Task InteractionCannotReuseOldFeedContinuity()
    {
        var (service, fake, _) = Harness();
        fake.LoseSemanticScreenAfterInteraction = true;
        var settings = Settings();
        settings.Options.Reactions = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(931, settings, _ => Task.FromResult(true), _ => Task.CompletedTask)
        );
        Assert.Contains("left Feed", ex.Message);
        Assert.Equal(0, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    [Fact]
    public void InstalledNotificationsHeaderAllowsItsBackButton()
    {
        var xml = Fixture("ld1-notifications-back");
        Assert.Contains("content-desc=\"Back\"", xml);
        Assert.True(ScreenTimeService.IsScreen(xml, "Notifications"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Feed"));
    }

    [Fact]
    public void BackButtonDoesNotSubstituteForNotificationsIdentity()
    {
        Assert.False(
            ScreenTimeService.IsScreen(
                "<hierarchy><node content-desc='Back'/><node text='Other screen'/></hierarchy>",
                "Notifications"
            )
        );
    }

    [Theory]
    [InlineData("ld1-feed-top")]
    [InlineData("ld1-feed-scrolled")]
    [InlineData("ld1-feed-action-row")]
    [InlineData("ld1-feed-action-row-upper")]
    [InlineData("ld1-feed-action-row-lower")]
    [InlineData("ld1-feed-sponsored-reel")]
    public void InstalledFeedIsRecognizedBeforeAndAfterHiddenNavigation(string name)
    {
        var xml = Fixture(name);
        Assert.True(ScreenTimeService.IsScreen(xml, "Feed"));
        Assert.False(ScreenTimeService.IsScreen(xml, "Reels"));
        if (name.EndsWith("scrolled"))
        {
            Assert.DoesNotContain("What's on your mind?", xml);
            Assert.DoesNotContain("Home, tab", xml);
        }
    }

    [Theory]
    [InlineData("missing-comment")]
    [InlineData("disabled-share")]
    [InlineData("other-package")]
    [InlineData("missing-viewpager")]
    [InlineData("back-overlay")]
    public void PostActionLabelsAloneCannotEstablishFeed(string mutation)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-action-row-lower"));
        foreach (var node in doc.Descendants("node").ToArray())
        {
            var desc = node.Attribute("content-desc")?.Value;
            if (mutation == "missing-comment" && desc == "Comment")
                node.Remove();
            if (
                mutation == "disabled-share"
                && desc == "Share button. Double tap to share the post."
            )
                node.SetAttributeValue("enabled", "false");
            if (mutation == "other-package")
                node.SetAttributeValue("package", "other.app");
            if (
                mutation == "missing-viewpager"
                && node.Attribute("class")?.Value == "androidx.viewpager.widget.ViewPager"
            )
                node.SetAttributeValue("class", "android.widget.FrameLayout");
        }
        if (mutation == "back-overlay")
            doc.Root!.Add(new XElement("node", new XAttribute("content-desc", "Back")));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
    }

    [Fact]
    public async Task FeedSessionContinuesThroughCapturedMediaAndSponsoredReel()
    {
        var (service, fake, _) = Harness();
        fake.CapturedMedia = true;
        var settings = Settings();
        settings.NewsFeedMin = settings.NewsFeedMax = 6;
        var result = await service.RunAsync(
            927,
            settings,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.True(fake.Swipes >= 4);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, result.Reactions);
        Assert.Equal(0, result.Comments);
    }

    [Theory]
    [InlineData("Dismiss")]
    [InlineData("Close story")]
    [InlineData("Edit profile")]
    [InlineData("Professional dashboard")]
    [InlineData("Get support")]
    public void OverlaysAndProfilesCannotUseUnderlyingFeedEvidence(string label)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-scrolled"));
        doc.Root!.Add(new XElement("node", new XAttribute("content-desc", label)));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
    }

    [Fact]
    public void CommentInputAndDifferentSelectedTabAreNotFeed()
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-scrolled"));
        doc.Root!.Add(new XElement("node", new XAttribute("class", "android.widget.EditText")));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
        doc = XDocument.Parse(Fixture("ld1-feed-top"));
        foreach (var node in doc.Descendants("node"))
        {
            var desc = (string?)node.Attribute("content-desc") ?? "";
            if (desc.StartsWith("Home, tab"))
                node.SetAttributeValue("selected", "false");
            if (desc.StartsWith("Notifications, tab"))
                node.SetAttributeValue("selected", "true");
        }
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
    }

    [Theory]
    [InlineData("package", "other.app")]
    [InlineData("scrollable", "false")]
    [InlineData("resource-id", "other:list")]
    public void PostLabelsWithoutSupportedFacebookListDoNotEstablishFeed(string attr, string value)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-scrolled"));
        foreach (
            var node in doc.Descendants("node")
                .Where(n => (string?)n.Attribute("resource-id") == "android:id/list")
        )
            node.SetAttributeValue(attr, value);
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Feed"));
    }

    [Fact]
    public void FullSwitcherDoesNotNeedOffscreenFooterAndSelectionIsLocal()
    {
        var xml = Fixture("ld2-switcher-full");
        Assert.DoesNotContain("Go to Accounts Center", xml);
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml));
        var personal = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            xml,
            "Gerardo Lopez",
            "Profile"
        );
        Assert.Equal((360, 490, true), personal!.Value);
        var page = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            xml,
            "KC Waterheaters",
            "Page"
        );
        Assert.Equal((360, 644, false), page!.Value);
        Assert.False(
            PFlowPersonaSwitcherService
                .FindPersonaInSwitcher(xml, "KC Waterheaters")!
                .Value.IsSelected
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "KC Waterheaters",
                "Profile"
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "Gerardo Lopez", "Page")
        );
        Assert.Null(PFlowPersonaSwitcherService.FindPersonaInSwitcher(xml, "Gerardo"));
    }

    [Fact]
    public void ScrolledPagesOnlySwitcherDoesNotInferSelectedIdentity()
    {
        var xml = Fixture("ld56-switcher-scrolled-pages");
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml));
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "W500 COM", "Page")
        );
        var page = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            xml,
            "Survival Outdoors",
            "Page"
        );
        Assert.NotNull(page);
        Assert.False(page!.Value.IsSelected);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Survival Outdoors",
                "Profile"
            )
        );
        var doc = XDocument.Parse(xml);
        foreach (var node in doc.Descendants("node"))
            node.SetAttributeValue("scrollable", "false");
        Assert.False(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
    }

    [Theory]
    [InlineData("remove-selection")]
    [InlineData("two-selected")]
    [InlineData("remove-dismiss")]
    [InlineData("other-package")]
    [InlineData("remove-page-markers")]
    public void UnsupportedFullSheetDoesNotCreateSelectionEvidence(string mutation)
    {
        var doc = XDocument.Parse(Fixture("ld2-switcher-full"));
        foreach (var node in doc.Descendants("node").ToArray())
        {
            if (mutation == "remove-selection")
                node.SetAttributeValue("selected", "false");
            if (
                mutation == "two-selected"
                && (string?)node.Attribute("content-desc") == "John Ward, 1 notification"
            )
                node.SetAttributeValue("selected", "true");
            if (
                mutation == "remove-dismiss"
                && (string?)node.Attribute("content-desc") == "Dismiss"
            )
                node.Remove();
            if (mutation == "other-package")
                node.SetAttributeValue("package", "other.app");
            if (mutation == "remove-page-markers" && (string?)node.Attribute("text") == "Page")
                node.Remove();
        }
        Assert.False(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
    }

    [Fact]
    public void DuplicateExactRowsCannotChooseAnAccount()
    {
        var doc = XDocument.Parse(Fixture("ld2-switcher-full"));
        var row = doc.Descendants("node")
            .First(n => (string?)n.Attribute("content-desc") == "Gerardo Lopez");
        var duplicate = new XElement(row);
        foreach (var n in duplicate.DescendantsAndSelf())
            n.SetAttributeValue("selected", "false");
        row.AddAfterSelf(duplicate);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Gerardo Lopez",
                "Profile"
            )
        );
    }

    [Fact]
    public async Task SelectedPersonalRowDismissesSheetWithoutSwitchingAnotherActor()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, NavigationDevice>();
        var fake = (NavigationDevice)(object)ld;
        fake.Switcher = true;
        var result = await new PFlowPersonaSwitcherService(ld).SwitchToPersonaAsync(
            920,
            new FacebookPageTarget
            {
                PageName = "Gerardo Lopez",
                Type = "Profile",
                PageId = "123",
            }
        );
        Assert.True(result);
        Assert.Equal(1, fake.Backs);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task FeedSessionSurvivesObservedScrollAndKeepsSocialActionsDisabled()
    {
        var (service, fake, _) = Harness();
        var result = await service.RunAsync(
            921,
            Settings(),
            _ => Task.FromResult(true),
            _ => Task.CompletedTask
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.True(fake.Swipes >= 2);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, result.Likes);
        Assert.Equal(0, result.Comments);
        Assert.Equal(0, result.Reactions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyRequestedPageRowIsTappedAndAcknowledgementIsRequired(bool acknowledged)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, NavigationDevice>();
        var fake = (NavigationDevice)(object)ld;
        fake.Switcher = true;
        fake.PageActor = true;
        fake.TapAcknowledged = acknowledged;
        var result = await new PFlowPersonaSwitcherService(ld).SwitchToPersonaAsync(
            924,
            new FacebookPageTarget
            {
                PageName = "KC Waterheaters",
                Type = "Page",
                PageId = "123",
            }
        );
        Assert.Equal(acknowledged, result);
        Assert.Equal((360, 644), fake.LastTap);
        Assert.Equal(1, fake.Taps);
        Assert.Equal(0, fake.Backs);
    }

    [Fact]
    public async Task CancellationDuringScreenRecheckNeverScrollsAgain()
    {
        var (service, fake, _) = Harness();
        using var cts = new CancellationTokenSource();
        fake.FailAfterSwipe = true;
        fake.Transient = true;
        fake.OnFailure = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(
                925,
                Settings(),
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                ct: cts.Token
            )
        );
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
        using var lease = await PFlowDeviceLease.AcquireAsync(925, default);
    }

    [Fact]
    public async Task DiagnosticWriteFailurePreservesNavigationError()
    {
        var (service, fake, history) = Harness();
        fake.FailAfterSwipe = true;
        var folder = Path.Combine(Path.GetDirectoryName(history)!, "screentime-diagnostics");
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        File.WriteAllText(folder, "block diagnostic directory creation");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(926, Settings(), _ => Task.FromResult(true), _ => Task.CompletedTask)
        );
        Assert.Contains("left Feed", ex.Message);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFeedCheckStopsAndSavesRedactedObservation(bool transient)
    {
        var (service, fake, history) = Harness();
        fake.FailAfterSwipe = true;
        fake.Transient = transient;
        if (transient)
        {
            await service.RunAsync(
                922,
                Settings(),
                _ => Task.FromResult(true),
                _ => Task.CompletedTask
            );
            Assert.True(fake.Swipes > 1);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RunAsync(
                    923,
                    Settings(),
                    _ => Task.FromResult(true),
                    _ => Task.CompletedTask
                )
            );
            Assert.Contains("left Feed", ex.Message);
            Assert.Equal(1, fake.Swipes);
            var file = Assert.Single(
                Directory.GetFiles(
                    Path.Combine(Path.GetDirectoryName(history)!, "screentime-diagnostics"),
                    "navigation-feed-*.xml",
                    SearchOption.AllDirectories
                )
            );
            var xml = File.ReadAllText(file);
            Assert.DoesNotContain("PRIVATE_DRAFT", xml);
            Assert.Contains("input redacted", xml);
        }
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public void InstalledUnassignedRecentsMetadataStillChecksActualTaskTypes()
    {
        const string header =
            "ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)\nmRecentsUid=-1\nmRecentsComponent=null\n";
        const string home =
            " * Recent #0: TaskRecord{a #2 I=launcher}\n autoRemoveRecents=false isPersistable=true numFullscreen=1 activityType=2\n";
        const string app =
            " * Recent #1: TaskRecord{b #16 A=com.facebook.katana}\n autoRemoveRecents=false isPersistable=true numFullscreen=1 activityType=1\n";
        Assert.True(PFlowSession.RecentAppsEmpty(header + home));
        Assert.False(PFlowSession.RecentAppsEmpty(header + home + app));
        Assert.Null(PFlowSession.RecentAppsEmpty(header.Replace("Uid=-1", "Uid=-2") + home));
        Assert.Null(PFlowSession.RecentAppsEmpty(header + " Recent tasks:\n"));
    }

    private static ScreenTimeSetting Settings() =>
        new()
        {
            WatchVideo = false,
            NewsFeed = true,
            NewsFeedMin = 3,
            NewsFeedMax = 3,
            Options = new ScreenTimeOptions { FeedDwellSeconds = 1 },
        };

    private static (ScreenTimeService Service, NavigationDevice Device, string History) Harness()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, NavigationDevice>();
        var fake = (NavigationDevice)(object)ld;
        var now = DateTimeOffset.UtcNow;
        var history = Path.Combine(
            Path.GetTempPath(),
            "pflow-navigation-" + Guid.NewGuid().ToString("N"),
            "history.db"
        );
        return (
            new ScreenTimeService(
                ld,
                (duration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    now += duration;
                    return Task.CompletedTask;
                },
                () => now,
                new ScreenTimeHistoryStore(history)
            ),
            fake,
            history
        );
    }

    public class NavigationDevice : DispatchProxy
    {
        public bool ChapterAfterSwipe,
            ChapterRecoverySucceeds;
        public bool StuckReels;
        public int ReadsBeforeReelChange;
        public bool MissingKeyDuringRender;
        public int ChapterBacks;
        public int Swipes,
            Taps,
            Backs;
        public bool Switcher,
            FailAfterSwipe,
            Transient,
            PageActor;
        public bool TapAcknowledged = true;
        public bool CapturedMedia;
        public string? StartFixture,
            AfterSwipeFixture;
        public string? AfterSwipeXml;
        public bool LoseSemanticScreenAfterInteraction;
        private int _hierarchyReads;
        public (int, int) LastTap;
        public Action? OnFailure;
        private bool _missed;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            string Screen()
            {
                if (Swipes > 0 && AfterSwipeXml != null)
                    return AfterSwipeXml;
                if (LoseSemanticScreenAfterInteraction && _hierarchyReads >= 4)
                    return Fixture("ld1-feed-reel-no-comment");
                if (Swipes > 0 && AfterSwipeFixture != null)
                    return Fixture(AfterSwipeFixture);
                if (StartFixture != null)
                {
                    if (
                        ChapterAfterSwipe
                        && Swipes > 0
                        && (ChapterBacks == 0 || !ChapterRecoverySucceeds)
                    )
                    {
                        var chapters = XDocument.Parse(Fixture(StartFixture));
                        chapters.Root!.Add(
                            new XElement("node", new XAttribute("text", "Chapters")),
                            new XElement(
                                "node",
                                new XAttribute("text", "Chapters are generated by Meta AI.")
                            )
                        );
                        return chapters.ToString();
                    }
                    var observation = Fixture(StartFixture);
                    if (StartFixture == "ld1-reels-interest" && Swipes > 0 && !StuckReels)
                    {
                        if (ReadsBeforeReelChange > 0)
                        {
                            ReadsBeforeReelChange--;
                            if (MissingKeyDuringRender)
                            {
                                var pending = XDocument.Parse(observation);
                                var pendingDetails = pending
                                    .Descendants("node")
                                    .First(n =>
                                        n.Attribute("content-desc")?.Value == "Reels tab details"
                                    );
                                foreach (var ancestor in pendingDetails.Ancestors("node"))
                                    ancestor.SetAttributeValue("content-desc", "");
                                Assert.True(
                                    ScreenTimeService.IsScreen(pending.ToString(), "Reels")
                                );
                                Assert.Null(
                                    ScreenTimeNavigation.ReelContentKey(pending.ToString())
                                );
                                return pending.ToString();
                            }
                            return observation;
                        }
                        var reel = XDocument.Parse(observation);
                        var details = reel.Descendants("node")
                            .First(n => n.Attribute("content-desc")?.Value == "Reels tab details");
                        details.Parent!.SetAttributeValue(
                            "content-desc",
                            "Observed reel " + Swipes
                        );
                        return reel.ToString();
                    }
                    return observation;
                }
                if (Switcher)
                    return Backs == 0 && Taps == 0 ? Fixture("ld2-switcher-full")
                        : PageActor
                            ? "<hierarchy><node text='KC Waterheaters'/><node text='Professional dashboard'/></hierarchy>"
                        : Fixture("ld2-personal-professional");
                if (FailAfterSwipe && Swipes > 0 && (!Transient || !_missed))
                {
                    _missed = true;
                    OnFailure?.Invoke();
                    return Transient
                        ? "<hierarchy/>"
                        : "<hierarchy><node class='android.widget.EditText' text='PRIVATE_DRAFT'/></hierarchy>";
                }
                return Fixture(
                    CapturedMedia && Swipes > 0
                        ? new[]
                        {
                            "ld1-feed-action-row",
                            "ld1-feed-action-row-upper",
                            "ld1-feed-action-row-lower",
                            "ld1-feed-sponsored-reel",
                        }[(Swipes - 1) % 4]
                    : Swipes > 0 ? "ld1-feed-scrolled"
                    : "ld1-feed-top"
                );
            }
            switch (method!.Name)
            {
                case "SendKeyAsync" when ChapterAfterSwipe && Swipes > 0 && ChapterBacks == 0:
                    ChapterBacks++;
                    return Task.FromResult(true);
                case "DumpUiHierarchyAsync":
                    _hierarchyReads++;
                    return Task.FromResult<string?>(Screen());
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Screen())
                    );
                case "SwipeAsync":
                    Swipes++;
                    return Task.FromResult(true);
                case "PressBackAsync":
                    Backs++;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    LastTap = ((int)args![1]!, (int)args[2]!);
                    return Task.FromResult(TapAcknowledged);
            }
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(true);
            throw new NotSupportedException(method.Name);
        }
    }
}
