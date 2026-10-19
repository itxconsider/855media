using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Defines the actions and duration ranges for a ScreenTime session on a Facebook personal account in an LDPlayer instance.
/// </summary>
public class ScreenTimeSetting
{
    public ScreenTimeOptions Options { get; set; } = new();
    public string OwnerUid { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public bool WatchVideo { get; set; } = true;
    public int WatchVideoMin { get; set; } = 60;
    public int WatchVideoMax { get; set; } = 120;

    public bool Marketplace { get; set; } = false;
    public int MarketplaceMin { get; set; } = 20;
    public int MarketplaceMax { get; set; } = 50;

    public bool LikePost { get; set; } = false;
    public int LikePostMin { get; set; } = 1;
    public int LikePostMax { get; set; } = 3;

    public bool NewsFeed { get; set; } = false;
    public int NewsFeedMin { get; set; } = 60;
    public int NewsFeedMax { get; set; } = 120;

    public bool CheckIn { get; set; } = false;
    public string CheckInLocation { get; set; } = string.Empty;

    public bool ReadNotification { get; set; } = false;
    public bool ClearRecentApp { get; set; } = true;

    public bool AddFriendSuggest { get; set; } = false;
    public int AddFriendSuggestMin { get; set; } = 1;
    public int AddFriendSuggestMax { get; set; } = 1;

    public bool AcceptFriends { get; set; } = false;
    public int AcceptFriendsMin { get; set; } = 1;
    public int AcceptFriendsMax { get; set; } = 2;

    public bool DeleteFriendRequest { get; set; } = false;
    public int DeleteFriendRequestMin { get; set; } = 2;
    public int DeleteFriendRequestMax { get; set; } = 2;

    public bool HasActions =>
        Options.Stories
        || Options.Reactions
        || Options.Comments
        || Options.RandomFeedComments
        || WatchVideo
        || NewsFeed
        || Marketplace
        || ReadNotification
        || LikePost
        || CheckIn
        || AddFriendSuggest
        || AcceptFriends
        || DeleteFriendRequest;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        void Range(bool enabled, int min, int max, int limit, string label)
        {
            if (enabled && (min < 1 || max < min || max > limit))
                errors.Add($"{label}: use 1–{limit}, with minimum no greater than maximum.");
        }
        Range(WatchVideo, WatchVideoMin, WatchVideoMax, 3600, "Reels seconds");
        Range(NewsFeed, NewsFeedMin, NewsFeedMax, 3600, "Feed seconds");
        Range(Marketplace, MarketplaceMin, MarketplaceMax, 3600, "Marketplace seconds");
        Range(LikePost, LikePostMin, LikePostMax, 50, "Likes per session");
        if (LikePost && !NewsFeed && !WatchVideo)
            errors.Add("Likes require Feed or Reels browsing.");
        if (CheckIn || AddFriendSuggest || AcceptFriends || DeleteFriendRequest)
            errors.Add(
                "Check-in and friend changes are not implemented. Disable those saved options to run ScreenTime."
            );
        Range(true, 1, Options.MaxSessionSeconds, 14400, "Session seconds");
        Range(Options.Stories, 1, Options.StoryLimit, 20, "Story count");
        Range(Options.Stories, 3, Options.StoryDwellSeconds, 30, "Story dwell seconds");
        Range(NewsFeed, 1, Options.FeedDwellSeconds, 30, "Feed dwell seconds");
        Range(
            WatchVideo,
            Options.ReelDwellMinSeconds,
            Options.ReelDwellMaxSeconds,
            60,
            "Reel dwell seconds"
        );
        Range(Options.Reactions, 1, Options.ReactionLimit, 20, "Reaction limit");
        if (Options.Reactions && (!NewsFeed && !WatchVideo))
            errors.Add("Reactions require Feed or Reels.");
        if (Options.Reactions && !ScreenTimeOptions.ReactionTypes.Contains(Options.Reaction))
            errors.Add("Choose a supported reaction.");
        Range(Options.Comments, 1, Options.CommentLimit, 5, "Comment limit");
        Range(
            Options.Comments || Options.RandomFeedComments && Options.ReviewEachRandomComment,
            15,
            Options.ReviewTimeoutSeconds,
            600,
            "Review timeout seconds"
        );
        if (
            Options.Comments
            && (
                Options.Drafts.Count == 0
                || Options.Drafts.Any(d =>
                    string.IsNullOrWhiteSpace(d.Text)
                    || d.Text.Length > 2000
                    || d.PostId.Length == 0
                )
            )
        )
            errors.Add("Comments require Facebook post links and drafts of 1–2000 characters.");
        Range(
            Options.RandomFeedReactions || Options.RandomFeedComments,
            1,
            Options.InteractionChancePercent,
            100,
            "Random interaction chance percent"
        );
        Range(Options.RandomFeedComments, 1, Options.CommentLimit, 5, "Random comment limit");
        if ((Options.RandomFeedReactions || Options.RandomFeedComments) && !NewsFeed && !WatchVideo)
            errors.Add("Random interactions require Feed or Reels browsing.");
        if (Options.RandomFeedReactions && !Options.Reactions)
            errors.Add("Enable reactions to use random Feed/Reel reactions.");
        if (
            Options.RandomFeedComments
            && (
                string.IsNullOrWhiteSpace(Options.RandomCommentTexts)
                || Options.RandomCommentTexts.Split('\n').Any(x => x.Trim().Length > 2000)
            )
        )
            errors.Add(
                "Random comments require a saved comment list: one nonblank comment per line, up to 2000 characters each."
            );
        return errors;
    }

    public string Summary =>
        !HasActions
            ? "No ScreenTime actions selected. Posting can run by itself."
            : string.Join(
                " • ",
                new[]
                {
                    ReadNotification ? "Notifications" : null,
                    Options.Stories
                        ? $"Stories up to {Options.StoryLimit} × {Options.StoryDwellSeconds}s"
                        : null,
                    Options.Reactions ? $"{Options.Reaction}: up to {Options.ReactionLimit}" : null,
                    Options.Comments ? $"Reviewed comments: up to {Options.CommentLimit}" : null,
                    Options.RandomFeedComments
                        ? $"Random comments: up to {Options.CommentLimit}, {(Options.ReviewEachRandomComment ? "approval each send" : "automatic saved list")}"
                        : null,
                    Options.RandomFeedReactions ? "Random public Feed/Reel reactions" : null,
                    $"Session cap {Options.MaxSessionSeconds}s",
                    NewsFeed ? $"Feed {NewsFeedMin}–{NewsFeedMax}s" : null,
                    WatchVideo ? $"Reels {WatchVideoMin}–{WatchVideoMax}s" : null,
                    Marketplace ? $"Marketplace {MarketplaceMin}–{MarketplaceMax}s" : null,
                    LikePost
                        ? $"Up to {LikePostMin}–{LikePostMax} likes across the session"
                        : "No likes",
                }.Where(x => x != null)
            );

    public static ScreenTimeSetting FromConfig(AutoPostConfig c) =>
        new()
        {
            Options = (c.ScreenTimeOptions ?? new()).Snapshot(),
            WatchVideo = c.IsWatchVideo,
            WatchVideoMin = c.WatchVideoMin,
            WatchVideoMax = c.WatchVideoMax,
            NewsFeed = c.IsNewsFeed,
            NewsFeedMin = c.NewsFeedMin,
            NewsFeedMax = c.NewsFeedMax,
            Marketplace = c.IsMarketPlace,
            MarketplaceMin = c.MarketPlaceMin,
            MarketplaceMax = c.MarketPlaceMax,
            LikePost = c.IsLikePost,
            LikePostMin = c.LikePostMin,
            LikePostMax = c.LikePostMax,
            ReadNotification = c.IsReadNotification,
            ClearRecentApp = c.IsClearRecentApp,
            CheckIn = c.IsCheckIn,
            CheckInLocation = c.CheckInLocation,
            AddFriendSuggest = c.AddFriendSuggest,
            AddFriendSuggestMin = c.AddFriendSuggestMin,
            AddFriendSuggestMax = c.AddFriendSuggestMax,
            AcceptFriends = c.AcceptFriends,
            AcceptFriendsMin = c.AcceptFriendsMin,
            AcceptFriendsMax = c.AcceptFriendsMax,
            DeleteFriendRequest = c.DeleteFriendRequest,
            DeleteFriendRequestMin = c.DeleteFriendRequestMin,
            DeleteFriendRequestMax = c.DeleteFriendRequestMax,
        };
}
