using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowPhotoGroupTests : IDisposable
{
    readonly string dir = Path.Combine(
        Path.GetTempPath(),
        "photo-groups-" + Guid.NewGuid().ToString("N")
    );

    public PFlowPhotoGroupTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    string Photo(string name, string? caption = null)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, name);
        if (caption != null)
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), caption);
        return path;
    }

    async Task<PFlowJob> Job(params string[] paths)
    {
        var job = new PFlowJob
        {
            TargetId = "target",
            TargetName = "Target",
            PostType = "Picture",
            SourcePath = paths[0],
            PhotoFiles =
                paths.Length > 1
                    ? paths.Select(p => new PFlowPhotoFile { SourcePath = p }).ToList()
                    : [],
        };
        await PFlowJob.FingerprintAsync(job, default);
        return job;
    }

    [Fact]
    public void GroupsExactOriginalCaptionsAndIgnoresSuffix()
    {
        var a = Photo("a.jpg", "A caption\r\n#tag ");
        var b = Photo("b.png", " A caption\n#tag");
        var c = Photo("c.webp", "Different caption");
        var groups = PFlowPhotoGroups.Group([a, b, c, a], true);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { a, b }, groups[0]);
        Assert.Equal(new[] { c }, groups[1]);
        Assert.Equal(3, PFlowPhotoGroups.Group([a, b, c], false).Count);
    }

    [Fact]
    public void FileSourceFindsOnlyMatchingSiblingPhotosWhenEnabled()
    {
        var a = Photo("a.jpg", "Same");
        var b = Photo("b.png", "Same");
        Photo("c.jpg", "Other");
        var target = new FacebookPageTarget { ImagePath = a, PhotoTitleFromTextFile = true };
        Assert.Equal(new[] { a }, target.GetUploadMediaFiles("Picture"));
        target.GroupPhotosWithSameCaption = true;
        Assert.Equal(new[] { a, b }, target.GetUploadMediaFiles("Picture"));
    }

    [Fact]
    public void FilenameFallbackAndBoundedGroupsRemainDeterministic()
    {
        var a = Photo("Same.jpg");
        var b = Photo("Same.png");
        Assert.Equal(new[] { a, b }, Assert.Single(PFlowPhotoGroups.Group([b, a], true)));
        var many = Enumerable.Range(0, 23).Select(i => Photo($"p{i:D2}.jpg", "Caption")).ToArray();
        Assert.Equal(new[] { 10, 10, 3 }, PFlowPhotoGroups.Group(many, true).Select(g => g.Length));
    }

    [Fact]
    public async Task FingerprintIncludesEveryPhotoSurvivesJsonAndRejectsChanges()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.png");
        var job = await Job(a, b);
        var reverse = await Job(b, a);
        Assert.Equal(job.Sha256, reverse.Sha256);
        Assert.Equal(new FileInfo(a).Length + new FileInfo(b).Length, job.Size);
        Assert.Equal(2, job.RemoteMediaPaths.Count);
        Assert.All(job.RemoteMediaPaths, p => Assert.Contains("Photo_" + job.JobId, p));
        var restored = JsonSerializer.Deserialize<PFlowJob>(JsonSerializer.Serialize(job))!;
        Assert.Equal(job.PhotoFiles[1].Sha256, restored.PhotoFiles[1].Sha256);
        File.WriteAllText(b, "Changed");
        await Assert.ThrowsAsync<IOException>(() => PFlowJob.FingerprintAsync(restored, default));
    }

    [Fact]
    public async Task GroupAndSingleOverlapAreBlockedTransactionallyAndCountOnePost()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.png");
        var c = Photo("c.jpg");
        var history = new PFlowHistoryStore(Path.Combine(dir, "history.db"));
        var group = await Job(a, b);
        Assert.True(await history.ReserveAsync(group));
        Assert.False(await history.ReserveAsync(await Job(b)));
        Assert.False(await history.ReserveAsync(await Job(b, c)));
        Assert.True(await history.ReserveAsync(await Job(c), dailyLimit: 2));
        var target = new FacebookPageTarget { PageId = "target", ImagePath = dir };
        var available = await new PFlowMediaAvailability().ReadAsync(
            target,
            "Picture",
            await history.ReadAsync()
        );
        Assert.Empty(available.Files);
        Assert.Equal(3, available.Held);
    }

    [Fact]
    public async Task SingleHistoryBlocksGroupAndConfirmedMembersStayPosted()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.png");
        var history = new PFlowHistoryStore(Path.Combine(dir, "history.db"));
        var single = await Job(b);
        Assert.True(await history.ReserveAsync(single));
        single.State = PFlowState.Confirmed;
        single.Evidence = "Manual review: published";
        single.ConfirmedUtc = DateTime.UtcNow;
        await history.SaveAsync(single);
        Assert.False(await history.ReserveAsync(await Job(a, b)));
        var available = await new PFlowMediaAvailability().ReadAsync(
            new() { PageId = "target", ImagePath = dir },
            "Picture",
            await history.ReadAsync()
        );
        Assert.Equal(new[] { a }, available.Files);
        Assert.Equal(1, available.Posted);
    }

    [Fact]
    public async Task BackupAndDeleteCoverAllMembersAndSidecars()
    {
        var a = Photo("a.jpg", "Caption");
        var b = Photo("b.png", "Caption");
        var history = new PFlowHistoryStore(Path.Combine(dir, "history.db"));
        var job = await Job(a, b);
        Assert.True(await history.ReserveAsync(job));
        job.State = PFlowState.Confirmed;
        job.Evidence = "Manual review: published";
        job.ConfirmedUtc = DateTime.UtcNow;
        job.BackupRequested = true;
        job.DeleteRequested = true;
        await history.SaveAsync(job);
        var ld = DispatchProxy.Create<ILDPlayerService, PhotoDevice>();
        var runner = new PFlowRunner(ld, history);
        await runner.BackupAsync(job, default);
        Assert.Equal("Verified", job.BackupState);
        Assert.All(job.PhotoFiles, f => Assert.True(File.Exists(f.BackupPath)));
        await history.SaveAsync(job);
        await runner.DeleteSharedSourceAsync(job, [job], default);
        Assert.Equal("Verified", job.LocalDeleteState);
        Assert.All(
            new[] { a, b, Path.ChangeExtension(a, ".txt"), Path.ChangeExtension(b, ".txt") },
            p => Assert.False(File.Exists(p))
        );
        Assert.All(job.PhotoFiles, f => Assert.True(File.Exists(f.BackupPath)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingConsumerOrChangedMemberRetainsWholeGroup(bool pending)
    {
        var a = Photo("a.jpg");
        var b = Photo("b.png");
        var history = new PFlowHistoryStore(Path.Combine(dir, "history.db"));
        var job = await Job(a, b);
        Assert.True(await history.ReserveAsync(job));
        job.State = PFlowState.Confirmed;
        job.Evidence = "Manual review: published";
        job.ConfirmedUtc = DateTime.UtcNow;
        job.DeleteRequested = true;
        await history.SaveAsync(job);
        var consumer = await Job(b);
        consumer.TargetId = "another-target";
        consumer.DeleteRequested = true;
        if (pending)
            Assert.True(await history.ReserveAsync(consumer));
        else
            File.WriteAllText(b, "Changed");
        var runner = new PFlowRunner(
            DispatchProxy.Create<ILDPlayerService, PhotoDevice>(),
            history
        );
        await runner.DeleteSharedSourceAsync(job, pending ? [job, consumer] : [job], default);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.NotEqual("Verified", job.LocalDeleteState);
    }

    [Fact]
    public async Task RemoteCleanupDeletesAllExactPathsAndHeldGroupsStayUntouched()
    {
        var job = await Job(Photo("a.jpg"), Photo("b.png"));
        var ld = DispatchProxy.Create<ILDPlayerService, PhotoDevice>();
        var device = (PhotoDevice)(object)ld;
        device.Job = job;
        var runner = new PFlowRunner(ld, new(Path.Combine(dir, "history.db")));
        job.State = PFlowState.Unknown;
        await runner.CleanupRemoteAsync(job, null, default);
        Assert.Empty(device.Deletes);
        job.State = PFlowState.CancelledBeforeSubmission;
        await runner.CleanupRemoteAsync(job, null, default);
        Assert.Equal("Verified", job.RemoteCleanupState);
        Assert.Equal(job.RemoteMediaPaths, device.Deletes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(10)]
    public async Task NativeAlbumSelectsEveryPhotoOnceAndNeverTapsPost(int count)
    {
        var files = Enumerable.Range(0, count).Select(i => Photo($"{i}.jpg")).ToArray();
        var job = await Job(files);
        var ld = DispatchProxy.Create<ILDPlayerService, PhotoDevice>();
        var device = (PhotoDevice)(object)ld;
        device.Job = job;
        device.PageSize = 3;
        var service = new PFlowAutoPostService(ld);
        var open = typeof(PFlowAutoPostService).GetMethod(
            "OpenNativePhotoGroupComposerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var xml = await (Task<string>)
            open.Invoke(
                service,
                [job, (Func<PFlowJob, Task>)(_ => Task.CompletedTask), null, CancellationToken.None]
            )!;
        Assert.Contains("Create post", xml);
        Assert.Equal(files.Length, device.Selected);
        Assert.StartsWith("Native photo album verified:", job.MediaSelectionEvidence);
        Assert.Equal(0, device.PostTaps);
        Assert.Equal(0, device.ShareIntents);
    }

    [Fact]
    public async Task WrongAlbumIndexBlocksBeforePhotoSelection()
    {
        var job = await Job(Photo("a.jpg"), Photo("b.jpg"));
        var ld = DispatchProxy.Create<ILDPlayerService, PhotoDevice>();
        var device = (PhotoDevice)(object)ld;
        device.Job = job;
        device.BadIndex = true;
        var service = new PFlowAutoPostService(ld);
        var open = typeof(PFlowAutoPostService).GetMethod(
            "OpenNativePhotoGroupComposerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            (Task<string>)
                open.Invoke(
                    service,
                    [
                        job,
                        (Func<PFlowJob, Task>)(_ => Task.CompletedTask),
                        null,
                        CancellationToken.None,
                    ]
                )!
        );
        Assert.Equal(0, device.Selected + device.PostTaps + device.ShareIntents);
    }

    [Theory]
    [InlineData("2 photos", 2, true)]
    [InlineData("3 photos", 2, false)]
    [InlineData("2 photos selected", 2, true)]
    [InlineData("Photo 1 of 2", 2, true)]
    [InlineData("", 2, false)]
    public void ComposerCountMustMatch(string label, int count, bool expected) =>
        Assert.Equal(
            expected,
            PFlowPhotoGroups.ComposerCountMatches(
                $"<hierarchy><node text='{label}'/></hierarchy>",
                count
            )
        );

    [Theory]
    [InlineData("", true)]
    [InlineData("2 photos", true)]
    [InlineData("3 photos", false)]
    public async Task ComposerUsesFreshNativeSelectionProofAndRejectsContradictoryCounts(
        string label,
        bool expected
    )
    {
        var job = await Job(Photo("a.jpg"), Photo("b.jpg"));
        var xml = $"<hierarchy><node text='{label}'/></hierarchy>";
        Assert.False(PFlowPhotoGroups.ComposerMatchesGroup(xml, job));
        job.MediaSelectionEvidence =
            $"Native photo album verified: Photo_{job.JobId}; count=2; all selected once";
        Assert.Equal(expected, PFlowPhotoGroups.ComposerMatchesGroup(xml, job));
        job.JobId = Guid.NewGuid().ToString("N");
        Assert.False(PFlowPhotoGroups.ComposerMatchesGroup(xml, job));
    }

    [Fact]
    public async Task PublisherTransfersAllPhotosAndReachesReadyComposerWithoutPost()
    {
        var job = await Job(Photo("a.jpg"), Photo("b.jpg"));
        job.State = PFlowState.IdentityVerified;
        var ld = DispatchProxy.Create<ILDPlayerService, PhotoDevice>();
        var device = (PhotoDevice)(object)ld;
        device.Job = job;
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            default,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(job.RemoteMediaPaths, device.Pushes);
        Assert.Equal(2, device.Selected);
        Assert.Equal(0, device.PostTaps + device.ShareIntents);
    }

    public class PhotoDevice : DispatchProxy
    {
        public PFlowJob Job = null!;
        public bool BadIndex;
        public int Selected,
            PostTaps,
            ShareIntents;
        public List<string> Deletes = [];
        public List<string> Pushes = [];
        public bool EmptyGalleryEntry,
            CreateMenuEntry;
        public int GalleryTaps;
        public int EntryDelayReads,
            EntryReads,
            EarlyEntryTaps;
        public bool TransientPhotoControl,
            WrongEntryActor,
            FilledEntry;

        string EmptyEntry()
        {
            EntryReads++;
            var doc = System.Xml.Linq.XDocument.Parse(
                FacebookLayoutRegistryTests
                    .Fixture("ld56-empty-new-post-gallery")
                    .Replace("KCWaterheaters", WrongEntryActor ? "Other Actor" : Job.TargetName)
            );
            if (FilledEntry)
                doc.Descendants("node")
                    .Single(n => n.Attribute("class")?.Value == "android.widget.EditText")
                    .SetAttributeValue("text", "Changed draft text");
            if (EntryReads <= EntryDelayReads)
            {
                doc.Descendants("node")
                    .Single(n =>
                        n.Attribute("class")?.Value == "android.widget.Button"
                        && n.Attribute("content-desc")?.Value == "Gallery"
                    )
                    .Remove();
                if (TransientPhotoControl)
                    doc.Root!.Add(
                        System.Xml.Linq.XElement.Parse(Node("Photo/video", "[20,800][200,900]"))
                    );
            }
            return doc.ToString();
        }

        public int PageSize = 10;
        int offset;
        string state = "Own";
        bool album;

        public static string Node(string label, string bounds, string extra = "") =>
            $"<node package='com.facebook.katana' text='{label}' content-desc='{label}' enabled='true' clickable='true' bounds='{bounds}' {extra}/>";

        string Picker() =>
            "<hierarchy><node bounds='[0,0][720,1280]'>"
            + Node("New post", "[250,20][450,60]")
            + Node("Select multiple", "[400,100][700,140]")
            + Node(
                "Select album, " + (album ? "Photo_" + Job.JobId : "Pictures"),
                "[0,100][240,140]",
                "class='android.widget.Spinner'"
            )
            + (
                album
                    ? "<node package='com.facebook.katana' class='android.widget.GridView' scrollable='true' bounds='[0,240][720,1000]'>"
                        + string.Join(
                            "",
                            Enumerable
                                .Range(0, Job.MediaFiles.Count)
                                .Skip(offset)
                                .Take(PageSize)
                                .Select(i =>
                                    Node(
                                        "Photo, " + i,
                                        $"[{(i - offset) % 3 * 200},{250 + (i - offset) / 3 * 220}][{(i - offset) % 3 * 200 + 190},{450 + (i - offset) / 3 * 220}]",
                                        $"selected='{(i < Selected ? "true" : "false")}'"
                                    )
                                )
                        )
                        + "</node>"
                    : ""
            )
            + Node("Next", "[500,1150][700,1250]")
            + "</node></hierarchy>";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(
                        state switch
                        {
                            "Own" => "<hierarchy>"
                                + Node("Target", "[0,0][200,40]")
                                + Node("Professional dashboard", "[0,50][200,90]")
                                + "</hierarchy>",
                            "Feed" => FacebookLayoutRegistryTests
                                .Fixture("ld2-feed-create-reel-card")
                                .Replace(
                                    "</hierarchy>",
                                    (
                                        CreateMenuEntry
                                            ? Node("Create", "[500,20][600,60]")
                                            : Node("Photo/video", "[610,62][700,130]")
                                    ) + "</hierarchy>"
                                ),
                            "CreateMenu" =>
                                "<hierarchy><node package='com.facebook.katana' class='android.widget.ListView'>"
                                    + string.Join(
                                        "",
                                        new[] { "Post", "Story", "Reel", "Live" }.Select(
                                            (label, i) =>
                                                Node(
                                                    label,
                                                    $"[20,{150 + i * 80}][300,{210 + i * 80}]",
                                                    "class='android.widget.Button'"
                                                )
                                        )
                                    )
                                    + "</node></hierarchy>",
                            "EmptyEntry" => EmptyEntry(),
                            "Albums" =>
                                "<hierarchy><node package='com.facebook.katana' class='android.widget.ListView'>"
                                    + Node(
                                        "Select album, Photo_" + Job.JobId,
                                        "[0,180][300,220]",
                                        "class='android.widget.Button'"
                                    )
                                    + "</node></hierarchy>",
                            "Composer" => "<hierarchy>"
                                + Node("Create post", "[0,0][300,40]")
                                + Node("Target", "[0,50][200,90]")
                                + Node(
                                    "Write something...",
                                    "[0,150][700,250]",
                                    "class='android.widget.EditText'"
                                )
                                + Node(Job.MediaFiles.Count + " photos", "[0,100][200,140]")
                                + Node("Post", "[500,1150][700,1250]")
                                + "</hierarchy>",
                            _ => Picker(),
                        }
                    );
                case "ExecuteAdbAsync":
                    var cmd = (string)args![1]!;
                    if (cmd.Contains("fb://feed"))
                        state = "Feed";
                    var member = Job
                        .RemoteMediaPaths.Select((p, i) => (p, i))
                        .FirstOrDefault(x => cmd.Contains(x.p));
                    if (cmd.Contains("stat -c"))
                        return Task.FromResult(Job.MediaFiles[member.i].Size.ToString());
                    if (cmd.Contains("sha256sum"))
                        return Task.FromResult(Job.MediaFiles[member.i].Sha256 + " file");
                    if (cmd.Contains("content query") && cmd.Contains("bucket_display_name"))
                        return Task.FromResult(
                            string.Join(
                                "\n",
                                Job.MediaFiles.Select(
                                    (f, i) =>
                                        $"Row: {i} _id={i + 1}, _data={Job.PhotoRemotePath(i)}, _display_name={Path.GetFileName(Job.PhotoRemotePath(i))}, _size={f.Size}, mime_type=image/jpeg, bucket_display_name=Photo_{(BadIndex ? new string('0', 32) : Job.JobId)}, media_type=1"
                                )
                            )
                        );
                    return Task.FromResult(
                        cmd.Contains("REMOVED")
                            ? (cmd.Contains("ALBUM_REMOVED") ? "ALBUM_REMOVED" : "REMOVED")
                        : cmd.Contains("content query") ? "No result found."
                        : ""
                    );
                case "DeleteFileFromEmulatorAsync":
                    Deletes.Add((string)args![1]!);
                    return Task.FromResult(true);
                case "PushFileToEmulatorAsync":
                    Pushes.Add((string)args![2]!);
                    return Task.FromResult(true);
                case "ShareImageToFacebookAsync":
                case "ShareVideoToFacebookAsync":
                    ShareIntents++;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    if (state == "Picker")
                        offset = Math.Min(offset + PageSize, Job.MediaFiles.Count - 1);
                    return Task.FromResult(true);
                case "TapAsync":
                    var y = (int)args![2]!;
                    if (state == "Feed")
                        state =
                            CreateMenuEntry ? "CreateMenu"
                            : EmptyGalleryEntry ? "EmptyEntry"
                            : "Picker";
                    else if (state == "CreateMenu")
                        state = "EmptyEntry";
                    else if (state == "EmptyEntry")
                    {
                        if (EntryReads <= EntryDelayReads)
                        {
                            EarlyEntryTaps++;
                            throw new Exception("Tapped incomplete photo entry");
                        }
                        if (y < 1000)
                            throw new Exception("Unexpected Next/publish tap from empty draft");
                        GalleryTaps++;
                        state = "Picker";
                    }
                    else if (state == "Picker" && y == 120 && (int)args[1]! < 250)
                        state = "Albums";
                    else if (state == "Albums")
                    {
                        album = true;
                        state = "Picker";
                    }
                    else if (state == "Picker" && y >= 250 && y < 1000)
                        Selected++;
                    else if (state == "Picker" && y == 1200)
                        state = "Composer";
                    else if (state == "Composer")
                        PostTaps++;
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
