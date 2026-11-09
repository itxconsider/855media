using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowHistoryPresentationTests
{
    [Fact]
    public void ReviewEvidenceTakesPriorityOverConfirmedAndRetryFilters()
    {
        var uncertain = new PFlowJob
        {
            State = PFlowState.Confirmed,
            Evidence = "",
            CreatedUtc = DateTime.UtcNow,
        };
        Assert.Equal("Needs review", PFlowHistoryPresentation.Outcome(uncertain));
        Assert.Empty(PFlowHistoryPresentation.Filter([uncertain], "", 2, 0, DateTime.Now));
        Assert.Single(PFlowHistoryPresentation.Filter([uncertain], "", 1, 0, DateTime.Now));
        var failure = new PFlowJob { State = PFlowState.Failed, SubmittedUtc = DateTime.UtcNow };
        Assert.Empty(PFlowHistoryPresentation.Filter([failure], "", 3, 0, DateTime.Now));
    }

    [Fact]
    public void SearchCombinesDeviceTargetCaptionAndSourceWithoutMutatingHistory()
    {
        var job = new PFlowJob
        {
            DeviceIndex = 56,
            TargetName = "Z Outdoors+",
            Caption = "Shelter in forest",
            SourcePath = "A Part 2.mp4",
            PartNumber = 2,
            State = PFlowState.Failed,
        };
        Assert.Single(
            PFlowHistoryPresentation.Filter([job], "LD-56 outdoors forest", 3, 0, DateTime.Now)
        );
        Assert.Empty(PFlowHistoryPresentation.Filter([job], "LD-55 forest", 0, 0, DateTime.Now));
        Assert.Equal(PFlowState.Failed, job.State);
        Assert.True(job.CanRetry);
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 6, true)]
    [InlineData(2, 7, false)]
    [InlineData(3, 29, true)]
    [InlineData(3, 30, false)]
    public void DateFiltersUseInclusiveLocalCalendarDays(int period, int daysAgo, bool included)
    {
        var now = DateTime.Now;
        var job = new PFlowJob
        {
            CreatedUtc = now.Date.AddDays(-daysAgo).AddHours(12).ToUniversalTime(),
        };
        Assert.Equal(
            included ? 1 : 0,
            PFlowHistoryPresentation.Filter([job], "", 0, period, now).Count()
        );
    }
}
