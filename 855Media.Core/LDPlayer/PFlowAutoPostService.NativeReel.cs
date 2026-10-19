using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    private sealed class NativeReelUnavailable(string reason) : Exception(reason);

    private sealed class NativeReelProcessingTimeout()
        : Exception("Reel editor Processing exceeded 180 seconds");

    internal static bool IsReelEditorProcessing(string xml, string package) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("package") == package
                && (string?)n.Attribute("visible-to-user") != "false"
                && !n.Ancestors().Any(a => (string?)a.Attribute("visible-to-user") == "false")
                && (
                    (string?)n.Attribute("text") == "Processing"
                    || (string?)n.Attribute("content-desc") == "Processing"
                )
            );

    private async Task<string?> TryOpenNativeReelComposerAsync(
        PFlowJob job,
        Func<PFlowJob, Task> checkpoint,
        string? path,
        CancellationToken ct,
        bool continuedSession = false,
        bool processingRetried = false
    )
    {
        if (!job.NativeReelAlbum || job.PostType is not ("Reel" or "Video"))
            return null;
        if (
            !Guid.TryParseExact(job.JobId, "N", out _)
            || job.Size <= 0
            || !System.Text.RegularExpressions.Regex.IsMatch(job.Sha256, "^[0-9a-fA-F]{64}$")
        )
            throw new InvalidOperationException(
                "Native Reel selection requires a valid fingerprinted job."
            );
        var album = "Reel_" + job.JobId;
        var query =
            "shell content query --uri content://media/external/file --projection _id:_data:_display_name:_size:mime_type:bucket_display_name:media_type:duration --where "
            + ShellQuote("bucket_display_name='" + album + "'");
        async Task<string> Observe()
        {
            var xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            if (!ScreenTimeHierarchy.Nodes(xml).Any())
                throw new NativeReelUnavailable("Empty UI hierarchy");
            if (
                PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                || ScreenTimeHierarchy
                    .Nodes(xml)
                    .Any(n => (string?)n.Attribute("password") == "true")
            )
                throw new InvalidOperationException(
                    "Native Reel navigation reached authentication; no fallback attempted."
                );
            var packages = ScreenTimeHierarchy
                .Nodes(xml)
                .Select(n => (string?)n.Attribute("package"))
                .Where(p => !string.IsNullOrWhiteSpace(p) && p != "com.android.systemui")
                .Distinct()
                .ToArray();
            if (packages.Any(p => p != job.AppPackage))
                throw new InvalidOperationException(
                    "Native Reel navigation reached another app; no fallback attempted."
                );
            return xml;
        }
        async Task Tap(XElement? node)
        {
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
                throw new NativeReelUnavailable(
                    "Requested native Reel control missing, ambiguous, or unacknowledged"
                );
            await Task.Delay(600, ct);
        }
        async Task<string> WaitFor(FacebookScreenKind kind)
        {
            var until = Stopwatch.StartNew();
            do
            {
                var xml = await Observe();
                if (FacebookLayoutRegistry.Match(xml, kind).IsMatch)
                    return xml;
                await Task.Delay(400, ct);
            } while (until.Elapsed < TimeSpan.FromSeconds(10));
            throw new NativeReelUnavailable("Native Reel layout unavailable: " + kind);
        }
        async Task<PFlowStoryMedia> Index(bool waitForScan = false)
        {
            var attempts = waitForScan ? 12 : 1;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _ldPlayer.ExecuteAdbAsync(job.DeviceIndex, query, path, ct);
                var indexed = PFlowStoryMedia.ReadAlbum(result, job, album);
                if (indexed != null)
                    return indexed;
                var rows = System
                    .Text.RegularExpressions.Regex.Matches(result, @"(?m)^Row:\s*\d+\s+")
                    .Count;
                var reason =
                    rows == 0 ? "no-index-row"
                    : rows > 1 ? "multiple-index-rows"
                    : "indexed-metadata-mismatch";
                await PFlowTrace.SessionAsync(
                    job.DeviceIndex,
                    "reel.index.wait",
                    $"observation={attempt + 1}/{attempts}; rows={rows}; reason={reason}",
                    ct
                );
                if (attempt + 1 < attempts)
                {
                    if (attempt == 1)
                        await _ldPlayer.ScanMediaFileAsync(
                            job.DeviceIndex,
                            job.RemotePath,
                            path,
                            ct
                        );
                    await Task.Delay(500, ct);
                }
            }
            throw new NativeReelUnavailable(
                "Native Reel album did not verify exactly the indexed job file after "
                    + attempts
                    + " observation(s)"
            );
        }
        async Task VerifyBytes()
        {
            var size = await _ldPlayer.ExecuteAdbAsync(
                job.DeviceIndex,
                "shell stat -c %s " + ShellQuote(job.RemotePath),
                path,
                ct
            );
            var hash = await _ldPlayer.ExecuteAdbAsync(
                job.DeviceIndex,
                "shell sha256sum " + ShellQuote(job.RemotePath),
                path,
                ct
            );
            if (
                !long.TryParse(size.Trim(), out var bytes)
                || bytes != job.Size
                || !string.Equals(
                    hash.Trim()
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(),
                    job.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException(
                    "Native Reel staged media changed; no fallback attempted."
                );
        }
        var navigated = false;
        try
        {
            var xml = await Observe();
            // Never continue a previous job's existing draft/editor.
            if (
                !PFlowPostTargetSession.HasOwnProfile(job, xml)
                && !(
                    continuedSession && PFlowPostTargetSession.CanContinueFrom(xml, job.AppPackage)
                )
            )
                throw new InvalidOperationException(
                    "Native Reel initial target profile was not confirmed."
                );
            job.CurrentStep = "Opening Reel through Facebook Bloks";
            job.StepStartedUtc = DateTime.UtcNow;
            await checkpoint(job);
            var indexed = await Index(waitForScan: true);
            var entry = PFlowReelLayout.CreateEntry(xml);
            if (entry == null)
            {
                navigated = true;
                await _ldPlayer.ExecuteAdbAsync(
                    job.DeviceIndex,
                    "shell am start -a android.intent.action.VIEW -d 'fb://feed' -p "
                        + ShellQuote(job.AppPackage),
                    path,
                    ct
                );
                xml = await WaitFor(FacebookScreenKind.NewsFeed);
                var feedEntry = await PFlowReelNavigation.FindFeedEntryAsync(
                    _ldPlayer,
                    job.DeviceIndex,
                    xml,
                    Observe,
                    path,
                    ct
                );
                xml = feedEntry.Xml;
                entry = feedEntry.Reel;
                if (entry == null)
                {
                    var create = feedEntry.Create;
                    if (
                        create == null
                        || !await _ldPlayer.TapAsync(job.DeviceIndex, create.X, create.Y, path, ct)
                    )
                        throw new NativeReelUnavailable(
                            "Native Create entry unavailable after fresh Feed observations and navigation reveal"
                        );
                    xml = await WaitFor(FacebookScreenKind.CreateMenu);
                    entry = PFlowReelLayout.CreateEntry(xml);
                }
            }
            navigated = true;
            await Tap(entry);
            xml = await WaitFor(FacebookScreenKind.ReelPicker);
            await Tap(PFlowStoryLayout.Dropdown(xml));
            xml = await WaitFor(FacebookScreenKind.StoryAlbums);
            var albumFound = false;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var row = PFlowStoryLayout.Album(xml, album);
                if (row != null)
                {
                    await Tap(row);
                    albumFound = true;
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
                await Task.Delay(500, ct);
                xml = await WaitFor(FacebookScreenKind.StoryAlbums);
            }
            if (!albumFound)
                throw new NativeReelUnavailable("Job Reel album was not visible");
            // Two fresh observations plus an exact index/byte recheck before selection.
            for (var observation = 0; observation < 2; observation++)
            {
                xml = await WaitFor(FacebookScreenKind.ReelPicker);
                indexed = await Index();
                if (PFlowReelLayout.Media(xml, indexed) == null)
                    throw new NativeReelUnavailable(
                        "Native Reel picker did not expose exactly the verified job media"
                    );
                await Task.Delay(400, ct);
            }
            await VerifyBytes();
            indexed = await Index();
            xml = await Observe();
            if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelPicker).IsMatch)
                throw new NativeReelUnavailable("Reel picker changed before selection");
            await Tap(PFlowReelLayout.Media(xml, indexed));
            job.MediaSelectionEvidence =
                $"Bloks Reel picker; album={album}; mediaId={indexed.MediaId}; size={indexed.Size}; sha256={job.Sha256}";
            var editorWait = Stopwatch.StartNew();
            var processingSeen = false;
            while (editorWait.Elapsed < TimeSpan.FromSeconds(180))
            {
                xml = await Observe();
                if (IsReelEditorProcessing(xml, job.AppPackage))
                {
                    if (!processingSeen)
                    {
                        processingSeen = true;
                        job.CurrentStep =
                            "Waiting for Facebook Reel editor processing (up to 180 seconds)";
                        await checkpoint(job);
                        await PFlowTrace.SessionAsync(
                            job.DeviceIndex,
                            "reel.editor.processing",
                            "Waiting before Share; timeoutSeconds=180",
                            ct
                        );
                    }
                    await Task.Delay(600, ct);
                    continue;
                }
                if (IsComposer(xml, job.PostType))
                {
                    await PFlowTrace.SessionAsync(
                        job.DeviceIndex,
                        "reel.route",
                        "route=Bloks; exactMediaVerified=true",
                        ct
                    );
                    return xml;
                }
                var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelEditor);
                var next = match.Control(FacebookControlRole.Continue);
                if (match.IsMatch && next != null)
                {
                    if (!await _ldPlayer.TapAsync(job.DeviceIndex, next.X, next.Y, path, ct))
                        throw new NativeReelUnavailable("Native Reel Next tap not acknowledged");
                }
                if (!processingSeen && editorWait.Elapsed >= TimeSpan.FromSeconds(20))
                    break;
                await Task.Delay(600, ct);
            }
            if (IsReelEditorProcessing(await Observe(), job.AppPackage))
                throw new NativeReelProcessingTimeout();
            throw new NativeReelUnavailable("Native Reel composer did not open");
        }
        catch (NativeReelProcessingTimeout)
        {
            await PFlowDiagnostics.CaptureAsync(
                _ldPlayer,
                job.DeviceIndex,
                "reel-editor-processing-timeout",
                path
            );
            if (job.MayHaveSubmitted || processingRetried)
                throw new InvalidOperationException(
                    "Facebook Reel editor processing remained stuck after one restart; no post submitted."
                );
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "reel.editor.processing.restart",
                "attempt=1; force-stop and reopen Facebook; retry same job before Share",
                ct
            );
            job.MediaSelectionEvidence = "";
            if (
                !await _ldPlayer.StopFacebookAppAsync(job.DeviceIndex, path, ct)
                || !await _ldPlayer.LaunchAppAsync(job.DeviceIndex, job.AppPackage, path, ct)
            )
                throw new InvalidOperationException(
                    "Could not restart Facebook after Reel editor processing timeout."
                );
            await Task.Delay(1500, ct);
            await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, path, ct);
            if (
                !await PFlowOwnProfileNavigation.OpenAsync(
                    _ldPlayer,
                    job.DeviceIndex,
                    job.TargetName,
                    path,
                    s => PFlowPostTargetSession.HasOwnProfile(job, s.RawXml ?? ""),
                    ct
                )
            )
                throw new InvalidOperationException(
                    "Could not restore target after Facebook processing restart; no post submitted."
                );
            return await TryOpenNativeReelComposerAsync(
                job,
                checkpoint,
                path,
                ct,
                processingRetried: true
            );
        }
        catch (NativeReelUnavailable failure)
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "reel.native-unavailable",
                failure.Message,
                ct
            );
            await PFlowDiagnostics.CaptureAsync(
                _ldPlayer,
                job.DeviceIndex,
                "native-reel-unavailable",
                path
            );
            job.MediaSelectionEvidence = "";
            if (job.MayHaveSubmitted)
                throw new InvalidOperationException(
                    "Reel fallback is prohibited after submission."
                );
            // Do not open a second composer until the previous navigation is closed
            // and the same target's own profile is freshly observed again.
            bool ContinuedFeed(string xml) =>
                continuedSession
                && !navigated
                && PFlowPostTargetSession.CanContinueFrom(xml, job.AppPackage);
            var fallbackStart = await Observe();
            if (
                navigated
                || (
                    !PFlowPostTargetSession.HasOwnProfile(job, fallbackStart)
                    && !ContinuedFeed(fallbackStart)
                )
            )
            {
                var restored = false;
                for (var back = 0; back < 5; back++)
                {
                    var xml = await Observe();
                    if (PFlowPostTargetSession.HasOwnProfile(job, xml))
                    {
                        restored = true;
                        break;
                    }
                    if (
                        PFlowReelLayout.IsEditor(new(xml))
                        || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelPicker).IsMatch
                        || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryAlbums).IsMatch
                        || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.CreateMenu).IsMatch
                    )
                    {
                        if (!await _ldPlayer.PressBackAsync(job.DeviceIndex, path, ct))
                            break;
                        await Task.Delay(500, ct);
                        continue;
                    }
                    restored = await PFlowOwnProfileNavigation.OpenAsync(
                        _ldPlayer,
                        job.DeviceIndex,
                        job.TargetName,
                        path,
                        s => PFlowPostTargetSession.HasOwnProfile(job, s.RawXml ?? ""),
                        ct
                    );
                    break;
                }
                if (!restored)
                    throw new InvalidOperationException(
                        "Native Reel failed and the target could not be restored; Android fallback stopped.",
                        failure
                    );
            }
            await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, path, ct);
            var fallbackReady = await Observe();
            if (
                !PFlowPostTargetSession.HasOwnProfile(job, fallbackReady)
                && !ContinuedFeed(fallbackReady)
            )
                throw new InvalidOperationException("Target changed before Android Reel fallback.");
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "reel.route",
                "route=AndroidFallback; reason=" + failure.Message,
                ct
            );
            return null;
        }
    }
}
