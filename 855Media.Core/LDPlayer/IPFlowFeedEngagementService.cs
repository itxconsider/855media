using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Domain service interface for Facebook feed engagement, natural human scrolling kinetics,
/// video/reels feed navigation, and post interactions on LDPlayer instances.
/// </summary>
public interface IPFlowFeedEngagementService
{
    /// <summary>
    /// Finds and taps the Like / Reaction button on the currently visible Facebook newsfeed post.
    /// </summary>
    Task<bool> LikeCurrentFeedPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Performs an authentic, human-like finger scroll transition on the Facebook newsfeed.
    /// Emulates natural thumb flick kinetics (momentum glide), reading drags, micro-adjustments,
    /// thumb arc curvature/drift, and returns the recommended dwell time in ms.
    /// </summary>
    Task<int> ScrollNewsFeedLikeHumanAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Finds and taps the Like button on the currently playing Facebook video/reel.
    /// </summary>
    Task<bool> LikeCurrentVideoPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Performs an upward swipe gesture specifically tuned for video/reels feeds to advance to the next video,
    /// ensuring any blocking comment overlays or modal sheets are dismissed and swiping with sufficient travel
    /// and duration to reliably trigger ViewPager page switching without snapping back.
    /// </summary>
    Task<bool> ScrollToNextVideoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Verifies that the instance is currently on the feed or active screen,
    /// dismissing any intrusive bottom sheets, comment trays, browser popups, or story overlays.
    /// </summary>
    Task<bool> EnsureOnFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );
}
