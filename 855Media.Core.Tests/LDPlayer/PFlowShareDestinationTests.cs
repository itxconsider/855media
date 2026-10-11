using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowShareDestinationTests
{
    const string Xml =
        "<hierarchy><node text='Share with'/><node text='Feed' clickable='true' bounds='[0,0][20,20]'/><node text='Reels' clickable='true' bounds='[0,30][20,50]'/><node text='JUST ONCE' enabled='true' clickable='true' bounds='[0,60][20,80]'/><node text='ALWAYS' clickable='true' bounds='[30,60][50,80]'/></hierarchy>";

    [Theory]
    [InlineData("Reel", 40)]
    [InlineData("Picture", 10)]
    [InlineData("Video", 40)]
    public void ChoosesRequestedSurface(string type, int y)
    {
        Assert.True(PFlowShareDestination.IsChooser(Xml));
        Assert.Equal(
            y,
            ScreenTimeHierarchy.Center(PFlowShareDestination.Destination(Xml, type))!.Value.Y
        );
    }

    [Fact]
    public void UnsupportedTypeNeverDefaultsToFeed() =>
        Assert.Null(PFlowShareDestination.Destination(Xml, "unsupported"));

    [Fact]
    public void DoesNotSelectOnNormalFeed()
    {
        Assert.Null(PFlowShareDestination.Destination(Xml.Replace("Share with", "Home"), "Reel"));
    }

    [Fact]
    public void NeverSelectsDisabledOnceOrAlways()
    {
        Assert.Null(
            PFlowShareDestination.JustOnce(Xml.Replace("enabled='true'", "enabled='false'"))
        );
        Assert.Equal(10, ScreenTimeHierarchy.Center(PFlowShareDestination.JustOnce(Xml))!.Value.X);
    }

    [Fact]
    public void MissingDestinationIsRejected()
    {
        Assert.Null(
            PFlowShareDestination.Destination(
                Xml.Replace("text='Reels'", "text='Your groups'"),
                "Reel"
            )
        );
    }
}
