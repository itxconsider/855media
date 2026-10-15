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
