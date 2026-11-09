using System;
using System.Globalization;

namespace _855Media.Core.Resolving;

public sealed class InstagramRateLimitException(string message)
    : InvalidOperationException(message);

// Shared by feed capture and the post reader for one browser session. Navigation
// and Clear do not cancel a server-requested cooldown. No automatic retry occurs.
public sealed class InstagramRequestCooldown(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public DateTimeOffset RetryAt { get; private set; }
    public bool IsActive => RetryAt > _clock.GetUtcNow();
    public string Message =>
        "Instagram is limiting requests (HTTP 429). Import is paused. "
        + $"Try Import again after {RetryAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}. "
        + "Collected links and completed downloads are retained; unfinished items will be retried."
        + " This is an earliest retry time, not a guarantee Instagram will allow access.";

    public void Observe(string? retryAfter)
    {
        var now = _clock.GetUtcNow();
        var next = now.AddMinutes(15); // Conservative pause when no usable header exists.
        if (
            long.TryParse(
                retryAfter,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds
            )
            && seconds >= 0
        )
            next = now.AddSeconds(
                Math.Min(seconds, (long)(DateTimeOffset.MaxValue - now).TotalSeconds)
            );
        else if (
            DateTimeOffset.TryParse(
                retryAfter,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date
            )
            && date > now
        )
            next = date;
        // Zero/past dates must not cause an immediate retry storm.
        if (next <= now)
            next = now.AddMinutes(1);
        if (next > RetryAt)
            RetryAt = next;
    }

    public void ThrowIfActive()
    {
        if (IsActive)
            throw new InstagramRateLimitException(Message);
    }
}
