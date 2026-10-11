using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPageDetailsNavigationTests
{
    [Fact]
    public void HeaderNameWinsOverSameNameOnPost()
    {
        var header = Node("Van The Fabricator", false);
        header.SetAttributeValue("bounds", "[24,400][500,450]");
        var post = Node("Van The Fabricator");
        post.SetAttributeValue("bounds", "[128,1083][380,1121]");
        var dashboard = Node("Professional dashboard");
        dashboard.SetAttributeValue("bounds", "[24,679][445,751]");
        var screen = Screen(post, dashboard, header);
        var target = PFlowPageDetailsNavigation.FindHeaderName(screen, "Van The Fabricator");
        Assert.NotNull(target);
        Assert.Equal(400, target.Bounds.Top);
        Assert.Null(
            PFlowPageDetailsNavigation.FindHeaderName(Screen(post, dashboard), "Van The Fabricator")
        );
    }

    private static XElement Node(string text, bool clickable = true, params XElement[] children) =>
        new(
            "node",
            new XAttribute("text", text),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", clickable),
            new XAttribute("bounds", "[24,215][329,280]"),
            children
        );

    private static BloksScreenInfo Screen(params XElement[] nodes) =>
        AtxAgentClient.ParseBloksHierarchy(new XElement("hierarchy", nodes).ToString());

    [Theory]
    [InlineData("Create note: Thinking about…")]
    [InlineData("Thinking about…")]
    [InlineData("Share note")]
    [InlineData("A post about plumbing")]
    public void ComposerAndArbitraryAboutTextNeverMatch(string label) =>
        Assert.Null(
            PFlowPageDetailsNavigation.FindTarget(Screen(Node(label)), ["About", "See more about"])
        );

    [Theory]
    [InlineData("About")]
    [InlineData("About info")]
    [InlineData("See your About info")]
    [InlineData("Page transparency")]
    public void ExactDetailsControlsRemainSupported(string label) =>
        Assert.NotNull(PFlowPageDetailsNavigation.FindTarget(Screen(Node(label)), [label]));

    [Fact]
    public void ComposerParentCannotDonateTapBoundsToAboutChild() =>
        Assert.Null(
            PFlowPageDetailsNavigation.FindTarget(
                Screen(Node("Create note: Thinking about…", true, Node("About", false))),
                ["About"]
            )
        );

    [Fact]
    public void ExactNameQualifiedAboutLinkIsSupported()
    {
        var screen = Screen(Node("See more about Van The Fabricator"));
        Assert.NotNull(
            PFlowPageDetailsNavigation.FindTarget(screen, ["See more about"], "Van The Fabricator")
        );
        Assert.Null(
            PFlowPageDetailsNavigation.FindTarget(screen, ["See more about"], "Uncle Jhonn")
        );
    }
}
