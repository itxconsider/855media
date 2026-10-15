using System;

namespace _855Media.Core.LDPlayer;

internal readonly record struct ScreenTimeGesture(
    int HoldMs,
    int MovementTargetMs,
    bool ContinuousSwipe = false
)
{
    // The top-tab viewer ignores the separate ATX touch events on tested LDs.
    // A single Android swipe advances it; keep the hold/move profiles as fallbacks.
    internal static ScreenTimeGesture ReelsScrollAttempt(int attempt, bool topNavigation) =>
        topNavigation && attempt == 0
            ? new(0, 350, true)
            : ReelsAttempt(attempt - (topNavigation ? 1 : 0));

    internal static ScreenTimeGesture Reels => new(300, 100);

    internal static ScreenTimeGesture ReelsAttempt(int attempt) =>
        attempt switch
        {
            0 => Reels,
            1 => new(200, 50),
            2 => new(100, 75),
            _ => throw new ArgumentOutOfRangeException(nameof(attempt)),
        };

    internal static ScreenTimeGesture NewsFeed(int choice) =>
        choice switch
        {
            0 => new(100, 300),
            1 => new(300, 100),
            _ => throw new ArgumentOutOfRangeException(nameof(choice)),
        };
}
