using System;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowPublishedCountsTests
{
    private static PFlowJob Job(string type, PFlowState state = PFlowState.Confirmed) =>
        new()
        {
            TargetId = "page",
            PostType = type,
            State = state,
        };

    [Fact]
    public void CountsOnlyConfirmedReelsAndPhotosIncludingLegacyAliases()
    {
        var history = new[]
        {
            Job("Reel"),
            Job("Video"),
            Job("reel"),
            Job("Picture"),
            Job("Photo"),
            Job("photo"),
            Job("Story"),
            Job("Reel", PFlowState.Failed),
            Job("Photo", PFlowState.CancelledBeforeSubmission),
            Job("Reel", PFlowState.Queued),
            Job("Reel", PFlowState.Submitted),
            Job("Photo", PFlowState.SubmitRequested),
            Job("Reel", PFlowState.Unknown),
        };
        Assert.Equal(
            new PFlowPublishedCounts(3, 3),
            PFlowPublishedCounts.ForTarget(history, "page", false)
        );
    }

    [Fact]
    public void SeparatesTargetsAndProfilesAndDoesNotCountDuplicateRecords()
    {
        var reel = Job("Reel");
        var other = Job("Photo");
        other.TargetId = "other";
        var profile = Job("Photo");
        profile.IsProfile = true;
        var history = new[] { reel, reel, other, profile };
        Assert.Equal(
            new PFlowPublishedCounts(1, 0),
            PFlowPublishedCounts.ForTarget(history, "page", false)
        );
        Assert.Equal(
            new PFlowPublishedCounts(0, 1),
            PFlowPublishedCounts.ForTarget(history, "page", true)
        );
        Assert.Equal(
            new PFlowPublishedCounts(0, 0),
            PFlowPublishedCounts.ForTarget(history, "", false)
        );
    }

    [Fact]
    public void TotalsSurviveDaysDeviceChangesAndSourceDeletion()
    {
        var older = Job("Reel");
        older.ConfirmedUtc = DateTime.UtcNow.AddDays(-365);
        older.DeviceIndex = 1;
        older.SourcePath = "file-no-longer-on-disk.mp4";
        var newer = Job("Reel");
        newer.ConfirmedUtc = DateTime.UtcNow;
        newer.DeviceIndex = 37;
        newer.OwnerUid = "new-owner";
        Assert.Equal(
            new PFlowPublishedCounts(2, 0),
            PFlowPublishedCounts.ForTarget([older, newer], "page", false)
        );
        Assert.Equal(
            new PFlowPublishedCounts(0, 0),
            PFlowPublishedCounts.ForTarget([], "page", false)
        );
    }
}
