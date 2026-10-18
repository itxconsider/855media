using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowPublicationTrackingTests
{
    private static string Screen(string label) =>
        new XElement(
            "hierarchy",
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", label)
            )
        ).ToString();

    [Theory]
    [InlineData("Waiting to upload", PFlowPublicationStage.WaitingForUpload)]
    [InlineData("Uploading your reel…", PFlowPublicationStage.Uploading)]
    [InlineData("Uploading 45%", PFlowPublicationStage.Uploading)]
    [InlineData("Processing your reel", PFlowPublicationStage.Processing)]
    [InlineData("Processing your story", PFlowPublicationStage.Processing)]
    [InlineData("Running copyright scan...", PFlowPublicationStage.CopyrightScanning)]
    [InlineData("Checking for copyright", PFlowPublicationStage.CopyrightScanning)]
    [InlineData("Checking copyright", PFlowPublicationStage.CopyrightScanning)]
    [InlineData("Posting your reel", PFlowPublicationStage.Publishing)]
    [InlineData("Posting...", PFlowPublicationStage.Publishing)]
    [InlineData("Posting… Keep Facebook open.", PFlowPublicationStage.Publishing)]
    [InlineData("Sharing your story", PFlowPublicationStage.Publishing)]
    public void RecognizesEachObservedState(string label, PFlowPublicationStage expected)
    {
        Assert.Equal(expected, PFlowPublicationTracking.Pending(Screen(label))!.Stage);
        Assert.True(PFlowConfirmation.HasPendingPublication(Screen(label)));
    }

    [Theory]
    [InlineData("visible-to-user", "false")]
    [InlineData("class", "android.widget.EditText")]
    [InlineData("package", "other.app")]
    public void HiddenInputAndForeignSignalsAreExcluded(string attribute, string value)
    {
        var doc = XDocument.Parse(Screen("Running copyright scan"));
        doc.Root!.Element("node")!.SetAttributeValue(attribute, value);
        Assert.Null(PFlowPublicationTracking.Pending(doc.ToString()));
    }

    [Fact]
    public void CopyrightSignalWinsOverOverlappingUploadLabel()
    {
        var xml = Screen("Uploading your reel")
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' text='Running copyright scan'/></hierarchy>"
            );
        Assert.Equal(
            PFlowPublicationStage.CopyrightScanning,
            PFlowPublicationTracking.Pending(xml)!.Stage
        );
    }

    [Fact]
    public void RepeatedPollsAreDeduplicatedAndObservedRegressionsAreRetained()
    {
        var job = new PFlowJob();
        var start = DateTime.UtcNow;
        Assert.True(
            PFlowPublicationTracking.Transition(job, PFlowPublicationStage.Uploading, "UI", start)
        );
        Assert.False(
            PFlowPublicationTracking.Transition(
                job,
                PFlowPublicationStage.Uploading,
                "UI",
                start.AddSeconds(2)
            )
        );
        Assert.True(
            PFlowPublicationTracking.Transition(
                job,
                PFlowPublicationStage.Processing,
                "UI",
                start.AddSeconds(10)
            )
        );
        Assert.True(
            PFlowPublicationTracking.Transition(
                job,
                PFlowPublicationStage.Uploading,
                "UI retry",
                start.AddSeconds(12)
            )
        );
        Assert.Equal(3, job.PublicationTransitions.Count);
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            job.PublicationTransitions[1].StartedUtc - job.PublicationTransitions[0].StartedUtc
        );
        Assert.Equal(start.AddSeconds(12), job.PublicationStageStartedUtc);
        Assert.Equal(PFlowState.Reserved, job.State);
        Assert.Null(job.ConfirmedUtc);
    }

    [Fact]
    public void UploadCompletionIsNotPublicationAndOlderHistoryLoadsWithoutInventedStages()
    {
        var old = JsonSerializer.Deserialize<PFlowJob>("{\"JobId\":\"old\",\"State\":9}")!;
        Assert.Equal(PFlowPublicationStage.NotStarted, old.PublicationStage);
        Assert.Empty(old.PublicationTransitions);
        PFlowPublicationTracking.Transition(
            old,
            PFlowPublicationStage.UploadComplete,
            "notification"
        );
        Assert.Null(old.ConfirmedUtc);
        Assert.Equal(
            PFlowState.Unknown,
            PFlowConfirmation.RecordPublicationEvidence(
                old,
                "Facebook media-upload notification: upload complete"
            )
        );
        Assert.Equal(PFlowPublicationStage.Unconfirmed, old.PublicationStage);
        var restored = JsonSerializer.Deserialize<PFlowJob>(JsonSerializer.Serialize(old))!;
        Assert.Equal(old.PublicationTransitions, restored.PublicationTransitions);
        Assert.Null(restored.ConfirmedUtc);
    }
}

public sealed partial class PFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationTimelinePersistsActualStatesWithOptionalCopyrightScan(
        bool copyright
    )
    {
        var job = await Job();
        var ld = FakeLd.Create(job);
        var device = (FakeLd)(object)ld;
        static string Xml(string label) =>
            $"<hierarchy><node package='com.facebook.katana' text='{label}'/></hierarchy>";
        device.PostShareSequence.Enqueue(Xml("Uploading your reel"));
        device.PostShareSequence.Enqueue(Xml("Uploading your reel"));
        device.PostShareSequence.Enqueue(Xml("Processing your reel"));
        if (copyright)
            device.PostShareSequence.Enqueue(Xml("Running copyright scan"));
        device.PostShareSequence.Enqueue(Xml("Posting your reel"));
        device.PostShareXml = Xml("Your reel has been published!");
        await new PFlowRunner(ld, _store, confirmationTimeout: TimeSpan.FromSeconds(10)).RunAsync(
            job,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.Equal(PFlowPublicationStage.Published, job.PublicationStage);
        var stages = job.PublicationTransitions.Select(t => t.Stage).ToArray();
        Assert.Equal(1, stages.Count(s => s == PFlowPublicationStage.Uploading));
        Assert.Contains(PFlowPublicationStage.Processing, stages);
        Assert.Contains(PFlowPublicationStage.Publishing, stages);
        Assert.Equal(copyright, stages.Contains(PFlowPublicationStage.CopyrightScanning));
        Assert.Equal(PFlowPublicationStage.Published, stages.Last());
        var restored = (await _store.ReadAsync()).Single(j => j.JobId == job.JobId);
        Assert.Equal(job.PublicationTransitions, restored.PublicationTransitions);
        Assert.Equal(1, device.ShareTaps);
    }
}
