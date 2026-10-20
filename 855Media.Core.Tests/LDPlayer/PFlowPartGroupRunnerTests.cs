using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Theory]
    [InlineData(PFlowState.Failed)]
    [InlineData(PFlowState.Unknown)]
    [InlineData(PFlowState.Submitted)]
    public async Task PartOrderHoldDoesNotTouchEmulatorOrSubmit(PFlowState state)
    {
        var first = await Job();
        first.SourcePath = Path.Combine(_dir, "A Part 1.mp4");
        Assert.True(await _store.ReserveAsync(first));
        first.State = state;
        await _store.SaveAsync(first);
        var part = PFlowPartGroups.Parse(Path.Combine(_dir, "A Part 2.mp4"))!;
        var second = new PFlowJob
        {
            SourcePath = part.Path,
            PartGroupKey = part.GroupKey,
            PartNumber = 2,
            TargetId = first.TargetId,
            OwnerUid = first.OwnerUid,
            State = PFlowState.Queued,
        };
        var ld = FakeLd.Create(second);
        var spy = (FakeLd)(object)ld;
        var verified = false;
        string step = "";
        var result = await new PFlowRunner(ld, _store).RunAsync(
            second,
            _ =>
            {
                verified = true;
                return Task.FromResult(true);
            },
            s =>
            {
                step = s;
                return Task.CompletedTask;
            },
            null,
            default
        );
        Assert.False(result);
        Assert.False(verified);
        Assert.Contains("confirmed Part 1", step);
        Assert.Equal(PFlowState.Queued, second.State);
        Assert.Equal(0, spy.InstanceScans);
        Assert.Equal(0, spy.Transfers);
        Assert.Equal(0, spy.ShareTaps);
        Assert.Single(await _store.ReadAsync());
    }

    [Fact]
    public void PartOrderHoldRestoresForGuardedReplanningAndSurvivesQueueRestart()
    {
        var part = PFlowPartGroups.Parse(Path.Combine(_dir, "A Part 2.mp4"))!;
        var store = new PFlowDispatchQueueStore(Path.Combine(_dir, "queue.db"));
        var row = new AutoPostTaskItem
        {
            FilePath = part.Path,
            PartGroupKey = part.GroupKey,
            PartNumber = 2,
            Status = "Held (Part order): Waiting for Part 1",
        };
        store.Save(row);
        var restored = new PFlowDispatchQueueStore(store.FilePath).Read().Single();
        PFlowTaskRouting.RestoreUnmatchedQueueRow(restored);
        Assert.Equal("Queued", restored.Status);
        Assert.Equal(part.GroupKey, restored.PartGroupKey);
        Assert.Equal(2, restored.PartNumber);
        restored.Status = "Unknown (Publication needs review)";
        PFlowTaskRouting.RestoreUnmatchedQueueRow(restored);
        Assert.Equal("Unknown (Publication needs review)", restored.Status);
    }
}
