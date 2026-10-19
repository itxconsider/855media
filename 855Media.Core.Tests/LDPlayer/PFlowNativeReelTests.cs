using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNativeReelTests
{
    [Theory]
    [InlineData("ld2-feed-after-john-ward-confirmed")]
    [InlineData("ld2-feed-after-kc-waterheaters-confirmed")]
    public async Task VerifiedContinuationOpensNextNativeReelFromCapturedHome(string fixture)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReelDevice>();
        var device = (ReelDevice)(object)ld;
        device.InitialOwnXml = FacebookLayoutRegistryTests.Fixture(fixture);
        var job = new PFlowJob
        {
            TargetName = "Target",
            TargetId = "123",
            SourcePath = "test.mp4",
            State = PFlowState.IdentityVerified,
            Size = 1,
            Sha256 = new string('A', 64),
            NativeReelAlbum = true,
        };
        job.TargetContinuation = new(
            job.JobId,
            PFlowPostTargetSession.Target.Read(job),
            DateTime.UtcNow
        );
        device.Job = job;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(0, device.AndroidShares + device.PublishTaps);
        Assert.StartsWith("Bloks Reel picker", job.MediaSelectionEvidence);
    }

    [Fact]
    public async Task DelayedMediaIndexRecoversOnBloksInsteadOfFallingBack()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReelDevice>();
        var device = (ReelDevice)(object)ld;
        device.EmptyIndexReads = 2;
        var job = new PFlowJob
        {
            TargetName = "Target",
            TargetId = "123",
            SourcePath = "test.mp4",
            State = PFlowState.IdentityVerified,
            Size = 1,
            Sha256 = new string('A', 64),
            NativeReelAlbum = true,
        };
        device.Job = job;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(0, device.AndroidShares + device.PublishTaps);
        Assert.True(device.IndexReads >= 3);
        Assert.StartsWith("Bloks Reel picker", job.MediaSelectionEvidence);
    }

    [Fact]
    public void CapturedCreateMenuChoosesButtonDespiteDuplicateChildLabel()
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld56-create-menu-reel.xml"
            )
        );
        var node = PFlowReelLayout.CreateEntry(xml);
        Assert.NotNull(node);
        Assert.Equal("true", node!.Attribute("clickable")?.Value);
        Assert.Equal("Reel", node.Attribute("content-desc")?.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthenticationOrChangedBytesStopWithoutAndroidFallback(bool authentication)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReelDevice>();
        var device = (ReelDevice)(object)ld;
        device.Authentication = authentication;
        device.ChangedBytes = !authentication;
        var job = new PFlowJob
        {
            TargetName = "Target",
            TargetId = "123",
            SourcePath = "test.mp4",
            State = PFlowState.IdentityVerified,
            Size = 1,
            Sha256 = new string('A', 64),
            NativeReelAlbum = true,
        };
        device.Job = job;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAutoPostService(ld).PublishAsync(
                job,
                _ => Task.CompletedTask,
                null,
                CancellationToken.None,
                dryRun: true
            )
        );
        Assert.Contains("no fallback", error.Message);
        Assert.Equal(0, device.AndroidShares);
        Assert.Equal(0, device.PublishTaps);
    }

    [Fact]
    public void CapturedNativePickerAndAlbumsAreRegistered()
    {
        var picker = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld56-reel-picker.xml"
            )
        );
        var albums = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld56-reel-albums.xml"
            )
        );
        Assert.True(FacebookLayoutRegistry.Match(picker, FacebookScreenKind.ReelPicker).IsMatch);
        Assert.True(FacebookLayoutRegistry.Match(albums, FacebookScreenKind.StoryAlbums).IsMatch);
        Assert.False(FacebookLayoutRegistry.Match(picker, FacebookScreenKind.StoryPicker).IsMatch);
    }

    [Fact]
    public void GallerySelectionRejectsWrongAlbumMultipleTilesAndWrongDuration()
    {
        var media = new PFlowStoryMedia(
            42,
            "/storage/emulated/0/test.mp4",
            "Reel_test",
            "video/mp4",
            1,
            61000
        );
        var good = ReelDevice.Picker("Reel_test", true);
        Assert.NotNull(PFlowReelLayout.Media(good, media));
        Assert.Null(PFlowReelLayout.Media(good.Replace("Reel_test", "Other"), media));
        Assert.Null(
            PFlowReelLayout.Media(good.Replace("1 minute, 1 second", "2 minutes, 2 seconds"), media)
        );
        Assert.Null(
            PFlowReelLayout.Media(
                good.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' content-desc='Create reel with Video, item 2' clickable='true' bounds='[200,250][300,350]'/></hierarchy>"
                ),
                media
            )
        );
        Assert.Null(
            PFlowReelLayout.CreateEntry(
                "<hierarchy><node content-desc='Reel' clickable='true' bounds='[0,0][100,100]'/></hierarchy>"
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFirstOrVerifiedAndroidFallbackReachesDryRunComposer(bool unavailable)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ReelDevice>();
        var device = (ReelDevice)(object)ld;
        device.Unavailable = unavailable;
        var job = new PFlowJob
        {
            TargetName = "Target",
            TargetId = "123",
            SourcePath = "test.mp4",
            PostType = "Reel",
            State = PFlowState.IdentityVerified,
            Size = 1,
            Sha256 = new string('A', 64),
            NativeReelAlbum = true,
        };
        device.Job = job;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            CancellationToken.None,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(unavailable ? 1 : 0, device.AndroidShares);
        Assert.Equal(0, device.PublishTaps);
        Assert.Equal(!unavailable, job.MediaSelectionEvidence.StartsWith("Bloks Reel picker"));
    }

    public class ReelDevice : DispatchProxy
    {
        public PFlowJob Job = null!;
        public string? InitialOwnXml;
        public bool Unavailable,
            Authentication,
            ChangedBytes;
        int HashReads;
        public int EmptyIndexReads,
            IndexReads;
        public int AndroidShares,
            PublishTaps;
        string State = "Own";
        const string Head = "<hierarchy><node bounds='[0,0][720,1280]'/>",
            Tail = "</hierarchy>";

        static string Node(string label, string bounds, string extra = "") =>
            $"<node package='com.facebook.katana' text='{label}' content-desc='{label}' clickable='true' enabled='true' bounds='{bounds}' {extra}/>";

        public static string Picker(string album, bool tile) =>
            Head
            + Node("Create reel", "[250,20][450,60]")
            + Node("Select multiple", "[400,100][650,140]")
            + Node("Select album, " + album, "[0,100][200,140]", "class='android.widget.Spinner'")
            + (
                tile
                    ? Node(
                        "Create reel with Video, item 1, button, 1 minute, 1 second",
                        "[0,250][200,350]"
                    )
                    : ""
            )
            + Tail;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    if (Authentication && State == "Picker")
                        return Task.FromResult<string?>(
                            "<hierarchy><node package='com.facebook.katana' password='true' bounds='[0,0][200,40]'/></hierarchy>"
                        );
                    return Task.FromResult<string?>(
                        State switch
                        {
                            "Own" => InitialOwnXml
                                ?? Head
                                    + Node("Target", "[0,0][200,40]")
                                    + Node("Professional dashboard", "[0,50][200,90]")
                                    + Node("Create reel", "[0,100][200,140]")
                                    + Tail,
                            "Picker" => Picker("Gallery", false),
                            "Albums" => Head
                                + "<node package='com.facebook.katana' class='android.widget.ListView'>"
                                + Node(
                                    "Select album, Reel_" + Job.JobId,
                                    "[0,180][200,220]",
                                    "class='android.widget.Button'"
                                )
                                + "</node>"
                                + Tail,
                            "Selected" => Picker("Reel_" + Job.JobId, true),
                            "Editor" => Head
                                + Node("Add audio", "[0,0][100,40]")
                                + Node("Effects", "[100,0][200,40]")
                                + Node("Next", "[250,30][350,70]")
                                + Tail,
                            "Chooser" => PFlowShareDestinationTests.SelectedReelsXml,
                            _ => Head
                                + Node("Target", "[0,0][100,40]")
                                + Node("Reel Settings", "[100,0][300,50]")
                                + Node("Share now", "[200,1160][500,1260]")
                                + Tail,
                        }
                    );
                case "ExecuteAdbAsync":
                    var cmd = (string)args![1]!;
                    return Task.FromResult(
                        cmd.Contains("stat -c") ? "1"
                        : cmd.Contains("sha256sum")
                            ? ((++HashReads > 1 && ChangedBytes) ? new string('B', 64) : Job.Sha256)
                                + " file"
                        : cmd.Contains("content query")
                            ? (
                                (++IndexReads <= EmptyIndexReads || Unavailable)
                                    ? ""
                                    : "Row: 0 _id=42, _data="
                                        + PFlowStoryMedia.Canonical(Job.RemotePath)
                                        + ", _display_name="
                                        + Path.GetFileName(Job.RemotePath)
                                        + ", _size=1, mime_type=video/mp4, bucket_display_name=Reel_"
                                        + Job.JobId
                                        + ", media_type=3, duration=61000"
                            )
                        : ""
                    );
                case "ShareVideoToFacebookAsync":
                    AndroidShares++;
                    State = "Chooser";
                    return Task.FromResult(true);
                case "TapAsync":
                    var y = (int)args![2]!;
                    if (y == 1210)
                        PublishTaps++;
                    State = State switch
                    {
                        "Own" => "Picker",
                        "Picker" => "Albums",
                        "Albums" => "Selected",
                        "Selected" => "Editor",
                        "Editor" => "Composer",
                        "Chooser" => "Composer",
                        _ => State,
                    };
                    return Task.FromResult(true);
            }
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(true);
            if (method.ReturnType == typeof(Task))
                return Task.CompletedTask;
            throw new NotSupportedException(method.Name);
        }
    }
}
