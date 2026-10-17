using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeExpansionTests
{
    [Fact]
    public void NewActionsAreOptInAndLegacyLikesRemainSelected()
    {
        var s = ScreenTimeSetting.FromConfig(new() { IsLikePost = true, LikePostMax = 2 });
        Assert.True(s.LikePost);
        Assert.False(s.Options.Stories);
        Assert.False(s.Options.Reactions);
        Assert.False(s.Options.Comments);
        Assert.Equal(2, s.LikePostMax);
    }

    [Fact]
    public void OptionsSnapshotSeparatesDraftsAndPreservesJson()
    {
        var c = new AutoPostConfig();
        c.ScreenTimeOptions.Drafts.Add(
            new() { PostUrl = "https://www.facebook.com/a/posts/123", Text = "Nice" }
        );
        var snapshot = c.ScreenTimeOptions.Snapshot();
        c.ScreenTimeOptions.Drafts[0].Text = "Changed";
        Assert.Equal("Nice", snapshot.Drafts[0].Text);
    }

    [Fact]
    public void DraftAndRangeChangesNotifyPreview()
    {
        var o = new ScreenTimeOptions();
        int n = 0;
        o.PropertyChanged += (_, _) => n++;
        var d = new ScreenTimeCommentDraft();
        o.Drafts.Add(d);
        d.Text = "Nice";
        o.StoryLimit = 3;
        Assert.Equal(3, n);
        o.Drafts.Clear();
        n = 0;
        d.Text = "Removed";
        Assert.Equal(0, n);
    }

    [Theory]
    [InlineData("https://www.facebook.com/a/posts/123", "123")]
    [InlineData("https://m.facebook.com/story.php?story_fbid=456&id=1", "456")]
    [InlineData("https://evil.example/posts/123", null)]
    [InlineData("https://facebook.com.evil.example/posts/123", null)]
    [InlineData("http://facebook.com/a/posts/123", null)]
    [InlineData("Post ID: 123", "123")]
    [InlineData("Like", null)]
    public void TargetIdentifiersAreRestricted(string input, string? expected) =>
        Assert.Equal(expected, ScreenTimeHierarchy.PostId(input));

    [Fact]
    public void InvalidActionLimitsAndDraftsAreRejected()
    {
        var s = new ScreenTimeSetting
        {
            Options = new()
            {
                Stories = true,
                StoryDwellSeconds = 1,
                Reactions = true,
                Reaction = "Unsupported",
                Comments = true,
            },
        };
        Assert.True(s.Validate().Count >= 3);
    }

    [Fact]
    public async Task ActionHistoryBlocksUnknownAcrossRestartAndAllowsReviewedRetry()
    {
        using var h = new History();
        await h.Store.StartAsync("s", "owner", 1);
        Assert.True(await h.Store.ReserveAsync("a", "s", "owner", "123", "Comment"));
        await h.Store.ActionAsync("a", "Unknown", "No confirmation");
        var restart = new ScreenTimeHistoryStore(h.Store.FilePath);
        Assert.True(restart.NeedsReview(1));
        Assert.False(await restart.ReserveAsync("b", "s", "owner", "123", "Comment"));
        await restart.ResolveAsync("a", false, "Checked post: not applied");
        Assert.False(restart.NeedsReview(1));
        Assert.True(await restart.ReserveAsync("b", "s", "owner", "123", "Comment"));
        Assert.Equal(2, restart.ReadActions().Count);
    }

    [Fact]
    public async Task ConfirmedActionsRemainDuplicateProtected()
    {
        using var h = new History();
        await h.Store.StartAsync("s", "owner", 1);
        await h.Store.ReserveAsync("a", "s", "owner", "123", "Reaction");
        await h.Store.ActionAsync("a", "Confirmed", "Selected");
        Assert.False(await h.Store.ReserveAsync("b", "s", "owner", "123", "Reaction"));
        Assert.False(h.Store.NeedsReview(1));
    }

    [Fact]
    public async Task HistoryRequiresReviewEvidence()
    {
        using var h = new History();
        await Assert.ThrowsAsync<ArgumentException>(() => h.Store.ResolveAsync("id", false, ""));
    }

    [Fact]
    public async Task ExpandedSettingsPersistAndOldKeysStillLoad()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "st-settings-" + Guid.NewGuid().ToString("N")
        );
        var path = Path.Combine(folder, "config.json");
        var config = new AutoPostConfig { IsLikePost = true, LikePostMax = 2 };
        config.ScreenTimeOptions.Stories = true;
        config.ScreenTimeOptions.Reactions = true;
        config.ScreenTimeOptions.Reaction = "Love";
        config.ScreenTimeOptions.Drafts.Add(
            new() { PostUrl = "https://www.facebook.com/a/posts/123", Text = "Nice" }
        );
        await AutoPostConfigStorage.SaveConfigAsync(config, path);
        var loaded = await AutoPostConfigStorage.LoadConfigAsync(path);
        Assert.True(loaded.IsLikePost);
        Assert.Equal(2, loaded.LikePostMax);
        Assert.True(loaded.ScreenTimeOptions.Stories);
        Assert.Equal("Love", loaded.ScreenTimeOptions.Reaction);
        Assert.Equal("Nice", loaded.ScreenTimeOptions.Drafts[0].Text);
        await File.WriteAllTextAsync(path, "{\"IsWatchVideo\":false,\"IsLikePost\":true}");
        loaded = await AutoPostConfigStorage.LoadConfigAsync(path);
        Assert.False(loaded.ScreenTimeOptions.Comments);
        Assert.False(loaded.ScreenTimeOptions.Reactions);
        Assert.True(loaded.IsLikePost);
        File.Delete(path);
        Directory.Delete(folder);
    }

    [Fact]
    public void AmbiguousControlsAndMultiplePostIdentitiesAreRejected()
    {
        Assert.Null(
            ScreenTimeHierarchy.Control(
                "<hierarchy><node text='Like'/><node text='Like'/></hierarchy>",
                "Like"
            )
        );
        Assert.False(
            ScreenTimeHierarchy.HasOnlyPost(
                "<hierarchy><node text='Post ID: 123'/><node text='Post ID: 456'/></hierarchy>",
                "123"
            )
        );
    }

    [Fact]
    public void CapturedStoryTrayLabelsExcludeOwnAndCreateEntries()
    {
        var xml =
            """<hierarchy><node content-desc='Create a story' clickable='true' bounds='[0,0][20,20]'/><node content-desc="Owner&apos;s story, Unseen" clickable='true' bounds='[0,0][20,20]'/><node content-desc="Example Author&apos;s story, Suggested, Unseen" clickable='true' bounds='[20,0][40,20]'/></hierarchy>""";
        var entry = ScreenTimeHierarchy.StoryEntry(
            xml,
            "Owner",
            new System.Collections.Generic.HashSet<string>()
        );
        Assert.NotNull(entry);
        Assert.Contains("Example Author", ScreenTimeHierarchy.Label(entry!));
    }

    [Fact]
    public void OverlappingCapturedPickerRegionsAreNotEligibleForTap()
    {
        var xml =
            """<hierarchy><node text='Like' clickable='true' bounds='[8,755][180,915]'/><node text='Love' clickable='true' bounds='[86,755][258,915]'/></hierarchy>""";
        Assert.Null(ScreenTimeHierarchy.ReactionChoice(xml, "Love"));
        Assert.Null(ScreenTimeHierarchy.ReactionChoice(xml, "Like"));
    }

    [Fact]
    public async Task CommentComposerForPageCannotSendAsPersonalOwner()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        e.Device.ComposerOwner = "Example Page";
        await e.Actions.CommentAsync((_, _) => Task.FromResult(true), default);
        Assert.Equal(0, e.Device.SendTaps);
        Assert.Equal(0, e.Device.Taps);
        Assert.Contains("comment-actor-or-input-unverified", h.Store.ReadReport());
    }

    private static ScreenTimeSetting Setting() =>
        new()
        {
            OwnerUid = "owner",
            OwnerName = "Owner",
            WatchVideo = false,
            Options = new()
            {
                Reactions = true,
                ReactionLimit = 1,
                Comments = true,
                Drafts =
                [
                    new() { PostUrl = "https://www.facebook.com/a/posts/123", Text = "Nice" },
                ],
            },
        };

    private static async Task<(ScreenTimeInteractions Actions, Fake Device)> Engine(
        History h,
        string mode
    )
    {
        await h.Store.StartAsync("s", "owner", 1);
        var ld = DispatchProxy.Create<ILDPlayerService, Fake>();
        var fake = (Fake)(object)ld;
        fake.Mode = mode;
        var actions = new ScreenTimeInteractions(
            ld,
            h.Store,
            1,
            Setting(),
            "s",
            null,
            (_, c) =>
            {
                c.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );
        return (actions, fake);
    }

    [Fact]
    public async Task ReactionRequiresUniquePostIdentity()
    {
        using var h = new History();
        var e = await Engine(h, "reaction");
        e.Device.HideId = true;
        await e.Actions.ReactAsync(default);
        Assert.Equal(0, e.Device.Taps);
        Assert.Equal(1, e.Actions.Skipped);
    }

    [Fact]
    public async Task ReactionIsConfirmedAndBudgetSharedAcrossCalls()
    {
        using var h = new History();
        var e = await Engine(h, "reaction");
        await e.Actions.ReactAsync(default);
        await e.Actions.ReactAsync(default);
        Assert.Equal(1, e.Actions.Reactions);
        Assert.Equal(1, e.Device.Taps);
        Assert.Equal("Confirmed", h.Store.ReadActions().Single().State);
    }

    [Fact]
    public async Task ReactionWithoutConfirmationRemainsUnknown()
    {
        using var h = new History();
        var e = await Engine(h, "reaction");
        e.Device.Confirm = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Actions.ReactAsync(default));
        Assert.True(h.Store.NeedsReview(1));
        Assert.Equal(1, e.Device.Taps);
    }

    [Fact]
    public async Task CommentReviewDeclineNeverSends()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        await e.Actions.CommentAsync((_, _) => Task.FromResult(false), default);
        Assert.Equal(0, e.Device.SendTaps);
        Assert.Empty(h.Store.ReadActions());
    }

    [Fact]
    public async Task CommentSendHasDurableCheckpointAndAuthorConfirmation()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        e.Device.BeforeSend = () => Assert.Equal("Submitted", h.Store.ReadActions().Single().State);
        await e.Actions.CommentAsync((_, _) => Task.FromResult(true), default);
        Assert.Equal(1, e.Device.SendTaps);
        Assert.Equal(1, e.Actions.Comments);
        Assert.Equal("Confirmed", h.Store.ReadActions().Single().State);
    }

    [Fact]
    public async Task PressedReactionStateConfirmsWithoutBeingToggledAgain()
    {
        using var h = new History();
        var e = await Engine(h, "reaction");
        e.Device.PressedReactionLabel = true;
        await e.Actions.ReactAsync(default);
        await e.Actions.ReactAsync(default);
        Assert.Equal(1, e.Device.Taps);
        Assert.Equal(1, e.Actions.Reactions);
        Assert.Equal("Confirmed", h.Store.ReadActions().Single().State);
    }

    [Fact]
    public async Task SplitCommentRowConfirmsAfterSendControlDisappears()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        e.Device.SplitCommentRow = true;
        await e.Actions.CommentAsync((_, _) => Task.FromResult(true), default);
        Assert.Equal(1, e.Device.SendTaps);
        Assert.Equal(1, e.Actions.Comments);
        Assert.Equal("Confirmed", h.Store.ReadActions().Single().State);
    }

    [Fact]
    public async Task CommentWithoutConfirmationDoesNotResend()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        e.Device.Confirm = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Actions.CommentAsync((_, _) => Task.FromResult(true), default)
        );
        Assert.Equal(1, e.Device.SendTaps);
        Assert.True(h.Store.NeedsReview(1));
    }

    [Fact]
    public async Task ExistingMatchingCommentIsSkipped()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        e.Device.Existing = true;
        await e.Actions.CommentAsync((_, _) => Task.FromResult(true), default);
        Assert.Equal(0, e.Device.SendTaps);
        Assert.Empty(h.Store.ReadActions());
    }

    [Fact]
    public async Task StoriesRequireRecognizedViewerAndDwell()
    {
        using var h = new History();
        var e = await Engine(h, "stories");
        await e.Actions.ViewStoriesAsync(default);
        Assert.Equal(1, e.Actions.Stories);
        Assert.Equal(0, e.Device.SendTaps);
    }

    [Fact]
    public async Task UnsupportedStoryViewerUsesBackAndVerifiesReturn()
    {
        using var h = new History();
        var e = await Engine(h, "stories");
        e.Device.HideStoryControls = true;
        await e.Actions.ViewStoriesAsync(default);
        Assert.Equal(0, e.Actions.Stories);
        Assert.Equal(1, e.Device.BackTaps);
        Assert.Equal(1, e.Actions.Skipped);
    }

    [Fact]
    public async Task CancelBeforeCommentHasNoPublicEffect()
    {
        using var h = new History();
        var e = await Engine(h, "comment");
        using var c = new CancellationTokenSource();
        c.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            e.Actions.CommentAsync((_, _) => Task.FromResult(true), c.Token)
        );
        Assert.Equal(0, e.Device.SendTaps);
    }

    private sealed class History : IDisposable
    {
        public ScreenTimeHistoryStore Store { get; } =
            new(
                Path.Combine(
                    Path.GetTempPath(),
                    "st-expansion-" + Guid.NewGuid().ToString("N"),
                    "history.db"
                )
            );

        public void Dispose()
        {
            var d = Path.GetFullPath(Path.GetDirectoryName(Store.FilePath)!);
            if (
                !d.StartsWith(
                    Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException();
            if (Directory.Exists(d))
                Directory.Delete(d, true);
        }
    }

    public class Fake : DispatchProxy
    {
        public bool SplitCommentRow;
        public bool PressedReactionLabel;
        public bool HideStoryControls;
        public int BackTaps;
        public string ComposerOwner = "Owner";
        public string Mode = "reaction";
        public bool HideId,
            Existing;
        public bool Confirm = true;
        public int Taps,
            SendTaps;
        public Action? BeforeSend;
        private bool _reacted,
            _sent,
            _input,
            _story;
        private string _text = "";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            args ??= [];
            if (method!.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(Xml());
            if (method.Name == "OpenDeepLinkAsync")
                return Task.FromResult(true);
            if (method.Name == "InputTextAdbKeyboardAsync")
            {
                _input = true;
                _text = (string)args[1]!;
                return Task.FromResult(true);
            }
            if (method.Name == "SendKeyAsync" && Mode == "stories")
            {
                BackTaps++;
                _story = false;
                return Task.FromResult(true);
            }
            if (method.Name == "TapAsync")
            {
                Taps++;
                if (Mode == "reaction")
                    _reacted = true;
                else if (Mode == "stories")
                    _story = !_story;
                else if ((int)args[1]! == 25)
                {
                    BeforeSend?.Invoke();
                    SendTaps++;
                    _sent = true;
                }
                return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }

        private string Xml()
        {
            if (Mode == "stories")
                return _story && HideStoryControls
                        ? "<hierarchy><node text='Unrecognized viewer'/></hierarchy>"
                    : _story
                        ? "<hierarchy><node text='story_id: 77'/><node text='Close story' clickable='true' enabled='true' bounds='[10,10][20,20]'/></hierarchy>"
                    : "<hierarchy><node text='View story by Other' clickable='true' enabled='true' bounds='[0,0][10,10]'/></hierarchy>";
            string id = HideId ? "" : "<node text='Post ID: 123'/>";
            if (Mode == "reaction")
                return $"<hierarchy>{id}<node text='{(_reacted && Confirm ? (PressedReactionLabel ? "Like button, pressed. Double tap and hold to change reaction." : "Remove Like reaction") : "Like")}' clickable='true' bounds='[0,0][10,10]'/></hierarchy>";
            var comment =
                Existing || _sent && Confirm
                    ? SplitCommentRow
                        ? "<node><node text='Owner'/><node text='Just now'/><node text='Nice'/><node text='Reply'/></node>"
                        : "<node text='Comment by Owner: Nice'/>"
                    : "";
            var send =
                SplitCommentRow && _sent
                    ? ""
                    : "<node text='Send comment' clickable='true' bounds='[20,20][30,30]'/>";
            return $"<hierarchy><node>{id}<node text='Comment as {ComposerOwner}' clickable='true' bounds='[0,0][10,10]'/>{(_input ? $"<node class='android.widget.EditText' text='{_text}'/>" : "")}{send}{comment}</node></hierarchy>";
        }
    }
}
