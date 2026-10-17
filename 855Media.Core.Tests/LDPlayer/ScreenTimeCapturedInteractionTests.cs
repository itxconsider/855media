using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeCapturedInteractionTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    [Fact]
    public void CapturedIconFeedResolvesItsPublicPostAndControls()
    {
        var target = ScreenTimeFeedTarget.Resolve(Fixture("ld21-feed-icon-actions-captured"));
        Assert.NotNull(target);
        Assert.Equal("PIX11", target.Author);
        Assert.NotNull(target.CommentControl);
        Assert.NotNull(ScreenTimeHierarchy.Center(target.ReactionControl));
    }

    [Fact]
    public void CapturedTypedCommentFindsSendOnlyInsideTheCommentSheet()
    {
        var xml = Fixture("ld21-comments-typed-captured");
        Assert.True(ScreenTimeHierarchy.IsCommentSheet(xml, "Robert Munro"));
        Assert.NotNull(
            ScreenTimeHierarchy.Center(ScreenTimeHierarchy.Control(xml, "Send comment"))
        );
        Assert.False(
            ScreenTimeHierarchy.HasPublishedComment(xml, "Robert Munro", "wow that's amazing !!")
        );
        Assert.Null(
            ScreenTimeHierarchy.Control(
                "<hierarchy><node content-desc='Send' clickable='true' bounds='[0,0][50,50]'/></hierarchy>",
                "Send comment"
            )
        );
    }

    [Fact]
    public void CapturedPublishedCommentMatchesAuthorAndExactTextInOneRow()
    {
        var xml = Fixture("ld21-comments-published-captured");
        Assert.True(
            ScreenTimeHierarchy.HasPublishedComment(xml, "Robert Munro", "wow that's amazing !!")
        );
        Assert.False(
            ScreenTimeHierarchy.HasPublishedComment(xml, "Other", "wow that's amazing !!")
        );
        Assert.False(ScreenTimeHierarchy.HasPublishedComment(xml, "Robert Munro", "different"));
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Send comment"));
    }

    [Fact]
    public void CapturedLoveRequiresPressedControlRatherThanReactionSummary()
    {
        var xml = Fixture("ld21-love-pressed-captured");
        Assert.NotNull(ScreenTimeHierarchy.Control(xml, "Remove Love reaction"));
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Remove Like reaction"));
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Like"));
        var summaryOnly =
            "<hierarchy><node content-desc='Reacted: Love, change reaction'/></hierarchy>";
        Assert.Null(ScreenTimeHierarchy.Control(summaryOnly, "Remove Love reaction"));
    }

    [Fact]
    public void CapturedCommentSheetFindsOwnerInputWithoutBorrowingReplyButtons()
    {
        var xml = Fixture("ld21-comments-empty-captured");
        Assert.True(ScreenTimeHierarchy.IsCommentSheet(xml, "Robert Munro"));
        Assert.False(ScreenTimeHierarchy.IsCommentSheet(xml, "Wrong Owner"));
        Assert.NotNull(
            ScreenTimeHierarchy.Center(ScreenTimeHierarchy.Control(xml, "Comment as Robert Munro"))
        );
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Like"));
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Send comment"));
        Assert.Null(ScreenTimeHierarchy.VisiblePostId(xml));
    }

    [Fact]
    public void DailyLimitSurfaceIsNotAnInteractionTarget()
    {
        var xml = Fixture("ld21-daily-limit-captured");
        Assert.Null(ScreenTimeFeedTarget.Resolve(xml));
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Like"));
        Assert.Null(ScreenTimeHierarchy.Control(xml, "Send comment"));
    }

    [Fact]
    public void HiddenButtonDoesNotCompeteWithTheVisibleControl()
    {
        const string xml =
            "<hierarchy><node visible-to-user='false'><node text='Comment'/></node><node text='Comment' clickable='true' bounds='[0,0][50,50]'/></hierarchy>";
        Assert.Equal(
            (25, 25),
            ScreenTimeHierarchy.Center(ScreenTimeHierarchy.Control(xml, "Comment"))
        );
    }

    [Fact]
    public void PartialFeedCannotBorrowIdentityFromFriendSuggestions()
    {
        Assert.Null(ScreenTimeFeedTarget.Resolve(Fixture("ld21-feed-partial-actions-captured")));
    }

    [Fact]
    public void CapturedDockHasSevenDistinctChoicesAndRejectsUnknownOverlaps()
    {
        var xml = Fixture("ld21-reaction-banner-captured");
        Assert.True(ScreenTimeHierarchy.IsReactionPicker(xml));
        var points = ScreenTimeOptions
            .ReactionTypes.Select(r =>
                ScreenTimeHierarchy.Center(ScreenTimeHierarchy.ReactionChoice(xml, r))
            )
            .ToArray();
        Assert.All(points, p => Assert.NotNull(p));
        Assert.Equal(7, points.Distinct().Count());
        Assert.Equal((146, 988), points[1]);
        Assert.Null(
            ScreenTimeHierarchy.ReactionChoice(
                xml.Replace("RopeStyleUFIDockView", "UnknownDock"),
                "Love"
            )
        );
        Assert.Null(
            ScreenTimeHierarchy.ReactionChoice(
                xml.Replace("content-desc=\"Care\"", "content-desc=\"Other\""),
                "Love"
            )
        );
    }

    [Theory]
    [InlineData("Like button. Double tap and hold to react to the comment.", "Like")]
    [InlineData("Comment button. Double tap to comment.", "Comment")]
    [InlineData("Post comment", "Send comment")]
    [InlineData("Remove Love reaction. Double tap to remove.", "Remove Love reaction")]
    public void SupportedAccessibleButtonAliases(string exposed, string requested)
    {
        var xml = new XElement(
            "hierarchy",
            new XElement(
                "node",
                new XAttribute("content-desc", exposed),
                new XAttribute("clickable", true),
                new XAttribute("bounds", "[0,0][50,50]")
            )
        ).ToString();
        Assert.NotNull(ScreenTimeHierarchy.Control(xml, requested));
    }

    [Fact]
    public void SeparateAuthorAndTextMustBelongToTheSameCommentRow()
    {
        const string row =
            "<node><node text='Robert Munro'/><node text='Just now'/><node text='nice'/><node text='Reply'/></node>";
        Assert.True(
            ScreenTimeHierarchy.HasPublishedComment(
                "<hierarchy>" + row + "</hierarchy>",
                "Robert Munro",
                "nice"
            )
        );
        Assert.False(
            ScreenTimeHierarchy.HasPublishedComment(
                "<hierarchy>"
                    + row.Replace("nice", "different")
                    + row.Replace("Robert Munro", "Other")
                    + "</hierarchy>",
                "Robert Munro",
                "nice"
            )
        );
        Assert.False(
            ScreenTimeHierarchy.HasPublishedComment(
                "<hierarchy>"
                    + row.Replace("text='nice'", "class='android.widget.EditText' text='nice'")
                    + "</hierarchy>",
                "Robert Munro",
                "nice"
            )
        );
    }
}
