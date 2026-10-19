using System;

namespace _855Media.Core.LDPlayer;

internal readonly record struct ScreenTimeGesture(
    int HoldMs,
    int MovementTargetMs,
    bool ContinuousSwipe = false
)
{
    // Prefer the single upward ADB drag validated across current LD layouts.
    // Keep the separate ATX hold/move profiles as bounded fallbacks.
    internal static ScreenTimeGesture ReelsScrollAttempt(int attempt, bool topNavigation) =>
        attempt == 0 ? new(0, 450, true) : ReelsAttempt(attempt - 1);

    internal static int ReelWatchSeconds(ScreenTimeOptions options) =>
        Random.Shared.Next(options.ReelDwellMinSeconds, options.ReelDwellMaxSeconds + 1);

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
