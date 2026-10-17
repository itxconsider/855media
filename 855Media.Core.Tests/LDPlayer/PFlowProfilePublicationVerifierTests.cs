using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowProfilePublicationVerifierTests
{
    [Theory]
    [InlineData("more", true)]
    [InlineData("See more", true)]
    [InlineData("MORE", true)]
    [InlineData("Thank you @Frizzlife Solution we will be installing... more", true)]
    [InlineData("Thank you @Frizzlife Solution we will be installing… See more", true)]
    [InlineData("Another unrelated caption... more", false)]
    [InlineData("Share", false)]
    public void CaptionExpansionSupportsSeparateAndInlineMore(string label, bool expected)
    {
        var scope = new System.Xml.Linq.XElement(
            "node",
            new System.Xml.Linq.XAttribute("bounds", "[0,0][330,580]"),
            new System.Xml.Linq.XElement(
                "node",
                new System.Xml.Linq.XAttribute("text", label),
                new System.Xml.Linq.XAttribute("clickable", "true"),
                new System.Xml.Linq.XAttribute("enabled", "true"),
                new System.Xml.Linq.XAttribute("bounds", "[10,50][310,90]")
            )
        );
        var controls = PFlowProfilePublicationVerifier.CaptionExpansionControls(
            scope,
            "Thank you @Frizzlife Solution we will be installing it in our own homes to make things easier."
        );
        Assert.Equal(expected ? 1 : 0, controls.Length);
        scope.Element("node")!.SetAttributeValue("enabled", "false");
        Assert.Empty(
            PFlowProfilePublicationVerifier.CaptionExpansionControls(
                scope,
                "Thank you @Frizzlife Solution we will be installing it"
            )
        );
    }

    private static PFlowJob Job() =>
        new()
        {
            TargetName = "Example Page",
            TargetId = "123",
            OwnerUid = "456",
            Caption = "Exact caption #test",
            SubmittedUtc = DateTime.UtcNow.AddSeconds(-20),
        };

    private static string Node(string text, string bounds = "[0,0][300,30]", string extra = "") =>
        $"<node package='com.facebook.katana' text='{SecurityElement.Escape(text)}' bounds='{bounds}' {extra}/>";

    private static string Viewer(
        string caption = "Exact caption #test",
        string author = "Example Page",
        string time = "Just now",
        string id = "202"
    ) =>
        "<hierarchy>"
        + Node(author)
        + Node(caption)
        + Node(time)
        + Node("https://www.facebook.com/reel/" + id)
        + Node("Back", "[0,40][30,70]", "clickable='true' enabled='true'")
        + "</hierarchy>";

    [Theory]
    [InlineData("Exact caption #test", "Example Page", "Just now", "202", true)]
    [InlineData("Exact caption…", "Example Page", "Just now", "202", false)]
    [InlineData("Other caption", "Example Page", "Just now", "202", false)]
    [InlineData("Exact caption #test", "Other Page", "Just now", "202", false)]
    [InlineData("Exact caption #test", "Example Page", "1d", "202", false)]
    [InlineData("Exact caption #test", "Example Page", "5m", "202", false)]
    [InlineData("Exact caption #test", "Example Page", "", "202", false)]
    public void CaptionAuthorAndPublicationTimeAreRequired(
        string caption,
        string author,
        string time,
        string id,
        bool expected
    )
    {
        var job = Job();
        var result = PFlowProfilePublicationVerifier.MatchViewer(
            job,
            Viewer(caption, author, time, id),
            DateTime.UtcNow
        );
        Assert.Equal(expected, result != null);
        if (result != null)
        {
            Assert.Equal("202", result.Id);
            Assert.Contains("/reel/202", result.Url);
        }
    }

    [Fact]
    public void RejectsProcessingForeignHiddenAndAmbiguousEvidence()
    {
        var job = Job();
        foreach (
            var xml in new[]
            {
                Viewer().Replace("</hierarchy>", Node("Processing video") + "</hierarchy>"),
                Viewer().Replace("com.facebook.katana", "other.package"),
                Viewer()
                    .Replace("Exact caption #test", "Exact caption #test' visible-to-user='false"),
                Viewer()
                    .Replace(
                        "</hierarchy>",
                        Node("https://www.facebook.com/reel/303") + "</hierarchy>"
                    ),
            }
        )
            Assert.Null(PFlowProfilePublicationVerifier.MatchViewer(job, xml, DateTime.UtcNow));
    }

    [Fact]
    public async Task MissingSubmissionTimeNeverNavigatesOrConfirms()
    {
        var ld = DispatchProxy.Create<
            ILDPlayerService,
            PFlowProfilePublicationCapturedTests.Device
        >();
        var job = Job();
        job.SubmittedUtc = null;
        Assert.Null(await new PFlowProfilePublicationVerifier(ld).VerifyAsync(job, null, default));
        Assert.Equal(0, ((PFlowProfilePublicationCapturedTests.Device)(object)ld).Reads);
    }

    [Theory]
    [InlineData("Just now", 20, true)]
    [InlineData("1m", 90, true)]
    [InlineData("3 minutes ago", 220, true)]
    [InlineData("1h", 3700, true)]
    [InlineData("5m", 90, false)]
    [InlineData("Yesterday", 90, false)]
    [InlineData("", 90, false)]
    public void PublicationAgeMustFitSubmissionWindow(string age, int elapsedSeconds, bool expected)
    {
        var now = DateTime.UtcNow;
        var job = Job();
        job.SubmittedUtc = now.AddSeconds(-elapsedSeconds);
        Assert.Equal(
            expected,
            PFlowProfilePublicationVerifier.MatchViewer(job, Viewer(time: age), now) != null
        );
    }

    [Theory]
    [InlineData("Reel published", true)]
    [InlineData("Target profile Reel verified: exact match", false)]
    [InlineData("Manual review: checked matching Reel link", false)]
    public void BoundProfileCheckCannotBeBypassedForCleanup(string evidence, bool held)
    {
        var job = Job();
        job.State = PFlowState.Confirmed;
        job.ProfilePublicationCheckRequired = true;
        job.Evidence = evidence;
        Assert.Equal(held, job.RequiresPublicationReviewBeforeCleanup);
    }
}
