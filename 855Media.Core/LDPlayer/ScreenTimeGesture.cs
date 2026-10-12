using System;

namespace _855Media.Core.LDPlayer;

internal readonly record struct ScreenTimeGesture(int HoldMs, int MovementTargetMs)
{
    internal static ScreenTimeGesture Reels => new(300, 100);

    internal static ScreenTimeGesture NewsFeed(int choice) =>
        choice switch
        {
            0 => new(100, 300),
            1 => new(300, 100),
            _ => throw new ArgumentOutOfRangeException(nameof(choice)),
        };
}
