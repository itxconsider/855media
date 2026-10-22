using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public enum PFlowState
{
    Reserved,
    IdentityVerified,
    MediaReady,
    ComposerReady,
    SubmitRequested,
    Submitted,
    Confirmed,
    Failed,
    CancelledBeforeSubmission,
    Unknown,
    Queued = 100,
}

public sealed class PFlowJob
{
    [System.Text.Json.Serialization.JsonIgnore]
    public PFlowNativeIdentityScope? NativeIdentityScope { get; set; }
    public PFlowAccountBinding? AccountBinding { get; set; }
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public string BatchId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string OwnerUid { get; set; } = "";
    public string PersonaUid { get; set; } = "";
    public bool IsProfile { get; set; }
    public int DeviceIndex { get; set; }
    public string AppPackage { get; set; } = "com.facebook.katana";
    public string PartGroupKey { get; set; } = "";
    public int PartNumber { get; set; }
    public string SourcePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string CaptionFileSha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string PostType { get; set; } = "Reel";
    public string Caption { get; set; } = "";
    public string ReelTitle { get; set; } = "";
    public PFlowState State { get; set; } = PFlowState.Reserved;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public bool NoPublicationVerified { get; set; }
    public bool TargetIdentityConfirmationRequired { get; set; }
    public bool ProfilePublicationCheckRequired { get; set; }
    public PFlowPublicationStage PublicationStage { get; set; }
    public DateTime? PublicationStageStartedUtc { get; set; }
    public string PublicationStageEvidence { get; set; } = "";
    public string PublicationErrorMessage { get; set; } = "";
    public string PublicationErrorAttribute { get; set; } = "";
    public System.Collections.Generic.List<PFlowPublicationTransition> PublicationTransitions { get; set; } =
    [];
    public string Evidence { get; set; } = "";
    public string IdentityEvidence { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    internal PFlowPostTargetSession.Continuation? TargetContinuation { get; set; }
    public string MediaSelectionEvidence { get; set; } = "";
    public int? StoryCountBefore { get; set; }
    public int? StoryCountAfter { get; set; }
    public string CurrentStep { get; set; } = "";
    public DateTime StepStartedUtc { get; set; } = DateTime.UtcNow;
    public string PostId { get; set; } = "";
    public string PostUrl { get; set; } = "";
    public string Error { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public string BackupState { get; set; } = "Not requested";
    public string LocalDeleteState { get; set; } = "Keep original";
    public string RemoteCleanupState { get; set; } = "Pending";
    public bool BackupRequested { get; set; }
    public bool DeleteRequested { get; set; }
    public bool NativeReelAlbum { get; set; }
    public string RemotePath =>
        $"/sdcard/DCIM/855Media/{(PostType == "Story" ? "Story_" + JobId + "/" : NativeReelAlbum && PostType is "Reel" or "Video" ? "Reel_" + JobId + "/" : "")}{JobId}{Path.GetExtension(SourcePath).ToLowerInvariant()}";
    public string DuplicateKey =>
        $"{(IsProfile ? "Profile" : "Page")}:{TargetId}:{(PostType == "Video" ? "Reel" : PostType)}:{Sha256}";
    public bool CanRetry =>
        State is PFlowState.Failed or PFlowState.CancelledBeforeSubmission
        && (
            !SubmittedUtc.HasValue
            || State == PFlowState.Failed
                && (
                    NoPublicationVerified
                    || Evidence.StartsWith("Manual review: ", StringComparison.Ordinal)
                        && Error.StartsWith(
                            "Manual review verified no publication; a new attempt is permitted.",
                            StringComparison.Ordinal
                        )
                )
        );
    public bool MayHaveSubmitted =>
        State
            is PFlowState.SubmitRequested
                or PFlowState.Submitted
                or PFlowState.Unknown
                or PFlowState.Confirmed
        || SubmittedUtc.HasValue && !CanRetry;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasFacebookPublicationFailure =>
        State == PFlowState.Failed
        && PublicationStage == PFlowPublicationStage.PublicationError
        && PublicationErrorMessage.Length > 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresPublicationReviewBeforeCleanup =>
        State == PFlowState.Unknown
        || State == PFlowState.Failed && MayHaveSubmitted
        || State == PFlowState.Confirmed
            && (
                string.IsNullOrWhiteSpace(Evidence)
                || PFlowConfirmation.IsNotificationOnly(Evidence)
                || ProfilePublicationCheckRequired
                    && !Evidence.StartsWith(
                        "Target profile Reel verified:",
                        StringComparison.Ordinal
                    )
                    && !Evidence.StartsWith("Manual review: ", StringComparison.Ordinal)
            );

    public static async Task FingerprintAsync(PFlowJob job, CancellationToken ct)
    {
        job.SourcePath = Path.GetFullPath(job.SourcePath);
        await using var stream = new FileStream(
            job.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            true
        );
        job.Size = stream.Length;
        if (
            job.Size == 0
            && (
                FacebookPageTarget.ReelExtensions.Contains(
                    Path.GetExtension(job.SourcePath),
                    StringComparer.OrdinalIgnoreCase
                )
                || FacebookPageTarget.PhotoExtensions.Contains(
                    Path.GetExtension(job.SourcePath),
                    StringComparer.OrdinalIgnoreCase
                )
            )
        )
            throw new IOException("Media file is empty; no upload was started.");
        job.ModifiedUtc = File.GetLastWriteTimeUtc(job.SourcePath);
        job.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        var caption = Path.ChangeExtension(job.SourcePath, ".txt");
        if (
            !job.SourcePath.Equals(caption, StringComparison.OrdinalIgnoreCase)
            && File.Exists(caption)
        )
        {
            await using var text = new FileStream(
                caption,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            job.CaptionFileSha256 = Convert.ToHexString(await SHA256.HashDataAsync(text, ct));
        }
    }
}
