using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    private static readonly string[] CaptionHints =
    [
        "Describe your reel",
        "Write a caption",
        "Write something",
        "What's on your mind",
        "Say something",
        "Add description",
    ];

    public static XElement? FindPFlowCaptionField(string? xml)
    {
        try
        {
            var nodes = XDocument
                .Parse(xml ?? "")
                .Descendants("node")
                .Where(n =>
                    n.Attribute("enabled")?.Value != "false"
                    && n.Attribute("package")?.Value != "com.android.inputmethod.latin"
                )
                .ToList();
            bool Editable(XElement n) =>
                (n.Attribute("class")?.Value ?? "").Contains(
                    "EditText",
                    StringComparison.OrdinalIgnoreCase
                )
                || (n.Attribute("resource-id")?.Value ?? "").Contains(
                    "caption",
                    StringComparison.OrdinalIgnoreCase
                );
            bool Hint(XElement n) =>
                CaptionHints.Any(h =>
                    $"{n.Attribute("text")?.Value} {n.Attribute("content-desc")?.Value}".Contains(
                        h,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            return nodes
                .Where(n => Editable(n) || Hint(n))
                .OrderByDescending(n => Editable(n) && Hint(n))
                .ThenByDescending(n => Editable(n) && n.Attribute("focused")?.Value == "true")
                .ThenByDescending(Editable)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static bool CaptionTextMatches(string? xml, string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            return true;
        static string Normalize(string s) =>
            System
                .Text.RegularExpressions.Regex.Replace(
                    s.Normalize(System.Text.NormalizationForm.FormC)
                        .Replace("\u200B", "")
                        .Replace("\uFEFF", ""),
                    @"\s+",
                    " "
                )
                .Trim();
        var expected = Normalize(caption);
        try
        {
            return XDocument
                .Parse(xml ?? "")
                .Descendants("node")
                .Any(n =>
                {
                    var text = Normalize(n.Attribute("text")?.Value ?? "");
                    var desc = Normalize(n.Attribute("content-desc")?.Value ?? "");
                    // Full caption is required. Truncated previews and unrelated partial text cannot confirm it.
                    return text == expected
                        || desc == expected
                        || CaptionHints.Any(h =>
                            desc.StartsWith(h, StringComparison.OrdinalIgnoreCase)
                            && desc.EndsWith(expected, StringComparison.Ordinal)
                        );
                });
        }
        catch
        {
            return false;
        }
    }

    public static bool HasEnabledPublishControl(string? xml, string[] labels)
    {
        try
        {
            return XDocument
                .Parse(xml ?? "")
                .Descendants("node")
                .Any(n =>
                    labels.Any(label =>
                        string.Equals(
                            n.Attribute("text")?.Value?.Trim(),
                            label,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            n.Attribute("content-desc")?.Value?.Trim(),
                            label,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    && n.AncestorsAndSelf("node")
                        .Take(3)
                        .Any(button =>
                            button.Attribute("clickable")?.Value == "true"
                            && button.Attribute("enabled")?.Value != "false"
                            && !n.AncestorsAndSelf("node")
                                .Any(parent => parent.Attribute("enabled")?.Value == "false")
                        )
                );
        }
        catch
        {
            return false;
        }
    }

    public TimeSpan PublicationTimeout { get; set; } = TimeSpan.FromSeconds(180);

    public async Task PublishAsync(
        PFlowJob job,
        Func<PFlowJob, Task> checkpoint,
        string? customPath,
        CancellationToken ct,
        bool dryRun = false
    )
    {
        if (job.State != PFlowState.IdentityVerified)
            throw new InvalidOperationException("A verified target session is required.");
        await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, customPath, ct);
        var identityXml =
            await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
        bool ownProfile =
            ComposerMatchesTarget(identityXml, job.TargetName)
            && !identityXml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
            && (
                job.IsProfile
                    ? identityXml.Contains("Edit profile", StringComparison.OrdinalIgnoreCase)
                    : identityXml.Contains(
                        "Professional dashboard",
                        StringComparison.OrdinalIgnoreCase
                    ) || identityXml.Contains("Manage Page", StringComparison.OrdinalIgnoreCase)
            );
        if (!ownProfile)
            throw new InvalidOperationException(
                "Active own-profile identity changed before media transfer."
            );
        job.IdentityEvidence =
            $"Native switcher and own-profile controls verified {job.TargetId} ({job.TargetName}) before share intent.";
        await checkpoint(job);
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
        await Step("Transferring and verifying media");
        await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell mkdir -p /sdcard/DCIM/855Media",
            customPath,
            ct
        );
        if (
            !await _ldPlayer.PushFileToEmulatorAsync(
                job.DeviceIndex,
                job.SourcePath,
                job.RemotePath,
                customPath,
                ct
            )
        )
            throw new InvalidOperationException("Media transfer failed.");
        var size = await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            $"shell stat -c %s {ShellQuote(job.RemotePath)}",
            customPath,
            ct
        );
        if (!long.TryParse(size.Trim(), out var remoteSize) || remoteSize != job.Size)
            throw new InvalidOperationException("Remote media size was not verified.");
        var checksum = await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            $"shell sha256sum {ShellQuote(job.RemotePath)}",
            customPath,
            ct
        );
        if (!checksum.Trim().StartsWith(job.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Remote media checksum was not verified.");
        if (!await _ldPlayer.ScanMediaFileAsync(job.DeviceIndex, job.RemotePath, customPath, ct))
            throw new InvalidOperationException("Media scan failed.");
        await Stage(PFlowState.MediaReady);
        await Step("Opening composer");
        bool opened =
            job.PostType == "Picture"
                ? await _ldPlayer.ShareImageToFacebookAsync(
                    job.DeviceIndex,
                    job.RemotePath,
                    job.AppPackage,
                    customPath,
                    ct
                )
                : await _ldPlayer.ShareVideoToFacebookAsync(
                    job.DeviceIndex,
                    job.RemotePath,
                    job.AppPackage,
                    customPath,
                    ct
                );
        if (!opened)
            throw new InvalidOperationException("Facebook share intent failed.");
        string xml = "";
        bool shareDestinationSelected = false,
            shareOnceTapped = false;
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(30))
        {
            ct.ThrowIfCancellationRequested();
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
            if (!PFlowShareDestination.IsChooser(xml) && IsComposer(xml, job.PostType))
                break;
            if (PFlowShareDestination.IsChooser(xml))
            {
                if (!shareDestinationSelected)
                {
                    if (PFlowShareDestination.IsDestinationSelected(xml, job.PostType))
                    {
                        await Step(
                            "Share destination already selected: "
                                + (job.PostType == "Picture" ? "Feed" : "Reels")
                        );
                        shareDestinationSelected = true;
                    }
                    else
                    {
                        var destination = PFlowShareDestination.Destination(xml, job.PostType);
                        var point = ScreenTimeHierarchy.Center(destination);
                        if (!point.HasValue)
                            throw new InvalidOperationException(
                                $"Share destination {(job.PostType == "Reel" ? "Reels" : "Feed")} is missing or ambiguous."
                            );
                        await Step(
                            "Selecting share destination: "
                                + (job.PostType == "Reel" ? "Reels" : "Feed")
                        );
                        if (
                            !await _ldPlayer.TapAsync(
                                job.DeviceIndex,
                                point.Value.X,
                                point.Value.Y,
                                customPath,
                                ct
                            )
                        )
                            throw new InvalidOperationException(
                                "Share destination tap was not acknowledged."
                            );
                        shareDestinationSelected = true;
                    }
                }
                else if (!shareOnceTapped)
                {
                    if (
                        PFlowShareDestination.HasSelectedDestination(xml)
                        && !PFlowShareDestination.IsDestinationSelected(xml, job.PostType)
                    )
                        throw new InvalidOperationException(
                            "Selected share destination changed before Just once."
                        );
                    var once = ScreenTimeHierarchy.Center(PFlowShareDestination.JustOnce(xml));
                    if (once.HasValue)
                    {
                        if (
                            !await _ldPlayer.TapAsync(
                                job.DeviceIndex,
                                once.Value.X,
                                once.Value.Y,
                                customPath,
                                ct
                            )
                        )
                            throw new InvalidOperationException(
                                "Share Just once tap was not acknowledged."
                            );
                        shareOnceTapped = true;
                    }
                }
                await Task.Delay(600, ct);
                continue;
            }
            if (HasLabel(xml, ["Create Reel", "Share as Reel"]))
                await ClickElementByMatchingTextAsync(
                    job.DeviceIndex,
                    xml,
                    ["Create Reel", "Share as Reel"],
                    customPath,
                    ct
                );
            else if (HasLabel(xml, ["Add audio", "Effects", "Edit video"]))
                await ClickElementByMatchingTextAsync(
                    job.DeviceIndex,
                    xml,
                    ["Next", "Continue"],
                    customPath,
                    ct
                );
            await Task.Delay(600, ct);
        }
        if (!IsComposer(xml, job.PostType))
            throw new InvalidOperationException(
                "Expected composer was not recognized; no guessed taps were made."
            );
        if (!await ComposerIdentityMatchesAsync(xml))
            throw new InvalidOperationException("Composer identity was not confirmed.");
        if (!string.IsNullOrWhiteSpace(job.Caption))
        {
            await Step("Entering and verifying caption");
            // Changing IME can recreate the editor; enable it before resolving/tapping the field.
            if (!await _ldPlayer.EnableAdbKeyboardAsync(job.DeviceIndex, customPath, ct))
                throw new InvalidOperationException("ADB keyboard unavailable.");
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
            var node = FindPFlowCaptionField(xml);
            if (node == null)
                throw new InvalidOperationException("Caption field was not found.");
            var (x, y) = ParseNodeBoundsCenter(node);
            if (x <= 0 || y <= 0)
                throw new InvalidOperationException("Caption field has invalid bounds.");
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "caption.field.tap",
                $"class={node.Attribute("class")?.Value}; x={x}; y={y}; characters={job.Caption.Length}",
                ct
            );
            await _ldPlayer.TapAsync(job.DeviceIndex, x, y, customPath, ct);
            // A label may open a separate caption editor. Resolve its editable control after navigation.
            await Task.Delay(600, ct);
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
            var editor = FindPFlowCaptionField(xml);
            if (editor == null)
                throw new InvalidOperationException("Caption editor did not open.");
            var (editorX, editorY) = ParseNodeBoundsCenter(editor);
            if (editorX <= 0 || editorY <= 0)
                throw new InvalidOperationException("Caption editor has invalid bounds.");
            await _ldPlayer.TapAsync(job.DeviceIndex, editorX, editorY, customPath, ct);
            await Task.Delay(250, ct);
            // Send once through the actual ADB shell. A timed-out ATX broadcast may still
            // deliver later, so retrying that request can duplicate the caption.
            var clearOutput = await _ldPlayer.ExecuteAdbAsync(
                job.DeviceIndex,
                "shell am broadcast -a ADB_CLEAR_TEXT",
                customPath,
                ct
            );
            var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(job.Caption));
            var output = await _ldPlayer.ExecuteAdbAsync(
                job.DeviceIndex,
                $"shell am broadcast -a ADB_INPUT_B64 --es msg {ShellQuote(payload)}",
                customPath,
                ct
            );
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "caption.adb.delivery",
                $"acknowledged={output.Contains("Broadcast completed", StringComparison.OrdinalIgnoreCase)}; clearAcknowledged={clearOutput.Contains("Broadcast completed", StringComparison.OrdinalIgnoreCase)}",
                ct
            );
            // The full field text below is authoritative, even if the command acknowledgment is missing.
            var captionWait = Stopwatch.StartNew();
            do
            {
                await Task.Delay(300, ct);
                xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
                if (CaptionTextMatches(xml, job.Caption))
                    break;
            } while (captionWait.Elapsed < TimeSpan.FromSeconds(8));
            if (!CaptionTextMatches(xml, job.Caption))
                throw new InvalidOperationException("Caption text could not be verified.");
            await _ldPlayer.PressBackAsync(job.DeviceIndex, customPath, ct);
        }
        xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
        if (!IsComposer(xml, job.PostType) || !await ComposerIdentityMatchesAsync(xml))
            throw new InvalidOperationException("Composer context changed before submission.");
        var buttons =
            job.PostType == "Picture"
                ? new[] { "Post", "POST", "Publish" }
                : new[] { "Share now", "Share reel", "Share Reel", "Share" };
        await Step("Waiting for composer readiness");
        xml = await PrepareComposerReadinessAsync(
            job,
            xml,
            buttons,
            ComposerIdentityMatchesAsync,
            customPath,
            ct
        );
        await Stage(PFlowState.ComposerReady);
        if (dryRun)
        {
            job.State = PFlowState.CancelledBeforeSubmission;
            job.Error = "Dry run completed without pressing Share.";
            await checkpoint(job);
            return;
        }
        var before = Labels(xml).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baselineDump = await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell dumpsys notification --noredact",
            customPath,
            ct
        );
        var notificationBaselineAvailable = PFlowConfirmation.HasActiveNotificationList(
            baselineDump
        );
        var notificationBaseline = PFlowConfirmation.ReadUploadNotifications(
            baselineDump,
            job.AppPackage
        );
        var toastAvailable =
            _atxClient != null && await _atxClient.ClearLastToastAsync(job.DeviceIndex, ct);
        await PFlowJobBindingGuard.RequireAsync(_ldPlayer, job, customPath, ct);
        xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
        if (
            !IsComposer(xml, job.PostType)
            || !await ComposerIdentityMatchesAsync(xml)
            || FindVisiblePublishControl(xml, buttons) == null
            || (
                !string.IsNullOrWhiteSpace(job.Caption)
                && FindPFlowCaptionField(xml) != null
                && !CaptionTextMatches(xml, job.Caption)
            )
        )
            throw new InvalidOperationException(
                "Ready composer changed before submission; no Share tap was made."
            );
        await Stage(PFlowState.SubmitRequested); // Durable boundary before any irreversible input.
        var publishPoint = ScreenTimeHierarchy.Center(FindVisiblePublishControl(xml, buttons));
        if (
            !publishPoint.HasValue
            || !await _ldPlayer.TapAsync(
                job.DeviceIndex,
                publishPoint.Value.X,
                publishPoint.Value.Y,
                customPath,
                ct
            )
        )
        {
            job.State = PFlowState.Unknown;
            job.Error = "Share action could not be confirmed.";
            await checkpoint(job);
            return;
        }
        await Stage(PFlowState.Submitted);
        await Step("Confirming publication");
        wait.Restart();
        var nextNotificationPoll = TimeSpan.Zero;
        while (wait.Elapsed < PublicationTimeout)
        {
            ct.ThrowIfCancellationRequested();
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, customPath, ct) ?? "";
            if (HasLabel(xml, ["Couldn't post", "Upload failed", "Unable to publish"]))
            {
                // Facebook may keep/retry a background upload despite the visible error.
                // A Share tap has happened, so neither another tap nor file cleanup is safe.
                job.State = PFlowState.Unknown;
                job.Error =
                    "Facebook reported a publication error after Share. Verify the target's posts before retrying.";
                await checkpoint(job);
                return;
            }
            var evidence = NewPublicationEvidence(xml, before, job.PostType);
            if (evidence == null && toastAvailable)
            {
                var toast = await _atxClient!.ReadLastToastAsync(job.DeviceIndex, ct);
                toastAvailable = toast.Supported;
                if (toast.Message != null && xml.Contains(job.AppPackage, StringComparison.Ordinal))
                    evidence = NewPublicationEvidence(
                        $"<hierarchy><node text='{System.Security.SecurityElement.Escape(toast.Message)}'/></hierarchy>",
                        new System.Collections.Generic.HashSet<string>(),
                        job.PostType
                    );
                if (evidence != null)
                    evidence = "Fresh Facebook toast: " + evidence;
            }
            if (
                evidence == null
                && notificationBaselineAvailable
                && wait.Elapsed >= nextNotificationPoll
            )
            {
                var notifications = PFlowConfirmation.ReadUploadNotifications(
                    await _ldPlayer.ExecuteAdbAsync(
                        job.DeviceIndex,
                        "shell dumpsys notification --noredact",
                        customPath,
                        ct
                    ),
                    job.AppPackage
                );
                evidence = PFlowConfirmation.NewUploadCompletion(
                    notificationBaseline,
                    notifications,
                    Labels(xml).Any() && !IsComposer(xml, job.PostType)
                );
                nextNotificationPoll = wait.Elapsed + TimeSpan.FromSeconds(2);
            }
            if (evidence != null)
            {
                job.Evidence = evidence;
                job.ConfirmedUtc = DateTime.UtcNow;
                await Stage(PFlowState.Confirmed);
                return;
            }
            await Task.Delay(400, ct);
        }
        job.State = PFlowState.Unknown;
        job.Error =
            $"No new publication confirmation was exposed within {PublicationTimeout.TotalSeconds:F0} seconds.";
        await checkpoint(job);
        await PFlowDiagnostics.CaptureAsync(
            _ldPlayer,
            job.DeviceIndex,
            $"unconfirmed-{job.JobId}",
            customPath
        );

        async Task<bool> ComposerIdentityMatchesAsync(string hierarchy)
        {
            if (ComposerMatchesTarget(hierarchy, job.TargetName))
                return true;
            // This Facebook version hides the actor in Reel Settings. Bind that recognized
            // screen to the just-verified native session; the device lease prevents another
            // P-Flow worker switching identity while the composer is open.
            if (
                job.PostType == "Picture"
                || !HasLabel(hierarchy, ["Reel Settings"])
                || Labels(hierarchy)
                    .Select(label => label.Trim())
                    .Any(label =>
                        label.StartsWith("Posting as ", StringComparison.OrdinalIgnoreCase)
                        || label.StartsWith("Sharing as ", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("Switch profile", StringComparison.OrdinalIgnoreCase)
                    )
            )
                return false;
            var activity = await _ldPlayer.ExecuteAdbAsync(
                job.DeviceIndex,
                "shell dumpsys activity activities",
                customPath,
                ct
            );
            return activity
                .Split('\n')
                .Any(line =>
                    (
                        line.Contains("mResumedActivity", StringComparison.Ordinal)
                        || line.Contains("topResumedActivity", StringComparison.Ordinal)
                    ) && line.Contains(job.AppPackage + "/", StringComparison.Ordinal)
                );
        }
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    internal static IEnumerable<string> Labels(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];
        try
        {
            return SafeParseXmlDocuments(xml)
                .SelectMany(d => d.Descendants("node"))
                .SelectMany(n =>
                    new[]
                    {
                        n.Attribute("text")?.Value ?? "",
                        n.Attribute("content-desc")?.Value ?? "",
                    }
                )
                .Where(s => s.Length > 0)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool HasLabel(string xml, string[] labels) =>
        Labels(xml)
            .Any(x =>
                labels.Any(l => string.Equals(x.Trim(), l, StringComparison.OrdinalIgnoreCase))
            );

    internal static bool IsComposer(string xml, string type) =>
        type == "Picture"
            ? Labels(xml)
                .Any(x =>
                    x.Contains("Create post", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("What's on your mind", StringComparison.OrdinalIgnoreCase)
                    || x.Contains(
                        "Say something about this photo",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            : Labels(xml)
                .Any(x =>
                    x.Contains("Describe your reel", StringComparison.OrdinalIgnoreCase)
                    || x.Equals("Reel Settings", StringComparison.OrdinalIgnoreCase)
                );

    public static bool ComposerMatchesTarget(string xml, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        // Native profile headers can expose line breaks and nonbreaking spaces.
        // Preserve exact actor matching after normalizing presentation whitespace.
        static string Normalize(string label) => Regex.Replace(label.Trim(), @"\s+", " ");
        var expected = Normalize(name);
        return Labels(xml)
            .Select(Normalize)
            .Any(label =>
                label.Equals(expected, StringComparison.OrdinalIgnoreCase)
                || label.Equals("Posting as " + expected, StringComparison.OrdinalIgnoreCase)
                || label.Equals("Sharing as " + expected, StringComparison.OrdinalIgnoreCase)
            );
    }

    internal static string? NewPublicationEvidence(string xml, ISet<string> before, string type)
    {
        var allowed =
            type == "Picture"
                ? new[] { "Your post has been shared", "Your post was published", "Post published" }
                : new[]
                {
                    "Your reel has been shared",
                    "Your reel was published",
                    "Reel published",
                };
        return Labels(xml)
            .FirstOrDefault(x =>
                !before.Contains(x)
                && allowed.Any(l => string.Equals(x.Trim(), l, StringComparison.OrdinalIgnoreCase))
            );
    }
}
