using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowMediaAvailabilityTests
{
    [Fact]
    public async Task ConfirmedCleanupRetainsOriginalTotalAcrossRefreshesAndSharedConsumers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "retained-total-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var files = new[] { "a.mp4", "b.mp4", "c.mp4", "d.mp4" }
                .Select(n => Path.Combine(folder, n))
                .ToArray();
            foreach (var file in files)
                await File.WriteAllTextAsync(file, file);
            var first = await Confirmed(files[0], "1");
            var second = await Confirmed(files[1], "1");
            var target = new FacebookPageTarget { PageId = "1", VideoReelPath = folder };
            var history = new[] { first, second };
            Assert.Equal(
                "2 / 4",
                (await new PFlowMediaAvailability().ReadAsync(target, "Reel", history)).Display
            );
            File.Delete(files[0]);
            File.Delete(files[1]);
            first.LocalDeleteState = second.LocalDeleteState = "Verified";
            var shared = await Confirmed(files[2], "2"); // A separate consumer of the same source folder.
            var duplicate = new PFlowJob
            {
                TargetId = "2",
                SourcePath = first.SourcePath,
                PostType = "Reel",
                State = PFlowState.Confirmed,
                DeleteRequested = true,
                LocalDeleteState = "Verified",
                Evidence = "Observed publication states completed",
            };
            var result = await new PFlowMediaAvailability().ReadAsync(
                target,
                "Reel",
                new[] { first, second, duplicate, first, shared }
            );
            Assert.Equal("2 / 4", result.Display);
            Assert.Equal(2, result.Posted);
            Assert.Equal(2, result.Files.Count);
            target.PageId = "2";
            result = await new PFlowMediaAvailability().ReadAsync(
                target,
                "Reel",
                new[] { first, second, duplicate, shared }
            );
            Assert.Equal("1 / 4", result.Display); // Shared historical removals included only once.
            Assert.Equal(2, result.Posted); // One retained file and one consumed file posted to this target.
            target.VideoReelPath = Path.Combine(folder, "other");
            Directory.CreateDirectory(target.VideoReelPath);
            Assert.Equal(
                "0 / 0",
                (await new PFlowMediaAvailability().ReadAsync(target, "Reel", history)).Display
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(PFlowState.Unknown, "Verified", true)]
    [InlineData(PFlowState.Failed, "Verified", true)]
    [InlineData(PFlowState.Confirmed, "Keep original", false)]
    [InlineData(PFlowState.Confirmed, "Pending", true)]
    public async Task UnverifiedOrManualRemovalsCannotInventOriginalInventory(
        PFlowState state,
        string cleanup,
        bool requested
    )
    {
        var folder = Path.Combine(Path.GetTempPath(), "excluded-total-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var job = new PFlowJob
            {
                TargetId = "1",
                SourcePath = Path.Combine(folder, "removed.mp4"),
                State = state,
                DeleteRequested = requested,
                LocalDeleteState = cleanup,
            };
            var result = await new PFlowMediaAvailability().ReadAsync(
                new() { PageId = "1", VideoReelPath = folder },
                "Reel",
                new[] { job }
            );
            Assert.Equal("0 / 0", result.Display);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ConsumedSingleFileIsZeroOfOneAndMissingUnrelatedSourcesStayUnavailable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "single-total-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "a.mp4");
            await File.WriteAllTextAsync(file, "a");
            var job = await Confirmed(file, "1");
            File.Delete(file);
            job.LocalDeleteState = "Verified; caption retained";
            var target = new FacebookPageTarget { PageId = "1", VideoReelPath = file };
            Assert.Equal(
                "0 / 1",
                (
                    await new PFlowMediaAvailability().ReadAsync(target, "Reel", new[] { job })
                ).Display
            );
            target.VideoLongPath = Path.Combine(folder, "missing-folder");
            Assert.Equal(
                "Unavailable",
                (
                    await new PFlowMediaAvailability().ReadAsync(target, "Reel", new[] { job })
                ).Display
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static async Task<PFlowJob> Confirmed(string file, string target)
    {
        var job = new PFlowJob
        {
            TargetId = target,
            SourcePath = file,
            State = PFlowState.Confirmed,
            DeleteRequested = true,
            Evidence = "Observed publication states completed",
        };
        await PFlowJob.FingerprintAsync(job, default);
        return job;
    }

    [Fact]
    public async Task SharedFolderCountsArePerPageAndDeduplicateHistory()
    {
        var folder = Path.Combine(Path.GetTempPath(), "availability-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var a = Path.Combine(folder, "a.mp4");
            var b = Path.Combine(folder, "b.mp4");
            var c = Path.Combine(folder, "c.mp4");
            await File.WriteAllTextAsync(a, "aaa");
            await File.WriteAllTextAsync(b, "bbb");
            await File.WriteAllTextAsync(c, "ccc");
            var posted = new PFlowJob
            {
                TargetId = "1",
                SourcePath = a,
                State = PFlowState.Confirmed,
            };
            var failed = new PFlowJob
            {
                TargetId = "1",
                SourcePath = b,
                State = PFlowState.Failed,
            };
            var held = new PFlowJob
            {
                TargetId = "1",
                SourcePath = c,
                State = PFlowState.Unknown,
            };
            foreach (var job in new[] { posted, failed, held })
                await PFlowJob.FingerprintAsync(job, CancellationToken.None);
            var service = new PFlowMediaAvailability();
            var target = new FacebookPageTarget { PageId = "1", VideoReelPath = folder };
            var result = await service.ReadAsync(
                target,
                "Reel",
                new[] { posted, posted, failed, held }
            );
            Assert.Equal("1 / 3", result.Display);
            Assert.Equal(1, result.Posted);
            Assert.Equal(1, result.Held);
            Assert.Equal(b, Assert.Single(result.Files));
            target.PageId = "2";
            Assert.Equal(
                "3 / 3",
                (await service.ReadAsync(target, "Reel", new[] { posted, held })).Display
            );
            target.PageId = "1";
            File.Delete(a);
            Assert.Equal(
                "1 / 2",
                (await service.ReadAsync(target, "Reel", new[] { posted, held })).Display
            );
            held.State = PFlowState.Failed;
            Assert.Equal(
                "2 / 2",
                (await service.ReadAsync(target, "Reel", new[] { posted, held })).Display
            );
            File.Copy(b, a);
            failed.State = PFlowState.Confirmed;
            Assert.Equal(
                "1 / 3",
                (await service.ReadAsync(target, "Reel", new[] { failed })).Display
            );
            await File.WriteAllTextAsync(a, "new");
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddSeconds(5));
            Assert.Equal(
                "2 / 3",
                (await service.ReadAsync(target, "Reel", new[] { failed })).Display
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
