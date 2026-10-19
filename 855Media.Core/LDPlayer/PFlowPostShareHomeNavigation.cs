using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Both composer routes meet at the same durable Submitted boundary.
internal sealed class PFlowPostShareHomeNavigation
{
    private bool _attempted;

    internal bool CanNavigate(PFlowJob job, string xml) =>
        !_attempted
        && job.PostType == "Reel"
        && job.State == PFlowState.Submitted
        && job.SubmittedUtc.HasValue
        && PFlowProfilePublicationVerifier.Safe(xml)
        && !PFlowAutoPostService.IsComposer(xml, job.PostType)
        && !PFlowAutoPostService.HasComposerLayoutEvidence(xml, job.PostType);

    internal async Task<bool> TryAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string xml,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!CanNavigate(job, xml))
            return false;
        _attempted = true;
        if (FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "publication.home-after-share",
                "Home feed already visible; continuing publication monitoring.",
                ct
            );
            return false;
        }
        try
        {
            var acknowledged = await ld.OpenDeepLinkAsync(
                job.DeviceIndex,
                "fb://feed",
                job.AppPackage,
                path,
                ct
            );
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "publication.home-after-share",
                $"Home navigation requested after Share; acknowledged={acknowledged}; publicationConfirmed=false",
                ct
            );
            await (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(400), ct);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or IOException && !ct.IsCancellationRequested)
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "publication.home-after-share-unverified",
                $"Home navigation unavailable ({ex.GetType().Name}); continuing monitoring without resubmitting.",
                ct
            );
        }
        // Even an unacknowledged navigation can change the screen. Read again.
        return true;
    }
}
