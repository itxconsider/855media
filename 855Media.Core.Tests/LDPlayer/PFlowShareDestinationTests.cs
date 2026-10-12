using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowShareDestinationTests
{
    internal const string SelectedReelsXml =
        "<hierarchy><node bounds='[0,0][720,1280]'>"
        + "<node package='android' resource-id='android:id/title' text='Share with Reels' bounds='[144,734][656,862]'/>"
        + "<node package='android' resource-id='android:id/button_once' text='JUST ONCE' enabled='true' clickable='true' bounds='[344,878][540,974]'/>"
        + "<node package='android' resource-id='android:id/button_always' text='ALWAYS' enabled='true' clickable='true' bounds='[540,878][656,974]'/>"
        + "<node package='android' text='Use a different app'/>"
        + "<node package='android' text='Feed' clickable='true' bounds='[144,1135][221,1184]'/>"
        + "</node></hierarchy>";

    [Theory]
    [InlineData("Reel", true)]
    [InlineData("Video", true)]
    [InlineData("Picture", false)]
    [InlineData("unsupported", false)]
    public void RecognizesPreselectedAndroidDestinationWithoutConfusingAlternatives(
        string type,
        bool selected
    )
    {
        Assert.True(PFlowShareDestination.IsChooser(SelectedReelsXml));
        Assert.Equal(selected, PFlowShareDestination.IsDestinationSelected(SelectedReelsXml, type));
        Assert.Null(PFlowShareDestination.Destination(SelectedReelsXml, "Reel"));
        Assert.Equal(
            (442, 926),
            ScreenTimeHierarchy.Center(PFlowShareDestination.JustOnce(SelectedReelsXml))
        );
    }

    [Fact]
    public void FacebookTextCannotImpersonateTheSelectedAndroidTitle()
    {
        Assert.False(
            PFlowShareDestination.IsDestinationSelected(
                SelectedReelsXml.Replace("package='android'", "package='com.facebook.katana'"),
                "Reel"
            )
        );
        Assert.False(
            PFlowShareDestination.IsDestinationSelected(
                SelectedReelsXml.Replace("Share with Reels", "Share with Feed"),
                "Reel"
            )
        );
        Assert.False(
            PFlowShareDestination.IsDestinationSelected(
                SelectedReelsXml.Replace("Share with Reels", "Share with Reels extra"),
                "Reel"
            )
        );
    }

    [Fact]
    public void ConflictingSelectedTitlesAndDisabledOnceCannotProceed()
    {
        var conflicting = SelectedReelsXml.Replace(
            "</hierarchy>",
            "<node package='android' resource-id='android:id/title' text='Share with Feed'/></hierarchy>"
        );
        Assert.False(PFlowShareDestination.IsDestinationSelected(conflicting, "Reel"));
        Assert.Null(
            PFlowShareDestination.JustOnce(
                SelectedReelsXml.Replace("enabled='true'", "enabled='false'")
            )
        );
    }

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
