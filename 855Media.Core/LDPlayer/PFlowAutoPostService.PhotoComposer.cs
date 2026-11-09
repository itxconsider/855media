using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    // Invocation-local proof. Never serialized or reused for a different job/draft.
    internal sealed class PhotoSettingsProof(PFlowJob job, string process)
    {
        private readonly string _job = job.JobId,
            _caption = job.Caption,
            _target = job.TargetId,
            _name = job.TargetName,
            _owner = job.OwnerUid,
            _persona = job.PersonaUid,
            _sha = job.Sha256,
            _package = job.AppPackage,
            _source = job.SourcePath,
            _type = job.PostType,
            _selection = job.MediaSelectionEvidence,
            _members = string.Join("\n", job.MediaFiles.Select(f => f.SourcePath + ":" + f.Sha256));
        private readonly int _device = job.DeviceIndex;
        private readonly bool _profile = job.IsProfile;
        private readonly PFlowAccountBinding? _binding = job.AccountBinding;
        private readonly PFlowNativeIdentityScope? _scope = job.NativeIdentityScope;

        internal bool MatchesSnapshot(PFlowJob current) =>
            current.JobId == _job
            && current.Caption == _caption
            && current.TargetId == _target
            && current.TargetName == _name
            && current.OwnerUid == _owner
            && current.PersonaUid == _persona
            && current.Sha256 == _sha
            && current.AppPackage == _package
            && current.SourcePath == _source
            && current.PostType == _type
            && current.MediaSelectionEvidence == _selection
            && string.Join("\n", current.MediaFiles.Select(f => f.SourcePath + ":" + f.Sha256))
                == _members
            && current.DeviceIndex == _device
            && current.IsProfile == _profile
            && current.AccountBinding == _binding
            && ReferenceEquals(_scope, current.NativeIdentityScope)
            && (_scope == null || _scope.Matches(current));

        internal async Task<bool> VerifyAsync(
            ILDPlayerService ld,
            PFlowJob current,
            string xml,
            string? path,
            CancellationToken ct
        ) =>
            MatchesSnapshot(current)
            && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer).IsMatch
            && (
                PFlowPhotoComposerLayout.IsSettings(xml)
                || PFlowPhotoComposerLayout.IsReview(xml)
                    && PFlowPhotoComposerLayout.ReviewTargetMatches(xml, _name)
                    && (string.IsNullOrWhiteSpace(_caption) || CaptionTextMatches(xml, _caption))
            )
            && (!current.IsPhotoGroup || PFlowPhotoGroups.ComposerMatchesGroup(xml, current))
            && !PFlowPhotoComposerLayout.ActorContradicts(xml, _name)
            && await ReadPhotoProcessAsync(ld, current, path, ct) == process;
    }

    private static async Task<string?> ReadPhotoProcessAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        CancellationToken ct
    )
    {
        var focused = await ld.GetCurrentFocusedActivityAsync(job.DeviceIndex, path, ct) ?? "";
        if (
            focused != job.AppPackage
            && !focused.StartsWith(job.AppPackage + "/", StringComparison.Ordinal)
        )
            return null;
        var pid = (
            await ld.ExecuteAdbAsync(
                job.DeviceIndex,
                "shell pidof " + ShellQuote(job.AppPackage),
                path,
                ct
            ) ?? ""
        ).Trim();
        return
            pid.Length > 0
            && pid.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(s => s.All(char.IsAsciiDigit))
            ? pid
            : null;
    }

    internal async Task<(string Xml, PhotoSettingsProof Proof)> AdvancePhotoSettingsAsync(
        PFlowJob job,
        string xml,
        Func<string, Task<bool>> identity,
        Func<PFlowJob, Task> checkpoint,
        string? path,
        CancellationToken ct
    )
    {
        job.CurrentStep = "Verifying photo caption and opening Post settings";
        await checkpoint(job);
        // Resolve again after caption/keyboard changes; no saved Next coordinates.
        xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        PFlowPageIdentityRestriction.ThrowIfVisible(job, xml);
        if (
            !FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer).IsMatch
            || !PFlowPhotoComposerLayout.IsDraft(xml)
            || !await identity(xml)
            || !PFlowPhotoComposerLayout.DraftTargetMatches(xml, job.TargetName)
            || !PFlowPhotoComposerLayout.CaptionMatches(xml, job.Caption)
            || PFlowPhotoComposerLayout.ActorContradicts(xml, job.TargetName)
            || job.IsPhotoGroup && !PFlowPhotoGroups.ComposerMatchesGroup(xml, job)
        )
            throw new InvalidOperationException(
                "Photo draft target, caption or attachments changed before Next; no Share tap was made."
            );
        var process = await ReadPhotoProcessAsync(_ldPlayer, job, path, ct);
        if (process == null)
            throw new InvalidOperationException(
                "Photo draft process/foreground could not be verified."
            );
        await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, path, ct);
        var proof = new PhotoSettingsProof(job, process);
        var timer = Stopwatch.StartNew();
        while (
            PFlowPhotoComposerLayout.Header(xml, "Next") == null
            && timer.Elapsed < TimeSpan.FromSeconds(30)
        )
        {
            await Task.Delay(400, ct);
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            if (
                !PFlowPhotoComposerLayout.IsDraft(xml)
                || !await identity(xml)
                || !PFlowPhotoComposerLayout.DraftTargetMatches(xml, job.TargetName)
                || !PFlowPhotoComposerLayout.CaptionMatches(xml, job.Caption)
                || !proof.MatchesSnapshot(job)
                || await ReadPhotoProcessAsync(_ldPlayer, job, path, ct) != process
            )
                throw new InvalidOperationException("Photo draft changed while waiting for Next.");
        }
        xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        if (
            !FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer).IsMatch
            || !PFlowPhotoComposerLayout.IsDraft(xml)
            || !await identity(xml)
            || !PFlowPhotoComposerLayout.DraftTargetMatches(xml, job.TargetName)
            || !PFlowPhotoComposerLayout.CaptionMatches(xml, job.Caption)
            || PFlowPhotoComposerLayout.ActorContradicts(xml, job.TargetName)
            || !proof.MatchesSnapshot(job)
            || await ReadPhotoProcessAsync(_ldPlayer, job, path, ct) != process
        )
            throw new InvalidOperationException(
                "Photo draft changed before the fresh Next tap; no Share tap was made."
            );
        var next = PFlowPhotoComposerLayout.Header(xml, "Next");
        var point = next == null ? null : FacebookLayoutRegistry.UsableCenter(next);
        if (
            point == null
            || !await _ldPlayer.TapAsync(job.DeviceIndex, point.Value.X, point.Value.Y, path, ct)
        )
            throw new InvalidOperationException(
                "Photo Next was unavailable or unacknowledged; no repeated handoff attempted."
            );
        timer.Restart();
        do
        {
            await Task.Delay(400, ct);
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            PFlowPageIdentityRestriction.ThrowIfVisible(job, xml);
            if (
                !proof.MatchesSnapshot(job)
                || await ReadPhotoProcessAsync(_ldPlayer, job, path, ct) != process
            )
                throw new InvalidOperationException(
                    "Photo target/process changed after Next; no Share tap was made."
                );
            if (
                (PFlowPhotoComposerLayout.IsSettings(xml) || PFlowPhotoComposerLayout.IsReview(xml))
                && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer).IsMatch
            )
            {
                if (!await proof.VerifyAsync(_ldPlayer, job, xml, path, ct))
                    throw new InvalidOperationException(
                        "Post settings could not be bound to the verified photo draft."
                    );
                await PFlowTrace.SessionAsync(
                    job.DeviceIndex,
                    "photo.settings.handoff",
                    "captionVerified=true; sameJob=true; sameProcess=true; Graph=0; noShareTap=true",
                    ct
                );
                return (xml, proof);
            }
            if (
                ScreenTimeHierarchy
                    .Nodes(xml)
                    .Any(n =>
                        n.Attribute("password")?.Value == "true"
                        || n.Attribute("package")?.Value is { Length: > 0 } p
                            && p != job.AppPackage
                            && p != "com.android.systemui"
                    )
            )
                throw new InvalidOperationException("Photo Next reached an unsafe/foreign screen.");
        } while (timer.Elapsed < TimeSpan.FromSeconds(20));
        throw new InvalidOperationException(
            "Post settings did not appear after the single Next tap; no Share tap was made."
        );
    }
}
