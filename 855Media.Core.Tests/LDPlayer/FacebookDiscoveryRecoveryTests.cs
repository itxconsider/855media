using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookDiscoveryRecoveryTests
{
    private const string Android9Recents = """
        <hierarchy><node package="com.android.systemui" resource-id="com.android.systemui:id/recents_view"
          bounds="[0,0][720,1280]" enabled="true" clickable="false" visible-to-user="true">
          <node package="com.android.systemui" resource-id="com.android.systemui:id/button"
            class="android.widget.TextView" text="CLEAR ALL" bounds="[519,69][716,155]"
            enabled="true" clickable="false" visible-to-user="true" />
        </node></hierarchy>
        """;

    [Fact]
    public void RecentsClear_RecognizesObservedAndroid9NonClickableButton()
    {
        var target = LDPlayerService.FindRecentAppsClearTarget(Android9Recents);
        Assert.NotNull(target);
        Assert.Equal((617, 112), LDPlayerService.RecentAppsClearPoint(target));
    }

    [Theory]
    [InlineData("visible-to-user=\"true\"", "visible-to-user=\"false\"")]
    [InlineData("enabled=\"true\"", "enabled=\"false\"")]
    [InlineData("id/recents_view", "id/unrelated")]
    [InlineData("id/button", "id/unrelated")]
    [InlineData("[519,69][716,155]", "[519,69][800,155]")]
    [InlineData("[519,69][716,155]", "[716,69][519,155]")]
    public void RecentsClear_RejectsUntrustedOrUnavailableLegacyButton(string before, string after)
    {
        Assert.Null(
            LDPlayerService.FindRecentAppsClearTarget(Android9Recents.Replace(before, after))
        );
    }

    [Fact]
    public void RecentsClear_RejectsTwoDistinctTargets()
    {
        var node = System.Xml.Linq.XDocument.Parse(Android9Recents).Descendants("node").Last();
        var second = new System.Xml.Linq.XElement(node);
        second.SetAttributeValue("bounds", "[200,69][400,155]");
        var xml = Android9Recents.Replace("</node></hierarchy>", second + "</node></hierarchy>");
        Assert.Null(LDPlayerService.FindRecentAppsClearTarget(xml));
    }

    [Theory]
    [InlineData("com.android.launcher3", "CLEAR ALL", "", "")]
    [InlineData("com.android.systemui", "", "Close all apps", "")]
    [InlineData("com.android.launcher3", "", "", "com.android.launcher3:id/clear_all")]
    public void RecentsClear_AcceptsAndroidLabelDescriptionOrResource(
        string package,
        string text,
        string description,
        string id
    )
    {
        var xml = $"""
            <hierarchy><node package="{package}" text="{text}" content-desc="{description}"
              resource-id="{id}" clickable="true" enabled="true" bounds="[500,50][700,130]" /></hierarchy>
            """;
        Assert.NotNull(LDPlayerService.FindRecentAppsClearTarget(xml));
    }

    [Fact]
    public void RecentsClear_RejectsClearAllInFacebookAndDisabledControls()
    {
        Assert.Null(
            LDPlayerService.FindRecentAppsClearTarget(
                """
                <hierarchy><node package="com.facebook.katana" text="Clear all" bounds="[10,10][100,100]" /></hierarchy>
                """
            )
        );
        Assert.Null(
            LDPlayerService.FindRecentAppsClearTarget(
                """
                <hierarchy><node package="com.android.launcher3" text="Clear all" enabled="false" bounds="[10,10][100,100]" /></hierarchy>
                """
            )
        );
    }

    private const string Profile = """
        <hierarchy>
          <node text="A feed author" bounds="[0,0][400,80]" />
          <node content-desc="Profile picture" bounds="[150,180][550,550]" />
          <node text="Ana Carvalho" bounds="[100,568][620,623]" />
          <node content-desc="Ana Carvalho" bounds="[100,568][620,623]" />
          <node content-desc="3 posts" bounds="[310,623][410,674]" />
          <node text="Message" bounds="[10,750][700,800]" />
        </hierarchy>
        """;

    [Fact]
    public void MissingOwnerName_UsesProfileHeaderEvenInVisitorViewThenRequiresSeparateVerification()
    {
        Assert.Equal("Ana Carvalho", LDPlayerService.ReadProfileHeaderName(Profile));
        Assert.False(
            LDPlayerService.IsExpectedOwnProfileVisible(
                AtxAgentClient.ParseBloksHierarchy(Profile),
                "123",
                "Ana Carvalho"
            )
        );
    }

    [Fact]
    public void MissingOwnerName_DoesNotChooseFeedAuthorOrAmbiguousHeader()
    {
        Assert.Null(
            LDPlayerService.ReadProfileHeaderName(
                "<hierarchy><node text='A feed author' /></hierarchy>"
            )
        );
        Assert.Null(
            LDPlayerService.ReadProfileHeaderName(
                Profile.Replace("content-desc=\"Ana Carvalho\"", "content-desc=\"Other Account\"")
            )
        );
        Assert.Null(
            LDPlayerService.ReadProfileHeaderName(Profile.Replace("Ana Carvalho", "Ana Car…"))
        );
    }

    [Fact]
    public void MissingOwnerName_ExcludesProfessionalCategoryBelowTitle()
    {
        var xml = Profile
            .Replace(
                "content-desc=\"3 posts\" bounds=\"[310,623][410,674]\"",
                "content-desc=\"61 followers\" bounds=\"[310,672][410,723]\""
            )
            .Replace(
                "</hierarchy>",
                "<node text='Digital creator' bounds='[200,625][500,658]' /></hierarchy>"
            );
        Assert.Equal("Ana Carvalho", LDPlayerService.ReadProfileHeaderName(xml));
    }

    [Fact]
    public void ProfessionalProfile_IgnoresVerificationBadgeAndRecognizesOwnerPhotoControl()
    {
        var xml = Profile.Replace(
            "</hierarchy>",
            """
            <node text=" " content-desc="Get verified" bounds="[630,568][690,623]" />
            <node content-desc="Edit profile picture" bounds="[436,456][508,528]" />
            <node text="Dashboard" bounds="[10,810][340,860]" />
            </hierarchy>
            """
        );
        Assert.Equal("Ana Carvalho", LDPlayerService.ReadProfileHeaderName(xml));
        Assert.True(
            LDPlayerService.IsExpectedOwnProfileVisible(
                AtxAgentClient.ParseBloksHierarchy(xml),
                "123",
                "Ana Carvalho"
            )
        );
    }
}
