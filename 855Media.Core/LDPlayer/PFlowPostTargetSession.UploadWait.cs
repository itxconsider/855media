using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowPostTargetSession
{
    public bool CanReuseConfirmedTarget(PFlowJob job) =>
        _previous != null
        && _previous.JobId != job.JobId
        && _previous.State == PFlowState.Confirmed
        && _previous.ConfirmedUtc.HasValue
        && !string.IsNullOrWhiteSpace(_previous.Evidence)
        && _target == Target.Read(job)
        && job.AccountBinding != null
        && !string.IsNullOrWhiteSpace(job.BatchId)
        && SupportsContinuation(_previous)
        && SupportsContinuation(job);

    // Preserve the in-memory proof while an old upload label settles. This
    // authorizes waiting only; VerifyAsync must still authorize the next job.
    public async Task<string> WaitForConfirmedUploadClearAsync(
        PFlowJob job,
        string initialXml,
        Func<CancellationToken, Task<string>> observe,
        Func<string, CancellationToken, Task<bool>> verifySession,
        Func<CancellationToken, Task<bool>> uploadsInactive,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null
    )
    {
        delay ??= Task.Delay;
        now ??= () => DateTimeOffset.UtcNow;
        var deadline = now().AddSeconds(30);
        var generation = _generation;
        var xml = initialXml;
        var clearFrames = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var token = budget.Token;
        try
        {
            for (var poll = 0; poll < 64 && now() < deadline; poll++)
            {
                token.ThrowIfCancellationRequested();
                if (
                    generation != _generation
                    || !CanReuseConfirmedTarget(job)
                    || job.State != PFlowState.Reserved
                    || job.MayHaveSubmitted
                    || !CanContinueContext(xml, job.AppPackage)
                    || !await verifySession(xml, token)
                )
                    throw new InvalidOperationException(
                        "Confirmed Page session changed while waiting for the previous upload. No new post was started."
                    );
                if (CanContinueForJob(job, xml) && await uploadsInactive(token))
                    clearFrames++;
                else
                    clearFrames = 0;
                token.ThrowIfCancellationRequested();
                if (
                    generation != _generation
                    || !CanReuseConfirmedTarget(job)
                    || job.State != PFlowState.Reserved
                    || job.MayHaveSubmitted
                )
                    throw new InvalidOperationException(
                        "Posting target changed during the previous-upload wait."
                    );
                if (clearFrames >= 2 && now() < deadline)
                    return xml;
                await delay(TimeSpan.FromMilliseconds(500), token);
                xml = await observe(token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The previous-upload check timed out; no new post was started."
            );
        }
        throw new InvalidOperationException(
            "The previous upload has not cleared or its status is unavailable. No new post was started; retry after Facebook finishes."
        );
    }
}
