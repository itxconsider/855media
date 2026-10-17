using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    private async Task<PFlowJob> Reviewable(string target, DateTime submitted)
    {
        var job = await Job(target);
        await _store.ReserveAsync(job);
        job.State = PFlowState.Unknown;
        job.SubmittedUtc = submitted;
        job.RemoteCleanupState = "Held for publication review";
        await _store.SaveAsync(job);
        return job;
    }

    [Fact]
    public async Task BulkPublishedReviewKeepsEachJobsDateAndFiles()
    {
        var a = await Reviewable("a", DateTime.UtcNow.AddDays(-2));
        var b = await Reviewable("b", DateTime.UtcNow.AddDays(-1));
        var untouched = await Reviewable("c", DateTime.UtcNow.AddHours(-1));
        Assert.Equal(
            2,
            await _store.ReviewAsync(
                [a, b],
                true,
                "Matched each caption and media. Details: checked Reels tab.",
                true
            )
        );
        var rows = await _store.ReadAsync();
        foreach (var snapshot in new[] { a, b })
        {
            var saved = rows.Single(j => j.JobId == snapshot.JobId);
            Assert.Equal(PFlowState.Confirmed, saved.State);
            Assert.Equal(snapshot.SubmittedUtc, saved.ConfirmedUtc);
            Assert.StartsWith("Manual review: Matched each caption", saved.Evidence);
            Assert.False(saved.NoPublicationVerified);
            Assert.Equal("Pending", saved.RemoteCleanupState);
            Assert.True(File.Exists(saved.SourcePath));
        }
        Assert.Equal(PFlowState.Unknown, rows.Single(j => j.JobId == untouched.JobId).State);
    }

    [Fact]
    public async Task BulkNotPublishedReviewAllowsRetryWithManualReason()
    {
        var a = await Reviewable("a", DateTime.UtcNow.AddHours(-2));
        var b = await Reviewable("b", DateTime.UtcNow.AddHours(-1));
        await _store.ReviewAsync(
            [a, b],
            false,
            "Custom reason: checked both targets; neither post exists.",
            true
        );
        Assert.All(
            await _store.ReadAsync(),
            job =>
            {
                Assert.Equal(PFlowState.Failed, job.State);
                Assert.True(job.NoPublicationVerified);
                Assert.True(job.CanRetry);
                Assert.Null(job.ConfirmedUtc);
                Assert.Contains("Custom reason:", job.Evidence);
            }
        );
    }

    [Theory]
    [InlineData(false, "checked")]
    [InlineData(true, " ")]
    public async Task BulkReviewNeedsAcknowledgementAndEvidence(bool acknowledged, string evidence)
    {
        var job = await Reviewable("a", DateTime.UtcNow.AddHours(-1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ReviewAsync([job], true, evidence, acknowledged)
        );
        Assert.Equal(PFlowState.Unknown, (await _store.ReadAsync()).Single().State);
    }

    [Fact]
    public async Task StaleSelectionDoesNotPartiallyReviewOtherRows()
    {
        var a = await Reviewable("a", DateTime.UtcNow.AddHours(-2));
        var b = await Reviewable("b", DateTime.UtcNow.AddHours(-1));
        var stale = JsonSerializer.Deserialize<PFlowJob>(JsonSerializer.Serialize(b))!;
        b.Error = "New outcome information";
        await _store.SaveAsync(b);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ReviewAsync([a, stale], false, "checked", true)
        );
        var rows = await _store.ReadAsync();
        Assert.All(rows, job => Assert.Equal(PFlowState.Unknown, job.State));
        Assert.DoesNotContain("Manual review:", rows.Single(j => j.JobId == a.JobId).Evidence);
        Assert.Equal("New outcome information", rows.Single(j => j.JobId == b.JobId).Error);
    }

    [Fact]
    public async Task MissingRecordedDateRollsBackUnlessVerifiedTimeProvided()
    {
        var a = await Reviewable("a", DateTime.UtcNow.AddHours(-2));
        var b = await Reviewable("b", DateTime.UtcNow.AddHours(-1));
        b.SubmittedUtc = null;
        await _store.SaveAsync(b);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ReviewAsync([a, b], true, "checked", true)
        );
        Assert.All(await _store.ReadAsync(), job => Assert.Equal(PFlowState.Unknown, job.State));
        var verified = DateTime.UtcNow.AddMinutes(-10);
        await _store.ReviewAsync(
            [a, b],
            true,
            "Verified publication time and posts",
            true,
            verified
        );
        Assert.All(await _store.ReadAsync(), job => Assert.Equal(verified, job.ConfirmedUtc));
    }

    [Fact]
    public async Task DuplicateOrAlreadyResolvedSelectionIsRejected()
    {
        var a = await Reviewable("a", DateTime.UtcNow.AddHours(-2));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ReviewAsync([a, a], true, "checked", true)
        );
        await _store.ReviewAsync([a], true, "checked", true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ReviewAsync([a], false, "checked", true)
        );
        Assert.Equal(PFlowState.Confirmed, (await _store.ReadAsync()).Single().State);
    }
}
