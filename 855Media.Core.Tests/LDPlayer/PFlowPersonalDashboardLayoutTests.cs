using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPersonalDashboardLayoutTests
{
    private static string Fixture =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld56-personal-dashboard-captured.xml"
            )
        );
    private static FacebookPageTarget Target =>
        new() { PageName = "Fixture Personal", Type = "Profile" };

    [Fact]
    public void CapturedProfessionalPersonalProfileMatchesNameWithoutEditProfileButton()
    {
        var xml = Fixture;
        Assert.DoesNotContain(
            ScreenTimeHierarchy.Nodes(xml),
            n =>
                (string?)n.Attribute("text") == "Edit profile"
                || (string?)n.Attribute("content-desc") == "Edit profile"
        );
        Assert.True(PFlowNativeAccountNavigator.OwnProfile(xml));
        Assert.False(PFlowSessionProfileUi.Contradicts(xml, Target));
        Assert.True(PFlowSessionProfileUi.Confirms(xml, Target));
        Assert.Equal(
            "Fixture Personal",
            PFlowPostingNameVerification.PersonalName(xml, ["Another Person", "Fixture Personal"])
        );
        Assert.True(
            PFlowPostingNameVerification.PersonalOrPageProfile(xml, "FixturePersonal", true)
        );
    }

    [Theory]
    [InlineData("missing-cover")]
    [InlineData("missing-picture")]
    [InlineData("disabled-cover")]
    [InlineData("hidden-picture")]
    [InlineData("wrong-name")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("page")]
    public void DashboardAloneOrUnsafeEvidenceDoesNotConfirmPersonal(string change)
    {
        var doc = XDocument.Parse(Fixture);
        var cover = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Edit cover photo");
        var picture = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Edit profile picture");
        if (change == "missing-cover")
            cover.Remove();
        if (change == "missing-picture")
            picture.Remove();
        if (change == "disabled-cover")
            cover.SetAttributeValue("enabled", "false");
        if (change == "hidden-picture")
            picture.SetAttributeValue("visible-to-user", "false");
        if (change == "wrong-name")
            foreach (
                var a in doc.Descendants("node")
                    .Attributes()
                    .Where(a => a.Name.LocalName is "text" or "content-desc")
            )
                a.Value = a.Value.Replace("Fixture Personal", "Another Person");
        if (change == "foreign")
            foreach (var n in doc.Descendants("node"))
                n.SetAttributeValue("package", "foreign.app");
        if (change == "input")
            doc.Root!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.EditText")
                )
            );
        if (change == "page")
            doc.Root!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Manage Page")
                )
            );
        Assert.False(PFlowSessionProfileUi.Confirms(doc.ToString(), Target));
        Assert.Null(
            PFlowPostingNameVerification.PersonalName(doc.ToString(), ["Fixture Personal"])
        );
    }
}
