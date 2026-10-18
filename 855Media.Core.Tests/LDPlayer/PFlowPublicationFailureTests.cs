using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPublicationFailureTests
{
    private static XElement Node(string attribute, string value) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute(attribute, value)
        );

    [Theory]
    [InlineData("text", "Couldn't post")]
    [InlineData("text", "Couldn’t post. Please try again later.")]
    [InlineData("content-desc", "Upload failed. Tap to retry")]
    [InlineData("content-desc", "Unable to publish: connection lost")]
    [InlineData("text", "Couldn't share story")]
    [InlineData("text", "Couldn't upload your story")]
    public void FailureReasonPreservesActualTextOrDescription(string attribute, string message)
    {
        var xml = new XElement("hierarchy", Node(attribute, message)).ToString();
        var failure = PFlowPublicationFailure.Read(xml)!;
        Assert.Equal(message, failure.Message);
        Assert.Equal(attribute, failure.Attribute);
        var job = new PFlowJob { State = PFlowState.Submitted, SubmittedUtc = DateTime.UtcNow };
        failure.Record(job);
        Assert.Equal(PFlowState.Failed, job.State);
        Assert.Equal(message, job.Error);
        Assert.Equal(PFlowPublicationStage.PublicationError, job.PublicationStage);
        Assert.Contains(attribute, job.Evidence);
        Assert.False(job.NoPublicationVerified);
        Assert.False(job.CanRetry);
        Assert.True(job.RequiresPublicationReviewBeforeCleanup);
    }

    [Theory]
    [InlineData("visible-to-user", "false")]
    [InlineData("class", "android.widget.EditText")]
    [InlineData("package", "another.app")]
    [InlineData("password", "true")]
    [InlineData("bounds", "[0,0][0,0]")]
    public void HiddenForeignInputAndEmptyBoundsDoNotReportFailure(string attribute, string value)
    {
        var node = Node("text", "Upload failed");
        node.SetAttributeValue(attribute, value);
        Assert.Null(PFlowPublicationFailure.Read(new XElement("hierarchy", node).ToString()));
    }

    [Fact]
    public void IgnoresHiddenAncestorCaptionAndPreexistingErrors()
    {
        var node = Node("text", "Upload failed");
        var xml = new XElement(
            "hierarchy",
            new XElement("node", new XAttribute("visible-to-user", "false"), node)
        ).ToString();
        Assert.Null(PFlowPublicationFailure.Read(xml));
        xml = new XElement("hierarchy", Node("text", "Upload failed")).ToString();
        Assert.Null(PFlowPublicationFailure.Read(xml, caption: "Upload failed"));
        Assert.Null(
            PFlowPublicationFailure.Read(xml, before: new HashSet<string> { "Upload failed" })
        );
        Assert.Null(
            PFlowPublicationFailure.Read(
                new XElement(
                    "hierarchy",
                    Node("text", "How to fix upload failed errors")
                ).ToString()
            )
        );
    }

    [Fact]
    public void PrefersMoreDetailedErrorDescriptionOverGenericTextOnSameNode()
    {
        var node = Node("text", "Upload failed");
        node.SetAttributeValue("content-desc", "Upload failed: the connection was lost.");
        var failure = PFlowPublicationFailure.Read(new XElement("hierarchy", node).ToString())!;
        Assert.Equal("content-desc", failure.Attribute);
        Assert.Equal("Upload failed: the connection was lost.", failure.Message);
    }
}

public sealed partial class PFlowTests
{
    [Fact]
    public async Task FacebookReportedFailureSurvivesRecoveryAndRemainsReviewable()
    {
        var job = await Job(delete: true);
        var ld = FakeLd.Create(job);
        ((FakeLd)(object)ld).PublicationError = "Unable to publish: connection lost";
        await new PFlowRunner(ld, _store).RunAsync(
            job,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        await _store.RecoverAsync();
        var restored = (await _store.ReadAsync()).Single(j => j.JobId == job.JobId);
        Assert.Equal(PFlowState.Failed, restored.State);
        Assert.Equal("Unable to publish: connection lost", restored.Error);
        Assert.False(restored.CanRetry);
        Assert.True(restored.RequiresPublicationReviewBeforeCleanup);
        Assert.Equal(
            "Failed: Unable to publish: connection lost",
            PFlowDispatchQueueStore.HistoryStatus(restored)
        );
        await _store.ReviewAsync(
            [restored],
            false,
            "Checked the Page; no post was published",
            true
        );
        Assert.True((await _store.ReadAsync()).Single(j => j.JobId == job.JobId).CanRetry);
    }
}
