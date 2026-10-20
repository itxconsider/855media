using System;
using System.IO;
using System.Linq;
using _855Media.Core.LDPlayer;
using Microsoft.Data.Sqlite;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowDispatchQueueStoreTests : IDisposable
{
    [Fact]
    public void BatchSavePreservesOrderMetadataAndExistingRows()
    {
        var store = new PFlowDispatchQueueStore(FilePath);
        var prior = new AutoPostTaskItem { Title = "Keep existing" };
        store.Save(prior);
        var rows = Enumerable
            .Range(1, 250)
            .Select(n => new AutoPostTaskItem
            {
                Title = "Caption " + n,
                PartGroupKey = "series",
                PartNumber = n,
            })
            .ToArray();
        store.SaveBatch(rows);
        var restored = new PFlowDispatchQueueStore(FilePath).Read();
        Assert.Equal(
            new[] { prior.QueueId }.Concat(rows.Select(r => r.QueueId)),
            restored.Select(r => r.QueueId)
        );
        Assert.Equal(Enumerable.Range(1, 250), restored.Skip(1).Select(r => r.PartNumber));
        rows[0].Title = "Updated";
        store.SaveBatch([rows[0]]);
        Assert.Equal("Updated", store.Read()[1].Title);
        Assert.Equal(251, store.Read().Count);
    }

    [Fact]
    public void InvalidBatchDoesNotWriteAnyRows()
    {
        var store = new PFlowDispatchQueueStore(FilePath);
        var prior = new AutoPostTaskItem();
        store.Save(prior);
        Assert.Throws<InvalidDataException>(() =>
            store.SaveBatch([new AutoPostTaskItem(), new AutoPostTaskItem { QueueId = "invalid" }])
        );
        Assert.Equal(prior.QueueId, Assert.Single(store.Read()).QueueId);
    }

    [Fact]
    public void DatabaseFailureRollsBackTheWholeBatch()
    {
        var store = new PFlowDispatchQueueStore(FilePath);
        var prior = new AutoPostTaskItem { Title = "Existing" };
        store.Save(prior);
        using (var connection = new SqliteConnection($"Data Source={FilePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TRIGGER reject_test_row BEFORE INSERT ON dispatch_queue WHEN json_extract(NEW.json,'$.Title')='reject' BEGIN SELECT RAISE(ABORT,'test rollback'); END";
            command.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() =>
            store.SaveBatch([
                new AutoPostTaskItem { Title = "Inserted before error" },
                new AutoPostTaskItem { Title = "reject" },
            ])
        );
        Assert.Equal(prior.QueueId, Assert.Single(store.Read()).QueueId);
    }

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "dispatch-queue-" + Guid.NewGuid().ToString("N")
    );
    private string FilePath => Path.Combine(directory, "queue.db");

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Fact]
    public void NewStoreInstanceRestoresEntireQueueAndBinding()
    {
        var store = new PFlowDispatchQueueStore(FilePath);
        var row = new AutoPostTaskItem
        {
            Index = 1,
            Title = "Caption 🎬 #test",
            FilePath = @"D:\media\one.mp4",
            DeviceName = "LD-11",
            App = "FB-01",
            Type = "Reel",
            Page = "Example Page",
            PageId = "123",
            OwnerUid = "456",
            AccountBinding = new("instance", 11, "LD-11", "com.facebook.katana", "456"),
            Status = "Queued",
        };
        store.Save(row);
        var restored = new PFlowDispatchQueueStore(FilePath).Read().Single();
        Assert.Equal(row.QueueId, restored.QueueId);
        Assert.Equal(row.Title, restored.Title);
        Assert.Equal(row.FilePath, restored.FilePath);
        Assert.Equal(row.AccountBinding, restored.AccountBinding);
        Assert.Equal(row.PageId, restored.PageId);
        Assert.Equal(row.OwnerUid, restored.OwnerUid);
        Assert.Equal("Queued", restored.Status);
    }

    [Fact]
    public void UpdatesDoNotDuplicateOrReorderRowsAndClearSurvivesRestart()
    {
        var store = new PFlowDispatchQueueStore(FilePath);
        var first = new AutoPostTaskItem { Title = "first" };
        var second = new AutoPostTaskItem { Title = "second" };
        store.Save(first);
        store.Save(second);
        first.Status = "Unknown (needs review)";
        first.PFlowJobId = "job-1";
        store.Save(first);
        var rows = new PFlowDispatchQueueStore(FilePath).Read();
        Assert.Equal(new[] { first.QueueId, second.QueueId }, rows.Select(r => r.QueueId));
        Assert.Equal(first.Status, rows[0].Status);
        Assert.Equal("job-1", rows[0].PFlowJobId);
        store.Remove(first.QueueId);
        Assert.Equal(
            second.QueueId,
            Assert.Single(new PFlowDispatchQueueStore(FilePath).Read()).QueueId
        );
        store.Clear();
        Assert.Empty(new PFlowDispatchQueueStore(FilePath).Read());
    }

    [Theory]
    [InlineData("Queued", "Queued")]
    [InlineData("Confirmed: cleanup pending", "Confirmed: cleanup pending")]
    [InlineData("Transferring and verifying media", "Unknown (Interrupted run; review History)")]
    [InlineData("SubmitRequested", "Unknown (Interrupted run; review History)")]
    public void InterruptedRowsNeverBecomeQueuedWithoutHistoryProof(string status, string expected)
    {
        var row = new AutoPostTaskItem { Status = status };
        Assert.Equal(expected, PFlowDispatchQueueStore.RestoredStatus(row));
        row.PFlowJobId = "recorded-job";
        Assert.StartsWith("Unknown", PFlowDispatchQueueStore.RestoredStatus(row));
    }

    [Fact]
    public void HistoryReconciliationHoldsUnknownAndWeakConfirmation()
    {
        var job = new PFlowJob { State = PFlowState.Unknown };
        Assert.StartsWith("Unknown", PFlowDispatchQueueStore.HistoryStatus(job));
        job.State = PFlowState.Confirmed;
        job.Evidence = "Facebook media-upload notification: upload complete";
        Assert.StartsWith("Unknown", PFlowDispatchQueueStore.HistoryStatus(job));
        job.Evidence = "Target profile Reel verified: matching content";
        Assert.Equal("Confirmed", PFlowDispatchQueueStore.HistoryStatus(job));
        job.State = PFlowState.CancelledBeforeSubmission;
        job.Error = "Previous process ended before submission";
        Assert.Equal("Queued", PFlowDispatchQueueStore.HistoryStatus(job));
        job.SubmittedUtc = DateTime.UtcNow;
        Assert.NotEqual("Queued", PFlowDispatchQueueStore.HistoryStatus(job));
    }

    [Fact]
    public void CorruptSavedRowIsReportedWithoutClearingDatabase()
    {
        var store = new PFlowDispatchQueueStore(FilePath);
        store.Save(new AutoPostTaskItem());
        using (var connection = new SqliteConnection("Data Source=" + FilePath + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dispatch_queue SET json='broken-json'";
            command.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(FilePath);
        Assert.ThrowsAny<Exception>(() => store.Read());
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
    }
}
