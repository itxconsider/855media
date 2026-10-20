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
            && j.State is PFlowState.SubmitRequested or PFlowState.Submitted or PFlowState.Unknown
        );

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
        if (!await ld.OpenDeepLinkAsync(index, "fb://feed", package, path, ct))
            throw new InvalidOperationException(
                "Could not return the starting Facebook screen to Feed; job was not started."
            );
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await delay(TimeSpan.FromMilliseconds(500), ct);
            activity = await ld.GetCurrentFocusedActivityAsync(index, path, ct) ?? "";
            if (!activity.StartsWith(package + "/", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Facebook left the foreground during starting-screen recovery; job was not started."
                );
            xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
            xml = await PFlowNavigationPrompts.DismissAsync(ld, index, xml, path, ct, delay) ?? "";
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
            if (
                !PFlowUiPrivacy.ContainsInput(xml)
                && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
            )
            {
                await PFlowTrace.SessionAsync(
                    index,
                    "job.start.feed-ready",
                    "feedRecognized=true; identityVerified=false",
                    ct
                );
                return new(xml, true);
            }
        }
        throw new InvalidOperationException(
            "Facebook Feed was not recognized within 10 seconds after starting-screen recovery; no post was submitted."
        );
    }
}
