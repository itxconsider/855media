using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    private async Task<string> OpenNativePhotoGroupComposerAsync(
        PFlowJob job,
        Func<PFlowJob, Task> checkpoint,
        string? path,
        CancellationToken ct
    )
    {
        var album = "Photo_" + job.JobId;
        job.CurrentStep = job.IsPhotoGroup
            ? "Opening native multi-photo album"
            : "Opening native photo album";
        await checkpoint(job);
        async Task<string> Observe()
        {
            var xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            xml = await PFlowMediaAccess.CompleteAsync(
                _ldPlayer,
                job.DeviceIndex,
                job.AppPackage,
                path,
                xml,
                ct
            );
            PFlowPageIdentityRestriction.ThrowIfVisible(job, xml);
            var nodes = ScreenTimeHierarchy.Nodes(xml);
            if (
                !nodes.Any()
                || nodes.Any(n => n.Attribute("password")?.Value == "true")
                || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                || nodes.Any(n =>
                    n.Attribute("package")?.Value is { Length: > 0 } p
                    && p != job.AppPackage
                    && p != "com.android.systemui"
                )
            )
                throw new InvalidOperationException(
                    "Photo navigation reached an empty, authentication or foreign layout."
                );
            return xml;
        }
        async Task<string> WaitFor(Func<string, bool> recognized)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                var xml = await Observe();
                if (recognized(xml))
                    return xml;
                await Task.Delay(400, ct);
            } while (timer.Elapsed < TimeSpan.FromSeconds(15));
            throw new InvalidOperationException(
                "Native photo layout was not recognized; no Post tap was made."
            );
        }
        async Task Tap(Func<string, XElement?> control)
        {
            var xml = await Observe();
            var node = control(xml);
            var point = node == null ? null : PFlowStoryLayout.ControlCenter(node);
            if (
                point == null
                || !await _ldPlayer.TapAsync(
                    job.DeviceIndex,
                    point.Value.X,
                    point.Value.Y,
                    path,
                    ct
                )
            )
                throw new InvalidOperationException(
                    "Native photo control missing, ambiguous or unacknowledged."
                );
            await Task.Delay(400, ct);
        }
        async Task VerifyIndex()
        {
            var query =
                "shell content query --uri content://media/external/file --projection _id:_data:_display_name:_size:mime_type:bucket_display_name:media_type --where "
                + ShellQuote("bucket_display_name='" + album + "'");
            for (var retry = 0; retry < 8; retry++)
            {
                if (
                    PFlowPhotoGroupLayout.IndexMatches(
                        await _ldPlayer.ExecuteAdbAsync(job.DeviceIndex, query, path, ct),
                        job
                    )
                )
                    return;
                await Task.Delay(400, ct);
            }
            throw new InvalidOperationException(
                "Photo album index did not match every staged file."
            );
        }
        var initial = await Observe();
        if (
            IsComposer(initial, "Picture")
            || PFlowPhotoGroupLayout.IsPicker(initial)
            || ScreenTimeHierarchy
                .Nodes(initial)
                .Any(n => n.Attribute("class")?.Value == "android.widget.EditText")
        )
            throw new InvalidOperationException(
                "Existing input/composer was left untouched; close it before posting."
            );
        await VerifyIndex();
        await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell am start -a android.intent.action.VIEW -d 'fb://feed' -p "
                + ShellQuote(job.AppPackage),
            path,
            ct
        );
        var feed = await WaitFor(x =>
            FacebookLayoutRegistry.Match(x, FacebookScreenKind.NewsFeed).IsMatch
        );
        if (PFlowPhotoGroupLayout.FeedPhotoEntry(feed) != null)
            await Tap(x => PFlowPhotoGroupLayout.FeedPhotoEntry(x));
        else
        {
            var create = FacebookLayoutRegistry
                .Match(await Observe(), FacebookScreenKind.NewsFeed)
                .Control(FacebookControlRole.Create);
            if (
                create == null
                || !await _ldPlayer.TapAsync(job.DeviceIndex, create.X, create.Y, path, ct)
            )
                throw new InvalidOperationException("Feed photo/Create control unavailable.");
            await WaitFor(x =>
                FacebookLayoutRegistry.Match(x, FacebookScreenKind.CreateMenu).IsMatch
            );
            await Tap(x =>
                FacebookLayoutRegistry.Match(x, FacebookScreenKind.CreateMenu).IsMatch
                    ? PFlowPhotoGroupLayout.Control(x, "Post")
                    : null
            );
        }
        job.CurrentStep = "Waiting for native photo media entry";
        await checkpoint(job);
        var post = await WaitFor(x =>
            PFlowPhotoGroupLayout.IsPicker(x)
            || FacebookLayoutRegistry.Match(x, FacebookScreenKind.PhotoPostEntry).IsMatch
            || PFlowPhotoGalleryEntry.LegacyMediaControl(x, job.TargetName) != null
        );
        if (FacebookLayoutRegistry.Match(post, FacebookScreenKind.PhotoPostEntry).IsMatch)
        {
            var fresh = post;
            var entry = FacebookLayoutRegistry.Match(fresh, FacebookScreenKind.PhotoPostEntry);
            var gallery = entry.Control(FacebookControlRole.PhotoGallery);
            if (
                !entry.IsMatch
                || gallery == null
                || !PFlowPhotoComposerLayout.DraftTargetMatches(fresh, job.TargetName)
                || PFlowPhotoComposerLayout.ActorContradicts(fresh, job.TargetName)
                || !await _ldPlayer.TapAsync(job.DeviceIndex, gallery.X, gallery.Y, path, ct)
            )
                throw new InvalidOperationException(
                    "Empty New post Gallery control or target could not be verified; no repeated tap attempted."
                );
            job.CurrentStep = "Opening Gallery from empty New post";
            await checkpoint(job);
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "photo.gallery.entry",
                "emptyDraft=true; targetMatched=true; GalleryTappedOnce=true; noNextOrShareTap=true",
                ct
            );
        }
        else if (!PFlowPhotoGroupLayout.IsPicker(post))
        {
            var legacy = PFlowPhotoGalleryEntry.LegacyMediaControl(post, job.TargetName);
            var point = legacy == null ? null : PFlowStoryLayout.ControlCenter(legacy);
            if (
                point == null
                || !await _ldPlayer.TapAsync(
                    job.DeviceIndex,
                    point.Value.X,
                    point.Value.Y,
                    path,
                    ct
                )
            )
                throw new InvalidOperationException(
                    "Legacy photo media action was unavailable or unacknowledged; no repeated tap attempted."
                );
        }
        await WaitFor(PFlowPhotoGroupLayout.IsPicker);
        await Tap(PFlowPhotoGroupLayout.Dropdown);
        var found = false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var xml = await Observe();
            if (PFlowPhotoGroupLayout.Album(xml, album) != null)
            {
                await Tap(x => PFlowPhotoGroupLayout.Album(x, album));
                found = true;
                break;
            }
            var viewport = PFlowStoryLayout.AlbumScrollBounds(xml);
            if (viewport == null)
                break;
            var b = viewport.Value;
            if (
                !await _ldPlayer.SwipeAsync(
                    job.DeviceIndex,
                    (b.Left + b.Right) / 2,
                    b.Bottom - 20,
                    (b.Left + b.Right) / 2,
                    b.Top + 20,
                    450,
                    path,
                    ct
                )
            )
                break;
            await Task.Delay(400, ct);
        }
        if (!found)
            throw new InvalidOperationException("Job photo album was not visible.");
        var picker = await WaitFor(x =>
            PFlowPhotoGroupLayout.IsPicker(x) && PFlowPhotoGroupLayout.AlbumSelected(x, album)
        );
        var initialTiles = PFlowPhotoGroupLayout.VisibleTiles(picker, album);
        if (
            initialTiles.Length == 0
            || initialTiles.Length > job.MediaFiles.Count
            || initialTiles.Any(PFlowPhotoGroupLayout.Selected)
        )
            throw new InvalidOperationException("Photo grid was incomplete or already selected.");
        if (job.MediaFiles.Count == 1)
        {
            await VerifyIndex();
            await Tap(x =>
            {
                var tiles = PFlowPhotoGroupLayout.VisibleTiles(x, album);
                return tiles.Length == 1 && !PFlowPhotoGroupLayout.Selected(tiles[0])
                    ? tiles[0]
                    : null;
            });
            job.MediaSelectionEvidence =
                $"Native photo album verified: {album}; count=1; all selected once";
            await checkpoint(job);
            var single = await WaitFor(x =>
                IsComposer(x, "Picture")
                || (
                    PFlowPhotoGroupLayout.VisibleTiles(x, album) is { Length: 1 } tiles
                    && PFlowPhotoGroupLayout.Selected(tiles[0])
                )
            );
            if (IsComposer(single, "Picture"))
                return single;
            await Tap(x =>
            {
                var tiles = PFlowPhotoGroupLayout.VisibleTiles(x, album);
                return tiles.Length == 1 && PFlowPhotoGroupLayout.Selected(tiles[0])
                    ? PFlowPhotoGroupLayout.Control(x, "Next")
                    : null;
            });
            return await WaitFor(x => IsComposer(x, "Picture"));
        }
        var multiple = PFlowPhotoGroupLayout.Control(picker, "Select multiple", "Select multi");
        if (
            !PFlowPhotoGroupLayout.HasNativeCheckboxTiles(picker)
            && (multiple == null || !PFlowPhotoGroupLayout.Selected(multiple))
        )
            await Tap(x => PFlowPhotoGroupLayout.Control(x, "Select multiple", "Select multi"));
        await VerifyIndex();
        var selectedCount = 0;
        for (var step = 0; selectedCount < job.MediaFiles.Count && step < 40; step++)
        {
            var current = await Observe();
            var tiles = PFlowPhotoGroupLayout.VisibleTiles(current, album);
            if (tiles.Length == 0 || tiles.Length > job.MediaFiles.Count)
                throw new InvalidOperationException("Photo grid changed during multi-selection.");
            var index = Array.FindIndex(tiles, t => !PFlowPhotoGroupLayout.Selected(t));
            if (index < 0)
            {
                var containers = tiles[0]
                    .Ancestors()
                    .Where(n =>
                        n.Attribute("scrollable")?.Value == "true"
                        && tiles.All(t => t.Ancestors().Contains(n))
                    )
                    .Take(2)
                    .ToArray();
                if (containers.Length != 1)
                    throw new InvalidOperationException(
                        "Not all grouped photos were selected and no unique photo grid can scroll."
                    );
                var match = System.Text.RegularExpressions.Regex.Match(
                    containers[0].Attribute("bounds")?.Value ?? "",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                );
                if (!match.Success)
                    throw new InvalidOperationException("Photo grid bounds unavailable.");
                var b = Enumerable
                    .Range(1, 4)
                    .Select(i => int.Parse(match.Groups[i].Value))
                    .ToArray();
                if (
                    b[2] - b[0] < 80
                    || b[3] - b[1] < 160
                    || !await _ldPlayer.SwipeAsync(
                        job.DeviceIndex,
                        (b[0] + b[2]) / 2,
                        b[3] - 40,
                        (b[0] + b[2]) / 2,
                        b[1] + 40,
                        450,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException("Photo grid scroll was not acknowledged.");
                await Task.Delay(400, ct);
                continue;
            }
            var bounds = tiles.Select(n => n.Attribute("bounds")?.Value).ToArray();
            var before = tiles.Select(PFlowPhotoGroupLayout.Selected).ToArray();
            await Tap(x =>
            {
                var fresh = PFlowPhotoGroupLayout.VisibleTiles(x, album);
                return
                    fresh.Length == tiles.Length
                    && fresh.Select(n => n.Attribute("bounds")?.Value).SequenceEqual(bounds)
                    && fresh.Select(PFlowPhotoGroupLayout.Selected).SequenceEqual(before)
                    ? fresh[index]
                    : null;
            });
            before[index] = true;
            await WaitFor(x =>
            {
                var fresh = PFlowPhotoGroupLayout.VisibleTiles(x, album);
                return fresh.Length == tiles.Length
                    && fresh.Select(n => n.Attribute("bounds")?.Value).SequenceEqual(bounds)
                    && fresh.Select(PFlowPhotoGroupLayout.Selected).SequenceEqual(before);
            });
            selectedCount++;
        }
        if (selectedCount != job.MediaFiles.Count)
            throw new InvalidOperationException(
                "Not every grouped photo was selected before the navigation limit."
            );
        await VerifyIndex();
        job.MediaSelectionEvidence =
            $"Native photo album verified: {album}; count={job.MediaFiles.Count}; all selected once";
        await checkpoint(job);
        await Tap(x =>
        {
            var tiles = PFlowPhotoGroupLayout.VisibleTiles(x, album);
            return
                selectedCount == job.MediaFiles.Count
                && tiles.Length > 0
                && tiles.All(PFlowPhotoGroupLayout.Selected)
                ? PFlowPhotoGroupLayout.Control(x, "Next")
                : null;
        });
        return await WaitFor(x => IsComposer(x, "Picture"));
    }
}
