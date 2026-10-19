using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeFeedRecognitionTests
{
    [Theory]
    [InlineData("posts", "less")]
    [InlineData("posts", "fewer")]
    [InlineData("reels", "less")]
    public void NewsFeedPreferenceCardAllowsScrollingWithoutInteracting(
        string content,
        string amount
    )
    {
        // Place the captured LD-14 card in a captured Feed list to exercise the
        // shared rule. This is a constructed Feed variant, not a live Feed dump.
        var reel = XDocument.Parse(Fixture("ld14-reels-interest-card"));
        var close = reel.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Close");
        var card = new XElement(close.Parent!);
        foreach (var n in card.Descendants("node"))
        foreach (var a in n.Attributes().Where(a => a.Name.LocalName is "text" or "content-desc"))
            a.Value = a.Value.Replace("more or less reels", $"more or {amount} {content}");
        var feed = XDocument.Parse(Fixture("ld1-feed-top"));
        feed.Descendants("node")
            .Single(n => (string?)n.Attribute("resource-id") == "android:id/list")
            .Add(card);
        close = card.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Close");
        Assert.True(ScreenTimeNavigation.IsInlineCardDismissal(close, "Feed"));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(feed.ToString(), "Feed"));
        var match = FacebookLayoutRegistry.Match(feed.ToString(), FacebookScreenKind.NewsFeed);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.False(match.Browse!.InteractionReady);

        close.Remove();
        feed.Root!.Add(close);
        Assert.False(ScreenTimeNavigation.IsInlineCardDismissal(close, "Feed"));
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(feed.ToString(), "Feed"));
        Assert.False(
            FacebookLayoutRegistry.Match(feed.ToString(), FacebookScreenKind.NewsFeed).IsMatch
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptionLinksAndUnicodeActionLabelsDoNotHideNewsFeed(bool shortLabels)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-action-row"));
        foreach (var n in doc.Descendants("node"))
        foreach (var a in n.Attributes().Where(a => a.Name.LocalName is "text" or "content-desc"))
        {
            if (shortLabels)
                a.Value = a
                    .Value.Replace(
                        "Like button. Double tap and hold to react to the comment.",
                        "Like"
                    )
                    .Replace("Share button. Double tap to share the post.", "Share");
            a.Value = a.Value.Replace(" ", "\n\u00a0\u200b").ToUpperInvariant();
        }
        var list = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("resource-id") == "android:id/list");
        list.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("class", "android.widget.Button"),
                new XAttribute("clickable", "true"),
                new XAttribute("bounds", "[20,400][680,480]"),
                new XAttribute(
                    "text",
                    "Read this: https://example.com/watch?next=Share%20now #video"
                )
            )
        );
        var xml = doc.ToString();
        Assert.True(ScreenTimeNavigation.IsScrolledFeed(xml));
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(xml, "Feed"));
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("missing-share")]
    public void NewsFeedFallbackStillRequiresVisibleCompleteActions(string mutation)
    {
        var doc = XDocument.Parse(Fixture("ld1-feed-action-row"));
        var list = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("resource-id") == "android:id/list");
        if (mutation == "missing-share")
            foreach (
                var n in list.Descendants("node")
                    .Where(n =>
                        ((string?)n.Attribute("content-desc"))?.StartsWith("Share button") == true
                    )
                    .ToArray()
            )
                n.Remove();
        else
            list.SetAttributeValue(mutation == "hidden" ? "visible-to-user" : "enabled", "false");
        Assert.False(ScreenTimeNavigation.IsScrolledFeed(doc.ToString()));
    }

    [Theory]
    [InlineData("https://example.com/Log%20in?tab=Home")]
    [InlineData("https://example.com/reels?q=Share%20now#Comment")]
    public void ReelCaptionLinksRemainContentInsteadOfNavigation(string link)
    {
        var doc = XDocument.Parse(Fixture("ld9-reels-fullscreen-multiline-creator"));
        foreach (var n in doc.Descendants("node"))
        foreach (var a in n.Attributes().Where(a => a.Name.LocalName is "text" or "content-desc"))
            if (a.Value.Contains("https://www.google.com/share.google"))
                a.Value = "Caption with link " + link;
        var xml = doc.ToString();
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelsFeed).IsMatch);
        Assert.False(ScreenTimeNavigation.IsBlockingScreen(xml, "Reels"));
        Assert.NotNull(ScreenTimeNavigation.ReelObservationKey(xml));
        Assert.False(ScreenTimeNavigation.ReelContentChanged(xml, xml));
    }

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );
}
