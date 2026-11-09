using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPhotoComposerTests
{
    internal static string Draft(string caption = "Fixture photo caption")
    {
        var xml = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld56-new-photo-post-filled")
        );
        var field = xml.Descendants("node")
            .Single(n => n.Attribute("class")?.Value == "android.widget.EditText");
        field.SetAttributeValue("text", caption);
        return xml.ToString();
    }

    internal static string Settings() =>
        FacebookLayoutRegistryTests.Fixture("photo-post-settings-reconstructed");

    static (PFlowJob Job, ILDPlayerService Ld, PhotoDevice Device) Harness()
    {
        var job = new PFlowJob
        {
            DeviceIndex = 995,
            TargetId = "target",
            TargetName = "KCWaterheaters",
            PostType = "Picture",
            SourcePath = "fixture.jpg",
            Caption = "Fixture photo caption",
            Size = 1,
            Sha256 = new string('A', 64),
            State = PFlowState.IdentityVerified,
        };
        var ld = DispatchProxy.Create<ILDPlayerService, PhotoDevice>();
        var device = (PhotoDevice)(object)ld;
        device.Job = job;
        return (job, ld, device);
    }

    static Task<(string Xml, PFlowAutoPostService.PhotoSettingsProof Proof)> Advance(
        PFlowJob job,
        ILDPlayerService ld,
        CancellationToken ct = default
    ) =>
        new PFlowAutoPostService(ld).AdvancePhotoSettingsAsync(
            job,
            Draft(),
            x => Task.FromResult(PFlowAutoPostService.ComposerMatchesTarget(x, job.TargetName)),
            _ => Task.CompletedTask,
            null,
            ct
        );

    [Fact]
    public void CapturedFilledDraftRemainsRecognizedAndNextIsNeverPublish()
    {
        foreach (
            var caption in new[] { "What's on your mind?", "123", "Fixture photo caption\n#tag" }
        )
        {
            var xml = Draft(caption);
            var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoComposer);
            Assert.True(match.IsMatch, match.Diagnostic);
            Assert.Equal("composer.photo-native.v1", match.VariantId);
            Assert.NotNull(match.Control(FacebookControlRole.Continue));
            Assert.Null(match.Control(FacebookControlRole.Publish));
            Assert.True(PFlowPhotoComposerLayout.CaptionMatches(xml, caption));
        }
        var settings = FacebookLayoutRegistry.Match(Settings(), FacebookScreenKind.PhotoComposer);
        Assert.True(settings.IsMatch, settings.Diagnostic);
        Assert.Equal("composer.photo-settings.v1", settings.VariantId);
        Assert.NotNull(settings.Control(FacebookControlRole.Publish));
        Assert.Null(settings.Control(FacebookControlRole.Continue));
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("foreign")]
    [InlineData("picker")]
    [InlineData("no-photo")]
    public void UnknownOrUnsafeDraftDoesNotMatch(string mutation)
    {
        var doc = XDocument.Parse(Draft());
        if (mutation == "hidden")
            doc.Descendants("node")
                .First(n =>
                    n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("content-desc")?.Value == "Next"
                )
                .SetAttributeValue("visible-to-user", "false");
        if (mutation == "foreign")
            foreach (
                var n in doc.Descendants("node")
                    .Where(n => n.Attribute("package")?.Value == "com.facebook.katana")
            )
                n.SetAttributeValue("package", "another.app");
        if (mutation == "picker")
            doc.Root!.Add(new XElement("node", new XAttribute("text", "Select multiple")));
        if (mutation == "no-photo")
            foreach (
                var n in doc.Descendants("node")
                    .Where(n =>
                        new[] { "Photo", "Remove photo", "Edit photo" }.Contains(
                            n.Attribute("content-desc")?.Value
                        )
                    )
                    .ToArray()
            )
                n.Remove();
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.PhotoComposer).IsMatch
        );
    }

    [Fact]
    public async Task OneNextCreatesLocalProofWithoutShareOrGraphReads()
    {
        var (job, ld, d) = Harness();
        var result = await Advance(job, ld);
        Assert.True(await result.Proof.VerifyAsync(ld, job, result.Xml, null, default));
        Assert.Equal(1, d.NextTaps);
        Assert.Equal(0, d.ShareTaps);
        Assert.Equal(0, d.CaptionSets);
        Assert.All(d.Commands, c => Assert.DoesNotContain("authentication", c));
        Assert.Contains("Off · Marc Aurelius", result.Xml);
    }

    [Fact]
    public async Task NativeReviewRequiresSameActorAndVisibleCaptionProof()
    {
        var (job, ld, d) = Harness();
        d.Review = true;
        var result = await Advance(job, ld);
        Assert.True(await result.Proof.VerifyAsync(ld, job, result.Xml, null, default));
        Assert.False(
            await result.Proof.VerifyAsync(
                ld,
                job,
                result.Xml.Replace("KCWaterheaters", "Other Actor"),
                null,
                default
            )
        );
        Assert.False(
            await result.Proof.VerifyAsync(
                ld,
                job,
                result.Xml.Replace(job.Caption, "Different caption"),
                null,
                default
            )
        );
        Assert.Equal(0, d.ShareTaps);
    }

    [Theory]
    [InlineData("Caption")]
    [InlineData("Target")]
    [InlineData("Job")]
    [InlineData("Source")]
    [InlineData("Hash")]
    [InlineData("Process")]
    public async Task SettingsProofRejectsChangedJobOrProcess(string mutation)
    {
        var (job, ld, d) = Harness();
        var result = await Advance(job, ld);
        switch (mutation)
        {
            case "Caption":
                job.Caption = "changed";
                break;
            case "Target":
                job.TargetName = "Other";
                break;
            case "Job":
                job.JobId = Guid.NewGuid().ToString("N");
                break;
            case "Source":
                job.SourcePath = "other.jpg";
                break;
            case "Hash":
                job.Sha256 = "changed";
                break;
            case "Process":
                d.Pid = "202";
                break;
        }
        Assert.False(await result.Proof.VerifyAsync(ld, job, result.Xml, null, default));
        Assert.Equal(0, d.ShareTaps);
    }

    [Theory]
    [InlineData("wrong-caption")]
    [InlineData("stale-next")]
    [InlineData("changed-pid")]
    [InlineData("wrong-actor")]
    public async Task BrokenHandoffStopsWithoutShare(string reason)
    {
        var (job, ld, d) = Harness();
        d.Failure = reason;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Advance(job, ld));
        Assert.Equal(0, d.ShareTaps);
        Assert.True(d.NextTaps <= 1);
        if (reason is "wrong-caption" or "stale-next")
            Assert.Equal(0, d.NextTaps);
    }

    [Fact]
    public async Task CancellationNeverAdvances()
    {
        var (job, ld, d) = Harness();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Advance(job, ld, cancel.Token)
        );
        Assert.Equal(0, d.NextTaps + d.ShareTaps);
    }

    [Fact]
    public async Task SettingsCannotBeAcceptedWithoutThisAttemptsProof()
    {
        var (job, ld, d) = Harness();
        d.State = "Settings";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Advance(job, ld));
        Assert.Equal(0, d.NextTaps + d.ShareTaps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullPublisherTypesCaptionOnceAdvancesNextAndStopsBeforeShare(bool review)
    {
        var (job, ld, d) = Harness();
        d.State = "Own";
        d.Review = review;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(1, d.CaptionSets);
        Assert.Equal(1, d.NextTaps);
        Assert.Equal(0, d.ShareTaps);
        Assert.Null(job.SubmittedUtc);
        Assert.Equal(job.Caption, d.Caption);
    }

    [Fact]
    public async Task SinglePhotoUsesNativeAlbumDirectFeedIconWithoutSharingIntent()
    {
        var job = new PFlowJob
        {
            TargetId = "target",
            TargetName = "Target",
            PostType = "Picture",
            SourcePath = "fixture.jpg",
            Size = 1,
            Sha256 = new string('A', 64),
            State = PFlowState.IdentityVerified,
            NativePhotoAlbum = true,
        };
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowPhotoGroupTests.PhotoDevice>();
        var d = (PFlowPhotoGroupTests.PhotoDevice)(object)ld;
        d.Job = job;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(1, d.Selected);
        Assert.Equal(0, d.ShareIntents + d.PostTaps);
        Assert.Equal(job.RemotePath, Assert.Single(d.Pushes));
        Assert.Contains("Photo_" + job.JobId, job.RemotePath);
    }

    [Fact]
    public void DirectFeedPhotoIconIsBoundToComposerRowNotAFeedImage()
    {
        var feed = FacebookLayoutRegistryTests
            .Fixture("ld2-feed-create-reel-card")
            .Replace(
                "</hierarchy>",
                PhotoDevice.Node("Photo/video", "[610,62][700,130]")
                    + PhotoDevice.Node("Photo", "[500,700][680,900]")
                    + "</hierarchy>"
            );
        var entry = PFlowPhotoGroupLayout.FeedPhotoEntry(feed);
        Assert.NotNull(entry);
        Assert.Equal("[610,62][700,130]", entry!.Attribute("bounds")?.Value);
    }

    [Fact]
    public void FeedPhotoActionCannotBeSelectedBehindPhotoPicker()
    {
        var feed = FacebookLayoutRegistryTests
            .Fixture("ld2-feed-create-reel-card")
            .Replace(
                "</hierarchy>",
                PhotoDevice.Node("New post", "[250,20][450,60]")
                    + PhotoDevice.Node("Select multiple", "[400,100][700,140]")
                    + "<node package='com.facebook.katana' class='android.widget.Spinner' content-desc='Select album, Gallery' clickable='true' enabled='true' bounds='[0,100][200,140]'/></hierarchy>"
            );
        Assert.True(FacebookLayoutRegistry.Match(feed, FacebookScreenKind.PhotoPicker).IsMatch);
        Assert.False(FacebookLayoutRegistry.Match(feed, FacebookScreenKind.NewsFeed).IsMatch);
        Assert.Null(PFlowPhotoGroupLayout.FeedPhotoEntry(feed));
    }

    [Fact]
    public async Task SingleNativePhotoCleansExactFileAndItsAlbum()
    {
        var job = new PFlowJob
        {
            PostType = "Picture",
            SourcePath = "fixture.jpg",
            NativePhotoAlbum = true,
            State = PFlowState.CancelledBeforeSubmission,
            Size = 1,
            Sha256 = new string('A', 64),
        };
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowPhotoGroupTests.PhotoDevice>();
        var d = (PFlowPhotoGroupTests.PhotoDevice)(object)ld;
        d.Job = job;
        var runner = new PFlowRunner(
            ld,
            new PFlowHistoryStore(
                Path.Combine(
                    Path.GetTempPath(),
                    "photo-cleanup-" + Guid.NewGuid().ToString("N") + ".db"
                )
            )
        );
        await runner.CleanupRemoteAsync(job, null, default);
        Assert.Equal("Verified", job.RemoteCleanupState);
        Assert.Equal(job.RemotePath, Assert.Single(d.Deletes));
        Assert.EndsWith("/001.jpg", job.RemotePath);
    }

    [Fact]
    public void CaptionCannotStandInForThePhotoDraftActorName()
    {
        var doc = XDocument.Parse(Draft("KCWaterheaters"));
        foreach (
            var n in doc.Descendants("node")
                .Where(n => n.Attribute("class")?.Value != "android.widget.EditText")
        )
        {
            if (n.Attribute("text")?.Value == "KCWaterheaters")
                n.SetAttributeValue("text", "Other actor");
            if (n.Attribute("content-desc")?.Value == "KCWaterheaters")
                n.SetAttributeValue("content-desc", "Other actor");
        }
        Assert.True(PFlowPhotoComposerLayout.IsDraft(doc.ToString()));
        Assert.False(PFlowPhotoComposerLayout.DraftTargetMatches(doc.ToString(), "KCWaterheaters"));
    }

    public class PhotoDevice : DispatchProxy
    {
        public PFlowJob Job = null!;
        public bool Review;
        public string State = "Draft",
            Caption = "Fixture photo caption",
            Pid = "101",
            Failure = "";
        public int NextTaps,
            ShareTaps,
            CaptionSets,
            DraftReads;
        public List<string> Commands = [];

        public static string Node(string text, string bounds) =>
            $"<node package='com.facebook.katana' text='{text}' clickable='true' enabled='true' bounds='{bounds}'/>";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    if (State == "Draft" && ++DraftReads >= 2 && Failure == "stale-next")
                        State = "Settings";
                    return Task.FromResult<string?>(
                        State switch
                        {
                            "Own" => "<hierarchy>"
                                + Node("KCWaterheaters", "[0,0][400,40]")
                                + Node("Professional dashboard", "[0,50][400,90]")
                                + "</hierarchy>",
                            "Chooser" => PFlowShareDestinationTests.SelectedReelsXml.Replace(
                                "Share with Reels",
                                "Share with Feed"
                            ),
                            "Settings" => (
                                Review
                                    ? FacebookLayoutRegistryTests
                                        .Fixture("ld6-photo-review")
                                        .Replace("Navigation validation. Not published.", Caption)
                                    : Settings()
                            ).Replace(
                                "</hierarchy>",
                                Failure == "wrong-actor"
                                    ? Node("Posting as Other", "[20,1100][400,1150]")
                                        + "</hierarchy>"
                                    : "</hierarchy>"
                            ),
                            _ => Draft(Failure == "wrong-caption" ? "Different caption" : Caption),
                        }
                    );
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(
                        "com.facebook.katana/com.facebook.katana.MainActivity"
                    );
                case "ExecuteAdbAsync":
                    var cmd = (string)args![1]!;
                    Commands.Add(cmd);
                    if (
                        cmd.Contains("authentication")
                        || cmd.Contains("underlying_account")
                        || cmd.Contains("graph.facebook")
                    )
                        throw new Exception("Unexpected identity read");
                    if (cmd.Contains("pidof"))
                        return Task.FromResult(
                            State == "Settings" && Failure == "changed-pid" ? "202" : Pid
                        );
                    if (cmd.Contains("stat -c"))
                        return Task.FromResult("1");
                    if (cmd.Contains("sha256sum"))
                        return Task.FromResult(Job.Sha256 + " file");
                    if (cmd.Contains("ADB_CLEAR_TEXT"))
                        Caption = "";
                    if (cmd.Contains("ADB_INPUT_B64"))
                    {
                        Caption = Encoding.UTF8.GetString(
                            Convert.FromBase64String(
                                Regex.Match(cmd, @"--es msg '([^']*)'").Groups[1].Value
                            )
                        );
                        CaptionSets++;
                    }
                    return Task.FromResult("Broadcast completed: result=0");
                case "ShareImageToFacebookAsync":
                    State = "Chooser";
                    return Task.FromResult(true);
                case "TapAsync":
                    if (State == "Chooser")
                    {
                        State = "Draft";
                        Caption = "What's on your mind?";
                    }
                    else if (State == "Draft" && (int)args![1]! > 500 && (int)args[2]! < 250)
                    {
                        NextTaps++;
                        State = "Settings";
                    }
                    else if (State == "Settings")
                        ShareTaps++;
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
