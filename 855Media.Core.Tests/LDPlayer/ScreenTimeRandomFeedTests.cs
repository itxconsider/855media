using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeRandomFeedTests
{
    private const string Feed =
        "<hierarchy><node><node content-desc='More options for Example Author’s post'/><node text='Public'/><node text='A distinct example caption with useful content.'/><node text='Like' clickable='true' bounds='[0,0][10,10]'/><node text='Comment' clickable='true' bounds='[20,0][30,10]'/></node></hierarchy>";

    [Theory]
    [InlineData("Ad")]
    [InlineData("Brand Survey")]
    [InlineData("Popular in Miami")]
    [InlineData("Join")]
    [InlineData("Boost post")]
    [InlineData("Boost reel")]
    [InlineData("Create ad")]
    [InlineData("Public group")]
    [InlineData("Private group")]
    [InlineData("Shared with: Public group")]
    [InlineData("Someone•2d•Shared with: Public group")]
    [InlineData("Someone•2d•Shared with: Private group")]
    [InlineData("Ad•Shared with: Public")]
    [InlineData("Sponsored·Shared with: Public")]
    public void SpecialCardsCannotBorrowPublicPostTarget(string label)
    {
        var xml = Feed.Replace("</hierarchy>", $"<node text='{label}'/></hierarchy>");
        Assert.Null(ScreenTimeFeedTarget.Resolve(xml));
        Assert.True(ScreenTimeHierarchy.IsUnsafe(xml));
    }

    [Fact]
    public void DefaultsRemainOptInAndReviewEnabled()
    {
        var o = new ScreenTimeOptions();
        Assert.False(o.RandomFeedComments);
        Assert.False(o.RandomFeedReactions);
        Assert.True(o.ReviewEachRandomComment);
    }

    [Fact]
    public void FingerprintIgnoresReactionStateButRequiresPublicAuthorAndContent()
    {
        var t = ScreenTimeFeedTarget.Resolve(Feed);
        Assert.NotNull(t);
        Assert.StartsWith("visible:", t!.Key);
        Assert.Equal(
            t.Key,
            ScreenTimeFeedTarget
                .Resolve(Feed.Replace("text='Like'", "text='Remove Like reaction'"))!
                .Key
        );
        Assert.Null(ScreenTimeFeedTarget.Resolve(Feed.Replace("text='Public'", "text='Friends'")));
        Assert.Null(ScreenTimeFeedTarget.Resolve(Feed.Replace("Example Author", "")));
        Assert.Null(
            ScreenTimeFeedTarget.Resolve(
                Feed.Replace("A distinct example caption with useful content.", "")
            )
        );
    }

    [Fact]
    public void SponsoredOrAmbiguousItemsAreRejected()
    {
        Assert.Null(
            ScreenTimeFeedTarget.Resolve(Feed.Replace("text='Public'", "text='Sponsored'"))
        );
        Assert.Null(
            ScreenTimeFeedTarget.Resolve(
                Feed.Replace(
                    "</node></hierarchy>",
                    "<node text='Like' clickable='true' bounds='[40,0][50,10]'/></node></hierarchy>"
                )
            )
        );
    }

    [Theory]
    [InlineData("<node text='Like' enabled='false'/>")]
    [InlineData("<node text='Like'/>")]
    [InlineData("<node content-desc='More options for Another Author’s post'/>")]
    public void EnclosingFeedCannotBorrowIdentityFromAnotherPost(string neighbour)
    {
        var mixed = Feed.Replace(
            "<node text='Like' clickable='true' bounds='[0,0][10,10]'/>",
            "<node><node text='Like' clickable='true' bounds='[0,0][10,10]'/></node>" + neighbour
        );
        Assert.Null(ScreenTimeFeedTarget.Resolve(mixed));
    }

    [Fact]
    public void RandomSettingsRoundTripAndValidate()
    {
        var s = new ScreenTimeSetting
        {
            WatchVideo = true,
            Options = new()
            {
                RandomFeedComments = true,
                RandomCommentTexts = "Hello\nNice",
                ReviewEachRandomComment = false,
            },
        };
        Assert.Empty(s.Validate());
        var o = s.Options.Snapshot();
        Assert.True(o.RandomFeedComments);
        Assert.False(o.ReviewEachRandomComment);
        Assert.Equal("Hello\nNice", o.RandomCommentTexts);
        s.Options.RandomCommentTexts = "";
        Assert.NotEmpty(s.Validate());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApprovalAndAutomaticModesUseOneSendAndSharedCap(bool approval)
    {
        await Run(approval, true, true);
    }

    [Fact]
    public async Task DeclinedApprovalNeverSends()
    {
        await Run(true, false, true);
    }

    [Fact]
    public async Task UnconfirmedAutomaticSendStopsWithoutResend()
    {
        await Run(false, true, false);
    }

    [Fact]
    public async Task RandomReactionUsesFingerprintConfirmationAndSharedCap()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "random-reaction-" + Guid.NewGuid().ToString("N") + ".db"
        );
        var history = new ScreenTimeHistoryStore(path);
        await history.StartAsync("s", "owner", 1);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var settings = new ScreenTimeSetting
        {
            OwnerUid = "owner",
            OwnerName = "Owner",
            Options = new()
            {
                RandomFeedReactions = true,
                Reactions = true,
                ReactionLimit = 1,
            },
        };
        var engine = new ScreenTimeInteractions(
            ld,
            history,
            1,
            settings,
            "s",
            null,
            (_, _) => Task.CompletedTask
        );
        try
        {
            await engine.RandomFeedAsync(null, default);
            await engine.RandomFeedAsync(null, default);
            Assert.Equal(1, fake.ReactionTaps);
            Assert.Equal(1, engine.Reactions);
            Assert.Equal("Confirmed", history.ReadActions().Single().State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("hidden-id", false, 0)]
    [InlineData("wrong-panel", false, 0)]
    [InlineData("final-actor", false, 0)]
    [InlineData("detached-panel-id", false, 0)]
    [InlineData("other-panel-confirmation", true, 1)]
    [InlineData("other-post-confirmation", true, 1)]
    [InlineData("cleanup-after-unknown", true, 1)]
    public async Task UnsupportedOrChangedEvidenceNeverConfirmsOrResends(
        string mode,
        bool unknown,
        int sends
    )
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "comment-proof-" + Guid.NewGuid().ToString("N") + ".db"
        );
        var history = new ScreenTimeHistoryStore(path);
        await history.StartAsync("s", "owner", 1);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.ExposeId = mode != "hidden-id";
        fake.WrongPanelId = mode == "wrong-panel";
        fake.FinalActorChanged = mode == "final-actor";
        fake.DetachedTargetId = mode == "detached-panel-id";
        fake.ConfirmationElsewhere = mode == "other-panel-confirmation";
        fake.ConfirmationOnOtherPost = mode == "other-post-confirmation";
        fake.CleanupThrows = mode == "cleanup-after-unknown";
        fake.Confirm = mode != "cleanup-after-unknown";
        var engine = new ScreenTimeInteractions(
            ld,
            history,
            1,
            new()
            {
                OwnerName = "Owner",
                OwnerUid = "owner",
                Options = new()
                {
                    RandomFeedComments = true,
                    RandomCommentTexts = "Hello",
                    ReviewEachRandomComment = false,
                },
            },
            "s",
            null,
            (_, _) => Task.CompletedTask
        );
        try
        {
            if (unknown)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    engine.RandomFeedAsync(null, default)
                );
                Assert.Contains("needs review", error.Message);
                Assert.True(history.NeedsReview(1));
            }
            else
                await engine.RandomFeedAsync(null, default);
            Assert.Equal(sends, fake.Sends);
            Assert.Equal(0, engine.Comments);
            Assert.DoesNotContain(history.ReadActions(), a => a.State == "Confirmed");
            if (mode == "final-actor")
                Assert.Equal("NotSubmitted", history.ReadActions().Single().State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DiagnosticLogFailureDoesNotReplacePrimaryFailure()
    {
        await ScreenTimeInteractions.BestEffortNoteAsync(() => throw new IOException("disk full"));
        await ScreenTimeInteractions.BestEffortNoteAsync(() =>
            Task.FromException(new InvalidOperationException("database unavailable"))
        );
    }

    private static async Task Run(bool approval, bool allow, bool confirm)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "random-feed-" + Guid.NewGuid().ToString("N") + ".db"
        );
        var history = new ScreenTimeHistoryStore(path);
        await history.StartAsync("s", "owner", 1);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Confirm = confirm;
        fake.ExposeId = true;
        var setting = new ScreenTimeSetting
        {
            OwnerName = "Owner",
            OwnerUid = "owner",
            Options = new()
            {
                RandomFeedComments = true,
                ReviewEachRandomComment = approval,
                RandomCommentTexts = "Hello",
                CommentLimit = 1,
            },
        };
        var engine = new ScreenTimeInteractions(
            ld,
            history,
            1,
            setting,
            "s",
            null,
            (_, _) => Task.CompletedTask
        );
        int reviews = 0;
        Func<ScreenTimeCommentDraft, System.Threading.CancellationToken, Task<bool>> review = (
            d,
            c
        ) =>
        {
            reviews++;
            Assert.Contains("Example Author", d.PostUrl);
            return Task.FromResult(allow);
        };
        try
        {
            if (!confirm)
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    engine.RandomFeedAsync(review, default)
                );
            else
            {
                await engine.RandomFeedAsync(review, default);
                await engine.RandomFeedAsync(review, default);
            }
            Assert.Equal(approval ? 1 : 0, reviews);
            Assert.Equal(allow ? 1 : 0, fake.Sends);
            Assert.Equal(allow && confirm ? 1 : 0, engine.Comments);
            if (!confirm)
                Assert.True(history.NeedsReview(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    public class Device : DispatchProxy
    {
        public bool Confirm = true;
        public bool ExposeId,
            WrongPanelId,
            FinalActorChanged,
            ConfirmationOnOtherPost,
            CleanupThrows;
        public bool DetachedTargetId,
            ConfirmationElsewhere;
        private int panelDumps;
        public int Sends;
        public int ReactionTaps;
        private bool panel,
            input;
        private string text = "";

        protected override object? Invoke(MethodInfo? m, object?[]? a)
        {
            if (m!.Name == "DumpUiHierarchyAsync")
            {
                if (!panel)
                {
                    var feed = ExposeId
                        ? Feed.Replace(
                            "<node text='Public'/>",
                            "<node text='Public'/><node text='Post ID: 123'/>"
                        )
                        : Feed;
                    return Task.FromResult<string?>(
                        ReactionTaps > 0
                            ? feed.Replace("text='Like'", "text='Remove Like reaction'")
                            : feed
                    );
                }
                panelDumps++;
                var id = WrongPanelId || ConfirmationOnOtherPost && Sends > 0 ? "999" : "123";
                var actor = FinalActorChanged && panelDumps >= 3 ? "Other Page" : "Owner";
                var comment = Sends > 0 && Confirm ? "<node text='Comment by Owner: Hello'/>" : "";
                var post = $"<node text='Post ID: {id}'/>";
                var panelXml =
                    $"<node>{(DetachedTargetId ? "" : post)}<node text='Comment as {actor}' clickable='true' bounds='[40,0][50,10]'/>{(input ? $"<node class='android.widget.EditText' text='{text}'/>" : "")}<node text='Send comment' clickable='true' bounds='[60,0][70,10]'/>{(ConfirmationElsewhere ? "" : comment)}</node>";
                return Task.FromResult<string?>(
                    "<hierarchy>"
                        + (DetachedTargetId ? post : "")
                        + panelXml
                        + (ConfirmationElsewhere ? "<node>" + comment + "</node>" : "")
                        + "</hierarchy>"
                );
            }
            if (m.Name == "TapAsync")
            {
                var x = (int)a![1]!;
                if (x == 5)
                    ReactionTaps++;
                if (x == 25)
                    panel = true;
                if (x == 65)
                    Sends++;
                return Task.FromResult(true);
            }
            if (m.Name == "InputTextAdbKeyboardAsync")
            {
                input = true;
                text = (string)a![1]!;
                return Task.FromResult(true);
            }
            if (m.Name == "SendKeyAsync")
            {
                panel = false;
                if (CleanupThrows)
                    throw new IOException("Back failed");
                return Task.FromResult(true);
            }
            throw new NotSupportedException(m.Name);
        }
    }
}
