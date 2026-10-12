using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeReelObservationTests
{
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
