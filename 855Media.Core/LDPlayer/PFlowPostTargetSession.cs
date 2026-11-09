using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// In-memory target session for one device and batch; persisted history cannot restore it.
public sealed partial class PFlowPostTargetSession
{
    private PFlowJob? _previous;
    private Target? _target;
    private int _generation;

    internal sealed record Target(
        PFlowAccountBinding? Binding,
        int Device,
        string Package,
        string Batch,
        string Owner,
        string Id,
        string Persona,
        string Name,
        bool Profile
    )
    {
        internal static Target Read(PFlowJob job) =>
            new(
                job.AccountBinding,
                job.DeviceIndex,
                job.AppPackage,
                job.BatchId,
                job.OwnerUid,
                job.TargetId,
                job.PersonaUid,
                job.TargetName,
                job.IsProfile
            );
    }

    internal sealed record Continuation(string JobId, Target Target, DateTime VerifiedUtc);

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _previous = null;
        _target = null;
    }

    public async Task<bool> VerifyAsync(
        PFlowJob job,
        Func<CancellationToken, Task<string>> verifyLiveUid,
        Func<CancellationToken, Task<bool>> openOwnProfile,
        Func<CancellationToken, Task<string>> observe,
        CancellationToken ct,
        string? initialXml = null,
        bool allowFeedAfterLiveUid = false,
        Func<CancellationToken, Task>? verifyContinuationSession = null
    )
    {
        var target = Target.Read(job);
        var generation = _generation;
        var consecutive = CanReuseConfirmedTarget(job);
        _previous = null;
        _target = null;
        job.TargetContinuation = null;
        ct.ThrowIfCancellationRequested();
        var xml = initialXml ?? (consecutive ? await observe(ct) : "");
        var continued =
            consecutive
            && generation == _generation
            && Target.Read(job) == target
            && CanContinueForJob(job, xml);
        if (continued)
        {
            if (verifyContinuationSession != null)
                await verifyContinuationSession(ct);
            ct.ThrowIfCancellationRequested();
            if (generation != _generation || Target.Read(job) != target)
                throw new InvalidOperationException(
                    "Target session changed during continuation verification."
                );
            job.TargetContinuation = new(job.JobId, target, DateTime.UtcNow);
        }
        else
        {
            var method = await verifyLiveUid(ct);
            ct.ThrowIfCancellationRequested();
            if (
                method is not ("Graph UID" or "UI UID lookup" or "Session UI" or "Database UI name")
                || method == "Session UI"
                    && (
                        job.NativeIdentityScope == null
                        || job.NativeIdentityScope.UsesDatabaseName
                        || !job.NativeIdentityScope.Matches(job)
                    )
                || method == "Database UI name"
                    && (
                        job.NativeIdentityScope is not { UsesDatabaseName: true }
                        || !job.NativeIdentityScope.Matches(job)
                    )
            )
                throw new InvalidOperationException(
                    "Fresh target identity or database-name/UI verification was not confirmed."
                );
            if (Target.Read(job) != target)
                throw new InvalidOperationException(
                    "Posting target changed during identity verification."
                );
            var nameOnly = method == "Database UI name";
            var afterUid = allowFeedAfterLiveUid || nameOnly ? await observe(ct) : "";
            if (
                (allowFeedAfterLiveUid || nameOnly)
                && CanContinueForJob(job, afterUid)
                && (
                    !nameOnly
                    || !PFlowPostingNameVerification.Contradicts(
                        afterUid,
                        new FacebookPageTarget
                        {
                            PageName = job.TargetName,
                            Type = job.IsProfile ? "Profile" : "Page",
                        }
                    )
                )
                && (
                    nameOnly
                    || FacebookLayoutRegistry.Match(afterUid, FacebookScreenKind.NewsFeed).IsMatch
                )
            )
            {
                job.TargetContinuation = new(job.JobId, target, DateTime.UtcNow);
                xml = afterUid;
            }
            else
            {
                if (nameOnly)
                    throw new InvalidOperationException(
                        "Selected target name/type matched, but Facebook is not on a safe posting screen."
                    );
                if (!await openOwnProfile(ct))
                    throw new InvalidOperationException(
                        method == "Database UI name"
                            ? "Target name matched the database, but the own-profile screen could not be confirmed. No post was submitted."
                            : "Live UID was verified, but the target profile screen could not be confirmed. No post was submitted."
                    );
                xml = await observe(ct);
                if (!HasOwnProfile(job, xml))
                    throw new InvalidOperationException(
                        method == "Database UI name"
                            ? "Target name matched the database, but the expected own-profile controls were not visible. No post was submitted."
                            : "Live UID was verified, but the expected own-profile controls were not visible. No post was submitted."
                    );
            }
        }
        ct.ThrowIfCancellationRequested();
        if (Target.Read(job) != target)
        {
            job.TargetContinuation = null;
            throw new InvalidOperationException(
                "Posting target changed during profile verification."
            );
        }
        _previous = job;
        _target = target;
        return true;
    }

    private static bool SupportsContinuation(PFlowJob job) =>
        job.PostType is "Reel" or "Video" or "Picture" or "Story";

    internal static bool HasOwnProfile(PFlowJob job, string xml) =>
        !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowIdentitySwitchLayout.IsSwitching(xml)
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        && PFlowAutoPostService.ComposerMatchesTarget(xml, job.TargetName)
        && !xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
        && (
            job.IsProfile
                ? xml.Contains("Edit profile", StringComparison.OrdinalIgnoreCase)
                : xml.Contains("Professional dashboard", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Business Dashboard", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Creator Dashboard", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Manage Page", StringComparison.OrdinalIgnoreCase)
        );

    internal static bool CanContinueForJob(PFlowJob job, string xml) =>
        CanContinueContext(xml, job.AppPackage)
        && (
            !PFlowConfirmation.HasPendingPublication(xml)
            || job.BackgroundCopyright?.Allows(xml) == true
        );

    internal static bool CanContinueFrom(string xml, string package) =>
        !PFlowConfirmation.HasPendingPublication(xml) && CanContinueContext(xml, package);

    internal static bool CanContinueContext(string xml, string package)
    {
        using var scope = FacebookHierarchyCache.Enter();
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        // The verified session identifies the actor. This capture need not be a profile:
        // a completed upload can leave Feed or a Reel viewer open. No taps use this layout;
        // the share chooser and the correct composer are verified independently afterward.
        return package == "com.facebook.katana"
            && nodes.Any(n => n.Attribute("package")?.Value == package)
            && nodes.All(n =>
                string.IsNullOrEmpty(n.Attribute("package")?.Value)
                || n.Attribute("package")?.Value == package
                || n.Attribute("package")?.Value == "com.android.systemui"
            )
            && !PFlowUiPrivacy.ContainsInput(xml)
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            && !PFlowIdentitySwitchLayout.IsSwitching(xml)
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            && !PFlowShareDestination.IsChooser(xml)
            && !PFlowPageIdentityRestriction.IsVisible(xml)
            && PFlowMediaAccess.FindControl(xml, package) == null
            && !(
                PFlowAutoPostService.Labels(xml).Contains("New post")
                && PFlowAutoPostService.Labels(xml).Any(v => v is "Gallery" or "Select multiple")
            )
            && !PFlowAutoPostService.HasComposerLayoutEvidence(xml, "Reel")
            && !xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
            && !new[]
            {
                FacebookScreenKind.PhotoComposer,
                FacebookScreenKind.CreateMenu,
                FacebookScreenKind.StoryEditor,
                FacebookScreenKind.StoryPicker,
                FacebookScreenKind.StoryAlbums,
                FacebookScreenKind.OwnStoryViewer,
                FacebookScreenKind.ReelPicker,
                FacebookScreenKind.ReelEditor,
            }.Any(kind => FacebookLayoutRegistry.Match(xml, kind).IsMatch);
    }

    internal static bool ConsumeContinuation(PFlowJob job, string xml)
    {
        var proof = job.TargetContinuation;
        job.TargetContinuation = null;
        return proof != null
            && job.State == PFlowState.IdentityVerified
            && proof.JobId == job.JobId
            && proof.Target == Target.Read(job)
            && DateTime.UtcNow - proof.VerifiedUtc is { TotalSeconds: >= 0 and <= 90 }
            && SupportsContinuation(job)
            && CanContinueForJob(job, xml);
    }
}
