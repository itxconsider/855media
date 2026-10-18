using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowStoryTests
{
    [Fact]
    public async Task StoryObservedPublishingCycleCanCompleteWithoutBannerOrCopyrightScan()
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.NoConfirmation = true;
        device.PostingReadsRemaining = 1;
        device.ClearPostShareXml = FacebookLayoutRegistryTests.Fixture(
            "ld55-feed",
            "PFlowScreenTime"
        );
        await new PFlowAutoPostService(ld) { PublicationQuietPeriod = TimeSpan.Zero }.PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default
        );
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.StartsWith(PFlowPublicationCycle.EvidencePrefix, job.Evidence);
        Assert.Equal(1, device.ShareTaps);
    }

    [Fact]
    public async Task StoryFailureUsesActualDescriptionAndMarksFailed()
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.PublicationErrorDescription = "Couldn't share story: connection lost";
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default
        );
        Assert.Equal(PFlowState.Failed, job.State);
        Assert.Equal(device.PublicationErrorDescription, job.Error);
        Assert.Equal("content-desc", job.PublicationErrorAttribute);
        Assert.Equal(1, device.ShareTaps);
        Assert.Equal(0, device.Deletes);
        Assert.False(job.CanRetry);
        Assert.True(job.RequiresPublicationReviewBeforeCleanup);
    }

    [Fact]
    public void UploadLikeContentWithLongPunctuationIsNotAStatus()
    {
        var content = "Uploading" + new string('.', 1000) + " new ideas";
        var xml = Fixture("own-viewer").Replace("No viewers yet", content);
        Assert.False(PFlowStoryLayout.HasPendingUpload(xml));
        Assert.Equal(1, PFlowStoryLayout.OwnViewerCount(xml, "Target"));
    }

    [Theory]
    [InlineData("Uploading...")]
    [InlineData("Uploading 50%")]
    [InlineData("Posting story…")]
    [InlineData("Processing video")]
    [InlineData("Retry")]
    [InlineData("Sending...")]
    [InlineData("Sharing your story...")]
    [InlineData("Couldn't upload your story")]
    [InlineData("Upload failed. Tap to retry")]
    [InlineData("Waiting to upload")]
    [InlineData("Keep Facebook open while your upload finishes.")]
    public void UploadStatusStillBlocksStoryConfirmation(string status)
    {
        var xml = Fixture("own-viewer")
            .Replace("No viewers yet", System.Security.SecurityElement.Escape(status));
        Assert.True(PFlowStoryLayout.HasPendingUpload(xml));
        Assert.Null(PFlowStoryLayout.OwnViewerCount(xml, "Target"));
    }

    [Theory]
    [InlineData("Sharing Tips")]
    [InlineData("Retry Plumbing")]
    [InlineData("Processing Ideas")]
    public void AccountNamesAreNotPendingUploadStatuses(string name)
    {
        var xml = Fixture("own-viewer").Replace("Target", name);
        Assert.False(PFlowStoryLayout.HasPendingUpload(xml));
        Assert.Equal(1, PFlowStoryLayout.OwnViewerCount(xml, name));
    }

    [Fact]
    public async Task FailedPlaybackCloseDoesNotDiscardAlreadyVerifiedPublication()
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.ViewerConfirmation = true;
        device.CloseAfterShareFails = true;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default
        );
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.Equal(1, device.ShareTaps);
        Assert.Equal(1, job.StoryCountAfter);
    }

    [Fact]
    public void TextOnlyOwnStoryCardIsDistinctFromCreateAndMusicCards()
    {
        var doc = XDocument.Parse(Feed(true));
        var card = doc.Descendants("node")
            .Single(n => (n.Attribute("content-desc")?.Value ?? "").StartsWith("Your story"));
        card.SetAttributeValue("content-desc", "");
        card.Add(
            new XElement(
                "node",
                new XAttribute("text", "Your story"),
                new XAttribute("clickable", "false")
            )
        );
        Assert.NotNull(PFlowStoryLayout.OwnStoryEntry(doc.ToString()));
        Assert.False(PFlowStoryLayout.HasEmptyOwnStoryBaseline(doc.ToString()));
        var mixed = XDocument.Parse(Feed(false));
        mixed
            .Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Story tray")
            .Add(new XElement(card));
        Assert.False(PFlowStoryLayout.HasEmptyOwnStoryBaseline(mixed.ToString()));
        Assert.Null(
            PFlowStoryLayout.OwnStoryEntry(doc.ToString().Replace("Your story", "Create story"))
        );
        Assert.Null(PFlowStoryLayout.OwnStoryEntry(doc.ToString().Replace("Your story", "Music")));
    }

    [Fact]
    public async Task PostingOverlayOverridesSimultaneousSuccessLabelUntilItClears()
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.PostingReadsRemaining = 2;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            j =>
            {
                if (j.State == PFlowState.Confirmed)
                {
                    Assert.Equal(0, device.PostingReadsRemaining);
                    Assert.Equal(2, device.PostingObserved);
                }
                return Task.CompletedTask;
            },
            null,
            default
        );
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.Equal(1, device.ShareTaps);
    }

    [Fact]
    public void ExistingCardRepresentsACollectionAndCannotConfirmAnOldItemOrExpiredCount()
    {
        var job = Job();
        job.State = PFlowState.Submitted;
        job.SubmittedUtc = DateTime.UtcNow;
        job.IdentityEvidence = "verified";
        job.MediaSelectionEvidence = "single staged file";
        job.StoryCountBefore = 3;
        var old = Fixture("own-viewer").Replace("Story 1 of 1", "Story 1 of 4");
        Assert.Equal(4, PFlowStoryLayout.OwnViewerCount(old, "Target"));
        Assert.Null(PFlowStoryLayout.NewOwnStoryEvidence(job, old));
        var latest = old.Replace("Story 1 of 4", "Story 4 of 4");
        Assert.NotNull(PFlowStoryLayout.NewOwnStoryEvidence(job, latest));
        // Expiration can cancel out the count increase; presence of the card alone
        // must not convert either an unchanged or reduced active count to success.
        Assert.Null(
            PFlowStoryLayout.NewOwnStoryEvidence(
                job,
                latest.Replace("Story 4 of 4", "Story 3 of 3")
            )
        );
        Assert.Null(
            PFlowStoryLayout.NewOwnStoryEvidence(
                job,
                latest.Replace("Story 4 of 4", "Story 2 of 2")
            )
        );
    }

    [Fact]
    public void PendingLatestStoryCannotConfirmUntilPostingOverlayClears()
    {
        var job = Job();
        job.State = PFlowState.Submitted;
        job.SubmittedUtc = DateTime.UtcNow;
        job.IdentityEvidence = "verified";
        job.MediaSelectionEvidence = "verified";
        job.StoryCountBefore = 2;
        var ready = Fixture("own-viewer").Replace("Story 1 of 1", "Story 3 of 3");
        var posting = ready
            .Replace("No viewers yet", "Posting story…")
            .Replace("Share as post", "Hidden during upload");
        Assert.True(PFlowStoryLayout.HasPendingUpload(posting));
        Assert.Null(PFlowStoryLayout.OwnViewerCount(posting, "Target"));
        Assert.Null(PFlowStoryLayout.NewOwnStoryEvidence(job, posting));
        Assert.NotNull(PFlowStoryLayout.NewOwnStoryEvidence(job, ready));
    }

    [Fact]
    public void ViewerVariantUsesOwnAvatarWhenHeaderCombinesNameAndAge()
    {
        var xml = Fixture("own-viewer")
            .Replace("text=\"Target\"", "text=\"Target 21m\"")
            .Replace("content-desc=\"Target\"", "content-desc=\"Target 21m\"")
            .Replace("Add new story", "Add new");
        Assert.Equal(1, PFlowStoryLayout.OwnViewerCount(xml, "Target"));
        Assert.Null(PFlowStoryLayout.OwnViewerCount(xml, "Another account"));
    }

    private static string PassiveTrayFeed(bool own, bool header = false)
    {
        var doc = XDocument.Parse(Feed(own));
        var tray = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Story tray");
        var cards = tray.Elements("node").Select(n => new XElement(n)).ToArray();
        tray.RemoveNodes();
        tray.Add(
            new XElement(
                "node",
                new XAttribute("class", "android.view.ViewGroup"),
                new XAttribute("enabled", "false"),
                new XAttribute("clickable", "false"),
                new XAttribute("bounds", "[0,264][720,694]"),
                new XElement(
                    "node",
                    new XAttribute("class", "androidx.recyclerview.widget.RecyclerView"),
                    new XAttribute("enabled", "true"),
                    new XAttribute("bounds", "[0,264][720,694]"),
                    cards
                )
            )
        );
        if (!header)
            doc.Descendants("node")
                .Single(n => (n.Attribute("content-desc")?.Value ?? "").StartsWith("Create, "))
                .Remove();
        return doc.ToString();
    }

    [Fact]
    public void ObservedPassiveStoryTrayExposesOnlyEnabledVisibleCards()
    {
        var empty = PassiveTrayFeed(false);
        Assert.True(FacebookLayoutRegistry.Match(empty, FacebookScreenKind.NewsFeed).IsMatch);
        Assert.Null(
            FacebookLayoutRegistry
                .Match(empty, FacebookScreenKind.NewsFeed)
                .Control(FacebookControlRole.Create)
        );
        Assert.NotNull(PFlowStoryLayout.CreateStoryEntry(empty));
        Assert.True(PFlowStoryLayout.HasEmptyOwnStoryBaseline(empty));
        Assert.NotNull(PFlowStoryLayout.OwnStoryEntry(PassiveTrayFeed(true)));
        Assert.False(PFlowStoryLayout.HasEmptyOwnStoryBaseline(PassiveTrayFeed(true)));
        foreach (var attr in new[] { "enabled", "visible-to-user" })
        {
            var doc = XDocument.Parse(empty);
            doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == "Create story")
                .SetAttributeValue(attr, "false");
            Assert.Null(PFlowStoryLayout.CreateStoryEntry(doc.ToString()));
            Assert.False(PFlowStoryLayout.HasEmptyOwnStoryBaseline(doc.ToString()));
        }
        Assert.Null(PFlowStoryLayout.CreateStoryEntry(empty.Replace("Story tray", "Other tray")));
        Assert.Null(
            PFlowStoryLayout.CreateStoryEntry(empty.Replace("[9,286][213,668]", "[0,286][14,668]"))
        );
        Assert.Null(
            PFlowStoryLayout.CreateStoryEntry(
                empty.Replace(
                    "androidx.recyclerview.widget.RecyclerView",
                    "android.widget.FrameLayout"
                )
            )
        );
    }

    [Theory]
    [InlineData("Audio")]
    [InlineData("Music")]
    public void StoryEditorAcceptsAudioOrMusicWithUniqueEnabledShare(string audio)
    {
        var xml = Fixture("editor").Replace("Music", audio);
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryEditor);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(FacebookControlRole.Publish));
        var doc = XDocument.Parse(xml);
        var share = doc.Descendants("node")
            .Single(n =>
                n.Attribute("content-desc")?.Value == "Share"
                && n.Attribute("clickable")?.Value == "true"
            );
        var second = new XElement(share);
        second.RemoveNodes();
        second.SetAttributeValue("bounds", "[16,1160][216,1248]");
        doc.Root!.Add(second);
        Assert.Null(
            FacebookLayoutRegistry
                .Match(doc.ToString(), FacebookScreenKind.StoryEditor)
                .Control(FacebookControlRole.Publish)
        );
    }

    [Fact]
    public void EmptyGalleryIsRecognizedWithoutSelectMultipleButNeverSelectsMedia()
    {
        const string xml =
            "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node text='Create story'/><node text='No photos on this device'/><node class='android.widget.Spinner' content-desc='Select album, Gallery' clickable='true' enabled='true' bounds='[20,220][220,300]'/></node></hierarchy>";
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryPicker).IsMatch);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch);
        Assert.NotNull(PFlowStoryLayout.Dropdown(xml));
        var job = Job();
        Assert.Null(PFlowStoryLayout.Media(xml, PFlowStoryMedia.Read(Index(job), job)!));
        Assert.False(
            FacebookLayoutRegistry
                .Match(
                    xml.Replace("No photos on this device", "Loading gallery"),
                    FacebookScreenKind.StoryPicker
                )
                .IsMatch
        );
    }

    [Fact]
    public async Task ScrolledFeedUsesCreateStoryCardAndAudioEditorWithoutPublishingDuringDryRun()
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.ScrolledFeed = true;
        device.AudioEditor = true;
        device.DelayedGallery = true;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(0, device.ShareTaps);
        Assert.Equal(0, job.StoryCountBefore);
        Assert.False(string.IsNullOrEmpty(job.MediaSelectionEvidence));
    }

    [Fact]
    public async Task ProfessionalPersonalProfileUsesObservedOwnPhotoEditControls()
    {
        var job = Job();
        job.IsProfile = true;
        job.OwnerUid = job.TargetId;
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.ProfessionalProfile = true;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(0, device.ShareTaps);
        Assert.NotNull(job.MediaSelectionEvidence);
    }

    internal static string Feed(bool ownStory) =>
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node text='facebook'/><node text='What&#39;s on your mind?'/><node content-desc='Create, Double tap to create a new post, story, or reel' clickable='true' bounds='[448,48][536,136]'/><node content-desc='Story tray' bounds='[0,264][720,694]'>"
        + (
            ownStory
                ? "<node content-desc='Your story, Target is currently online.' clickable='true' bounds='[9,286][213,668]'/>"
                : "<node content-desc='Create story' clickable='true' bounds='[9,286][213,668]'/><node content-desc='Other&#39;s story, Unseen' clickable='true' bounds='[226,280][440,674]'/>"
        )
        + "</node></node></hierarchy>";

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(1, 1, false)]
    [InlineData(1, 3, false)]
    [InlineData(null, 1, false)]
    public void OwnStoryProofRequiresFreshSingleCountIncrease(int? before, int after, bool accepted)
    {
        var job = Job();
        job.State = PFlowState.Submitted;
        job.SubmittedUtc = DateTime.UtcNow;
        job.StoryCountBefore = before;
        job.IdentityEvidence = "verified";
        job.MediaSelectionEvidence = "verified single-file album";
        var xml = Fixture("own-viewer").Replace("Story 1 of 1", $"Story {after} of {after}");
        Assert.Equal(accepted, PFlowStoryLayout.NewOwnStoryEvidence(job, xml) != null);
        Assert.Equal(accepted ? after : (int?)null, job.StoryCountAfter);
    }

    [Theory]
    [InlineData("wrong-account")]
    [InlineData("pending")]
    [InlineData("posting")]
    [InlineData("keep-open")]
    [InlineData("foreign")]
    [InlineData("no-owner-controls")]
    [InlineData("ambiguous-count")]
    [InlineData("loading")]
    public void OwnStoryProofRejectsUnrelatedOrIncompleteViewer(string problem)
    {
        var xml = Fixture("own-viewer");
        xml = problem switch
        {
            "wrong-account" => xml.Replace("Target", "Another account"),
            "pending" => xml.Replace("No viewers yet", "Uploading..."),
            "posting" => xml.Replace("No viewers yet", "Posting story…"),
            "keep-open" => xml.Replace(
                "No viewers yet",
                "Keep Facebook open while your upload finishes."
            ),
            "foreign" => xml.Replace("com.facebook.katana", "other.app"),
            "loading" => xml.Replace(
                "</hierarchy>",
                "<node class='android.widget.ProgressBar'/></hierarchy>"
            ),
            "no-owner-controls" => xml.Replace("Add new story", "Other control")
                .Replace("Closes the list of viewers", "Other close"),
            _ => xml.Replace("</hierarchy>", "<node content-desc='Story 1 of 2'/></hierarchy>"),
        };
        Assert.Null(PFlowStoryLayout.OwnViewerCount(xml, "Target"));
    }

    [Fact]
    public void EmptyBaselineNeedsLoadedBeginningOfOwnStoryTray()
    {
        Assert.True(PFlowStoryLayout.HasEmptyOwnStoryBaseline(Feed(false)));
        Assert.False(PFlowStoryLayout.HasEmptyOwnStoryBaseline(Feed(true)));
        Assert.False(
            PFlowStoryLayout.HasEmptyOwnStoryBaseline(Feed(false).Replace("Create story", "Other"))
        );
        Assert.False(
            PFlowStoryLayout.HasEmptyOwnStoryBaseline(
                Feed(false)
                    .Replace(
                        "</hierarchy>",
                        "<node class='android.widget.ProgressBar'/></hierarchy>"
                    )
            )
        );
        Assert.Equal(1, PFlowStoryLayout.OwnViewerCount(Fixture("own-viewer"), "Target"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    public async Task NewOwnStoryConfirmsWithoutToastOrNotificationCompletion(int existing)
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.ViewerConfirmation = true;
        device.ExistingStories = existing;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.Equal(existing, job.StoryCountBefore);
        Assert.Equal(existing + 1, job.StoryCountAfter);
        Assert.Contains("Fresh own-Story viewer", job.Evidence);
        Assert.Equal(1, device.ShareTaps);
    }

    internal static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowStory", name + ".xml")
        );

    internal static PFlowJob Job() =>
        new()
        {
            PostType = "Story",
            SourcePath = "synthetic.mp4",
            Size = 123,
            Sha256 = new string('a', 64),
            TargetId = "12345",
            PersonaUid = "12345",
            OwnerUid = "67890",
            TargetName = "Target",
            State = PFlowState.IdentityVerified,
        };

    internal static string Index(PFlowJob job) =>
        $"Row: 0 _id=2713, _data={PFlowStoryMedia.Canonical(job.RemotePath)}, _display_name={Path.GetFileName(job.RemotePath)}, _size={job.Size}, mime_type={PFlowMediaIntent.MimeType(job.SourcePath)}, bucket_display_name={PFlowStoryMedia.AlbumFor(job)}, media_type={(job.SourcePath.EndsWith(".mp4") ? 3 : 1)}, duration=61000";

    [Theory]
    [InlineData("create-menu", FacebookScreenKind.CreateMenu)]
    [InlineData("gallery", FacebookScreenKind.StoryPicker)]
    [InlineData("albums", FacebookScreenKind.StoryAlbums)]
    [InlineData("editor", FacebookScreenKind.StoryEditor)]
    public void CapturedLayoutsHaveOneVariant(string file, FacebookScreenKind screen)
    {
        var result = FacebookLayoutRegistry.Match(Fixture(file));
        Assert.True(result.IsMatch, result.Diagnostic);
        Assert.Equal(screen, result.Screen);
        if (screen == FacebookScreenKind.StoryEditor)
            Assert.NotNull(result.Control(FacebookControlRole.Publish));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("size")]
    [InlineData("mime")]
    [InlineData("album")]
    [InlineData("multiple")]
    [InlineData("duplicate-field")]
    public void IndexRejectsWrongOrAmbiguousFile(string problem)
    {
        var job = Job();
        var row = Index(job);
        row = problem switch
        {
            "path" => row.Replace(
                PFlowStoryMedia.Canonical(job.RemotePath),
                "/storage/emulated/0/DCIM/other.mp4"
            ),
            "size" => row.Replace("_size=123", "_size=124"),
            "mime" => row.Replace("mime_type=video/mp4", "mime_type=image/jpeg"),
            "album" => row.Replace(PFlowStoryMedia.AlbumFor(job), "Gallery"),
            "multiple" => row + "\n" + row.Replace("Row: 0", "Row: 1"),
            _ => row + ", _size=123",
        };
        Assert.Null(PFlowStoryMedia.Read(row, job));
    }

    [Fact]
    public void ExactIndexAndAlbumAreRequiredBeforeThumbnailSelection()
    {
        var job = Job();
        var media = PFlowStoryMedia.Read(Index(job), job);
        Assert.NotNull(media);
        Assert.Null(PFlowStoryLayout.Media(Fixture("gallery"), media));
        var picker = Fixture("gallery")
            .Replace("Select album, Gallery", "Select album, " + media.Album);
        Assert.NotNull(PFlowStoryLayout.Media(picker, media));
        Assert.Null(
            PFlowStoryLayout.Media(
                picker.Replace("1 minute, 1 second", "2 minutes, 5 seconds"),
                media
            )
        );
        var doc = XDocument.Parse(picker);
        var thumbnail = doc.Descendants("node")
            .Single(n => (n.Attribute("content-desc")?.Value ?? "").StartsWith("Select video, "));
        doc.Root!.Add(new XElement(thumbnail));
        Assert.Null(PFlowStoryLayout.Media(doc.ToString(), media));
    }

    [Fact]
    public void DisabledShareAndAuthenticationNeverExposePublicationControl()
    {
        var doc = XDocument.Parse(Fixture("editor"));
        doc.Descendants("node")
            .Single(n =>
                n.Attribute("content-desc")?.Value == "Share"
                && n.Attribute("clickable")?.Value == "true"
            )
            .SetAttributeValue("enabled", "false");
        Assert.Null(
            FacebookLayoutRegistry
                .Match(doc.ToString(), FacebookScreenKind.StoryEditor)
                .Control(FacebookControlRole.Publish)
        );
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "Save your login info?")
            )
        );
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "Not now"),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", "true")
            )
        );
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.StoryEditor).IsMatch
        );
    }

    [Fact]
    public void OnlyFreshStorySpecificEvidenceCounts()
    {
        var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Your story has been shared",
        };
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(
                "<hierarchy><node text='Your story has been shared'/></hierarchy>",
                before,
                "Story"
            )
        );
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(
                "<hierarchy><node text='Reel published'/><node text='Your story'/></hierarchy>",
                before,
                "Story"
            )
        );
        Assert.NotNull(
            PFlowAutoPostService.NewPublicationEvidence(
                "<hierarchy><node text='Story shared'/></hierarchy>",
                before,
                "Story"
            )
        );
    }

    [Fact]
    public void StoryOverlayPreventsBackgroundFeedBrowsing()
    {
        var doc = XDocument.Parse(Fixture("editor"));
        doc.Root!.Add(
            new XElement("node", new XAttribute("text", "facebook")),
            new XElement("node", new XAttribute("text", "What's on your mind?"))
        );
        var match = FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.NewsFeed);
        Assert.False(match.IsMatch);
        Assert.All(
            match.Candidates,
            candidate => Assert.Contains("story-overlay", candidate.ExcludedBy)
        );
    }

    [Fact]
    public void MenuLabelsWithoutObservedListStructureNeverPermitStoryNavigation()
    {
        var xml =
            "<hierarchy><node text='Post'/><node text='Story'/><node text='Reel'/><node text='Live'/></hierarchy>";
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.CreateMenu).IsMatch);
        var picker = XDocument.Parse(Fixture("gallery"));
        var thumb = picker
            .Descendants("node")
            .Single(n => (n.Attribute("content-desc")?.Value ?? "").StartsWith("Select video, "));
        thumb.SetAttributeValue("enabled", "false");
        var job = Job();
        picker
            .Descendants("node")
            .Single(n => n.Attribute("class")?.Value == "android.widget.Spinner")
            .SetAttributeValue("content-desc", "Select album, " + PFlowStoryMedia.AlbumFor(job));
        Assert.Null(
            PFlowStoryLayout.Media(picker.ToString(), PFlowStoryMedia.Read(Index(job), job)!)
        );
    }

    [Theory]
    [InlineData(true, false, PFlowState.CancelledBeforeSubmission)]
    [InlineData(false, false, PFlowState.Confirmed)]
    [InlineData(false, true, PFlowState.Unknown)]
    public async Task StoryUsesUiRouteAndOneDurableShare(
        bool dryRun,
        bool noConfirmation,
        PFlowState expected
    )
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
        var device = (StoryDevice)(object)ld;
        device.Job = job;
        device.NoConfirmation = noConfirmation;
        var checkpoints = new List<PFlowState>();
        await new PFlowAutoPostService(ld)
        {
            PublicationTimeout = noConfirmation ? TimeSpan.Zero : TimeSpan.FromSeconds(1),
        }.PublishAsync(
            job,
            j =>
            {
                checkpoints.Add(j.State);
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None,
            dryRun
        );
        Assert.Equal(expected, job.State);
        Assert.Equal(dryRun ? 0 : 1, device.ShareTaps);
        Assert.Equal(0, device.IntentCalls);
        Assert.Contains("sha256=", job.MediaSelectionEvidence);
        if (!dryRun)
        {
            Assert.True(device.DurableBeforeShare);
            Assert.True(
                checkpoints.IndexOf(PFlowState.SubmitRequested)
                    < checkpoints.IndexOf(PFlowState.Submitted)
            );
        }
    }

    [Fact]
    public async Task IdentityOrChecksumFailureStopsBeforeAnyCreationInput()
    {
        foreach (var problem in new[] { "state", "checksum", "switch", "caption" })
        {
            var job = Job();
            if (problem == "state")
                job.State = PFlowState.Queued;
            if (problem == "caption")
                job.Caption = "Unsupported overlay";
            var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
            var device = (StoryDevice)(object)ld;
            device.Job = job;
            device.Problem = problem;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PFlowAutoPostService(ld).PublishAsync(
                    job,
                    _ => Task.CompletedTask,
                    null,
                    CancellationToken.None
                )
            );
            Assert.Equal(0, device.AllTaps);
        }
    }

    [Fact]
    public async Task CancellationAfterStoryShareKeepsUnknownHistoryAndSource()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "story-cancel-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        try
        {
            var job = Job();
            job.SourcePath = Path.Combine(folder, "clip.mp4");
            await File.WriteAllTextAsync(job.SourcePath, "test media");
            await PFlowJob.FingerprintAsync(job, CancellationToken.None);
            job.State = PFlowState.Queued;
            var store = new PFlowHistoryStore(Path.Combine(folder, "history.db"));
            var ld = DispatchProxy.Create<ILDPlayerService, StoryDevice>();
            var device = (StoryDevice)(object)ld;
            device.Job = job;
            using var cancel = new CancellationTokenSource();
            device.CancelOnShare = cancel;
            Assert.False(
                await new PFlowRunner(ld, store).RunAsync(
                    job,
                    _ => Task.FromResult(true),
                    _ => Task.CompletedTask,
                    null,
                    cancel.Token
                )
            );
            var saved = Assert.Single(await store.ReadAsync());
            Assert.Equal(PFlowState.Unknown, saved.State);
            Assert.False(saved.CanRetry);
            Assert.NotEmpty(saved.MediaSelectionEvidence);
            Assert.Equal(0, device.Deletes);
            Assert.True(File.Exists(job.SourcePath));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task StoryAvailabilityIsSeparateFromReelsAndFollowsFileHistory()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "story-count-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        try
        {
            var job = Job();
            job.SourcePath = Path.Combine(folder, "clip.mp4");
            await File.WriteAllTextAsync(job.SourcePath, "test media");
            await File.WriteAllTextAsync(Path.Combine(folder, "photo.jpg"), "photo");
            await File.WriteAllTextAsync(Path.Combine(folder, "ignore.txt"), "text");
            await PFlowJob.FingerprintAsync(job, CancellationToken.None);
            var page = new FacebookPageTarget
            {
                PageId = job.TargetId,
                StoryPath = folder,
                VideoReelPath = folder,
            };
            var counts = new PFlowMediaAvailability();
            job.State = PFlowState.Unknown;
            Assert.Equal("1 / 2", (await counts.ReadAsync(page, "Story", [job])).Display);
            Assert.Equal("1 / 1", (await counts.ReadAsync(page, "Reel", [job])).Display);
            job.State = PFlowState.Failed;
            Assert.Equal("2 / 2", (await counts.ReadAsync(page, "Story", [job])).Display);
            job.State = PFlowState.Confirmed;
            Assert.Equal("1 / 2", (await counts.ReadAsync(page, "Story", [job])).Display);
            page.StoryPath = "";
            Assert.Empty(page.GetUploadMediaFiles("Story"));
            page.StoryPath = job.SourcePath;
            Assert.Equal("0 / 1", (await counts.ReadAsync(page, "Story", [job])).Display);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    public class StoryDevice : DispatchProxy
    {
        public PFlowJob Job = null!;
        public int AllTaps,
            ShareTaps,
            IntentCalls,
            Deletes;
        public CancellationTokenSource? CancelOnShare;
        public bool NoConfirmation,
            DurableBeforeShare;
        public string Problem = "";
        public bool ViewerConfirmation;
        public bool ScrolledFeed,
            AudioEditor;
        public bool ProfessionalProfile;
        public bool CloseAfterShareFails;
        public int PostingReadsRemaining,
            PostingObserved;
        public bool DelayedGallery;
        public int SelectedReads;
        public int ExistingStories;
        public string? PublicationErrorDescription;
        public string? ClearPostShareXml;
        private string _screen = "profile";

        private string PostShareXml()
        {
            if (ShareTaps > 0 && PublicationErrorDescription != null)
                return new XElement(
                    "hierarchy",
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("content-desc", PublicationErrorDescription)
                    )
                ).ToString();
            var pending = "";
            if (ShareTaps > 0 && PostingReadsRemaining > 0)
            {
                PostingReadsRemaining--;
                PostingObserved++;
                pending =
                    "<node text='Posting story…'/><node text='Keep Facebook open while your upload finishes.'/>";
            }
            if (ShareTaps > 0 && pending.Length == 0 && ClearPostShareXml != null)
                return ClearPostShareXml;
            return "<hierarchy><node package='com.facebook.katana' text='"
                + (NoConfirmation ? "Home" : "Story shared")
                + "'/>"
                + pending
                + "</hierarchy>";
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken token)
                    token.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(
                        _screen switch
                        {
                            "profile" => ProfessionalProfile
                                ? "<hierarchy><node text='Target'/><node text='Dashboard'/><node package='com.facebook.katana' content-desc='Edit cover photo' clickable='true' enabled='true' bounds='[600,80][700,160]'/><node package='com.facebook.katana' content-desc='Edit profile picture' clickable='true' enabled='true' bounds='[250,250][350,350]'/></hierarchy>"
                                : "<hierarchy><node text='Target'/><node text='Professional dashboard'/></hierarchy>",
                            "feed" => ScrolledFeed
                                ? PassiveTrayFeed(
                                    ViewerConfirmation && (ExistingStories > 0 || ShareTaps > 0)
                                )
                                : Feed(
                                    ViewerConfirmation && (ExistingStories > 0 || ShareTaps > 0)
                                ),
                            "own-viewer" => Fixture("own-viewer")
                                .Replace(
                                    "Story 1 of 1",
                                    "Story "
                                        + (ShareTaps > 0 ? ExistingStories + 1 : 1)
                                        + " of "
                                        + (ShareTaps > 0 ? ExistingStories + 1 : ExistingStories)
                                ),
                            "create-menu" => Fixture("create-menu"),
                            "albums" => Fixture("albums")
                                .Replace(
                                    "Select album, DCIM",
                                    "Select album, " + PFlowStoryMedia.AlbumFor(Job)
                                ),
                            "picker" => Fixture("gallery"),
                            "selected" => DelayedGallery && SelectedReads++ < 2
                                ? "<hierarchy><node package='com.facebook.katana' text='Create story'/><node text='No photos on this device'/><node class='android.widget.Spinner' content-desc='Select album, "
                                    + PFlowStoryMedia.AlbumFor(Job)
                                    + "' clickable='true' enabled='true' bounds='[20,220][220,300]'/></hierarchy>"
                                : Fixture("gallery")
                                    .Replace(
                                        "Select album, Gallery",
                                        "Select album, " + PFlowStoryMedia.AlbumFor(Job)
                                    ),
                            "editor" => AudioEditor
                                ? Fixture("editor").Replace("Music", "Audio")
                                : Fixture("editor"),
                            _ => PostShareXml(),
                        }
                    );
                case "ExecuteAdbAsync":
                    var command = (string)args![1]!;
                    if (command.Contains("fb://feed"))
                        _screen = "feed";
                    return Task.FromResult(
                        command.Contains("sha256sum")
                            ? Problem == "checksum"
                                ? ""
                                : Job.Sha256 + " file"
                            : command.Contains("stat -c")
                                ? Job.Size.ToString()
                                : command.Contains("content query")
                                    ? Index(Job)
                                    : command.Contains("dumpsys notification")
                                        ? "  Notification List:\n  mArchive=Archive (0 notifications)"
                                        : ""
                    );
                case "SwitchToPersonaAsync":
                    return Task.FromResult(Problem != "switch");
                case "ShareVideoToFacebookAsync":
                case "ShareImageToFacebookAsync":
                    IntentCalls++;
                    return Task.FromResult(false);
                case "TapAsync":
                    AllTaps++;
                    if (_screen == "feed" && (int)args![2]! > 200)
                    {
                        _screen =
                            ScrolledFeed
                            && (!ViewerConfirmation || ExistingStories == 0 && ShareTaps == 0)
                                ? "picker"
                                : "own-viewer";
                        return Task.FromResult(true);
                    }
                    if (_screen == "editor")
                    {
                        ShareTaps++;
                        DurableBeforeShare =
                            Job.State == PFlowState.SubmitRequested && Job.SubmittedUtc.HasValue;
                        CancelOnShare?.Cancel();
                    }
                    _screen = _screen switch
                    {
                        "feed" => "create-menu",
                        "create-menu" => "picker",
                        "picker" => "albums",
                        "albums" => "selected",
                        "selected" => "editor",
                        "editor" => ViewerConfirmation ? "feed" : "done",
                        _ => throw new InvalidOperationException("Unexpected tap"),
                    };
                    return Task.FromResult(true);
                case "PressBackAsync":
                    if (CloseAfterShareFails && ShareTaps > 0)
                        return Task.FromResult(false);
                    _screen = "feed";
                    return Task.FromResult(true);
            }
            if (method.Name == "DeleteFileFromEmulatorAsync")
                Deletes++;
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(true);
            if (method.ReturnType == typeof(Task))
                return Task.CompletedTask;
            throw new NotSupportedException(method.Name);
        }
    }
}
