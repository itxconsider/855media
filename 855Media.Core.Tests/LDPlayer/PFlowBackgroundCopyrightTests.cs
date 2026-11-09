using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowBackgroundCopyrightTests
{
    [Fact]
    public void NameObservationRemovesOnlyConfirmedScanWithoutMutatingSource()
    {
        var (prior, next, scope) = Setup();
        var proof = PFlowBackgroundCopyright.Create(next, prior, scope)!;
        var cleaned = proof.NameObservation(Scan);
        Assert.NotNull(cleaned);
        Assert.False(PFlowConfirmation.HasPendingPublication(cleaned));
        Assert.True(PFlowConfirmation.HasPendingPublication(Scan));
        Assert.Null(
            proof.NameObservation(
                Scan.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' text='Uploading your reel'/></hierarchy>"
                )
            )
        );
        next.State = PFlowState.Submitted;
        Assert.Null(proof.NameObservation(Scan));
    }

    [Fact]
    public void NotificationOnlyConfirmationCannotAuthorizeAnotherJob()
    {
        var (prior, next, scope) = Setup();
        prior.Evidence = "Facebook media-upload notification: Facebook upload complete";
        Assert.Null(PFlowBackgroundCopyright.Create(next, prior, scope));
    }

    private const string Scan =
        "<hierarchy><node package='com.facebook.katana' content-desc='Finishing reel upload. Keep Facebook open.'><node package='com.facebook.katana' text='Running copyright scan…'/></node></hierarchy>";

    private static (PFlowJob Prior, PFlowJob Next, PFlowNativeIdentityScope Scope) Setup()
    {
        var binding = new PFlowAccountBinding(
            "fixture",
            900755,
            "LD-fixture",
            "com.facebook.katana",
            "100000000000001"
        );
        var target = new FacebookPageTarget
        {
            AccountBinding = binding,
            PageId = "200000000000001",
            PersonaUid = "300000000000001",
            UserUid = binding.OwnerUid,
            PageName = "Fixture Page",
            Type = "Page",
        };
        PFlowJob Job() =>
            new()
            {
                AccountBinding = binding,
                DeviceIndex = binding.DeviceIndex,
                OwnerUid = binding.OwnerUid,
                TargetId = target.PageId,
                PersonaUid = target.PersonaUid,
                TargetName = target.PageName,
                BatchId = "batch",
                PostType = "Reel",
            };
        var prior = Job();
        prior.State = PFlowState.Confirmed;
        prior.SubmittedUtc = DateTime.UtcNow.AddSeconds(-5);
        prior.ConfirmedUtc = DateTime.UtcNow;
        prior.PublicationStage = PFlowPublicationStage.Published;
        prior.Evidence = "Observed publication states completed";
        return (
            prior,
            Job(),
            PFlowNativeIdentityScope.FromSelectedName(
                binding,
                "123",
                target,
                new(target.PageName, "Page"),
                [target]
            )!
        );
    }

    [Fact]
    public void CapturedScanAllowsOnlyJobContinuationNotGeneralPendingOrPublication()
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld55-confirmed-background-copyright.xml"
            )
        );
        var (prior, next, scope) = Setup();
        next.BackgroundCopyright = PFlowBackgroundCopyright.Create(next, prior, scope);
        Assert.NotNull(next.BackgroundCopyright);
        Assert.True(PFlowBackgroundCopyright.IsOnlyBackgroundScan(xml));
        Assert.True(PFlowPostTargetSession.CanContinueForJob(next, xml));
        Assert.False(PFlowPostTargetSession.CanContinueFrom(xml, next.AppPackage));
        Assert.True(PFlowConfirmation.HasPendingPublication(xml));
        Assert.Equal(
            PFlowPublicationStage.CopyrightScanning,
            PFlowPublicationTracking.Pending(xml)!.Stage
        );
        Assert.DoesNotContain("BackgroundCopyright", JsonSerializer.Serialize(next));
        next.State = PFlowState.Submitted;
        Assert.False(PFlowPostTargetSession.CanContinueForJob(next, xml));
    }

    [Theory]
    [InlineData("Uploading your reel")]
    [InlineData("Waiting to upload")]
    [InlineData("Processing your reel")]
    [InlineData("Posting your reel")]
    [InlineData("Finishing reel upload. Keep Facebook open.")]
    public void SeparateActiveSignalCannotBeHiddenByCopyrightPriority(string label) =>
        Assert.False(
            PFlowBackgroundCopyright.IsOnlyBackgroundScan(
                Scan.Replace(
                    "</hierarchy>",
                    $"<node package='com.facebook.katana' text='{label}'/></hierarchy>"
                )
            )
        );

    [Theory]
    [InlineData("Upload complete")]
    [InlineData("Finishing reel upload. Keep Facebook open.")]
    [InlineData("Running copyright scan failed")]
    public void NoExplicitScanIsRejected(string label) =>
        Assert.False(
            PFlowBackgroundCopyright.IsOnlyBackgroundScan(
                $"<hierarchy><node package='com.facebook.katana' text='{label}'/></hierarchy>"
            )
        );

    [Theory]
    [InlineData(PFlowState.Unknown)]
    [InlineData(PFlowState.Submitted)]
    [InlineData(PFlowState.Failed)]
    public void UnconfirmedPreviousCannotAuthorize(PFlowState state)
    {
        var (prior, next, scope) = Setup();
        prior.State = state;
        Assert.Null(PFlowBackgroundCopyright.Create(next, prior, scope));
    }

    [Fact]
    public void ChangedTargetBatchAndMissingConfirmationCannotAuthorize()
    {
        var (prior, next, scope) = Setup();
        next.BatchId = "other";
        Assert.Null(PFlowBackgroundCopyright.Create(next, prior, scope));
        next.BatchId = prior.BatchId;
        next.TargetName = "other";
        Assert.Null(PFlowBackgroundCopyright.Create(next, prior, scope));
        next.TargetName = prior.TargetName;
        prior.ConfirmedUtc = null;
        Assert.Null(PFlowBackgroundCopyright.Create(next, prior, scope));
    }

    [Fact]
    public void ProofRevokedOnSubmissionOrTargetMutation()
    {
        var (prior, next, scope) = Setup();
        var proof = PFlowBackgroundCopyright.Create(next, prior, scope)!;
        Assert.True(proof.Allows(Scan));
        next.TargetId = "other";
        Assert.False(proof.Allows(Scan));
        next.TargetId = prior.TargetId;
        next.SubmittedUtc = DateTime.UtcNow;
        Assert.False(proof.Allows(Scan));
    }

    [Fact]
    public void DelayAppliesAcrossTargetsCountsCleanupAndIgnoresOtherWorkersAndBatches()
    {
        var (prior, next, _) = Setup();
        var now = DateTime.UtcNow;
        prior.ConfirmedUtc = now.AddSeconds(-12);
        prior.TargetId = "other-page";
        Assert.Equal(
            TimeSpan.FromSeconds(18),
            PFlowBackgroundCopyright.RemainingDelay(next, [prior], TimeSpan.FromSeconds(30), now)
        );
        Assert.Equal(
            TimeSpan.Zero,
            PFlowBackgroundCopyright.RemainingDelay(next, [prior], TimeSpan.FromSeconds(10), now)
        );
        prior.DeviceIndex++;
        Assert.Equal(
            TimeSpan.Zero,
            PFlowBackgroundCopyright.RemainingDelay(next, [prior], TimeSpan.FromSeconds(30), now)
        );
        prior.DeviceIndex = next.DeviceIndex;
        prior.BatchId = "old";
        Assert.Equal(
            TimeSpan.Zero,
            PFlowBackgroundCopyright.RemainingDelay(next, [prior], TimeSpan.FromSeconds(30), now)
        );
        Assert.Equal(
            TimeSpan.Zero,
            PFlowBackgroundCopyright.RemainingDelay(next, [], TimeSpan.FromSeconds(30), now)
        );
    }
}
