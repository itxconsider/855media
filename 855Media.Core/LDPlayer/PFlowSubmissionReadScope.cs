using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed class PFlowUiReaderUnavailableException(string message)
    : InvalidOperationException(message);

// Invocation-local, read-only retry permission for one existing submission.
// This never restores actor/input proof, restarts Facebook or retries Share.
internal sealed class PFlowSubmissionReadScope : IDisposable
{
    private static readonly AsyncLocal<PFlowSubmissionReadScope?> Current = new();
    private readonly PFlowSubmissionReadScope? previous;
    private readonly ILDPlayerService ld;
    private readonly PFlowJob job;
    private readonly string? path;
    private readonly PFlowNativeIdentityScope identity;
    private readonly PFlowPostTargetSession.Target target;
    private readonly string jobId,
        caption,
        sha;
    private readonly DateTime? submitted;
    private readonly PFlowSchedule? schedule;
    private readonly Func<PFlowJob, Task>? checkpoint;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Stopwatch budget = new();
    private int attempts;
    private bool disposed;

    internal static PFlowSubmissionReadScope? Begin(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        Func<PFlowJob, Task>? checkpoint = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        if (
            job.State != PFlowState.Submitted
            || job.SubmittedUtc == null
            || job.NativeIdentityScope is not { UsesDatabaseName: true } identity
            || !identity.Matches(job)
        )
            return null;
        return new(ld, job, path, identity, checkpoint, delay ?? Task.Delay);
    }

    private PFlowSubmissionReadScope(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        PFlowNativeIdentityScope identity,
        Func<PFlowJob, Task>? checkpoint,
        Func<TimeSpan, CancellationToken, Task> delay
    )
    {
        this.ld = ld;
        this.job = job;
        this.path = path;
        this.identity = identity;
        this.checkpoint = checkpoint;
        this.delay = delay;
        target = PFlowPostTargetSession.Target.Read(job);
        jobId = job.JobId;
        caption = job.Caption;
        sha = job.Sha256;
        submitted = job.SubmittedUtc;
        schedule = job.Schedule;
        previous = Current.Value;
        Current.Value = this;
    }

    private async Task RequireSameSubmission(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (
            disposed
            || job.JobId != jobId
            || job.SubmittedUtc != submitted
            || job.Caption != caption
            || job.Sha256 != sha
            || job.Schedule != schedule
            || PFlowPostTargetSession.Target.Read(job) != target
            || !ReferenceEquals(job.NativeIdentityScope, identity)
            || !await identity.ObserveSubmittedSessionAsync(job, ld, path, ct)
        )
            throw new InvalidOperationException(
                "Existing submission's process, foreground or target changed during UI-reader recovery. Review this post before continuing."
            );
        await PFlowJobBindingGuard.RequireAsync(ld, job, path, ct);
    }

    internal static Task<string?> ReadAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        Func<CancellationToken, Task<string?>> read,
        CancellationToken ct
    )
    {
        var scope = Current.Value;
        return
            scope is { disposed: false }
            && ReferenceEquals(scope.ld, ld)
            && scope.target.Device == index
            && scope.path == path
            ? scope.ReadCoreAsync(read, ct)
            : read(ct);
    }

    private async Task<string?> ReadCoreAsync(
        Func<CancellationToken, Task<string?>> read,
        CancellationToken ct
    )
    {
        try
        {
            return await read(ct);
        }
        catch (PFlowUiReaderUnavailableException) { }
        if (!budget.IsRunning)
        {
            budget.Start();
            if (target.Binding != null)
                PFlowUiReaderEpoch.Invalidate(target.Binding.InstanceIdentity);
        }
        var remaining = TimeSpan.FromSeconds(30) - budget.Elapsed;
        if (attempts >= 3 || remaining <= TimeSpan.Zero)
            throw new PFlowUiReaderUnavailableException(
                "UI reader remains unavailable after bounded submission monitoring; review the existing post before retrying."
            );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(remaining);
        try
        {
            var stable = 0;
            string? xml = null;
            while (attempts < 3)
            {
                await RequireSameSubmission(timeout.Token);
                attempts++;
                await PFlowTrace.SessionAsync(
                    job.DeviceIndex,
                    "publication.reader.wait",
                    $"attempt={attempts}; maximum=3; budgetSeconds=30; existingSubmissionOnly=true; noRestart=true; noResubmit=true",
                    timeout.Token
                );
                job.CurrentStep =
                    "Waiting for Facebook UI reader; monitoring existing submission only";
                if (checkpoint != null)
                    await checkpoint(job);
                await delay(TimeSpan.FromSeconds(1), timeout.Token);
                try
                {
                    xml = await read(timeout.Token);
                }
                catch (PFlowUiReaderUnavailableException)
                {
                    stable = 0;
                    continue;
                }
                await RequireSameSubmission(timeout.Token);
                if (xml == null || ScreenTimeHierarchy.Nodes(xml).Length == 0)
                {
                    stable = 0;
                    continue;
                }
                if (!PFlowProfilePublicationVerifier.Safe(xml))
                    throw new InvalidOperationException(
                        "Facebook context changed while its reader was unavailable. Review the existing submission."
                    );
                var name = PFlowPostingNameVerification.ActiveHeaderName(xml);
                if (name != null && !PFlowNameMatch.Same(name, job.TargetName))
                    throw new InvalidOperationException(
                        "Active actor changed during submission monitoring. Review the existing post."
                    );
                // Errors must be handled by the existing confirmation loop, never swallowed.
                if (PFlowPublicationFailure.Read(xml, job.Caption) != null || ++stable >= 2)
                {
                    job.CurrentStep = "UI reader recovered; checking existing publication";
                    if (checkpoint != null)
                        await checkpoint(job);
                    return xml;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PFlowUiReaderUnavailableException(
                "UI-reader monitoring deadline expired; review the existing submission before continuing."
            );
        }
        throw new PFlowUiReaderUnavailableException(
            "UI reader remains unavailable after three guarded reads; review the existing post before retrying."
        );
    }

    public void Dispose()
    {
        disposed = true;
        if (ReferenceEquals(Current.Value, this))
            Current.Value = previous;
    }
}
