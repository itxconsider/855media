using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class InstagramCooldownTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("Mon, 09 Nov 2026 12:05:00 GMT", 300)]
    [InlineData(null, 900)]
    [InlineData("invalid", 900)]
    [InlineData("-5", 900)]
    [InlineData("0", 60)]
    public void HonorsRetryAfterWithoutRetryingEarly(string? header, int seconds)
    {
        var clock = new Clock();
        var cooldown = new InstagramRequestCooldown(clock);
        cooldown.Observe(header);
        Assert.Equal(clock.Now.AddSeconds(seconds), cooldown.RetryAt);
        Assert.Throws<InstagramRateLimitException>(cooldown.ThrowIfActive);
        clock.Now = clock.Now.AddSeconds(seconds);
        cooldown.ThrowIfActive();
        Assert.False(cooldown.IsActive);
    }

    [Fact]
    public void LaterSignalsNeverShortenAnExistingCooldown()
    {
        var cooldown = new InstagramRequestCooldown(new Clock());
        cooldown.Observe("3600");
        var deadline = cooldown.RetryAt;
        cooldown.Observe("10");
        Assert.Equal(deadline, cooldown.RetryAt);
        Assert.Contains("retained", cooldown.Message);
    }
}
