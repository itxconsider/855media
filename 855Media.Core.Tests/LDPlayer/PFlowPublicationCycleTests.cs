using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPublicationCycleTests
{
    private static string Feed =>
        FacebookLayoutRegistryTests.Fixture("ld55-feed", "PFlowScreenTime");

    private static PFlowJob Job() =>
        new() { State = PFlowState.Submitted, SubmittedUtc = DateTime.UtcNow.AddSeconds(-90) };

    [Theory]
    [InlineData("Reel", PFlowPublicationStage.UploadComplete)]
    [InlineData("Reel", PFlowPublicationStage.CopyrightScanning)]
    [InlineData("Picture", PFlowPublicationStage.UploadComplete)]
    [InlineData("Picture", PFlowPublicationStage.CopyrightScanning)]
    [InlineData("Picture", PFlowPublicationStage.Publishing)]
    [InlineData("Video", PFlowPublicationStage.UploadComplete)]
    public void CompletedMediaStagesDoNotRequireRefreshOrThirtySecondWait(
        string postType,
        PFlowPublicationStage stage
    )
    {
        var job = Job();
        job.PostType = postType;
        PFlowPublicationTracking.Transition(job, stage, "Fresh post-submit observation");
        var proof = PFlowPublicationCycle.ReadCompletedMediaStages(job, Feed, false);
        Assert.NotNull(proof);
        job.ProfilePublicationCheckRequired = true;
        job.State = PFlowConfirmation.RecordPublicationEvidence(job, proof!);
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.False(job.ProfilePublicationCheckRequired);
    }

    [Theory]
    [InlineData("Running copyright scan", false)]
    [InlineData("Uploading your reel", false)]
    [InlineData("Upload failed: connection lost", false)]
    [InlineData("", true)]
    public void CompletedUploadCannotOverridePendingStagesOrErrors(string label, bool ongoing)
    {
        var job = Job();
        job.PostType = "Reel";
        PFlowPublicationTracking.Transition(
            job,
            PFlowPublicationStage.UploadComplete,
            "Fresh notification"
        );
        Assert.Null(PFlowPublicationCycle.ReadCompletedMediaStages(job, With(label), ongoing));
    }

    [Fact]
    public void UploadActivityAloneIsNotUploadCompletion()
    {
        var job = Job();
        job.PostType = "Reel";
        PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Uploading, "UI");
        Assert.Null(PFlowPublicationCycle.ReadCompletedMediaStages(job, Feed, false));
    }

    [Theory]
    [InlineData("Posting...")]
    [InlineData("Posting… Keep Facebook open.")]
    [InlineData("Upload failed: connection lost")]
    public void PicturePostingBannerOrErrorMustPreventConfirmation(string label)
    {
        var job = Job();
        job.PostType = "Picture";
        PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Publishing, "Posting...");
        PFlowPublicationTracking.Transition(
            job,
            PFlowPublicationStage.UploadComplete,
            "Fresh upload completion"
        );
        Assert.Null(PFlowPublicationCycle.ReadCompletedMediaStages(job, With(label), false));
        Assert.Null(PFlowPublicationCycle.ReadCompletedMediaStages(job, Feed, true));
        Assert.NotNull(PFlowPublicationCycle.ReadCompletedMediaStages(job, Feed, false));
    }

    private static string With(string text, string className = "")
    {
        var doc = XDocument.Parse(Feed);
        doc.Root!.Element("node")!
            .Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", text),
                    new XAttribute("class", className)
                )
            );
        return doc.ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservedCycleClearingConfirmsWithoutBannerWithOptionalCopyright(bool copyright)
    {
        var job = Job();
        var gate = new PFlowPublicationSettleGate();
        PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Uploading, "UI upload");
        Assert.False(gate.Observe(With("Uploading your reel"), TimeSpan.Zero));
        if (copyright)
        {
            PFlowPublicationTracking.Transition(
                job,
                PFlowPublicationStage.CopyrightScanning,
                "UI scan"
            );
            Assert.False(gate.Observe(With("Running copyright scan"), TimeSpan.FromSeconds(10)));
        }
        Assert.Null(
            PFlowPublicationCycle.Read(
                job,
                Feed,
                gate.Observe(Feed, TimeSpan.FromSeconds(20)),
                false
            )
        );
        Assert.Null(
            PFlowPublicationCycle.Read(
                job,
                Feed,
                gate.Observe(Feed, TimeSpan.FromSeconds(49)),
                false
            )
        );
        var proof = PFlowPublicationCycle.Read(
            job,
            Feed,
            gate.Observe(Feed, TimeSpan.FromSeconds(50)),
            false
        );
        Assert.NotNull(proof);
        Assert.StartsWith(PFlowPublicationCycle.EvidencePrefix, proof);
        job.ProfilePublicationCheckRequired = true;
        job.State = PFlowConfirmation.RecordPublicationEvidence(job, proof!);
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.NotNull(job.ConfirmedUtc);
        Assert.False(job.ProfilePublicationCheckRequired);
        Assert.False(job.RequiresPublicationReviewBeforeCleanup);
        Assert.Equal(
            copyright,
            job.PublicationTransitions.Any(t => t.Stage == PFlowPublicationStage.CopyrightScanning)
        );
    }

    [Theory]
    [InlineData("Uploading your reel")]
    [InlineData("Processing your reel")]
    [InlineData("Running copyright scan")]
    [InlineData("Posting your reel")]
    [InlineData("Upload failed: connection lost")]
    public void PendingOrErrorMessagesCannotBecomeCycleCompletion(string text)
    {
        var job = Job();
        PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Uploading, "UI");
        Assert.Null(PFlowPublicationCycle.Read(job, With(text), true, false));
    }

    [Fact]
    public void NoObservedWorkActiveNotificationAndUnknownScreensCannotComplete()
    {
        var job = Job();
        Assert.Null(PFlowPublicationCycle.Read(job, Feed, true, false));
        PFlowPublicationTracking.Transition(
            job,
            PFlowPublicationStage.UploadComplete,
            "notification only"
        );
        Assert.Null(PFlowPublicationCycle.Read(job, Feed, true, false));
        PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Uploading, "UI");
        Assert.Null(PFlowPublicationCycle.Read(job, Feed, true, true));
        Assert.Null(
            PFlowPublicationCycle.Read(
                job,
                "<hierarchy><node package='com.facebook.katana' text='Loading'/></hierarchy>",
                true,
                false
            )
        );
        Assert.Null(PFlowPublicationCycle.Read(job, "<hierarchy/>", true, false));
        Assert.Null(PFlowPublicationCycle.Read(job, With("Reel Settings"), true, false));
    }

    [Fact]
    public void UnknownIntermediateFramesDoNotCountTowardThirtyClearSeconds()
    {
        var job = Job();
        PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Uploading, "UI");
        var gate = new PFlowPublicationSettleGate();
        Assert.False(PFlowPublicationCycle.Observe(job, gate, Feed, TimeSpan.Zero));
        Assert.False(
            PFlowPublicationCycle.Observe(
                job,
                gate,
                "<hierarchy><node package='com.facebook.katana' text='Loading'/></hierarchy>",
                TimeSpan.FromSeconds(29)
            )
        );
        Assert.False(PFlowPublicationCycle.Observe(job, gate, Feed, TimeSpan.FromSeconds(30)));
        Assert.False(PFlowPublicationCycle.Observe(job, gate, Feed, TimeSpan.FromSeconds(59)));
        Assert.True(PFlowPublicationCycle.Observe(job, gate, Feed, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void AReappearingStateOrRefreshRestartsTheQuietWindow()
    {
        var gate = new PFlowPublicationSettleGate();
        Assert.False(gate.Observe(Feed, TimeSpan.Zero));
        Assert.False(gate.Observe(With("Processing your reel"), TimeSpan.FromSeconds(29)));
        Assert.False(gate.Observe(Feed, TimeSpan.FromSeconds(30)));
        Assert.False(gate.Observe(Feed, TimeSpan.FromSeconds(59)));
        gate.Reset();
        Assert.False(gate.Observe(Feed, TimeSpan.FromSeconds(60)));
        Assert.True(gate.Observe(Feed, TimeSpan.FromSeconds(90)));
    }
}

public sealed partial class PFlowTests
{
    [Theory]
    [InlineData("Uploading your reel")]
    [InlineData("Processing your reel")]
    [InlineData("Running copyright scan")]
    [InlineData("Posting your reel")]
    public async Task PublisherConfirmsObservedCycleWithoutSuccessBannerOrProfileScan(
        string observedState
    )
    {
        var job = await Job();
        job.State = PFlowState.IdentityVerified;
        var ld = FakeLd.Create(job);
        var device = (FakeLd)(object)ld;
        device.PostShareSequence.Enqueue(
            new XElement(
                "hierarchy",
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", observedState)
                )
            ).ToString()
        );
        device.PostShareXml = FacebookLayoutRegistryTests.Fixture("ld55-feed", "PFlowScreenTime");
        var publisher = new PFlowAutoPostService(ld) { PublicationQuietPeriod = TimeSpan.Zero };
        await publisher.PublishAsync(job, _ => Task.CompletedTask, null, default);
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.StartsWith(PFlowPublicationCycle.EvidencePrefix, job.Evidence);
        Assert.Equal(1, device.ShareTaps);
        Assert.False(job.ProfilePublicationCheckRequired);
    }
}
