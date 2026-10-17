using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowProfilePublicationCapturedTests
{
    private const string Caption = "Exact caption for this published Reel #test";

    private static string Fixture(string name) => FacebookLayoutRegistryTests.Fixture(name);

    private static PFlowJob Job() =>
        new()
        {
            TargetName = "Example Page",
            Caption = Caption,
            SubmittedUtc = DateTime.UtcNow.AddMinutes(-2),
        };

    [Fact]
    public void CapturedPostsTabWorksThroughPassiveWrappers()
    {
        var tabs = Fixture("ld7-profile-tabs-passive-wrapper");
        Assert.NotNull(PFlowProfilePublicationVerifier.PostsTab(tabs));
        var disabled = XDocument.Parse(tabs);
        disabled
            .Descendants("node")
            .First(n =>
                n.Attribute("class")?.Value == "android.widget.TabWidget"
                && n.Attribute("content-desc")?.Value.StartsWith("All,") == true
            )
            .SetAttributeValue("enabled", "false");
        Assert.Null(PFlowProfilePublicationVerifier.PostsTab(disabled.ToString()));
    }

    [Fact]
    public void CapturedExpandedOwnPostProvidesCaptionAndPublicAge()
    {
        var job = Job();
        var collapsed = Fixture("ld7-profile-post-collapsed");
        var expanded = Fixture("ld7-profile-post-expanded");
        Assert.Single(PFlowProfilePublicationVerifier.PostCards(expanded, job));
        Assert.Null(PFlowProfilePublicationVerifier.MatchViewer(job, collapsed, DateTime.UtcNow));
        Assert.NotNull(PFlowProfilePublicationVerifier.MatchViewer(job, expanded, DateTime.UtcNow));
        job.SubmittedUtc = DateTime.UtcNow.AddMinutes(-20);
        Assert.NotNull(
            PFlowProfilePublicationVerifier.MatchViewer(
                job,
                expanded.Replace("1m", "13m"),
                DateTime.UtcNow
            )
        );
        Assert.Null(
            PFlowProfilePublicationVerifier.MatchViewer(
                job,
                expanded.Replace("1m", "25m"),
                DateTime.UtcNow
            )
        );
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ConfirmsCaptionAndTimeWithoutScreenshotsSourceFilesOrReelsGrid(
        bool changedIdentity,
        bool expected
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.HideTabsUntilScroll = true;
        var identities = 0;
        var verifier = new PFlowProfilePublicationVerifier(ld)
        {
            VerifyIdentityOverride = (_, _, _) =>
                Task.FromResult(!changedIdentity || ++identities < 2),
            Delay = (_, _) => Task.CompletedTask,
        };
        var job = Job();
        job.SourcePath = "missing-source.mp4";
        var proof = await verifier.VerifyAsync(job, null, default);
        Assert.Equal(expected, proof != null);
        Assert.True(device.ExpandedCaption);
        Assert.True(device.OpenedAll);
        Assert.True(device.Refreshes >= 2);
        Assert.Equal(0, device.UnexpectedInputs);
        if (proof != null)
        {
            Assert.DoesNotContain("thumbnail", proof.Evidence);
            Assert.Contains("publication time", proof.Evidence);
        }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task TimelineTimeCanBeCombinedWithSameReelsViewerCaption(
        bool wrongCaption,
        bool oldPost,
        bool expected
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.HideTimelineCaption = true;
        device.WrongViewerCaption = wrongCaption;
        device.OldPost = oldPost;
        var verifier = new PFlowProfilePublicationVerifier(ld)
        {
            VerifyIdentityOverride = (_, _, _) => Task.FromResult(true),
            Delay = (_, _) => Task.CompletedTask,
        };
        Assert.Equal(expected, await verifier.VerifyAsync(Job(), null, default) != null);
        Assert.Equal(oldPost ? 0 : 1, device.ViewerOpens);
        Assert.Equal(0, device.UnexpectedInputs);
    }

    [Fact]
    public async Task ClippedReelIsRevealedBeforeOpeningToAvoidBottomNavigation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.HideTimelineCaption = true;
        device.ClippedMediaUntilScroll = true;
        var verifier = new PFlowProfilePublicationVerifier(ld)
        {
            VerifyIdentityOverride = (_, _, _) => Task.FromResult(true),
            Delay = (_, _) => Task.CompletedTask,
        };
        Assert.NotNull(await verifier.VerifyAsync(Job(), null, default));
        Assert.True(device.RevealedMedia);
        Assert.Equal(1, device.ViewerOpens);
        Assert.Equal(0, device.UnexpectedInputs);
    }

    [Fact]
    public void PostEvidenceCannotBeBorrowedFromAdjacentCard()
    {
        var doc = XDocument.Parse(Fixture("ld7-profile-post-expanded"));
        var card = PFlowProfilePublicationVerifier.PostCards(doc.ToString(), Job()).Single();
        var second = new XElement(card);
        card.Descendants("node").First(n => n.Attribute("content-desc")?.Value == "Reel").Remove();
        card.Parent!.Add(second);
        Assert.Single(PFlowProfilePublicationVerifier.PostCards(doc.ToString(), Job()));
    }

    public class Device : DispatchProxy
    {
        public string State = "header";
        public bool ClippedMediaUntilScroll,
            RevealedMedia,
            HideTimelineCaption,
            WrongViewerCaption,
            OldPost,
            HideTabsUntilScroll,
            RevealedTabs,
            ExpandedCaption,
            OpenedAll;
        public int ViewerOpens,
            Reads,
            Refreshes,
            Scrolls,
            UnexpectedInputs;

        private string Header()
        {
            var doc = XDocument.Parse(Fixture("ld7-profile-tabs-passive-wrapper"));
            var root = doc.Root!.Element("node")!;
            root.SetAttributeValue("class", "androidx.recyclerview.widget.RecyclerView");
            root.SetAttributeValue("scrollable", "true");
            root.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Example Page"),
                    new XAttribute("bounds", "[10,100][300,140]")
                )
            );
            root.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Creator Dashboard"),
                    new XAttribute("clickable", "true"),
                    new XAttribute("enabled", "true"),
                    new XAttribute("bounds", "[10,150][300,190]")
                )
            );
            if (HideTabsUntilScroll && !RevealedTabs)
                foreach (
                    var tab in doc.Descendants("node")
                        .Where(n => n.Attribute("class")?.Value == "android.widget.TabWidget")
                        .ToArray()
                )
                    tab.Remove();
            return doc.ToString();
        }

        private string Timeline(bool expanded)
        {
            var xml = Fixture(
                expanded ? "ld7-profile-post-expanded" : "ld7-profile-post-collapsed"
            );
            if (HideTimelineCaption)
            {
                var doc = XDocument.Parse(xml);
                foreach (
                    var node in doc.Descendants("node")
                        .Where(n =>
                            (n.Attribute("text")?.Value ?? "").StartsWith("Exact caption")
                            || (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                                "Exact caption"
                            )
                        )
                        .ToArray()
                )
                    node.Remove();
                xml = doc.ToString();
            }
            if (ClippedMediaUntilScroll)
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Root!.Element("node")!;
                root.SetAttributeValue("class", "androidx.recyclerview.widget.RecyclerView");
                root.SetAttributeValue("scrollable", "true");
                root.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("content-desc", "Ads, tab 4 of 6"),
                        new XAttribute("bounds", "[360,1168][480,1280]")
                    )
                );
                if (!RevealedMedia)
                    doc.Descendants("node")
                        .First(n => n.Attribute("content-desc")?.Value == "Reel")
                        .SetAttributeValue("bounds", "[0,1190][720,1280]");
                xml = doc.ToString();
            }
            return OldPost ? xml.Replace("1m", "25m") : xml;
        }

        private string Viewer() =>
            $"<hierarchy><node package='com.facebook.katana' text='Example Page, Privacy setting Public' bounds='[10,100][300,140]'/><node package='com.facebook.katana' content-desc='{(WrongViewerCaption ? "Other caption" : Caption)}' bounds='[10,150][600,190]'/><node package='com.facebook.katana' content-desc='Reel details' bounds='[0,0][720,1160]'/><node package='com.facebook.katana' text='Back' clickable='true' enabled='true' bounds='[10,40][50,80]'/></hierarchy>";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    Reads++;
                    return Task.FromResult<string?>(
                        State switch
                        {
                            "header" => Header(),
                            "collapsed" => Timeline(false),
                            "viewer" => Viewer(),
                            _ => Timeline(true),
                        }
                    );
                case "SwipeAsync":
                    Scrolls++;
                    var fromY = (int)args![2]!;
                    var toY = (int)args[4]!;
                    if (toY > fromY && State == "header")
                        Refreshes++;
                    else if (toY < fromY && State == "header" && Refreshes >= 2)
                        RevealedTabs = true;
                    else if (toY < fromY && State == "collapsed" && ClippedMediaUntilScroll)
                        RevealedMedia = true;
                    else
                        UnexpectedInputs++;
                    return Task.FromResult(true);
                case "TapAsync":
                    var x = (int)args![1]!;
                    var y = (int)args[2]!;
                    if (State == "header" && x < 120 && Refreshes >= 2)
                    {
                        State = "collapsed";
                        OpenedAll = true;
                    }
                    else if (State == "collapsed" && HideTimelineCaption)
                    {
                        if (ClippedMediaUntilScroll && !RevealedMedia)
                            UnexpectedInputs++;
                        State = "viewer";
                        ViewerOpens++;
                    }
                    else if (State == "viewer" && x < 100)
                        State = "collapsed";
                    else if (State == "collapsed")
                    {
                        State = "expanded";
                        ExpandedCaption = true;
                    }
                    else
                        UnexpectedInputs++;
                    return Task.FromResult(true);
            }
            UnexpectedInputs++;
            throw new NotSupportedException(method.Name);
        }
    }
}
