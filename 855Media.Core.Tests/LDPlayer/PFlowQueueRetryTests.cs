using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowQueueRetryTests
{
    [Fact]
    public void PhoneRestrictionHasExplicitDisplayStatusWithoutMakingSubmissionRetryable()
    {
        var job = new PFlowJob
        {
            State = PFlowState.Unknown,
            TargetIdentityConfirmationRequired = true,
            SubmittedUtc = System.DateTime.UtcNow,
        };
        Assert.StartsWith(
            "Blocked: Page phone verification required",
            PFlowDispatchQueueStore.HistoryStatus(job)
        );
        Assert.False(job.CanRetry);
        Assert.True(job.RequiresPublicationReviewBeforeCleanup);
        job.State = PFlowState.CancelledBeforeSubmission;
        job.SubmittedUtc = null;
        Assert.StartsWith("CancelledBeforeSubmission", PFlowDispatchQueueStore.HistoryStatus(job));
    }

    [Theory]
    [InlineData(PFlowState.Failed, true)]
    [InlineData(PFlowState.CancelledBeforeSubmission, true)]
    [InlineData(PFlowState.Unknown, false)]
    [InlineData(PFlowState.Confirmed, false)]
    [InlineData(PFlowState.Submitted, false)]
    public void RetryPreservesFileAndTargetAndOnlyResetsRetryableRows(
        PFlowState state,
        bool allowed
    )
    {
        var job = new PFlowJob
        {
            State = state,
            TargetId = "123",
            Sha256 = "abc",
        };
        var row = new AutoPostTaskItem
        {
            PFlowJobId = job.JobId,
            FilePath = "original.mp4",
            PageId = "123",
            Title = "original caption",
            Status = state.ToString(),
        };
        Assert.Equal(allowed, PFlowQueueRetry.Requeue(row, new[] { job }));
        Assert.Equal("original.mp4", row.FilePath);
        Assert.Equal("123", row.PageId);
        Assert.Equal("original caption", row.Title);
        Assert.Equal(allowed ? "" : job.JobId, row.PFlowJobId);
        Assert.Equal(allowed ? "Queued" : state.ToString(), row.Status);
        Assert.Equal(state, job.State);
    }

    [Fact]
    public void ConfirmedDuplicateAndMissingHistoryCannotBeRequeued()
    {
        var failed = new PFlowJob
        {
            State = PFlowState.Failed,
            TargetId = "123",
            Sha256 = "abc",
        };
        var confirmed = new PFlowJob
        {
            State = PFlowState.Confirmed,
            TargetId = "123",
            Sha256 = "abc",
        };
        var row = new AutoPostTaskItem { PFlowJobId = failed.JobId, Status = "Failed" };
        Assert.False(PFlowQueueRetry.Requeue(row, new[] { failed, confirmed }));
        Assert.False(PFlowQueueRetry.Requeue(row, System.Array.Empty<PFlowJob>()));
        failed.SubmittedUtc = System.DateTime.UtcNow;
        Assert.False(PFlowQueueRetry.Requeue(row, new[] { failed }));
        Assert.Equal("Failed", row.Status);
    }
}
