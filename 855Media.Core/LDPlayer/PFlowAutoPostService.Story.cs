using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    private async Task PublishStoryAsync(
        PFlowJob job,
        Func<PFlowJob, Task> checkpoint,
        string? customPath,
        CancellationToken ct,
        bool dryRun
    )
    {
        PFlowStoryMedia.ValidateJob(job);
        if (!string.IsNullOrWhiteSpace(job.Caption))
            throw new InvalidOperationException(
                "Story publishing currently supports media without a text overlay."
            );

        async Task Step(string name)
        {
            job.CurrentStep = name;
            job.StepStartedUtc = DateTime.UtcNow;
            await checkpoint(job);
        }
        async Task Stage(PFlowState state)
        {
            job.State = state;
            if (state == PFlowState.SubmitRequested)
                job.SubmittedUtc = DateTime.UtcNow;
            await Step(state.ToString());
        }
        Task<string> Shell(string command) =>
            _ldPlayer.ExecuteAdbAsync(job.DeviceIndex, "shell " + command, customPath, ct);
        async Task<string> Observe()
        {
            var observed =
                await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
            PFlowPageIdentityRestriction.ThrowIfVisible(job, observed);
            return observed;
        }
        async Task<string> WaitFor(FacebookScreenKind kind)
        {
            var timer = Stopwatch.StartNew();
            var lastDiagnostic = "No observation";
            do
            {
                ct.ThrowIfCancellationRequested();
                var current = await Observe();
                var match = FacebookLayoutRegistry.Match(current, kind);
                lastDiagnostic = match.Diagnostic;
                if (match.IsMatch)
                {
                    await PFlowTrace.SessionAsync(
                        job.DeviceIndex,
                        "layout.story." + kind,
                        match.Diagnostic,
                        ct
                    );
                    return current;
                }
                if (
                    match.Status == FacebookLayoutStatus.Blocked
                    && match.Candidates.Any(c =>
                        c.ExcludedBy.Contains("authentication")
                        || c.ExcludedBy.Contains("foreign-app")
                    )
                )
                    throw new InvalidOperationException(
                        "Story navigation stopped at an authentication or foreign screen."
                    );
                await Task.Delay(600, ct);
            } while (timer.Elapsed < TimeSpan.FromSeconds(20));
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "layout.story." + kind,
                lastDiagnostic,
                ct
            );
            throw new InvalidOperationException(
                "Expected Story layout was not recognized: " + kind
            );
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
                    customPath,
                    ct
                )
            )
                throw new InvalidOperationException(
                    "Story control is missing, ambiguous, or did not acknowledge input."
                );
            await Task.Delay(600, ct);
        }
        async Task TapRole(string current, FacebookScreenKind screen, FacebookControlRole role)
        {
            var control = FacebookLayoutRegistry.Match(current, screen).Control(role);
            if (
                control == null
                || !await _ldPlayer.TapAsync(job.DeviceIndex, control.X, control.Y, customPath, ct)
            )
                throw new InvalidOperationException(
                    "Story navigation control is missing or ambiguous: " + role
                );
            await Task.Delay(600, ct);
        }
        async Task VerifyBytes()
        {
            var size = await Shell("stat -c %s " + ShellQuote(job.RemotePath));
            var checksum = await Shell("sha256sum " + ShellQuote(job.RemotePath));
            if (
                !long.TryParse(size.Trim(), out var bytes)
                || bytes != job.Size
                || !string.Equals(
                    checksum
                        .Trim()
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(),
                    job.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException("Story staging file size or checksum changed.");
        }
        async Task<PFlowStoryMedia> RequireIndex()
        {
            var media = PFlowStoryMedia.Read(await Shell(PFlowStoryMedia.Query(job)), job);
            return media
                ?? throw new InvalidOperationException(
                    "Story album must contain exactly the indexed job file."
                );
        }

        async Task<(int? Count, string Xml)> InspectOwnStory(string feed, bool baseline)
        {
            var entry = PFlowStoryLayout.OwnStoryEntry(feed);
            if (entry == null)
                return (
                    baseline && PFlowStoryLayout.HasEmptyOwnStoryBaseline(feed) ? 0 : null,
                    feed
                );
            await Tap(entry);
            string viewer = "";
            int? count = null;
            var inspection = Stopwatch.StartNew();
            do
            {
                viewer = await Observe();
                count = PFlowStoryLayout.OwnViewerCount(viewer, job.TargetName);
                if (
                    PFlowStoryLayout.ViewerPosition(viewer) != null
                    && !PFlowStoryLayout.ViewerMatchesTarget(viewer, job.TargetName)
                )
                    break;
                if (
                    count.HasValue
                    && (
                        baseline
                        || PFlowStoryLayout.ViewerPosition(viewer) is { } position
                            && position.Index == position.Count
                    )
                )
                    break;
                await Task.Delay(200, ct);
            } while (inspection.Elapsed < TimeSpan.FromSeconds(baseline ? 3 : 12));
            var captured = viewer;
            // Own stories can advance automatically to someone else's story.
            // Close playback promptly; never react, reply, share or select another author.
            for (var back = 0; back < 3; back++)
            {
                if (FacebookLayoutRegistry.Match(viewer, FacebookScreenKind.NewsFeed).IsMatch)
                    return (count, captured);
                if (
                    !ScreenTimeHierarchy
                        .Nodes(viewer)
                        .Any(n =>
                            System.Text.RegularExpressions.Regex.IsMatch(
                                n.Attribute("content-desc")?.Value ?? "",
                                @"^Story \d+ of \d+$"
                            )
                            || n.Attribute("content-desc")?.Value == "Closes the list of viewers"
                        )
                )
                {
                    if (!baseline)
                    {
                        await PFlowTrace.SessionAsync(
                            job.DeviceIndex,
                            "story.viewer.close",
                            "Playback changed to an unrecognized surface; keeping captured evidence, no further input",
                            ct
                        );
                        return (count, captured);
                    }
                    throw new InvalidOperationException(
                        "Story playback could not be closed through a recognized layout."
                    );
                }
                if (!await _ldPlayer.PressBackAsync(job.DeviceIndex, customPath, ct))
                {
                    if (!baseline)
                    {
                        await PFlowTrace.SessionAsync(
                            job.DeviceIndex,
                            "story.viewer.close",
                            "Back was not acknowledged; keeping captured evidence",
                            ct
                        );
                        return (count, captured);
                    }
                    throw new InvalidOperationException(
                        "Story playback close was not acknowledged."
                    );
                }
                await Task.Delay(400, ct);
                viewer = await Observe();
            }
            if (!FacebookLayoutRegistry.Match(viewer, FacebookScreenKind.NewsFeed).IsMatch)
            {
                if (!baseline)
                {
                    await PFlowTrace.SessionAsync(
                        job.DeviceIndex,
                        "story.viewer.close",
                        "Feed did not return; keeping captured evidence",
                        ct
                    );
                    return (count, captured);
                }
                throw new InvalidOperationException(
                    "Feed did not return after Story verification."
                );
            }
            return (count, captured);
        }

        await Step("Staging and verifying Story media");
        var folder = job.RemotePath[..job.RemotePath.LastIndexOf('/')];
        await Shell("mkdir -p " + ShellQuote(folder));
        if (
            !await _ldPlayer.PushFileToEmulatorAsync(
                job.DeviceIndex,
                job.SourcePath,
                job.RemotePath,
                customPath,
                ct
            )
        )
            throw new InvalidOperationException("Story media transfer failed.");
        await VerifyBytes();
        if (!await _ldPlayer.ScanMediaFileAsync(job.DeviceIndex, job.RemotePath, customPath, ct))
            throw new InvalidOperationException("Story media scan failed.");
        PFlowStoryMedia? indexed = null;
        for (var attempt = 0; attempt < 12 && indexed == null; attempt++)
        {
            indexed = PFlowStoryMedia.Read(await Shell(PFlowStoryMedia.Query(job)), job);
            if (indexed == null)
                await Task.Delay(600, ct);
        }
        if (indexed == null)
            throw new InvalidOperationException("Story media index was not verified.");
        await Stage(PFlowState.MediaReady);

        // Transfer can be lengthy. Re-establish the verified persona before navigating away.
        await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, customPath, ct);
        if (
            !await _ldPlayer.SwitchToPersonaAsync(
                job.DeviceIndex,
                new FacebookPageTarget
                {
                    PageId = job.TargetId,
                    PersonaUid = job.PersonaUid,
                    UserUid = job.OwnerUid,
                    PageName = job.TargetName,
                    Type = job.IsProfile ? "Profile" : "Page",
                    AccountBinding = job.AccountBinding,
                },
                customPath,
                ct
            )
        )
            throw new InvalidOperationException("Story target identity could not be reconfirmed.");
        var profile = await Observe();
        if (
            !ComposerMatchesTarget(profile, job.TargetName)
            || !(
                job.IsProfile
                    ? PFlowNativeAccountNavigator.OwnProfile(profile)
                        || HasLabel(profile, ["Edit profile"])
                    : HasLabel(
                        profile,
                        [
                            "Professional dashboard",
                            "Creator Dashboard",
                            "Business Dashboard",
                            "Manage Page",
                            "Edit Page",
                        ]
                    )
            )
        )
            throw new InvalidOperationException(
                "Story target own-profile controls were not confirmed."
            );

        await Step("Opening Feed and Story creation");
        // This opens the current persona's Feed, not a share destination or composer.
        await Shell(
            "am start -a android.intent.action.VIEW -d 'fb://feed' -p " + ShellQuote(job.AppPackage)
        );
        var xml = await WaitFor(FacebookScreenKind.NewsFeed);
        await Step("Reading own-Story publication baseline");
        var trayWait = Stopwatch.StartNew();
        while (
            PFlowStoryLayout.OwnStoryEntry(xml) == null
            && !PFlowStoryLayout.HasEmptyOwnStoryBaseline(xml)
            && trayWait.Elapsed < TimeSpan.FromSeconds(3)
        )
        {
            await Task.Delay(200, ct);
            xml = await WaitFor(FacebookScreenKind.NewsFeed);
        }
        // The tray can initially show Create story while an existing own card is
        // still loading. Require separated observations before recording zero.
        var stableEmpty = PFlowStoryLayout.HasEmptyOwnStoryBaseline(xml);
        if (stableEmpty)
        {
            for (var sample = 0; sample < 2; sample++)
            {
                await Task.Delay(750, ct);
                xml = await Observe();
                if (!PFlowStoryLayout.HasEmptyOwnStoryBaseline(xml))
                {
                    stableEmpty = false;
                    break;
                }
            }
        }
        var storyBaseline = await InspectOwnStory(xml, baseline: true);
        if (storyBaseline.Count == 0 && !stableEmpty)
            storyBaseline = (null, storyBaseline.Xml);
        job.StoryCountBefore = storyBaseline.Count;
        await checkpoint(job);
        await PFlowTrace.SessionAsync(
            job.DeviceIndex,
            "story.confirmation.baseline",
            $"job={job.JobId}; ownStoryCount={job.StoryCountBefore?.ToString() ?? "unknown"}; ownEntry={PFlowStoryLayout.OwnStoryEntry(xml) != null}; emptyLoadedTray={PFlowStoryLayout.HasEmptyOwnStoryBaseline(xml)}",
            ct
        );
        xml = await WaitFor(FacebookScreenKind.NewsFeed);
        var create = FacebookLayoutRegistry
            .Match(xml, FacebookScreenKind.NewsFeed)
            .Control(FacebookControlRole.Create);
        if (create != null)
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "story.entry",
                "route=Create menu -> Story",
                ct
            );
            await TapRole(xml, FacebookScreenKind.NewsFeed, FacebookControlRole.Create);
            xml = await WaitFor(FacebookScreenKind.CreateMenu);
            await TapRole(xml, FacebookScreenKind.CreateMenu, FacebookControlRole.Story);
        }
        else
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "story.entry",
                "route=Feed Create story card; header Create unavailable",
                ct
            );
            await Step("Opening Create story from the Feed Story tray");
            await Tap(PFlowStoryLayout.CreateStoryEntry(xml));
        }
        xml = await WaitFor(FacebookScreenKind.StoryPicker);
        await Tap(PFlowStoryLayout.Dropdown(xml));
        xml = await WaitFor(FacebookScreenKind.StoryAlbums);
        var album = PFlowStoryMedia.AlbumFor(job);
        var foundAlbum = false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryAlbums).IsMatch)
                throw new InvalidOperationException("Story album list changed during selection.");
            var row = PFlowStoryLayout.Album(xml, album);
            if (row != null)
            {
                await Tap(row);
                foundAlbum = true;
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
                    customPath,
                    ct
                )
            )
                break;
            await Task.Delay(600, ct);
            xml = await Observe();
        }
        if (!foundAlbum)
            throw new InvalidOperationException(
                "The job's Story album was not visible; no other media was selected."
            );

        await Step("Verifying exact Story file selection");
        xml = await WaitFor(FacebookScreenKind.StoryPicker);
        // Require two fresh observations after the album transition; never tap a stale Gallery thumbnail.
        var pickerReady = Stopwatch.StartNew();
        var observations = 0;
        while (observations < 2)
        {
            indexed = await RequireIndex();
            if (
                FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryPicker).IsMatch
                && PFlowStoryLayout.Media(xml, indexed) != null
            )
                observations++;
            else
                observations = 0;
            if (observations < 2 && pickerReady.Elapsed >= TimeSpan.FromSeconds(12))
                throw new InvalidOperationException(
                    "Story album stayed empty or did not expose exactly one matching item. No media was selected."
                );
            if (observations < 2)
            {
                await Task.Delay(600, ct);
                xml = await Observe();
            }
        }
        await VerifyBytes();
        indexed = await RequireIndex();
        xml = await Observe();
        if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryPicker).IsMatch)
            throw new InvalidOperationException("Story picker changed before selection.");
        await Tap(PFlowStoryLayout.Media(xml, indexed));
        job.MediaSelectionEvidence =
            $"Single-file album={album}; mediaId={indexed.MediaId}; size={indexed.Size}; sha256={job.Sha256}";
        xml = await WaitFor(FacebookScreenKind.StoryEditor);
        if (
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.StoryEditor)
                .Control(FacebookControlRole.Publish) == null
        )
            throw new InvalidOperationException(
                "Story editor Share control is missing or ambiguous."
            );
        await Stage(PFlowState.ComposerReady);
        if (dryRun)
        {
            job.State = PFlowState.CancelledBeforeSubmission;
            job.Error = "Dry run completed at the verified Story editor without pressing Share.";
            await checkpoint(job);
            return;
        }

        var before = Labels(xml).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baselineDump = await Shell("dumpsys notification --noredact");
        var baselineAvailable = PFlowConfirmation.HasActiveNotificationList(baselineDump);
        var baseline = PFlowConfirmation.ReadUploadNotifications(baselineDump, job.AppPackage);
        if (baseline.Any(n => n.Ongoing))
            throw new InvalidOperationException(
                "Another Facebook upload is active; Story submission was stopped."
            );
        var toastAvailable =
            _atxClient != null && await _atxClient.ClearLastToastAsync(job.DeviceIndex, ct);
        await PFlowTrace.SessionAsync(
            job.DeviceIndex,
            "story.confirmation.channels",
            $"job={job.JobId}; notifications={baselineAvailable}; toast={toastAvailable}; ownStoryBaseline={job.StoryCountBefore?.ToString() ?? "unknown"}",
            ct
        );
        await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, customPath, ct);
        await VerifyBytes();
        await RequireIndex();
        xml = await Observe();
        var share = FacebookLayoutRegistry
            .Match(xml, FacebookScreenKind.StoryEditor)
            .Control(FacebookControlRole.Publish);
        if (share == null || string.IsNullOrWhiteSpace(job.MediaSelectionEvidence))
            throw new InvalidOperationException("Verified Story editor changed before submission.");
        await Stage(PFlowState.SubmitRequested);
        if (!await _ldPlayer.TapAsync(job.DeviceIndex, share.X, share.Y, customPath, ct))
        {
            job.State = PFlowState.Unknown;
            job.Error = "Story Share action could not be confirmed. Review before retrying.";
            await checkpoint(job);
            return;
        }
        await Stage(PFlowState.Submitted);
        await Step("Waiting for Story publication confirmation");
        var timer = Stopwatch.StartNew();
        var window = UploadConfirmationWindow(job.Size, PublicationTimeout);
        var nextNotification = TimeSpan.Zero;
        var nextStoryInspection = TimeSpan.FromSeconds(2);
        var storyInspectionDelay = TimeSpan.FromSeconds(6);
        var uploadOngoing = false;
        while (timer.Elapsed < window)
        {
            ct.ThrowIfCancellationRequested();
            xml = await Observe();
            if (
                HasLabel(
                    xml,
                    ["Couldn't share story", "Couldn't post", "Upload failed", "Unable to publish"]
                )
            )
                break;
            var evidence = NewPublicationEvidence(xml, before, "Story");
            if (evidence == null && toastAvailable)
            {
                var toast = await _atxClient!.ReadLastToastAsync(job.DeviceIndex, ct);
                toastAvailable = toast.Supported;
                if (toast.Message != null && xml.Contains(job.AppPackage, StringComparison.Ordinal))
                    evidence = NewPublicationEvidence(
                        "<hierarchy><node text='"
                            + System.Security.SecurityElement.Escape(toast.Message)
                            + "'/></hierarchy>",
                        new System.Collections.Generic.HashSet<string>(),
                        "Story"
                    );
                if (evidence != null)
                    evidence = "Fresh Facebook toast: " + evidence;
            }
            if (evidence == null && baselineAvailable && timer.Elapsed >= nextNotification)
            {
                var currentNotifications = PFlowConfirmation.ReadUploadNotifications(
                    await Shell("dumpsys notification --noredact"),
                    job.AppPackage
                );
                uploadOngoing = currentNotifications.Any(n => n.Ongoing);
                evidence = PFlowConfirmation.NewUploadCompletion(
                    baseline,
                    currentNotifications,
                    Labels(xml).Any()
                        && !PFlowStoryLayout.IsEditor(new(xml))
                        && !PFlowStoryLayout.IsPicker(new(xml))
                );
                nextNotification = timer.Elapsed + TimeSpan.FromSeconds(2);
            }
            if (
                evidence == null
                && !uploadOngoing
                && job.StoryCountBefore.HasValue
                && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnStoryViewer).IsMatch
            )
                evidence = PFlowStoryLayout.NewOwnStoryEvidence(job, xml);
            if (
                evidence == null
                && !uploadOngoing
                && job.StoryCountBefore.HasValue
                && timer.Elapsed >= nextStoryInspection
                && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
                && PFlowStoryLayout.OwnStoryEntry(xml) != null
            )
            {
                var inspected = await InspectOwnStory(xml, baseline: false);
                evidence = PFlowStoryLayout.NewOwnStoryEvidence(job, inspected.Xml);
                await PFlowTrace.SessionAsync(
                    job.DeviceIndex,
                    "story.confirmation.own-viewer",
                    $"job={job.JobId}; before={job.StoryCountBefore}; after={inspected.Count?.ToString() ?? "unknown"}; currentStory={PFlowStoryLayout.ViewerPosition(inspected.Xml)?.Index}; pending={PFlowStoryLayout.HasPendingUpload(inspected.Xml)}; accepted={evidence != null}",
                    ct
                );
                nextStoryInspection = timer.Elapsed + storyInspectionDelay;
                storyInspectionDelay = TimeSpan.FromSeconds(
                    Math.Min(30, storyInspectionDelay.TotalSeconds * 2)
                );
                // The fresh viewer evidence, rather than text from the visited surface,
                // is the only UI result used for this inspection.
            }
            if (PFlowStoryLayout.HasPendingUpload(xml))
                evidence = null;
            if (evidence != null)
            {
                await Stage(PFlowConfirmation.RecordPublicationEvidence(job, evidence));
                return;
            }
            await Task.Delay(400, ct);
        }
        job.State = PFlowState.Unknown;
        job.Error =
            "Story publication was not confirmed. The upload may still finish; review the target's Story before retrying.";
        await checkpoint(job);
        await PFlowDiagnostics.CaptureAsync(
            _ldPlayer,
            job.DeviceIndex,
            "story-unconfirmed-" + job.JobId,
            customPath
        );
    }
}
