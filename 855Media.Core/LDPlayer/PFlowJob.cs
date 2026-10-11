using System;
using System.IO;
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
    public string SourcePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string PostType { get; set; } = "Reel";
    public string Caption { get; set; } = "";
    public PFlowState State { get; set; } = PFlowState.Reserved;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public string Evidence { get; set; } = "";
    public string IdentityEvidence { get; set; } = "";
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
    public string RemotePath =>
        $"/sdcard/DCIM/855Media/{JobId}{Path.GetExtension(SourcePath).ToLowerInvariant()}";
    public string DuplicateKey =>
        $"{(IsProfile ? "Profile" : "Page")}:{TargetId}:{(PostType == "Video" ? "Reel" : PostType)}:{Sha256}";
    public bool MayHaveSubmitted =>
        State
            is PFlowState.SubmitRequested
                or PFlowState.Submitted
                or PFlowState.Unknown
                or PFlowState.Confirmed;

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
        job.ModifiedUtc = File.GetLastWriteTimeUtc(job.SourcePath);
        job.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
}
