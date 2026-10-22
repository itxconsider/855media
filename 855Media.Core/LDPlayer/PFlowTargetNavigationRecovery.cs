using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Raised only after Graph matched and the unchanged session remained on a safe Feed.
public sealed class PFlowTargetNavigationException : InvalidOperationException
{
    public PFlowTargetNavigationException()
        : base(
            "Fresh Graph identity matched, but own-profile navigation did not finish. No work started."
        ) { }
}

public static class PFlowTargetNavigationRecovery
{
    public static bool IsSafeFeed(string xml, FacebookPageTarget target) =>
        PFlowSessionProfileUi.Safe(xml)
        && !PFlowSessionProfileUi.Contradicts(xml, target)
        && !PFlowUiPrivacy.ContainsInput(xml)
        && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch;

    public static async Task<bool> RunAsync(
        PFlowJob? job,
        Func<Task<bool>> verify,
        Func<Task> restartAndReverifyAccount,
        CancellationToken ct
    )
    {
        try
        {
            return await verify();
        }
        catch (PFlowTargetNavigationException)
            when (job != null
                && job.State == PFlowState.Reserved
                && !job.SubmittedUtc.HasValue
                && !job.MayHaveSubmitted
                && !job.TargetIdentityConfirmationRequired
                && !ct.IsCancellationRequested
            )
        {
            ct.ThrowIfCancellationRequested();
            job.NativeIdentityScope = null;
            job.TargetContinuation = null;
            await restartAndReverifyAccount();
            ct.ThrowIfCancellationRequested();
            return await verify(); // Exactly one attempt; a second failure escapes.
        }
    }
}
