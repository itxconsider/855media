using System;
using System.Linq;

namespace _855Media.Core.LDPlayer;

internal sealed class PFlowPublicationSettleGate(TimeSpan? quietPeriod = null)
{
    private TimeSpan? _clearSince;
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _quietPeriod = quietPeriod ?? QuietPeriod;

    internal bool Observe(string xml, TimeSpan elapsed)
    {
        if (
            !PFlowProfilePublicationVerifier.Safe(xml)
            || PFlowConfirmation.HasPendingPublication(xml)
            || PFlowAutoPostService.HasComposerLayoutEvidence(xml, "Reel")
            || ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n =>
                    (n.Attribute("class")?.Value ?? "").Contains(
                        "ProgressBar",
                        StringComparison.Ordinal
                    )
                )
        )
        {
            _clearSince = null;
            return false;
        }
        _clearSince ??= elapsed;
        return elapsed - _clearSince.Value >= _quietPeriod;
    }

    internal void Reset() => _clearSince = null;
}
