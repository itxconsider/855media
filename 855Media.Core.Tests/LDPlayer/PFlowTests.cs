using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Microsoft.Data.Sqlite;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "pflow-test-" + Guid.NewGuid().ToString("N")
    );
    private readonly PFlowHistoryStore _store;

    public PFlowTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new(Path.Combine(_dir, "history.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    private async Task<PFlowJob> Job(
        string target = "Page-A",
        bool backup = false,
        bool delete = false
    )
    {
        var file = Path.Combine(_dir, "clip.mp4");
        if (!File.Exists(file))
            await File.WriteAllTextAsync(file, "video contents");
        var job = new PFlowJob
        {
            TargetId = target,
            TargetName = "Target",
            OwnerUid = "owner",
            SourcePath = file,
            BackupRequested = backup,
            DeleteRequested = delete,
        };
        await PFlowJob.FingerprintAsync(job, CancellationToken.None);
        return job;
    }

    private static void Confirm(PFlowJob job)
    {
        job.State = PFlowState.Confirmed;
        job.ConfirmedUtc = DateTime.UtcNow;
        job.Evidence = "Reel published";
    }

    [Fact]
    public async Task HistorySurvivesRestartAndSeparatesPages()
    {
        var a = await Job();
        Assert.True(await _store.ReserveAsync(a));
        Confirm(a);
        await _store.SaveAsync(a);
        var reopened = new PFlowHistoryStore(_store.FilePath);
        Assert.False(await reopened.ReserveAsync(await Job()));
        Assert.True(await reopened.ReserveAsync(await Job("Page-B")));
        Assert.Equal(2, (await reopened.ReadAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentReservationAdmitsOne()
    {
        var a = await Job();
        var b = await Job();
        var results = await Task.WhenAll(
            Task.Run(() => _store.ReserveAsync(a)),
            Task.Run(() => _store.ReserveAsync(b))
        );
        Assert.Single(results, x => x);
    }

    [Fact]
    public async Task UnknownBlocksAndFailedPermitsExplicitRetry()
    {
        var a = await Job();
        await _store.ReserveAsync(a);
        a.State = PFlowState.Unknown;
        await _store.SaveAsync(a);
        Assert.False(await _store.ReserveAsync(await Job()));
        a.State = PFlowState.Failed;
        a.Error = "Manual review: not published";
        await _store.SaveAsync(a);
        Assert.True(await _store.ReserveAsync(await Job()));
    }

    [Fact]
    public async Task RenameDoesNotBypassFingerprint()
    {
        var a = await Job();
        await _store.ReserveAsync(a);
        Confirm(a);
        await _store.SaveAsync(a);
        var b = await Job();
        b.SourcePath = Path.Combine(_dir, "renamed.mp4");
        File.Copy(a.SourcePath, b.SourcePath);
        await PFlowJob.FingerprintAsync(b, CancellationToken.None);
        Assert.False(await _store.ReserveAsync(b));
    }

    [Fact]
    public async Task ConfirmedRequiresEvidence()
    {
        var a = await Job();
        await _store.ReserveAsync(a);
        a.State = PFlowState.Confirmed;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SaveAsync(a));
    }

    [Fact]
    public async Task DailyCountersAreIdempotentAndReserveCapacity()
    {
        var a = await Job();
        Assert.Equal(2, await _store.DailyCountAsync(a.TargetId, 2));
        Assert.True(await _store.ReserveAsync(a, dailyLimit: 3));
        var b = await Job();
        b.Sha256 = "different";
        Assert.False(await _store.ReserveAsync(b, dailyLimit: 3));
        Confirm(a);
        await _store.SaveAsync(a);
        Assert.Equal(3, await _store.DailyCountAsync(a.TargetId, 2));
        Assert.Equal(3, await _store.DailyCountAsync(a.TargetId, 3));
    }

    [Theory]
    [InlineData(PFlowState.Reserved, PFlowState.CancelledBeforeSubmission)]
    [InlineData(PFlowState.SubmitRequested, PFlowState.Unknown)]
    public async Task AbandonedJobsRecoverConservatively(PFlowState before, PFlowState after)
    {
        var a = await Job();
        await _store.ReserveAsync(a);
        a.State = before;
        await _store.SaveAsync(a);
        using (var c = new SqliteConnection("Data Source=" + _store.FilePath))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE jobs SET pid=-123";
            cmd.ExecuteNonQuery();
        }
        await _store.RecoverAsync();
        Assert.Equal(after, (await _store.ReadAsync()).Single().State);
    }

    [Fact]
    public async Task CurrentProcessReservationsAreNotRecovered()
    {
        var a = await Job();
        await _store.ReserveAsync(a);
        await _store.RecoverAsync();
        Assert.Equal(PFlowState.Reserved, (await _store.ReadAsync()).Single().State);
    }

    [Fact]
    public async Task PlannedJobClaimsItsOwnReservation()
    {
        var a = await Job();
        a.State = PFlowState.Queued;
        Assert.True(await _store.ReserveAsync(a));
        a.State = PFlowState.Reserved;
        Assert.True(await _store.ReserveAsync(a, dailyLimit: 1));
        Assert.False(await _store.ReserveAsync(a));
        Assert.Single(await _store.ReadAsync());
    }

    [Fact]
    public async Task LegacyVideoUsesSameDuplicateNamespaceAsReel()
    {
        var a = await Job();
        Assert.True(await _store.ReserveAsync(a));
        var b = await Job();
        b.PostType = "Video";
        Assert.False(await _store.ReserveAsync(b));
    }

    [Fact]
    public async Task SourceCannotBeDeletedDuringPublication()
    {
        var a = await Job();
        await new PFlowRunner(FakeLd.Create(a), _store).RunAsync(
            a,
            _ =>
            {
                Assert.Throws<IOException>(() => File.Delete(a.SourcePath));
                return Task.FromResult(true);
            },
            _ => Task.CompletedTask,
            null,
            CancellationToken.None,
            dryRun: true
        );
        Assert.True(File.Exists(a.SourcePath));
    }

    [Fact]
    public async Task CorruptStoreFailsClosed()
    {
        await File.WriteAllTextAsync(_store.FilePath, "not sqlite");
        await Assert.ThrowsAsync<SqliteException>(() => _store.ReadAsync());
    }

    [Fact]
    public async Task SharedSourceWaitsForBothPagesThenDeletes()
    {
        var a = await Job(delete: true);
        var b = await Job("Page-B", delete: true);
        var runner = new PFlowRunner(FakeLd.Create(a), _store);
        await _store.ReserveAsync(a);
        Confirm(a);
        await _store.SaveAsync(a);
        await runner.DeleteSharedSourceAsync(a, new[] { a, b }, CancellationToken.None);
        Assert.True(File.Exists(a.SourcePath));
        await _store.ReserveAsync(b);
        Confirm(b);
        await _store.SaveAsync(b);
        await runner.DeleteSharedSourceAsync(a, new[] { a, b }, CancellationToken.None);
        Assert.False(File.Exists(a.SourcePath));
    }

    [Fact]
    public async Task KeepPolicyForAnyConsumerPreservesOriginal()
    {
        var a = await Job(delete: true);
        var b = await Job("Page-B");
        foreach (var j in new[] { a, b })
        {
            await _store.ReserveAsync(j);
            Confirm(j);
            await _store.SaveAsync(j);
        }
        await new PFlowRunner(FakeLd.Create(a), _store).DeleteSharedSourceAsync(
            a,
            new[] { a, b },
            CancellationToken.None
        );
        Assert.True(File.Exists(a.SourcePath));
    }

    [Fact]
    public async Task VerifiedBackupIncludesCaptionAndNeverOverwrites()
    {
        var a = await Job(backup: true);
        await File.WriteAllTextAsync(Path.ChangeExtension(a.SourcePath, ".txt"), "caption");
        await _store.ReserveAsync(a);
        Confirm(a);
        var runner = new PFlowRunner(FakeLd.Create(a), _store);
        await runner.BackupAsync(a, CancellationToken.None);
        Assert.Equal("Verified", a.BackupState);
        Assert.Equal(
            "caption",
            await File.ReadAllTextAsync(
                Path.Combine(Path.GetDirectoryName(a.BackupPath)!, "clip.txt")
            )
        );
        var b = await Job("Page-B", backup: true);
        Confirm(b);
        await runner.BackupAsync(b, CancellationToken.None);
        Assert.NotEqual(a.BackupPath, b.BackupPath);
    }

    [Fact]
    public async Task BackupFailurePreservesOriginal()
    {
        var a = await Job(backup: true, delete: true);
        await _store.ReserveAsync(a);
        Confirm(a);
        await _store.SaveAsync(a);
        await File.WriteAllTextAsync(Path.Combine(_dir, "backup"), "blocking directory creation");
        var runner = new PFlowRunner(FakeLd.Create(a), _store);
        await runner.BackupAsync(a, CancellationToken.None);
        await _store.SaveAsync(a);
        await runner.DeleteSharedSourceAsync(a, new[] { a }, CancellationToken.None);
        Assert.True(File.Exists(a.SourcePath));
        Assert.Equal("Pending", a.BackupState);
    }

    [Fact]
    public async Task ChangedSourceIsNotDeleted()
    {
        var a = await Job(delete: true);
        await _store.ReserveAsync(a);
        Confirm(a);
        await _store.SaveAsync(a);
        await File.WriteAllTextAsync(a.SourcePath, "changed");
        await new PFlowRunner(FakeLd.Create(a), _store).DeleteSharedSourceAsync(
            a,
            new[] { a },
            CancellationToken.None
        );
        Assert.True(File.Exists(a.SourcePath));
        Assert.Equal("Pending", a.LocalDeleteState);
    }

    [Fact]
    public async Task BackupAndLocalCleanupFailuresPreserveOriginalError()
    {
        var job = await Job(backup: true, delete: true);
        await _store.ReserveAsync(job);
        Confirm(job);
        job.Error = "Original outcome detail";
        await File.WriteAllTextAsync(Path.Combine(_dir, "backup"), "block");
        var runner = new PFlowRunner(FakeLd.Create(job), _store);
        await runner.BackupAsync(job, default);
        var backupError = job.Error;
        await runner.BackupAsync(job, default);
        Assert.Equal(backupError, job.Error);
        Assert.StartsWith("Original outcome detail | Backup:", job.Error);
        job.BackupRequested = false;
        await _store.SaveAsync(job);
        await File.WriteAllTextAsync(job.SourcePath, "changed");
        await runner.DeleteSharedSourceAsync(job, [job], default);
        Assert.StartsWith(backupError + " | Local cleanup:", job.Error);
        Assert.True(File.Exists(job.SourcePath));
    }

    [Fact]
    public async Task DryRunReachesComposerWithoutShare()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        var runner = new PFlowRunner(ld, _store);
        Assert.False(
            await runner.RunAsync(
                a,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                CancellationToken.None,
                dryRun: true
            )
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, a.State);
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
        Assert.True(File.Exists(a.SourcePath));
        Assert.Equal("Verified", a.RemoteCleanupState);
    }

    [Fact]
    public async Task ConfirmedPublicationPersistsBeforeCleanup()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        Assert.True(
            await new PFlowRunner(ld, _store).RunAsync(
                a,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                CancellationToken.None
            )
        );
        Assert.Equal(PFlowState.Confirmed, (await _store.ReadAsync()).Single().State);
        Assert.Equal(1, ((FakeLd)(object)ld).ShareTaps);
        Assert.True(File.Exists(a.SourcePath));
    }

    [Fact]
    public async Task FreshUploadNotificationRetainsFilesUntilTargetPublicationIsVerified()
    {
        var job = await Job();
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        fake.SuccessMessage = false;
        fake.NotificationSuccess = true;
        Assert.False(
            await new PFlowRunner(ld, _store).RunAsync(
                job,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                CancellationToken.None
            )
        );
        var saved = (await _store.ReadAsync()).Single();
        Assert.Equal(PFlowState.Unknown, saved.State);
        Assert.Null(saved.ConfirmedUtc);
        Assert.Equal(0, fake.Deletes);
        Assert.Equal("Held for publication review", saved.RemoteCleanupState);
        Assert.Contains("media-upload notification", saved.Evidence);
        Assert.Equal(1, fake.ShareTaps);
        Assert.True(File.Exists(job.SourcePath));
    }

    [Fact]
    public async Task FailedTransferNeverShares()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        ((FakeLd)(object)ld).TransferOk = false;
        Assert.False(
            await new PFlowRunner(ld, _store).RunAsync(
                a,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                CancellationToken.None
            )
        );
        Assert.Equal(PFlowState.Failed, a.State);
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
    }

    [Fact]
    public async Task WrongIdentityNeverTransfersOrShares()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        await new PFlowRunner(ld, _store).RunAsync(
            a,
            _ => Task.FromResult(false),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
        Assert.Equal(0, ((FakeLd)(object)ld).Transfers);
    }

    [Fact]
    public async Task TimeoutAfterShareBecomesUnknownAndRetainsRemote()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        var fake = (FakeLd)(object)ld;
        fake.SuccessMessage = false;
        await new PFlowRunner(ld, _store, confirmationTimeout: TimeSpan.Zero).RunAsync(
            a,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Unknown, a.State);
        Assert.Equal(0, fake.Deletes);
        Assert.False(await _store.ReserveAsync(await Job()));
    }

    [Fact]
    public async Task CancellationAfterShareBecomesUnknown()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        using var cts = new CancellationTokenSource();
        ((FakeLd)(object)ld).CancelOnShare = cts;
        await new PFlowRunner(ld, _store).RunAsync(
            a,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            cts.Token
        );
        Assert.Equal(PFlowState.Unknown, a.State);
        Assert.Equal(0, ((FakeLd)(object)ld).Deletes);
    }

    [Fact]
    public void OldOrWrongSuccessMessagesDoNotConfirm()
    {
        var xml = "<hierarchy><node text='Reel published'/></hierarchy>";
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(
                xml,
                new HashSet<string> { "Reel published" },
                "Reel"
            )
        );
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(xml, new HashSet<string>(), "Picture")
        );
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(
                "<hierarchy><node text='Uploading...'/></hierarchy>",
                new HashSet<string>(),
                "Reel"
            )
        );
    }

    [Theory]
    [InlineData("CONNECTED/CONNECTED", "INTERNET&amp;VALIDATED", true)]
    [InlineData("CONNECTED/CONNECTED", "INTERNET&amp;UNVALIDATED", false)]
    [InlineData("DISCONNECTED/DISCONNECTED", "INTERNET&amp;VALIDATED", false)]
    public void InternetRequiresConnectedValidatedNetwork(
        string state,
        string flags,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            PFlowSession.HasInternet(
                $"Active default network: 100\nCurrent Networks:\n  NetworkAgentInfo{{network{{100}} ni{{[state: {state}]}} nc{{[Capabilities: {flags.Replace("&amp;", "&")}]}}}}\nHistorical:\n"
            )
        );

    [Fact]
    public async Task ChangedQueueSnapshotRefusesSubmission()
    {
        var a = await Job();
        await File.WriteAllTextAsync(a.SourcePath, "changed");
        var ld = FakeLd.Create(a);
        await Assert.ThrowsAsync<IOException>(() =>
            new PFlowRunner(ld, _store).RunAsync(
                a,
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                null,
                CancellationToken.None
            )
        );
        Assert.Empty(await _store.ReadAsync());
    }

    [Fact]
    public async Task EmulatorDeletionFailureRemainsPending()
    {
        var a = await Job();
        Confirm(a);
        var ld = FakeLd.Create(a);
        ((FakeLd)(object)ld).DeleteOk = false;
        await new PFlowRunner(ld, _store).CleanupRemoteAsync(a, null, CancellationToken.None);
        Assert.Equal("Pending", a.RemoteCleanupState);
    }

    [Fact]
    public async Task UniqueRemoteNamesIgnoreSourceBasename()
    {
        var a = await Job();
        var b = await Job("B");
        Assert.NotEqual(a.RemotePath, b.RemotePath);
        Assert.StartsWith("/sdcard/DCIM/855Media/", a.RemotePath);
    }

    [Fact]
    public async Task HiddenReelActorUsesVerifiedNativeSession()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        ((FakeLd)(object)ld).HiddenActor = true;
        await new PFlowRunner(ld, _store).RunAsync(
            a,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, a.State);
        Assert.Contains("Live UID verification", a.IdentityEvidence);
        Assert.Contains("own-profile", a.IdentityEvidence);
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
    }

    [Fact]
    public async Task ConflictingComposerActorStopsBeforeShare()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        ((FakeLd)(object)ld).WrongActor = true;
        await new PFlowRunner(ld, _store).RunAsync(
            a,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Failed, a.State);
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
    }

    [Fact]
    public async Task TransferChecksumMismatchStopsBeforeComposer()
    {
        var a = await Job();
        var ld = FakeLd.Create(a);
        ((FakeLd)(object)ld).ChecksumOk = false;
        await new PFlowRunner(ld, _store).RunAsync(
            a,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Failed, a.State);
        Assert.Contains("checksum", a.Error);
        Assert.Equal(0, ((FakeLd)(object)ld).ShareTaps);
    }

    public class FakeLd : DispatchProxy
    {
        public PFlowJob Job = null!;
        public string? InitialXml;
        public int ShareTaps,
            Transfers,
            Deletes;
        public bool TransferOk = true,
            DeleteOk = true,
            SuccessMessage = true,
            ChecksumOk = true,
            HiddenActor,
            WrongActor,
            NotificationSuccess;
        public CancellationTokenSource? CancelOnShare;
        public string PublicationError = "";
        public string? PostShareXml;
        public System.Collections.Generic.Queue<string> PostShareSequence = new();
        public int MissingInstanceScans;
        public int InstanceScans;
        public bool RenamedInstance;
        private bool _composerOpened;
        private bool _chooserAccepted;

        public void NextJob(PFlowJob job, string initialXml)
        {
            Job = job;
            InitialXml = initialXml;
            _composerOpened = false;
            _chooserAccepted = false;
            ShareTaps = 0;
        }

        public static ILDPlayerService Create(PFlowJob job)
        {
            var ld = DispatchProxy.Create<ILDPlayerService, FakeLd>();
            ((FakeLd)(object)ld).Job = job;
            return ld;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetInstancesAsync":
                    InstanceScans++;
                    if (InstanceScans <= MissingInstanceScans)
                        return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([]);
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>(
                        new List<LDInstanceInfo>
                        {
                            new()
                            {
                                Index = Job.DeviceIndex,
                                Name = RenamedInstance ? "renamed" : Job.AccountBinding!.DeviceName,
                            },
                        }
                    );
                case "ExecuteAdbAsync":
                    var cmd = (string)args![1]!;
                    return Task.FromResult(
                        cmd.Contains("dumpsys notification")
                            ? (
                                ShareTaps > 0 && NotificationSuccess
                                    ? PFlowConfirmationTests.Dump()
                                    : "  Notification List:\n  mArchive=Archive (0 notifications)"
                            )
                        : cmd.Contains("stat -c") ? Job.Size.ToString()
                        : cmd.Contains("sha256sum") ? (ChecksumOk ? Job.Sha256 : "wrong") + " file"
                        : cmd.Contains("dumpsys activity")
                            ? "mResumedActivity: com.facebook.katana/ReelSettings"
                        : cmd.Contains("REMOVED")
                            ? DeleteOk ? "REMOVED"
                                : ""
                        : ""
                    );
                case "PushFileToEmulatorAsync":
                    Transfers++;
                    return Task.FromResult(TransferOk);
                case "DumpUiHierarchyAsync":
                    if (ShareTaps > 0 && PostShareSequence.Count > 0)
                        return Task.FromResult<string?>(PostShareSequence.Dequeue());
                    if (ShareTaps > 0 && PostShareXml != null)
                        return Task.FromResult<string?>(PostShareXml);
                    if (_composerOpened && !_chooserAccepted)
                        return Task.FromResult<string?>(
                            PFlowShareDestinationTests.SelectedReelsXml
                        );
                    return Task.FromResult<string?>(
                        !_composerOpened
                            ? InitialXml
                                ?? "<hierarchy><node text='Target'/><node text='Professional dashboard'/></hierarchy>"
                        : ShareTaps > 0 && PublicationError.Length > 0
                            ? $"<hierarchy><node text='{PublicationError}'/></hierarchy>"
                        : ShareTaps > 0 && NotificationSuccess
                            ? "<hierarchy><node text='Home'/></hierarchy>"
                        : ShareTaps > 0 && SuccessMessage
                            ? "<hierarchy><node text='Reel published'/></hierarchy>"
                        : HiddenActor || WrongActor
                            ? "<hierarchy><node text='Reel Settings'/><node text='Describe your reel'/><node text='Share now' clickable='true' enabled='true' bounds='[0,0][100,100]'/>"
                                + (WrongActor ? "<node text='Posting as someone else'/>" : "")
                                + "</hierarchy>"
                        : "<hierarchy><node text='Target'/><node text='Describe your reel'/><node text='Share now' clickable='true' enabled='true' bounds='[0,0][100,100]'/></hierarchy>"
                    );
                case "ShareVideoToFacebookAsync":
                case "ShareImageToFacebookAsync":
                    _composerOpened = true;
                    _chooserAccepted = false;
                    return Task.FromResult(true);
                case "TapAsync":
                    if (!_chooserAccepted && _composerOpened)
                    {
                        _chooserAccepted = true;
                        return Task.FromResult(true);
                    }
                    ShareTaps++;
                    CancelOnShare?.Cancel();
                    return Task.FromResult(true);
                case "DeleteFileFromEmulatorAsync":
                    Deletes++;
                    return Task.FromResult(DeleteOk);
            }
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(true);
            if (method.ReturnType == typeof(Task))
                return Task.CompletedTask;
            throw new NotSupportedException(method.Name);
        }
    }
}
