using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class CapturedStoryCardsTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowStory", name + ".xml")
        );

    [Theory]
    [InlineData("ld1-stacked-own-feed-captured", false, true)]
    [InlineData("ld2-stacked-empty-feed-captured", true, false)]
    [InlineData("ld4-stacked-empty-feed-captured", true, false)]
    [InlineData("ld6-music-create-own-feed-captured", false, true)]
    public void FeedVariantsResolveCreateAndBaseline(string name, bool empty, bool own)
    {
        var xml = Fixture(name);
        Assert.NotNull(PFlowStoryLayout.CreateStoryEntry(xml));
        Assert.Equal(empty, PFlowStoryLayout.HasEmptyOwnStoryBaseline(xml));
        Assert.Equal(own, PFlowStoryLayout.OwnStoryEntry(xml) != null);
        var story = ScreenTimeHierarchy.StoryEntry(xml, "Fixture Owner", new HashSet<string>());
        if (story != null)
        {
            Assert.Equal("true", story.Attribute("clickable")?.Value);
            Assert.DoesNotContain("Create", ScreenTimeHierarchy.Label(story));
            Assert.DoesNotContain("Your story", ScreenTimeHierarchy.Label(story));
        }
    }

    [Fact]
    public void NewOwnToolbarStillRequiresOwnerAndCompletedUpload()
    {
        var xml = Fixture("ld1-own-toolbar-viewer-captured");
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnStoryViewer).IsMatch);
        Assert.Equal(2, PFlowStoryLayout.OwnViewerCount(xml, "Fixture Page"));
        Assert.Null(PFlowStoryLayout.OwnViewerCount(xml, "Unrelated Page"));
        Assert.Null(ScreenTimeHierarchy.StoryPlaybackKey(xml));
        Assert.Null(
            PFlowStoryLayout.OwnViewerCount(xml.Replace("Share", "Uploading story"), "Fixture Page")
        );
    }

    [Fact]
    public void HorizontalStorySwipeIsConfinedToRecognizedFeedTray()
    {
        var xml = Fixture("ld2-stacked-empty-feed-captured");
        var swipe = ScreenTimeHierarchy.StoryTraySwipe(xml);
        Assert.NotNull(swipe);
        Assert.True(swipe.Value.FromX > swipe.Value.ToX);
        Assert.InRange(swipe.Value.Y, 253, 683);
        Assert.Null(ScreenTimeHierarchy.StoryTraySwipe(Fixture("ld6-suggested-viewer-captured")));
        Assert.Null(ScreenTimeHierarchy.StoryTraySwipe(xml.Replace("Story tray", "Other tray")));
    }

    [Fact]
    public void CapturedPersonalOwnViewerAndPickerRemainSupported()
    {
        var xml = Fixture("ld6-personal-own-viewer-captured");
        Assert.Equal(1, PFlowStoryLayout.OwnViewerCount(xml, "Fixture Personal"));
        Assert.Null(ScreenTimeHierarchy.StoryPlaybackKey(xml));
        Assert.True(
            FacebookLayoutRegistry
                .Match(
                    Fixture("ld6-personal-story-picker-captured"),
                    FacebookScreenKind.StoryPicker
                )
                .IsMatch
        );
    }

    [Fact]
    public void NativeViewerUsesPlaybackKeyNotInventedStoryId()
    {
        var xml = Fixture("ld6-suggested-viewer-captured");
        Assert.NotNull(
            ScreenTimeHierarchy.StoryPlaybackKey(Fixture("ld2-reactions-only-viewer-captured"))
        );
        Assert.Null(ScreenTimeHierarchy.StoryId(xml));
        Assert.NotNull(ScreenTimeHierarchy.StoryPlaybackKey(xml));
        Assert.NotEqual(
            ScreenTimeHierarchy.StoryPlaybackKey(xml),
            ScreenTimeHierarchy.StoryPlaybackKey(xml.Replace("Fixture Author", "Different Author"))
        );
        Assert.Null(
            ScreenTimeHierarchy.StoryPlaybackKey(xml.Replace("Story 1 of 1", "Video 1 of 1"))
        );
        Assert.Null(
            ScreenTimeHierarchy.StoryPlaybackKey(xml.Replace("Open story tray", "Other control"))
        );
        Assert.Null(
            ScreenTimeHierarchy.StoryPlaybackKey(xml.Replace("com.facebook.katana", "other.app"))
        );
    }

    [Fact]
    public void CreateAliasNeverChoosesCreateReelAndDisabledCreateIsRejected()
    {
        var xml = Fixture("ld2-stacked-empty-feed-captured");
        Assert.Equal(
            "Create a story",
            PFlowStoryLayout.CreateStoryEntry(xml)!.Attribute("content-desc")?.Value
        );
        var doc = XDocument.Parse(xml);
        var control = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Create a story");
        control.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowStoryLayout.CreateStoryEntry(doc.ToString()));
        Assert.False(PFlowStoryLayout.HasEmptyOwnStoryBaseline(doc.ToString()));
    }

    [Fact]
    public void SeenStatusChangeDoesNotReopenSameAuthor()
    {
        var xml = Fixture("ld2-stacked-empty-feed-captured");
        var entry = ScreenTimeHierarchy.StoryEntry(xml, "Owner", new HashSet<string>())!;
        var opened = new HashSet<string> { ScreenTimeHierarchy.StoryEntryKey(entry) };
        Assert.Null(
            ScreenTimeHierarchy.StoryEntry(xml.Replace(", Unseen", ", Seen"), "Owner", opened)
        );
    }

    [Fact]
    public void CapturedPagePickerIsRecognized() =>
        Assert.True(
            FacebookLayoutRegistry
                .Match(Fixture("ld2-page-story-picker-captured"), FacebookScreenKind.StoryPicker)
                .IsMatch
        );

    [Fact]
    public async Task NativeViewerIsWatchedAndBackExitsWithoutReactionOrReply()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Feed = Fixture("ld2-stacked-empty-feed-captured");
        device.Viewer = Fixture("ld6-suggested-viewer-captured");
        var history = new ScreenTimeHistoryStore(
            Path.Combine(Path.GetTempPath(), "855-story-test-" + Guid.NewGuid() + ".db")
        );
        await history.StartAsync("fixture", "123", 991);
        var settings = new ScreenTimeSetting
        {
            OwnerName = "Fixture Owner",
            OwnerUid = "123",
            Options = new()
            {
                Stories = true,
                StoryLimit = 1,
                StoryDwellSeconds = 5,
            },
        };
        var actions = new ScreenTimeInteractions(
            ld,
            history,
            991,
            settings,
            "fixture",
            null,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );
        await actions.ViewStoriesAsync(default);
        Assert.Equal(1, actions.Stories);
        Assert.Equal(1, device.Taps);
        Assert.Equal(1, device.Backs);
        Assert.False(device.InViewer);
    }

    public class StoryDevice : DispatchProxy
    {
        public string Feed = "",
            Viewer = "";
        public bool InViewer;
        public int Taps,
            Backs;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "OpenDeepLinkAsync")
                return Task.FromResult(true);
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(InViewer ? Viewer : Feed);
            if (method.Name == "TapAsync")
            {
                if (InViewer)
                    throw new Exception("Unexpected viewer tap");
                Taps++;
                InViewer = true;
                return Task.FromResult(true);
            }
            if (method.Name == "SendKeyAsync")
            {
                Assert.Equal(4, args![1]);
                Backs++;
                InViewer = false;
                return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
