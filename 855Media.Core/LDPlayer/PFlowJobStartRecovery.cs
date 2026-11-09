using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Navigation recovery only. A recognized Feed never establishes the posting actor.
public static class PFlowJobStartRecovery
{
    public sealed record Result(string Xml, bool Recovered);

    public static bool NeedsRecovery(string? xml, string package, string? targetName = null)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            package != "com.facebook.katana"
            || nodes.Length == 0
            || !nodes.Any(n => n.Attribute("package")?.Value == package)
            || nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } p
                && p != package
                && p != "com.android.systemui"
            )
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowIdentitySwitchLayout.IsSwitching(xml)
            || (PFlowUiPrivacy.ContainsInput(xml) && !PFlowMetaAccountLayout.IsSettings(xml))
        )
            return false;
        return FacebookLayoutRegistry.Match(xml, context: new(TargetName: targetName)).Status
            == FacebookLayoutStatus.Unknown;
    }

    public static bool HasPendingPublication(
        System.Collections.Generic.IEnumerable<PFlowJob> jobs,
        int index
    ) =>
        jobs.Any(j =>
            j.DeviceIndex == index
            && (
                j.State is PFlowState.SubmitRequested or PFlowState.Submitted or PFlowState.Unknown
                || j.State == PFlowState.Scheduled && !j.ScheduledVerified
                || j.State == PFlowState.Failed && j.RequiresPublicationReviewBeforeCleanup
            )
        );

    public static void RequireNoPendingPublication(
        System.Collections.Generic.IEnumerable<PFlowJob> jobs,
        int index
    )
    {
        var pending = jobs.FirstOrDefault(j =>
            j.DeviceIndex == index && HasPendingPublication([j], index)
        );
        if (pending != null)
            throw new InvalidOperationException(
                $"LD-{index} has an unconfirmed submission for {pending.TargetName} (job {pending.JobId}). Review its publication outcome in History before continuing. Facebook was left open; no restart or further submission was attempted."
            );
    }

    public static async Task<Result> RecoverAsync(
        ILDPlayerService ld,
        int index,
        string package,
        string? path,
        string xml,
        bool publicationPending,
        CancellationToken ct,
        PFlowJob? job = null,
        string? targetName = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (
            publicationPending
            || job?.SubmittedUtc != null
            || job?.State
                is PFlowState.SubmitRequested
                    or PFlowState.Submitted
                    or PFlowState.Unknown
                    or PFlowState.Confirmed
            || !NeedsRecovery(xml, package, targetName)
        )
            return new(xml, false);
        delay ??= Task.Delay;
        var activity = await ld.GetCurrentFocusedActivityAsync(index, path, ct) ?? "";
        if (!activity.StartsWith(package + "/", StringComparison.Ordinal))
            return new(xml, false);
        await PFlowTrace.SessionAsync(
            index,
            "job.start.feed-recovery",
            "unknownStartingScreen=true; submitted=false; freshIdentityRequired=true",
            ct
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var stableFrames = 0;
        var lastDiagnostic = "";
        try
        {
            if (!await ld.OpenDeepLinkAsync(index, "fb://feed", package, path, token))
                throw new InvalidOperationException(
                    "Could not return the starting Facebook screen to Feed; job was not started."
                );
            for (var attempt = 1; attempt <= 60; attempt++)
            {
                token.ThrowIfCancellationRequested();
                await delay(TimeSpan.FromMilliseconds(500), token);
                activity = await ld.GetCurrentFocusedActivityAsync(index, path, token) ?? "";
                if (!activity.StartsWith(package + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Facebook left the foreground during starting-screen recovery; job was not started."
                    );
                xml = await ld.DumpUiHierarchyAsync(index, path, token) ?? "";
                xml =
                    await PFlowNavigationPrompts.DismissAsync(ld, index, xml, path, token, delay)
                    ?? "";
                if (
                    ScreenTimeHierarchy
                        .Nodes(xml)
                        .Any(n =>
                            n.Attribute("package")?.Value is { Length: > 0 } p
                            && p != package
                            && p != "com.android.systemui"
                        )
                )
                    throw new InvalidOperationException(
                        "Another app or system dialog appeared during starting-screen recovery; job was not started."
                    );
                var authentication = PFlowLoginScreen.Classify(xml);
                if (PFlowLoginScreen.RequiresAuthentication(authentication))
                    throw new InvalidOperationException(PFlowLoginScreen.Message(authentication));
                var input = PFlowUiPrivacy.ContainsInput(xml);
                var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed);
                stableFrames = !input && match.IsMatch ? stableFrames + 1 : 0;
                var diagnostic = $"input={input}; {match.Diagnostic}";
                if (diagnostic != lastDiagnostic || attempt % 10 == 0)
                {
                    await PFlowTrace.SessionAsync(
                        index,
                        "job.start.feed-wait",
                        $"attempt={attempt}/60; stableFrames={stableFrames}/2; {diagnostic}",
                        token
                    );
                    lastDiagnostic = diagnostic;
                }
                if (stableFrames < 2)
                    continue;
                activity = await ld.GetCurrentFocusedActivityAsync(index, path, token) ?? "";
                if (!activity.StartsWith(package + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Facebook left the foreground during starting-screen recovery; job was not started."
                    );
                token.ThrowIfCancellationRequested();
                await PFlowTrace.SessionAsync(
                    index,
                    "job.start.feed-ready",
                    "feedRecognized=true; stableFrames=2; identityVerified=false",
                    token
                );
                return new(xml, true);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        ct.ThrowIfCancellationRequested();
        await PFlowDiagnostics.CaptureHierarchyAsync(index, "starting-feed-timeout", xml, ct);
        await PFlowTrace.SessionAsync(index, "job.start.feed-timeout", lastDiagnostic, ct);
        throw new InvalidOperationException(
            "Facebook Feed was not recognized in two stable observations during the bounded 30-second starting-screen recovery; no post was submitted."
        );
    }
}
