using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowPartGroupsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneGroupLimitAppliesToSavedPendingSeriesAfterClearingQueue(bool random)
    {
        var paths = Series("A").Concat(Series("B")).Concat(Series("C")).ToArray();
        var original = PFlowPartGroups.Plan(paths, paths, [], [], [], false, 1, 3, 7);
        Assert.Equal(3, original.PendingGroups.Count);
        Assert.Equal(7, original.Files.Count);
        var restored = JsonSerializer.Deserialize<ReelPartGroup[]>(
            JsonSerializer.Serialize(original.PendingGroups)
        )!;
        var reduced = PFlowPartGroups.Plan(paths, paths, [], [], restored, random, 1, 1, 50);
        Assert.Equal(Series("A"), reduced.Files.Select(p => p.Path));
        Assert.Equal(3, reduced.PendingGroups.Count);
        Assert.Contains(reduced.Warnings, w => w.Contains("2 unfinished series deferred"));
        // Repeated preparation cannot bypass the limit by jumping past queued parts.
        var repeated = PFlowPartGroups.Plan(
            paths,
            paths,
            Series("A"),
            [],
            reduced.PendingGroups,
            random,
            1,
            1,
            50
        );
        Assert.Empty(repeated.Files);
        Assert.Equal(3, repeated.PendingGroups.Count);
        var next = PFlowPartGroups.Plan(
            paths,
            paths.Except(Series("A")),
            [],
            Series("A").Select(Confirm),
            reduced.PendingGroups,
            random,
            1,
            1,
            50
        );
        Assert.Equal(Series("B"), next.Files.Select(p => p.Path));
    }

    [Fact]
    public void ReducedLimitKeepsDailyCapacityAndDefersOtherSavedSeries()
    {
        var paths = Series("A").Concat(Series("B")).ToArray();
        var saved = PFlowPartGroups.Plan(paths, paths, [], [], [], false, 1, 2, 50).PendingGroups;
        var reduced = PFlowPartGroups.Plan(paths, paths, [], [], saved, true, 1, 1, 1);
        Assert.Equal(File("A Part 1"), Assert.Single(reduced.Files).Path);
        Assert.Equal(2, reduced.Waiting);
        Assert.Equal(2, reduced.PendingGroups.Count);
    }

    [Fact]
    public void ChangedConfirmedPartCannotReuseOldSeriesProof()
    {
        var paths = Series("A");
        var plan = PFlowPartGroups.Plan(paths, paths, [], [Confirm(paths[0])], [], true, 1, 1, 50);
        Assert.Empty(plan.Files);
        Assert.Contains(plan.Warnings, w => w.Contains("changed content"));
    }

    [Fact]
    public void FullyConfirmedSeriesAllowsAnotherGroupOnNextPreparation()
    {
        var paths = Series("A");
        var original = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 50);
        var available = Series("B");
        var next = PFlowPartGroups.Plan(
            paths.Concat(available),
            available,
            [],
            paths.Select(Confirm),
            original.PendingGroups,
            true,
            1,
            1,
            50
        );
        Assert.Equal(available, next.Files.Select(p => p.Path));
    }

    [Fact]
    public void HistorySelectionRequiresTheExactTargetBindingAndDevice()
    {
        var binding = new PFlowAccountBinding(
            "instance",
            2,
            "LD-2",
            "com.facebook.katana",
            "owner"
        );
        var target = new FacebookPageTarget
        {
            PageId = "page",
            UserUid = "owner",
            AccountBinding = binding,
        };
        var job = Confirm(File("A Part 1"));
        job.AccountBinding = binding;
        Assert.True(PFlowPartGroups.SameTarget(job, target, 2));
        Assert.False(PFlowPartGroups.SameTarget(job, target, 3));
        job.AccountBinding = binding with { OwnerUid = "other" };
        Assert.False(PFlowPartGroups.SameTarget(job, target, 2));
    }

    [Fact]
    public void StandaloneVideosRemainIndividualGroupsAndTextFilesAreIgnored()
    {
        var paths = new[] { File("Standalone"), Path.ChangeExtension(File("Standalone"), ".txt") };
        var plan = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 50);
        Assert.Equal(paths[0], Assert.Single(plan.Files).Path);
        Assert.Empty(plan.Files[0].GroupKey);
        Assert.Equal(0, plan.Files[0].Number);
    }

    private static string File(string name) =>
        Path.GetFullPath(Path.Combine("part-tests", name + ".mp4"));

    private static string[] Series(string name, int count = 3) =>
        Enumerable.Range(1, count).Select(n => File($"{name} Part {n}")).ToArray();

    private static PFlowJob Confirm(string path) =>
        new()
        {
            SourcePath = path,
            State = PFlowState.Confirmed,
            TargetId = "page",
            OwnerUid = "owner",
            DeviceIndex = 2,
        };

    [Theory]
    [InlineData("A Part 1", "A Part 2")]
    [InlineData("Part 1 A", "Part 2 A")]
    [InlineData("A_Part01", "A-Part-02")]
    [InlineData("[013]_Part1_original1x", "[013]_Part2_original1x")]
    [InlineData("a PART 1", "A part 2")]
    public void SupportsPartPositionsSeparatorsAndCase(string first, string second)
    {
        var one = PFlowPartGroups.Parse(File(first))!;
        var two = PFlowPartGroups.Parse(File(second))!;
        Assert.Equal(one.GroupKey, two.GroupKey);
        Assert.Equal(1, one.Number);
        Assert.Equal(2, two.Number);
    }

    [Theory]
    [InlineData("Part 1")]
    [InlineData("A Part 0")]
    [InlineData("A Part 10001")]
    [InlineData("A Part 1 Part 2")]
    [InlineData("Apartment 1")]
    [InlineData("A EP1")]
    [InlineData("Episode 2 A")]
    public void RejectsAmbiguousOrUnsupportedMarkers(string name) =>
        Assert.Null(PFlowPartGroups.Parse(File(name)));

    [Fact]
    public void TextFilesAreNotMediaAndDifferentFoldersAreDifferentSeries()
    {
        Assert.Null(PFlowPartGroups.Parse(Path.ChangeExtension(File("A Part 1"), ".txt")));
        Assert.NotEqual(
            PFlowPartGroups.Parse(File("A Part 1"))!.GroupKey,
            PFlowPartGroups.Parse(Path.Combine("different", "A Part 1.mp4"))!.GroupKey
        );
    }

    [Fact]
    public void RandomSelectsOneWholeSeriesInNumericOrder()
    {
        var paths = Series("A", 10).Concat(Series("B")).Reverse().ToArray();
        var plan = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 50, new Random(4));
        Assert.Single(plan.Files.Select(f => f.GroupKey).Distinct());
        Assert.Equal(Enumerable.Range(1, plan.Files.Count), plan.Files.Select(f => f.Number));
        Assert.True(plan.Files.Count is 3 or 10);
        Assert.Single(plan.PendingGroups);
    }

    [Fact]
    public void SequentialRangeCountsGroups()
    {
        var paths = Series("A").Concat(Series("B")).ToArray();
        var plan = PFlowPartGroups.Plan(paths, paths, [], [], [], false, 2, 2, 50);
        Assert.Equal(Series("B"), plan.Files.Select(f => f.Path));
    }

    [Theory]
    [InlineData("A Part 2", "A Part 3")]
    [InlineData("A Part 1", "A Part 3")]
    [InlineData("A Part 1", "A Part 01")]
    public void MissingOrDuplicateNumbersWarnAndCannotQueue(string first, string second)
    {
        var paths = new[] { File(first), File(second) };
        var plan = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 50);
        Assert.Empty(plan.Files);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void ConfirmedMissingEarlierPartAllowsContinuation()
    {
        var paths = Series("A");
        var plan = PFlowPartGroups.Plan(
            paths.Skip(1),
            paths.Skip(1),
            [],
            [Confirm(paths[0])],
            [],
            true,
            1,
            1,
            50
        );
        Assert.Equal(paths.Skip(1), plan.Files.Select(f => f.Path));
    }

    [Fact]
    public void DailyCapacityPersistsSeriesAndResumesBeforeAnotherRandomGroup()
    {
        var paths = Series("A");
        var first = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 1);
        Assert.Equal(paths.Take(1), first.Files.Select(f => f.Path));
        Assert.Equal(2, first.Waiting);
        var saved = JsonSerializer.Deserialize<ReelPartGroup[]>(
            JsonSerializer.Serialize(first.PendingGroups)
        )!;
        var nextPaths = paths.Skip(1).Concat(Series("B")).ToArray();
        var next = PFlowPartGroups.Plan(
            nextPaths,
            nextPaths,
            [],
            [Confirm(paths[0])],
            saved,
            true,
            1,
            1,
            1
        );
        Assert.Equal(paths[1], Assert.Single(next.Files).Path);
        Assert.Equal(1, next.Waiting);
        var last = PFlowPartGroups.Plan(
            nextPaths,
            paths.Skip(2),
            [],
            [Confirm(paths[0]), Confirm(paths[1])],
            next.PendingGroups,
            true,
            1,
            1,
            50
        );
        Assert.Equal(paths[2], Assert.Single(last.Files).Path);
    }

    [Fact]
    public void RepeatedPreparationDoesNotRepeatReservedPartsOrChooseANewGroup()
    {
        var paths = Series("A");
        var first = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 50);
        var all = paths.Concat(Series("B")).ToArray();
        var next = PFlowPartGroups.Plan(all, all, paths, [], first.PendingGroups, true, 1, 1, 50);
        Assert.Empty(next.Files);
        Assert.Single(next.PendingGroups);
    }

    [Fact]
    public void ConflictingReservationDoesNotSelectAnUnreservedTail()
    {
        var paths = Series("A");
        Assert.Empty(PFlowPartGroups.Plan(paths, paths, [paths[0]], [], [], true, 1, 1, 50).Files);
    }

    [Fact]
    public void MissingPendingFileHoldsSeriesAndDoesNotJumpToAnotherGroup()
    {
        var paths = Series("A");
        var pending = PFlowPartGroups.Plan(paths, paths, [], [], [], true, 1, 1, 1).PendingGroups;
        var remaining = new[] { paths[2] }.Concat(Series("B")).ToArray();
        var next = PFlowPartGroups.Plan(
            remaining,
            remaining,
            [],
            [Confirm(paths[0])],
            pending,
            true,
            1,
            1,
            50
        );
        Assert.Empty(next.Files);
        Assert.NotEmpty(next.Warnings);
    }

    [Theory]
    [InlineData(PFlowState.Failed)]
    [InlineData(PFlowState.Unknown)]
    [InlineData(PFlowState.SubmitRequested)]
    [InlineData(PFlowState.Submitted)]
    [InlineData(PFlowState.Queued)]
    public void UnconfirmedEarlierPartCannotUnlockNextPart(PFlowState state)
    {
        var paths = Series("A");
        var part = PFlowPartGroups.Parse(paths[1])!;
        var job = Confirm(paths[1]);
        job.State = PFlowState.Queued;
        job.PartGroupKey = part.GroupKey;
        job.PartNumber = 2;
        var earlier = Confirm(paths[0]);
        earlier.State = state;
        Assert.Contains("Part 1", PFlowPartGroups.BlockingReason(job, [earlier]));
        earlier.State = PFlowState.Confirmed;
        Assert.Empty(PFlowPartGroups.BlockingReason(job, [earlier]));
    }

    [Fact]
    public void OtherTargetOwnerDeviceOrBindingCannotSupplyConfirmation()
    {
        var paths = Series("A");
        var part = PFlowPartGroups.Parse(paths[1])!;
        var job = Confirm(paths[1]);
        job.PartGroupKey = part.GroupKey;
        job.PartNumber = 2;
        foreach (
            var change in new Action<PFlowJob>[]
            {
                j => j.TargetId = "other",
                j => j.OwnerUid = "other",
                j => j.DeviceIndex = 3,
                j => j.AppPackage = "other",
                j => j.IsProfile = true,
            }
        )
        {
            var earlier = Confirm(paths[0]);
            change(earlier);
            Assert.NotEmpty(PFlowPartGroups.BlockingReason(job, [earlier]));
        }
    }

    [Fact]
    public void MetadataSurvivesQueueAndHistoryJsonWithoutChangingCaption()
    {
        var part = PFlowPartGroups.Parse(File("A Part 1"))!;
        var row = new AutoPostTaskItem
        {
            PartGroupKey = part.GroupKey,
            PartNumber = 1,
            Title = "Original caption",
            FilePath = part.Path,
        };
        var restored = JsonSerializer.Deserialize<AutoPostTaskItem>(JsonSerializer.Serialize(row))!;
        Assert.Equal(row.PartGroupKey, restored.PartGroupKey);
        Assert.Equal("Original caption", restored.Title);
        Assert.Contains("Part 1", restored.PartGroupDisplay);
        var job = new PFlowJob
        {
            PartGroupKey = part.GroupKey,
            PartNumber = 1,
            SourcePath = part.Path,
        };
        Assert.Empty(
            PFlowPartGroups.BlockingReason(
                JsonSerializer.Deserialize<PFlowJob>(JsonSerializer.Serialize(job))!,
                []
            )
        );
        job.SourcePath = File("A Part 2");
        Assert.NotEmpty(PFlowPartGroups.BlockingReason(job, []));
        Assert.False(JsonSerializer.Deserialize<FacebookPageTarget>("{}")!.KeepReelPartsTogether);
        var target = new FacebookPageTarget
        {
            KeepReelPartsTogether = true,
            PendingReelPartGroups = [new() { Key = part.GroupKey, Files = [part.Path] }],
        };
        var saved = JsonSerializer.Deserialize<FacebookPageTarget>(
            JsonSerializer.Serialize(target)
        )!;
        Assert.True(saved.KeepReelPartsTogether);
        Assert.Single(saved.PendingReelPartGroups);
    }
}
