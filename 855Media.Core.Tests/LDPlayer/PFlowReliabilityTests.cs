using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Fact]
    public async Task EmptyMediaIsRejectedBeforeReservationOrTransfer()
    {
        var job = await Job();
        await File.WriteAllTextAsync(job.SourcePath, "");
        var ld = FakeLd.Create(job);
        await Assert.ThrowsAsync<IOException>(() =>
            new PFlowRunner(ld, _store).RunAsync(
                job,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                default
            )
        );
        Assert.Equal(0, ((FakeLd)(object)ld).Transfers);
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
        Assert.Empty(await _store.ReadAsync());
        Assert.True(File.Exists(job.SourcePath));
    }

    [Fact]
    public async Task CorruptBackupRetryPreservesDamagedCopyAndCreatesVerifiedReplacement()
    {
        var job = await Job(backup: true);
        Confirm(job);
        var runner = new PFlowRunner(FakeLd.Create(job), _store);
        await runner.BackupAsync(job, default);
        await File.WriteAllTextAsync(job.BackupPath, "damaged data");
        await runner.BackupAsync(job, default);
        Assert.Equal("Verified", job.BackupState);
        Assert.Equal(
            await File.ReadAllTextAsync(job.SourcePath),
            await File.ReadAllTextAsync(job.BackupPath)
        );
        var damaged = Assert.Single(
            Directory.GetFiles(Path.GetDirectoryName(job.BackupPath)!, "*.invalid")
        );
        Assert.Equal("damaged data", await File.ReadAllTextAsync(damaged));
    }

    [Fact]
    public async Task VerifiedNotPublishedReviewSurvivesCleanupErrorsAndRestart()
    {
        var job = await Job();
        await _store.ReserveAsync(job);
        job.State = PFlowState.Failed;
        job.SubmittedUtc = DateTime.UtcNow;
        job.NoPublicationVerified = true;
        job.Evidence = "Manual review: target checked";
        job.Error =
            "Manual review verified no publication; a new attempt is permitted. | Emulator cleanup: unavailable";
        await _store.SaveAsync(job);
        await _store.RecoverAsync();
        var saved = (await _store.ReadAsync()).Single();
        Assert.True(saved.CanRetry);
        Assert.Equal(PFlowState.Failed, saved.State);
        Assert.True(await _store.ReserveAsync(await Job()));
    }

    [Fact]
    public async Task ReviewFlagCannotAuthorizeRetryWithoutEvidence()
    {
        var job = await Job();
        await _store.ReserveAsync(job);
        job.State = PFlowState.Failed;
        job.NoPublicationVerified = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SaveAsync(job));
    }

    [Fact]
    public async Task FailedTransferThenSafeRetryPublishesExactlyOnce()
    {
        var job = await Job();
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        var runner = new PFlowRunner(ld, _store);
        fake.TransferOk = false;
        Assert.False(
            await runner.RunAsync(
                job,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                default
            )
        );
        fake.TransferOk = true;
        Assert.True(
            await runner.RunAsync(
                job,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                default
            )
        );
        Assert.Equal(1, fake.ShareTaps);
        Assert.Equal(PFlowState.Confirmed, (await _store.ReadAsync()).Single().State);
        Assert.Empty(job.Error);
    }

    [Fact]
    public async Task ChangedCaptionIsPreservedDuringConfirmedMediaDeletion()
    {
        var caption = Path.Combine(_dir, "clip.txt");
        await File.WriteAllTextAsync(caption, "queued caption");
        var job = await Job(delete: true);
        await _store.ReserveAsync(job);
        Confirm(job);
        await _store.SaveAsync(job);
        await File.WriteAllTextAsync(caption, "new caption written later");
        await new PFlowRunner(FakeLd.Create(job), _store).DeleteSharedSourceAsync(
            job,
            [job],
            default
        );
        Assert.False(File.Exists(job.SourcePath));
        Assert.Equal("new caption written later", await File.ReadAllTextAsync(caption));
        Assert.Contains("caption retained", job.LocalDeleteState);
    }

    [Fact]
    public async Task MissingCaptionBackupBlocksMediaDeletion()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "clip.txt"), "queued caption");
        var job = await Job(backup: true, delete: true);
        await _store.ReserveAsync(job);
        Confirm(job);
        var runner = new PFlowRunner(FakeLd.Create(job), _store);
        await runner.BackupAsync(job, default);
        await _store.SaveAsync(job);
        File.Delete(Path.ChangeExtension(job.BackupPath, ".txt"));
        await runner.DeleteSharedSourceAsync(job, [job], default);
        Assert.True(File.Exists(job.SourcePath));
        Assert.Equal("Waiting for backup", job.LocalDeleteState);
    }

    [Fact]
    public async Task ChangedCaptionCannotBeMarkedAsVerifiedBackupOfQueuedCaption()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "clip.txt"), "queued caption");
        var job = await Job(backup: true);
        Confirm(job);
        await File.WriteAllTextAsync(Path.Combine(_dir, "clip.txt"), "changed caption");
        await new PFlowRunner(FakeLd.Create(job), _store).BackupAsync(job, default);
        Assert.Equal("Pending", job.BackupState);
        Assert.True(File.Exists(job.SourcePath));
    }

    [Fact]
    public async Task LoginRecoveryCanReserveItsOwnFailedJobAgain()
    {
        var job = await Job();
        Assert.True(await _store.ReserveAsync(job));
        job.State = PFlowState.Failed;
        job.Error = "Login required before submission";
        await _store.SaveAsync(job);
        job.State = PFlowState.Reserved;
        Assert.True(await _store.ReserveAsync(job));
        Assert.Equal(PFlowState.Reserved, (await _store.ReadAsync()).Single().State);
    }

    [Fact]
    public async Task RetryCannotReplaceJobIdentityOrBypassAnotherReservation()
    {
        var job = await Job();
        await _store.ReserveAsync(job);
        job.State = PFlowState.Failed;
        await _store.SaveAsync(job);
        var other = await Job();
        Assert.True(await _store.ReserveAsync(other));
        job.State = PFlowState.Reserved;
        Assert.False(await _store.ReserveAsync(job));
        job.Sha256 = "different-content";
        Assert.False(await _store.ReserveAsync(job));
    }

    [Fact]
    public async Task LegacyFailureAfterSubmissionCannotReleaseDuplicateProtection()
    {
        var job = await Job();
        await _store.ReserveAsync(job);
        job.State = PFlowState.Failed;
        job.SubmittedUtc = DateTime.UtcNow;
        job.Error = "Facebook reported a publication error.";
        await _store.SaveAsync(job);
        Assert.False(await _store.ReserveAsync(await Job()));
        await _store.RecoverAsync();
        Assert.Equal(PFlowState.Unknown, (await _store.ReadAsync()).Single().State);
    }

    [Fact]
    public async Task ManualNoPublicationReviewAllowsRetryAfterSubmission()
    {
        var job = await Job();
        await _store.ReserveAsync(job);
        job.State = PFlowState.Failed;
        job.SubmittedUtc = DateTime.UtcNow;
        job.Evidence = "Manual review: checked the target's posts";
        job.Error = "Manual review verified no publication; a new attempt is permitted.";
        await _store.SaveAsync(job);
        Assert.True(await _store.ReserveAsync(await Job()));
    }

    [Theory]
    [InlineData("Upload failed")]
    [InlineData("Unable to publish")]
    public async Task PublicationErrorAfterShareRequiresReviewAndRetainsBothCopies(string error)
    {
        var job = await Job(delete: true);
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        fake.PublicationError = error;
        Assert.False(
            await new PFlowRunner(ld, _store).RunAsync(
                job,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                default
            )
        );
        Assert.Equal(PFlowState.Unknown, job.State);
        Assert.Equal(1, fake.ShareTaps);
        Assert.Equal(0, fake.Deletes);
        Assert.True(File.Exists(job.SourcePath));
        Assert.False(await _store.ReserveAsync(await Job()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptVerifiedBackupPreventsSourceDeletion(bool corrupt)
    {
        var job = await Job(backup: true, delete: true);
        await _store.ReserveAsync(job);
        Confirm(job);
        var runner = new PFlowRunner(FakeLd.Create(job), _store);
        await runner.BackupAsync(job, default);
        await _store.SaveAsync(job);
        if (corrupt)
            await File.WriteAllTextAsync(job.BackupPath, "damaged backup");
        else
            File.Delete(job.BackupPath);
        await runner.DeleteSharedSourceAsync(job, [job], default);
        Assert.True(File.Exists(job.SourcePath));
        Assert.Equal("Waiting for backup", job.LocalDeleteState);
        Assert.Equal("Pending", (await _store.ReadAsync()).Single().BackupState);
    }

    [Fact]
    public async Task RetryRebuildsMissingVerifiedBackupBeforeDeletingSource()
    {
        var job = await Job(backup: true, delete: true);
        await _store.ReserveAsync(job);
        Confirm(job);
        var runner = new PFlowRunner(FakeLd.Create(job), _store);
        await runner.BackupAsync(job, default);
        File.Delete(job.BackupPath);
        await runner.BackupAsync(job, default);
        Assert.True(File.Exists(job.BackupPath));
        Assert.Equal("Verified", job.BackupState);
        await _store.SaveAsync(job);
        await runner.DeleteSharedSourceAsync(job, [job], default);
        Assert.False(File.Exists(job.SourcePath));
    }

    [Fact]
    public async Task SharedSourceChecksEveryConsumersBackupBeforeDeletion()
    {
        var a = await Job(backup: true, delete: true);
        var b = await Job("Page-B", backup: true, delete: true);
        var runner = new PFlowRunner(FakeLd.Create(a), _store);
        foreach (var job in new[] { a, b })
        {
            await _store.ReserveAsync(job);
            Confirm(job);
            await runner.BackupAsync(job, default);
            await _store.SaveAsync(job);
        }
        File.Delete(b.BackupPath);
        await runner.DeleteSharedSourceAsync(a, [a, b], default);
        Assert.True(File.Exists(a.SourcePath));
        Assert.Equal(
            "Pending",
            (await _store.ReadAsync()).Single(j => j.JobId == b.JobId).BackupState
        );
    }

    [Fact]
    public async Task CancelledBackupDoesNotLeavePublishedPartialFileAndCanRetry()
    {
        var job = await Job(backup: true);
        Confirm(job);
        var runner = new PFlowRunner(FakeLd.Create(job), _store);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await runner.BackupAsync(job, canceled.Token);
        Assert.Equal("Pending", job.BackupState);
        var destination = Path.Combine(_dir, "backup", job.JobId, "clip.mp4");
        Assert.False(File.Exists(destination));
        await runner.BackupAsync(job, default);
        Assert.Equal("Verified", job.BackupState);
        Assert.True(File.Exists(destination));
    }
}
