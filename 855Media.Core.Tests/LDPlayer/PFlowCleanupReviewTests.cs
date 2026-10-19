using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Fact]
    public async Task UnknownSubmissionKeepsAndroidMediaAndIndexForReview()
    {
        var job = await Job();
        job.State = PFlowState.Unknown;
        job.SubmittedUtc = System.DateTime.UtcNow;
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        await new PFlowRunner(ld, _store).CleanupRemoteAsync(job, null, default);
        Assert.Equal(0, fake.Deletes);
        Assert.Empty(fake.CleanupIndexCommands);
    }

    [Theory]
    [InlineData("Reel", true)]
    [InlineData("Story", false)]
    [InlineData("Photo", false)]
    public async Task ConfirmedMediaIndexCleanupIsScopedToJobPaths(string type, bool native)
    {
        var job = await Job();
        job.PostType = type;
        job.NativeReelAlbum = native;
        Confirm(job);
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        fake.CleanupIndexResults.Enqueue("Row: 0 _id=42, _data=" + job.RemotePath);
        await new PFlowRunner(ld, _store).CleanupRemoteAsync(job, null, default);
        Assert.Equal("Verified", job.RemoteCleanupState);
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.True(File.Exists(job.SourcePath));
        Assert.Equal(3, fake.CleanupIndexCommands.Count);
        var delete = Assert.Single(fake.CleanupIndexCommands, c => c.Contains("content delete"));
        Assert.Contains(job.RemotePath, delete);
        Assert.Contains(job.RemotePath.Replace("/sdcard/", "/storage/emulated/0/"), delete);
        Assert.DoesNotContain("bucket_display_name", delete);
        Assert.DoesNotContain(" LIKE ", delete);
        if (type == "Story" || native)
            Assert.Contains("/" + (type == "Story" ? "Story_" : "Reel_") + job.JobId + "'", delete);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Error: provider unavailable")]
    public async Task UnreadableMediaIndexKeepsCleanupPending(string output)
    {
        var job = await Job();
        Confirm(job);
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        fake.CleanupIndexResults.Enqueue(output);
        await new PFlowRunner(ld, _store).CleanupRemoteAsync(job, null, default);
        Assert.Equal("Pending", job.RemoteCleanupState);
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.DoesNotContain(fake.CleanupIndexCommands, c => c.Contains("content delete"));
    }

    [Fact]
    public async Task PersistingIndexIsPendingAndCanBeRecoveredIdempotently()
    {
        var job = await Job();
        Confirm(job);
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        for (var i = 0; i < 4; i++)
            fake.CleanupIndexResults.Enqueue("Row: 0 _id=42, _data=" + job.RemotePath);
        var runner = new PFlowRunner(ld, _store);
        await runner.CleanupRemoteAsync(job, null, default);
        Assert.Equal("Pending", job.RemoteCleanupState);
        Assert.Equal(3, fake.CleanupIndexCommands.Count(c => c.Contains("content delete")));
        await runner.CleanupRemoteAsync(job, null, default);
        Assert.Equal("Verified", job.RemoteCleanupState);
        Assert.DoesNotContain("Emulator cleanup:", job.Error);
    }

    [Fact]
    public async Task LegacyNotificationConfirmationCannotDeleteSourceOrRemote()
    {
        var job = await Job(delete: true);
        await _store.ReserveAsync(job);
        Confirm(job);
        job.Evidence =
            "Facebook media-upload notification: Facebook upload complete; composer closed";
        await _store.SaveAsync(job);
        var ld = FakeLd.Create(job);
        var runner = new PFlowRunner(ld, _store);
        await runner.CleanupRemoteAsync(job, null, default);
        await runner.DeleteSharedSourceAsync(job, [job], default);
        Assert.True(File.Exists(job.SourcePath));
        Assert.Equal(0, ((FakeLd)(object)ld).Deletes);
        Assert.Equal("Held for publication review", job.RemoteCleanupState);
        Assert.Equal("Held for publication review", job.LocalDeleteState);
    }

    [Fact]
    public async Task SharedSourceRetainedWhenAnotherConsumerHasOnlyUploadNotification()
    {
        var good = await Job(delete: true);
        var uncertain = await Job("other-page", delete: true);
        await _store.ReserveAsync(good);
        await _store.ReserveAsync(uncertain);
        Confirm(good);
        Confirm(uncertain);
        uncertain.Evidence = "Facebook media-upload notification: Facebook upload complete";
        await _store.SaveAsync(good);
        await _store.SaveAsync(uncertain);
        await new PFlowRunner(FakeLd.Create(good), _store).DeleteSharedSourceAsync(
            good,
            [good, uncertain],
            default
        );
        Assert.True(File.Exists(good.SourcePath));
        Assert.Equal("Waiting for all shared consumers", good.LocalDeleteState);
    }

    [Theory]
    [InlineData(1, false, 2, 1, "Verified")]
    [InlineData(5, false, 3, 0, "Pending")]
    [InlineData(0, true, 1, 0, "Pending")]
    public async Task CleanupRetriesOnlyMissingAssignedInstance(
        int missing,
        bool renamed,
        int scans,
        int deletes,
        string state
    )
    {
        var job = await Job();
        Confirm(job);
        job.OwnerUid = "123";
        var root = Path.Combine(_dir, "ld");
        Directory.CreateDirectory(Path.Combine(root, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(root, "vms", "leidian0"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "vms", "config", "leidian0.config"),
            "{\"propertySettings.phoneAndroidId\":\"test-device\"}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "vms", "leidian0", "data.vmdk"),
            "test-disk"
        );
        job.AccountBinding = new(
            PFlowDeviceRouting.ReadIdentity(root, 0),
            0,
            "LD",
            job.AppPackage,
            job.OwnerUid
        );
        job.Error = "Other warning | Emulator cleanup: earlier missing scan";
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        fake.MissingInstanceScans = missing;
        fake.RenamedInstance = renamed;
        await new PFlowRunner(ld, _store).CleanupRemoteAsync(job, root, default);
        Assert.Equal(scans, fake.InstanceScans);
        Assert.Equal(deletes, fake.Deletes);
        Assert.Equal(state, job.RemoteCleanupState);
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.StartsWith("Other warning", job.Error);
        if (state == "Verified")
            Assert.DoesNotContain("Emulator cleanup:", job.Error);
    }
}
