using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPhotoGalleryEntryTests
{
    [Fact]
    public void CapturedCaptionEditorHasUniqueNonPublishingDone()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld14-photo-caption-editor");
        Assert.NotNull(PFlowPhotoComposerLayout.CaptionEditorDone(xml));
        Assert.Null(
            PFlowPhotoComposerLayout.CaptionEditorDone(
                xml.Replace("com.facebook.katana", "com.other.app")
            )
        );
        Assert.Null(
            PFlowPhotoComposerLayout.CaptionEditorDone(xml.Replace("Add text", "Other editor"))
        );
        var doc = XDocument.Parse(xml);
        var done = doc.Descendants("node")
            .Single(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Done"
            );
        done.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowPhotoComposerLayout.CaptionEditorDone(doc.ToString()));
        done.SetAttributeValue("enabled", "true");
        done.AddAfterSelf(new XElement(done));
        Assert.Null(PFlowPhotoComposerLayout.CaptionEditorDone(doc.ToString()));
    }

    [Fact]
    public void CapturedCompactDraftSupportsBottomNextAndEditableCaption()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld14-compact-photo-draft");
        Assert.True(PFlowPhotoComposerLayout.IsDraft(xml));
        Assert.True(PFlowPhotoComposerLayout.CaptionMatches(xml, ""));
        Assert.NotNull(PFlowAutoPostService.FindPFlowCaptionField(xml));
        Assert.False(PFlowPhotoGalleryEntry.IsEmptyEntry(xml));
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(FacebookControlRole.Continue));
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.True(PFlowUiPrivacy.ContainsInput(xml));
        var doc = XDocument.Parse(xml);
        var field = doc.Descendants("node")
            .Single(n => n.Attribute("class")?.Value == "android.widget.AutoCompleteTextView");
        field.SetAttributeValue("text", "Your post is now published");
        Assert.True(
            PFlowPhotoComposerLayout.CaptionMatches(doc.ToString(), "Your post is now published")
        );
        Assert.DoesNotContain(
            "Your post is now published",
            PFlowUiPrivacy.RedactForDiagnostics(doc.ToString())
        );
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(
                doc.ToString(),
                new HashSet<string>(),
                "Picture"
            )
        );
    }

    [Fact]
    public void CapturedAndroid14CompactEntryHasExactGalleryButNoSubmissionControl()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld14-compact-photo-entry");
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoPostEntry);
        Assert.True(match.IsMatch, match.Diagnostic);
        var gallery = match.Control(FacebookControlRole.PhotoGallery)!;
        Assert.Equal((68, 1212), (gallery.X, gallery.Y));
        Assert.True(PFlowPhotoComposerLayout.DraftTargetMatches(xml, "Alucar Trax"));
        Assert.False(PFlowPhotoComposerLayout.DraftTargetMatches(xml, "Alucard"));
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.Null(match.Control(FacebookControlRole.Continue));
    }

    [Theory]
    [InlineData("caption")]
    [InlineData("disabled-gallery")]
    [InlineData("disabled-parent")]
    [InlineData("foreign")]
    [InlineData("duplicate-field")]
    [InlineData("clipped")]
    public void CompactEntryRejectsUnsafeOrNonemptyFrames(string mutation)
    {
        var doc = XDocument.Parse(FacebookLayoutRegistryTests.Fixture("ld14-compact-photo-entry"));
        var field = doc.Descendants("node")
            .Single(n => n.Attribute("class")?.Value == "android.widget.AutoCompleteTextView");
        var gallery = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Gallery");
        if (mutation == "caption")
            field.SetAttributeValue("text", "Existing user draft");
        if (mutation == "disabled-gallery")
            gallery.SetAttributeValue("enabled", "false");
        if (mutation == "disabled-parent")
            gallery.Parent!.Parent!.Parent!.SetAttributeValue("enabled", "false");
        if (mutation == "foreign")
            field.SetAttributeValue("package", "com.other.app");
        if (mutation == "duplicate-field")
            field.AddAfterSelf(new XElement(field));
        if (mutation == "clipped")
            gallery.SetAttributeValue("bounds", "[24,1260][112,1350]");
        Assert.False(PFlowPhotoGalleryEntry.IsEmptyEntry(doc.ToString()));
    }

    static string Fixture() => FacebookLayoutRegistryTests.Fixture("ld56-empty-new-post-gallery");

    [Fact]
    public void CapturedEmptyDraftUsesEnabledGalleryInsideOnlyKnownDisabledWrapper()
    {
        var xml = Fixture();
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoPostEntry);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("photo.post-entry-gallery.v1", match.VariantId);
        var control = match.Control(FacebookControlRole.PhotoGallery);
        Assert.NotNull(control);
        Assert.InRange(control!.X, 24, 211);
        Assert.InRange(control.Y, 1003, 1134);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer).IsMatch);
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.Null(match.Control(FacebookControlRole.Continue));
        Assert.Contains(
            XDocument.Parse(xml).Descendants("node"),
            n =>
                n.Attribute("enabled")?.Value == "false"
                && n.Attribute("content-desc")?.Value == "Add to your post"
        );
    }

    [Fact]
    public void CapturedGroupDraftSupportsNextButDoesNotExposePublicationOrEmptyGallery()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld3-photo-group-draft");
        Assert.True(PFlowPhotoComposerLayout.IsDraft(xml));
        Assert.True(PFlowPhotoGroups.ComposerCountMatches(xml, 3));
        Assert.False(PFlowPhotoGroups.ComposerCountMatches(xml, 2));
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(FacebookControlRole.Continue));
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.False(PFlowPhotoGalleryEntry.IsEmptyEntry(xml));
    }

    [Fact]
    public void CapturedDefaultMultiPickerNeedsNoSelectMultipleToggle()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld6-photo-default-multi");
        Assert.Null(PFlowPhotoGroupLayout.Control(xml, "Select multiple"));
        Assert.True(PFlowPhotoGroupLayout.HasNativeCheckboxTiles(xml));
        Assert.True(PFlowPhotoGroupLayout.IsPicker(xml));
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer).IsMatch);
    }

    [Fact]
    public void CapturedGroupReviewHasReadOnlyPhotoButtonsAndSingleBottomPost()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld6-photo-group-review");
        Assert.True(PFlowPhotoComposerLayout.IsReview(xml));
        Assert.True(PFlowPhotoGroups.ComposerCountMatches(xml, 3));
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("composer.photo-review.v1", match.VariantId);
        Assert.NotNull(match.Control(FacebookControlRole.Publish));
        Assert.Null(match.Control(FacebookControlRole.Continue));
    }

    [Fact]
    public void LatestLd56FailureCaptureHasOneUsableGalleryAction()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld56-empty-gallery-20261026");
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoPostEntry);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(FacebookControlRole.PhotoGallery));
        Assert.True(PFlowPhotoComposerLayout.DraftTargetMatches(xml, "KCWaterheaters"));
        Assert.Null(PFlowPhotoGalleryEntry.LegacyMediaControl(xml, "KCWaterheaters"));
        Assert.False(PFlowAutoPostService.IsComposer(xml, "Picture"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteModernDraftCannotChooseLegacyRouteEvenWithPhotoControl(bool photoControl)
    {
        var doc = XDocument.Parse(Fixture());
        doc.Descendants("node")
            .Single(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Gallery"
            )
            .Remove();
        if (photoControl)
            doc.Root!.Add(
                XElement.Parse(
                    PFlowPhotoGroupTests.PhotoDevice.Node("Photo/video", "[20,800][200,900]")
                )
            );
        var xml = doc.ToString();
        Assert.True(PFlowAutoPostService.IsComposer(xml, "Picture")); // Reproduces the premature legacy classification.
        Assert.False(PFlowPhotoGalleryEntry.IsEmptyEntry(xml));
        Assert.Null(PFlowPhotoGalleryEntry.LegacyMediaControl(xml, "KCWaterheaters"));
    }

    [Fact]
    public void LegacyCreatePostRequiresUsableMediaActionAndMatchingActor()
    {
        var xml = Fixture()
            .Replace("New post", "Create post")
            .Replace(
                "</hierarchy>",
                PFlowPhotoGroupTests.PhotoDevice.Node("Photo/video", "[20,800][200,900]")
                    + "</hierarchy>"
            );
        Assert.NotNull(PFlowPhotoGalleryEntry.LegacyMediaControl(xml, "KCWaterheaters"));
        Assert.Null(PFlowPhotoGalleryEntry.LegacyMediaControl(xml, "Other Actor"));
        Assert.Null(
            PFlowPhotoGalleryEntry.LegacyMediaControl(
                xml.Replace("Photo/video", "Missing action"),
                "KCWaterheaters"
            )
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("filled")]
    [InlineData("wrong-actor")]
    [InlineData("cancelled")]
    public async Task UnreadyOrChangedModernEntryNeverTapsMediaOrShare(string failure)
    {
        var job = new PFlowJob
        {
            TargetId = "target",
            TargetName = "Target",
            PostType = "Picture",
            SourcePath = "a.jpg",
            Size = 1,
            Sha256 = new string('A', 64),
            NativePhotoAlbum = true,
        };
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowPhotoGroupTests.PhotoDevice>();
        var d = (PFlowPhotoGroupTests.PhotoDevice)(object)ld;
        d.Job = job;
        d.EmptyGalleryEntry = true;
        d.EntryDelayReads = failure == "missing" ? int.MaxValue : 0;
        d.FilledEntry = failure == "filled";
        d.WrongEntryActor = failure == "wrong-actor";
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(1400));
        if (failure == "cancelled")
            cancel.Cancel();
        var open = typeof(PFlowAutoPostService).GetMethod(
            "OpenNativePhotoGroupComposerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        async Task Run() =>
            await (Task<string>)
                open.Invoke(
                    new PFlowAutoPostService(ld),
                    [job, (Func<PFlowJob, Task>)(_ => Task.CompletedTask), null, cancel.Token]
                )!;
        if (failure == "wrong-actor")
            await Assert.ThrowsAsync<InvalidOperationException>(Run);
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        Assert.Equal(
            0,
            d.GalleryTaps + d.EarlyEntryTaps + d.Selected + d.PostTaps + d.ShareIntents
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedPlainCategoryWrapperRequiresAllKnownCategoryButtons(bool removeLive)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld9-empty-new-post-gallery")
        );
        if (removeLive)
            doc.Descendants("node")
                .Single(n =>
                    n.Attribute("class")?.Value == "android.widget.Button"
                    && n.Attribute("content-desc")?.Value == "Live"
                )
                .Remove();
        Assert.Equal(
            !removeLive,
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.PhotoPostEntry).IsMatch
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedMultiPickerCountsEachCompoundTileOnceAndReadsSelection(bool selected)
    {
        var doc = XDocument.Parse(FacebookLayoutRegistryTests.Fixture("ld3-photo-multiselect"));
        const string album = "Photo_063a3850812f44b3bf9524be5424af1d";
        if (selected)
            doc.Descendants("node")
                .First(n => n.Attribute("class")?.Value == "android.widget.CompoundButton")
                .SetAttributeValue("checked", "true");
        var tiles = PFlowPhotoGroupLayout.VisibleTiles(doc.ToString(), album);
        Assert.Equal(3, tiles.Length);
        Assert.All(
            tiles,
            t => Assert.Equal("android.widget.CompoundButton", t.Attribute("class")?.Value)
        );
        Assert.Equal(selected, PFlowPhotoGroupLayout.Selected(tiles[0]));
        Assert.False(PFlowPhotoGroupLayout.Selected(tiles[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedPhotoAlbumRowRequiresExactAlbumAndEnabledRow(bool disabled)
    {
        var doc = XDocument.Parse(FacebookLayoutRegistryTests.Fixture("ld3-photo-albums"));
        const string album = "Photo_c471b91849ef45d6b338dbc1c0ddb0a6";
        if (disabled)
            doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == album + ", 1, Media")
                .SetAttributeValue("enabled", "false");
        var xml = doc.ToString();
        Assert.Equal(!disabled, PFlowPhotoGroupLayout.Album(xml, album) != null);
        Assert.Null(PFlowPhotoGroupLayout.Album(xml, "Photo_other"));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("disabled-button")]
    [InlineData("disabled-grid")]
    [InlineData("wrong-bounds")]
    public void CapturedGridPickerOnlyToleratesKnownDecorativeWrapper(string mutation)
    {
        var doc = XDocument.Parse(FacebookLayoutRegistryTests.Fixture("ld3-photo-picker-gallery"));
        var button = doc.Descendants("node")
            .Single(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Select multiple"
            );
        var grid = button
            .Ancestors()
            .Single(n => n.Attribute("class")?.Value == "android.widget.GridView");
        if (mutation == "disabled-button")
            button.SetAttributeValue("enabled", "false");
        if (mutation == "disabled-grid")
            grid.SetAttributeValue("enabled", "false");
        if (mutation == "wrong-bounds")
            grid.Parent!.SetAttributeValue("bounds", "[0,0][720,1200]");
        var xml = doc.ToString();
        Assert.Equal(mutation == "valid", PFlowPhotoGroupLayout.IsPicker(xml));
        if (mutation == "valid")
            Assert.NotNull(PFlowPhotoGroupLayout.Dropdown(xml));
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedUnnamedCategoryWrapperRequiresMatchingViewport(bool wrongBounds)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld3-empty-new-post-gallery")
        );
        if (wrongBounds)
            doc.Descendants("node")
                .Single(n =>
                    n.Attribute("class")?.Value == "android.view.ViewGroup"
                    && n.Attribute("enabled")?.Value == "false"
                    && n.Parent?.Attribute("class")?.Value == "android.widget.AbsListView"
                )
                .SetAttributeValue("bounds", "[0,900][720,1165]");
        var match = FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.PhotoPostEntry);
        Assert.Equal(!wrongBounds, match.IsMatch);
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.NewsFeed).IsMatch
        );
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("filled")]
    [InlineData("attached")]
    [InlineData("disabled-parent")]
    [InlineData("offscreen")]
    public void UnsafeOrChangedEntryIsRejected(string mutation)
    {
        var doc = XDocument.Parse(Fixture());
        var gallery = doc.Descendants("node")
            .Single(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Gallery"
            );
        switch (mutation)
        {
            case "disabled":
                gallery.SetAttributeValue("enabled", "false");
                break;
            case "hidden":
                gallery.SetAttributeValue("visible-to-user", "false");
                break;
            case "duplicate":
                gallery.Parent!.Add(new XElement(gallery));
                break;
            case "foreign":
                foreach (
                    var n in doc.Descendants("node")
                        .Where(n => n.Attribute("package")?.Value == "com.facebook.katana")
                )
                    n.SetAttributeValue("package", "other.app");
                break;
            case "filled":
                doc.Descendants("node")
                    .Single(n => n.Attribute("class")?.Value == "android.widget.EditText")
                    .SetAttributeValue("text", "Existing draft text");
                break;
            case "attached":
                doc.Root!.Add(new XElement("node", new XAttribute("content-desc", "Remove photo")));
                break;
            case "disabled-parent":
                gallery.Parent!.Parent!.SetAttributeValue("enabled", "false");
                break;
            case "offscreen":
                gallery.SetAttributeValue("bounds", "[24,2003][211,2134]");
                break;
        }
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.PhotoPostEntry).IsMatch
        );
    }

    [Theory]
    [InlineData(false, 1, 0, false)]
    [InlineData(true, 1, 0, false)]
    [InlineData(true, 3, 0, false)]
    [InlineData(false, 1, 3, false)]
    [InlineData(true, 1, 3, false)]
    [InlineData(true, 3, 3, false)]
    [InlineData(false, 1, 3, true)]
    public async Task GalleryStepContinuesThroughExactAlbumWithoutNextOrShareFromEmptyDraft(
        bool createMenu,
        int count,
        int delayedReads,
        bool transientPhoto
    )
    {
        var job = new PFlowJob
        {
            TargetId = "target",
            TargetName = "Target",
            PostType = "Picture",
            SourcePath = "a.jpg",
            Size = 1,
            Sha256 = new string('A', 64),
            NativePhotoAlbum = true,
        };
        if (count > 1)
            job.PhotoFiles = Enumerable
                .Range(0, count)
                .Select(i => new PFlowPhotoFile
                {
                    SourcePath = $"{i}.jpg",
                    Size = 1,
                    Sha256 = new string((char)('A' + i), 64),
                })
                .ToList();
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowPhotoGroupTests.PhotoDevice>();
        var device = (PFlowPhotoGroupTests.PhotoDevice)(object)ld;
        device.Job = job;
        device.EmptyGalleryEntry = true;
        device.CreateMenuEntry = createMenu;
        device.EntryDelayReads = delayedReads;
        device.TransientPhotoControl = transientPhoto;
        var open = typeof(PFlowAutoPostService).GetMethod(
            "OpenNativePhotoGroupComposerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var result = await (Task<string>)
            open.Invoke(
                new PFlowAutoPostService(ld),
                [job, (Func<PFlowJob, Task>)(_ => Task.CompletedTask), null, CancellationToken.None]
            )!;
        Assert.Contains("Create post", result);
        Assert.Equal(1, device.GalleryTaps);
        Assert.Equal(0, device.EarlyEntryTaps);
        Assert.True(device.EntryReads > delayedReads);
        Assert.Equal(count, device.Selected);
        Assert.Equal(0, device.ShareIntents + device.PostTaps);
    }
}
