using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Additional UI settling time for discovery only, not a bypass for platform limits.
public static class PFlowFetchPacing
{
    public const int ExtraStepMilliseconds = 500;

    public static TimeSpan WithPause(TimeSpan existingDelay)
    {
        if (existingDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(existingDelay));
        return existingDelay + TimeSpan.FromMilliseconds(ExtraStepMilliseconds);
    }

    public static Task DelayAsync(TimeSpan existingDelay, CancellationToken ct) =>
        Task.Delay(WithPause(existingDelay), ct);

    public static Task DelayAsync(int existingMilliseconds, CancellationToken ct) =>
        DelayAsync(TimeSpan.FromMilliseconds(existingMilliseconds), ct);
}
