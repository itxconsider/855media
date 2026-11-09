using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal sealed class PFlowConfirmationRecovery
{
    internal bool Attempted { get; private set; }

    internal bool AcceptsEvidence(string? evidence) =>
        evidence != null
        && (
            !Attempted
            || !evidence.StartsWith(PFlowPublicationCycle.EvidencePrefix, StringComparison.Ordinal)
        );

    internal async Task<bool> TryAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? clearApps = null,
        Func<CancellationToken, Task<bool>>? refresh = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (Attempted || job.State != PFlowState.Submitted || !job.SubmittedUtc.HasValue)
            return false;
        Attempted = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            await PFlowJobBindingGuard.RequireAsync(ld, job, path, deadline.Token);
            var foreground = await ld.GetCurrentFocusedActivityAsync(
                job.DeviceIndex,
                path,
                deadline.Token
            );
            var xml = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, deadline.Token) ?? "";
            if (
                !System.Text.RegularExpressions.Regex.IsMatch(
                    foreground ?? "",
                    @"(?<![\w.])com\.facebook\.katana/"
                )
                || !PFlowProfilePublicationVerifier.Safe(xml)
                || PFlowPublicationFailure.Read(xml, job.Caption) != null
                || PFlowAutoPostService.IsComposer(xml, job.PostType)
            )
                return false;
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "publication.timeout.recovery",
                "Confirmation timed out; clearing recent apps and reopening Home once. Existing submission only; no Share retry.",
                deadline.Token
            );
            await (
                clearApps
                ?? (token => PFlowSession.ClearAppsAsync(ld, job.DeviceIndex, path, token))
            )(deadline.Token);
            await PFlowJobBindingGuard.RequireAsync(ld, job, path, deadline.Token);
            if (!await ld.LaunchAppAsync(job.DeviceIndex, job.AppPackage, path, deadline.Token))
                return false;
            if (refresh != null)
                return await refresh(deadline.Token);
            if (
                !await ld.OpenDeepLinkAsync(
                    job.DeviceIndex,
                    "fb://feed",
                    job.AppPackage,
                    path,
                    deadline.Token
                )
            )
                return false;
            for (var poll = 0; poll < 20; poll++)
            {
                await Task.Delay(500, deadline.Token);
                await PFlowJobBindingGuard.RequireAsync(ld, job, path, deadline.Token);
                var activity =
                    await ld.GetCurrentFocusedActivityAsync(job.DeviceIndex, path, deadline.Token)
                    ?? "";
                var home =
                    await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, deadline.Token) ?? "";
                if (
                    System.Text.RegularExpressions.Regex.IsMatch(
                        activity,
                        @"(?<![\w.])com\.facebook\.katana/"
                    )
                    && PFlowProfilePublicationVerifier.Safe(home)
                    && FacebookLayoutRegistry.Match(home, FacebookScreenKind.NewsFeed).IsMatch
                )
                    return true;
            }
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "publication.timeout.recovery-failed",
                "Confirmation recovery unavailable; publication still requires proof.",
                ct
            );
            return false;
        }
    }
}
