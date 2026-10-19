using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeReelWatchTests
{
    [Fact]
    public void NewAndLegacySavedOptionsUseEightToThirtySecondRange()
    {
        var options = JsonSerializer.Deserialize<ScreenTimeOptions>("{\"ReelDwellSeconds\":5}")!;
        Assert.Equal(8, options.ReelDwellMinSeconds);
        Assert.Equal(30, options.ReelDwellMaxSeconds);
        var copy = options.Snapshot();
        Assert.Equal(8, copy.ReelDwellMinSeconds);
        Assert.Equal(30, copy.ReelDwellMaxSeconds);
        for (var i = 0; i < 1000; i++)
            Assert.InRange(ScreenTimeGesture.ReelWatchSeconds(copy), 8, 30);
    }

    [Fact]
    public void CustomRangePersistsAndRejectsReversedBounds()
    {
        var options = new ScreenTimeOptions { ReelDwellMinSeconds = 12, ReelDwellMaxSeconds = 12 };
        var copy = options.Snapshot();
        Assert.Equal(12, ScreenTimeGesture.ReelWatchSeconds(copy));
        copy.ReelDwellMinSeconds = 31;
        copy.ReelDwellMaxSeconds = 30;
        Assert.Contains(
            new ScreenTimeSetting { Options = copy }.Validate(),
            e => e.StartsWith("Reel dwell seconds")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryReelLayoutUsesAdbFirstAndBoundedAtxFallbacks(bool topNavigation)
    {
        Assert.Equal(
            new ScreenTimeGesture(0, 450, true),
            ScreenTimeGesture.ReelsScrollAttempt(0, topNavigation)
        );
        for (var i = 0; i < 3; i++)
            Assert.Equal(
                ScreenTimeGesture.ReelsAttempt(i),
                ScreenTimeGesture.ReelsScrollAttempt(i + 1, topNavigation)
            );
    }
}
