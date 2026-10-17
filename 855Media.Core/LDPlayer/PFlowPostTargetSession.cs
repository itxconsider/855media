using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// In-memory target session for one device and batch; persisted history cannot restore it.
public sealed class PFlowPostTargetSession
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
        string? initialXml = null
    )
    {
        var target = Target.Read(job);
        var generation = _generation;
        var consecutive =
            _previous != null
            && _previous.JobId != job.JobId
            && _previous.State == PFlowState.Confirmed
            && _previous.ConfirmedUtc.HasValue
            && !string.IsNullOrWhiteSpace(_previous.Evidence)
            && _target == target
            && target.Binding != null
            && !string.IsNullOrWhiteSpace(target.Batch)
            && SupportsContinuation(_previous)
            && SupportsContinuation(job);
        _previous = null;
        _target = null;
        job.TargetContinuation = null;
        ct.ThrowIfCancellationRequested();
        var xml = initialXml ?? (consecutive ? await observe(ct) : "");
        var continued =
            consecutive
            && generation == _generation
            && Target.Read(job) == target
            && CanContinueFrom(xml, job.AppPackage);
        if (continued)
            job.TargetContinuation = new(job.JobId, target, DateTime.UtcNow);
        else
        {
            var method = await verifyLiveUid(ct);
            ct.ThrowIfCancellationRequested();
            if (method is not ("Graph UID" or "UI UID lookup"))
                throw new InvalidOperationException(
                    "Fresh target UID verification was not confirmed."
                );
            if (Target.Read(job) != target)
                throw new InvalidOperationException(
                    "Posting target changed during UID verification."
                );
            if (!await openOwnProfile(ct))
                throw new InvalidOperationException(
                    "Live UID was verified, but the target profile screen could not be confirmed. No post was submitted."
                );
            xml = await observe(ct);
            if (!HasOwnProfile(job, xml))
                throw new InvalidOperationException(
                    "Live UID was verified, but the expected own-profile controls were not visible. No post was submitted."
                );
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

    private static bool CanContinueFrom(string xml, string package)
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
            && CanContinueFrom(xml, job.AppPackage);
    }
}
