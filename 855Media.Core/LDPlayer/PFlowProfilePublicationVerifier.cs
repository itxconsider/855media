using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Read-only verification of the current actor's own Reels. Unsupported layouts
// return no proof. No account switching, Share, Like, overflow or edit actions.
internal sealed class PFlowProfilePublicationVerifier(ILDPlayerService ld)
{
    internal sealed record Proof(string Evidence, string Id, string Url);

    internal sealed record Rect(int X, int Y, int Width, int Height);

    internal Func<
        PFlowJob,
        string?,
        CancellationToken,
        Task<bool>
    >? VerifyIdentityOverride { get; init; }
    internal Func<PFlowPublicationStage, string, Task>? PublicationProgress { get; init; }
    internal string FailureReason { get; private set; } = "";
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    internal static bool Required(PFlowJob job) =>
        job.AccountBinding != null && job.PostType is "Reel" or "Video";

    private static FacebookPageTarget Target(PFlowJob job) =>
        new()
        {
            PageId = job.TargetId,
            PersonaUid = job.PersonaUid,
            UserUid = job.OwnerUid,
            PageName = job.TargetName,
            Type = job.IsProfile ? "Profile" : "Page",
        };

    private async Task<bool> Identity(PFlowJob job, string? path, CancellationToken ct)
    {
        await PFlowJobBindingGuard.RequireAsync(ld, job, path, ct);
        if (VerifyIdentityOverride != null)
            return await VerifyIdentityOverride(job, path, ct);
        try
        {
            if (job.NativeIdentityScope == null || !job.NativeIdentityScope.Matches(job))
            {
                FailureReason = "missing-native-identity-session";
                return false;
            }
            if (!await job.NativeIdentityScope.VerifyAsync(ld, path, ct))
            {
                FailureReason = "native-identity-not-confirmed";
                return false;
            }
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "publication.session-ui",
                "Graph-bound authenticated session and exact own-profile UI verified; Graph not called; settings lookup=0.",
                ct
            );
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailureReason = "live-identity-unavailable:" + ex.GetType().Name;
            return false;
        }
    }

    private async Task<string> Observe(PFlowJob job, string? path, CancellationToken ct)
    {
        var xml = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        PFlowPageIdentityRestriction.ThrowIfVisible(job, xml);
        return xml;
    }

    internal static bool Safe(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowIdentitySwitchLayout.IsSwitching(xml)
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        && !PFlowShareDestination.IsChooser(xml)
        && ScreenTimeHierarchy
            .Nodes(xml)
            .All(n =>
                string.IsNullOrEmpty((string?)n.Attribute("package"))
                || (string?)n.Attribute("package")
                    is "com.facebook.katana"
                        or "com.android.systemui"
            )
        && ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n => (string?)n.Attribute("package") == "com.facebook.katana");

    internal static Rect? Bounds(XElement node)
    {
        var m = Regex.Match(
            (string?)node.Attribute("bounds") ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        if (
            !m.Success
            || node.AncestorsAndSelf().Any(n => (string?)n.Attribute("visible-to-user") == "false")
        )
            return null;
        var values = m
            .Groups.Cast<Group>()
            .Skip(1)
            .Select(g => int.TryParse(g.Value, out var v) ? v : -1)
            .ToArray();
        return values.All(v => v >= 0) && values[2] > values[0] && values[3] > values[1]
            ? new(values[0], values[1], values[2] - values[0], values[3] - values[1])
            : null;
    }

    internal static XElement? PostsTab(string xml) =>
        ProfileTab(xml, "All") ?? ProfileTab(xml, "Posts");

    private static XElement? ProfileTab(string xml, string label)
    {
        if (!Safe(xml))
            return null;
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        var photos = nodes.Where(n => Exact(n, "Photos") && Bounds(n) != null).ToArray();
        var tabs = nodes
            .Where(n =>
                (
                    Exact(n, label)
                    || Regex.IsMatch(
                        (string?)n.Attribute("content-desc") ?? "",
                        "^" + Regex.Escape(label) + @", (?:tab(?:,| )|\d+ of \d+)",
                        RegexOptions.IgnoreCase
                    )
                )
                && FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true) is { } p
                && photos.Any(photo =>
                    Bounds(photo) is { } r && Math.Abs(p.Y - (r.Y + r.Height / 2)) < 45
                )
            )
            .GroupBy(n => FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true))
            .Select(g => g.First())
            .ToArray();
        return tabs.Length == 1 ? tabs[0] : null;
    }

    private async Task<bool> OpenProfile(PFlowJob job, string? path, CancellationToken ct)
    {
        var initial = await Observe(job, path, ct);
        if (
            !Safe(initial)
            || PFlowConfirmation.HasPendingPublication(initial)
            || PFlowAutoPostService.HasComposerLayoutEvidence(initial, "Reel")
        )
            return false;
        if (!await Identity(job, path, ct))
            return false;
        initial = await Observe(job, path, ct); // Session/UI verification can navigate to the current actor's own profile.
        if (
            !PFlowPostTargetSession.HasOwnProfile(job, initial)
            && (
                FacebookLayoutRegistry.Match(initial, FacebookScreenKind.ReelsFeed).IsMatch
                || Labels(initial).Contains("Reel details", StringComparer.OrdinalIgnoreCase)
            )
        )
        {
            var back = ScreenTimeHierarchy.Control(initial, "Back");
            var backPoint =
                back == null ? null : FacebookLayoutRegistry.UsableCenter(back, readOnlyList: true);
            if (
                backPoint == null
                || !await ld.TapAsync(
                    job.DeviceIndex,
                    backPoint.Value.X,
                    backPoint.Value.Y,
                    path,
                    ct
                )
            )
                return false;
            await Delay(TimeSpan.FromMilliseconds(600), ct);
            if (!Safe(await Observe(job, path, ct)))
                return false;
        }
        if (
            !await PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                ld,
                job.DeviceIndex,
                path,
                Target(job),
                ct
            )
        )
            return false;
        var xml = await Observe(job, path, ct);
        return Safe(xml) && PFlowPostTargetSession.HasOwnProfile(job, xml);
    }

    // A navigation acknowledgement may leave an already-open, cached timeline.
    // Reveal its top first, then issue a separate pull-to-refresh before reading posts.
    private async Task<bool> RefreshProfile(PFlowJob job, string? path, CancellationToken ct)
    {
        var xml = await Observe(job, path, ct);
        if (!Safe(xml) || !PFlowPostTargetSession.HasOwnProfile(job, xml))
            return false;
        for (var reveal = 0; reveal < 4; reveal++)
        {
            if (!await ScrollProfile(job, xml, path, ct, towardTop: true))
                return false;
            var next = await Observe(job, path, ct);
            if (!Safe(next))
                return false;
            if (next == xml)
                break;
            xml = next;
        }
        if (
            !PFlowPostTargetSession.HasOwnProfile(job, xml)
            || !await ScrollProfile(job, xml, path, ct, towardTop: true)
        )
            return false;
        await Delay(TimeSpan.FromMilliseconds(1500), ct);
        for (var settle = 0; settle < 4; settle++)
        {
            xml = await Observe(job, path, ct);
            if (!Safe(xml))
                return false;
            if (PFlowPostTargetSession.HasOwnProfile(job, xml))
                return true;
            await Delay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }

    internal async Task<Proof?> VerifyAsync(PFlowJob job, string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.Caption) || !job.SubmittedUtc.HasValue)
            return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        try
        {
            FailureReason = "opening-target-profile";
            if (PublicationProgress != null)
                await PublicationProgress(
                    PFlowPublicationStage.CheckingProfile,
                    "Opening mapped target profile"
                );
            if (!await OpenProfile(job, path, token))
                return null;
            FailureReason = "refreshing-target-profile";
            if (PublicationProgress != null)
                await PublicationProgress(
                    PFlowPublicationStage.RefreshingProfile,
                    "Pull-to-refresh on mapped target profile"
                );
            if (!await RefreshProfile(job, path, token))
                return null;
            FailureReason = "matching-caption-and-publication-time";
            if (PublicationProgress != null)
                await PublicationProgress(
                    PFlowPublicationStage.CheckingProfile,
                    "Reading target post caption and publication age"
                );
            var proof = await ReadProfilePost(job, path, token);
            if (proof == null || !await Identity(job, path, token))
                return null;
            FailureReason = "";
            return proof;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            FailureReason = "profile-check-timeout";
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch when (!job.TargetIdentityConfirmationRequired)
        {
            return null;
        }
    }

    internal static Proof? MatchViewer(PFlowJob job, string xml, DateTime now) =>
        MatchPost(job, xml, now, captionVerifiedInViewer: false);

    private static Proof? MatchPost(
        PFlowJob job,
        string xml,
        DateTime now,
        bool captionVerifiedInViewer
    )
    {
        if (
            !Safe(xml)
            || PFlowConfirmation.HasPendingPublication(xml)
            || !job.SubmittedUtc.HasValue
            || PFlowAutoPostService.HasComposerLayoutEvidence(xml, "Reel")
        )
            return null;
        var labels = Labels(xml);
        if (
            labels.Any(s =>
                Regex.IsMatch(
                    s,
                    @"^(?:Uploading|Processing|Posting|Sharing your reel|Waiting to upload|Upload failed|Retry)(?:\b|[. …])",
                    RegexOptions.IgnoreCase
                )
            )
        )
            return null;
        if (!CaptionAndAuthor(job, xml) && !captionVerifiedInViewer)
            return null;
        var elapsed = now - job.SubmittedUtc.Value;
        if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromHours(2))
            return null;
        var publishedAge = labels
            .Select(s => Regex.Replace(s, @"[^\p{L}\p{N}]+Shared with: Public$", "").Trim())
            .FirstOrDefault(s => RecentAge(s, elapsed));
        if (publishedAge == null)
            return null;
        var ids = Ids(xml);
        if (ids.Count > 1)
            return null;
        var urls = labels
            .Where(s =>
                Uri.TryCreate(s, UriKind.Absolute, out var u)
                && u.Scheme == "https"
                && (
                    u.Host == "facebook.com"
                    || u.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
                )
                && Regex.IsMatch(u.AbsolutePath, @"/(?:reel|videos)/\d+")
            )
            .Distinct()
            .ToArray();
        if (urls.Length > 1)
            return null;
        return new(
            $"Target profile Reel verified: exact caption and author on the target profile, with publication time matching submission. Displayed age={publishedAge}; submittedUtc={job.SubmittedUtc:O}; checkedUtc={now:O}.",
            ids.SingleOrDefault() ?? "",
            urls.SingleOrDefault() ?? ""
        );
    }

    private static bool CaptionAndAuthor(PFlowJob job, string xml)
    {
        var labels = Labels(xml);
        return Safe(xml)
            && labels.Any(s => Normalize(s) == Normalize(job.Caption))
            && labels.Any(s =>
                s.Equals(job.TargetName, StringComparison.OrdinalIgnoreCase)
                || s.Equals(
                    job.TargetName + ", Privacy setting Public",
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    private static bool RecentAge(string value, TimeSpan elapsed)
    {
        if (value.Equals("Just now", StringComparison.OrdinalIgnoreCase))
            return true;
        var m = Regex.Match(
            value,
            @"^(?<n>\d{1,3})\s*(?<unit>s|m|h|seconds?|minutes?|hours?)(?: ago)?$",
            RegexOptions.IgnoreCase
        );
        if (!m.Success)
            return false;
        var amount = int.Parse(m.Groups["n"].Value);
        var unit = m.Groups["unit"].Value.ToLowerInvariant()[0];
        var age =
            unit == 's' ? TimeSpan.FromSeconds(amount)
            : unit == 'm' ? TimeSpan.FromMinutes(amount)
            : TimeSpan.FromHours(amount);
        // Facebook rounds visible relative ages. Allow a small UI/clock margin,
        // but never accept an older age merely because the caption matches.
        return age <= elapsed + TimeSpan.FromSeconds(30);
    }

    private async Task<Proof?> ReadProfilePost(PFlowJob job, string? path, CancellationToken ct)
    {
        var xml = await Observe(job, path, ct);
        if (!Safe(xml) || !PFlowPostTargetSession.HasOwnProfile(job, xml))
            return null;
        // The refreshed main profile starts at its cover; its post tabs may be below the fold.
        for (var reveal = 0; reveal < 4 && PostsTab(xml) == null; reveal++)
        {
            if (!await ScrollProfile(job, xml, path, ct))
                return null;
            xml = await Observe(job, path, ct);
            if (!Safe(xml))
                return null;
        }
        var tab = PostsTab(xml);
        var point =
            tab == null ? null : FacebookLayoutRegistry.UsableCenter(tab, readOnlyList: true);
        if (
            point == null
            || !await ld.TapAsync(job.DeviceIndex, point.Value.X, point.Value.Y, path, ct)
        )
            return null;
        await Delay(TimeSpan.FromMilliseconds(600), ct);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            xml = await Observe(job, path, ct);
            if (!Safe(xml))
                return null;
            foreach (var post in PostCards(xml, job))
            {
                var cardXml = "<hierarchy>" + post + "</hierarchy>";
                var prefix = Normalize(job.Caption);
                prefix = prefix[..Math.Min(24, prefix.Length)];
                if (prefix.Length == 0)
                    continue;
                var hasCaptionPrefix = Labels(cardXml)
                    .Any(s => Normalize(s).StartsWith(prefix, StringComparison.Ordinal));
                if (!hasCaptionPrefix)
                {
                    // Some refreshed timelines omit the caption entirely. Read it
                    // from this exact observed Reel; keep the age scoped to its card.
                    var timed = MatchPost(
                        job,
                        cardXml,
                        DateTime.UtcNow,
                        captionVerifiedInViewer: true
                    );
                    if (timed != null)
                    {
                        if (await ReadCardCaption(job, post, path, ct))
                            return timed;
                        xml = await Observe(job, path, ct);
                        if (!Safe(xml) || PostCards(xml, job).Length == 0)
                            return null;
                    }
                    continue;
                }
                var proof = MatchViewer(job, cardXml, DateTime.UtcNow);
                if (proof != null)
                    return proof;
                var more = CaptionExpansionControls(post, job.Caption);
                if (more.Length == 1)
                {
                    var p = more[0];
                    await ld.TapAsync(job.DeviceIndex, p.X, p.Y, path, ct);
                    await Delay(TimeSpan.FromMilliseconds(500), ct);
                    var expanded = await Observe(job, path, ct);
                    foreach (var candidate in PostCards(expanded, job))
                    {
                        proof = MatchViewer(
                            job,
                            "<hierarchy>" + candidate + "</hierarchy>",
                            DateTime.UtcNow
                        );
                        if (proof != null)
                            return proof;
                    }
                }
            }
            if (attempt == 3 || !await ScrollProfile(job, xml, path, ct))
                return null;
        }
        return null;
    }

    private async Task<bool> ReadCardCaption(
        PFlowJob job,
        XElement post,
        string? path,
        CancellationToken ct
    )
    {
        var current = await Observe(job, path, ct);
        if (!Safe(current) || !PostCards(current, job).Any(p => XNode.DeepEquals(p, post)))
            return false;
        // RecyclerView nodes can extend behind Facebook's fixed bottom tabs.
        // Scroll a clipped Reel into view instead of tapping an obscured center.
        var navigationTop = ScreenTimeHierarchy
            .Nodes(current)
            .Where(n =>
                Regex.IsMatch(n.Attribute("content-desc")?.Value ?? "", @"^.+, tab \d+ of \d+$")
                && Bounds(n) is { } r
                && r.Y > 500
            )
            .Select(n => Bounds(n)!.Y)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        var controls = post.DescendantsAndSelf("node")
            .Where(n =>
                n.Attribute("content-desc")?.Value == "Reel"
                && n.Attribute("class")?.Value
                    is "android.widget.FrameLayout"
                        or "android.widget.Button"
            )
            .Select(n => FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true))
            .Where(p => p != null && p.Value.Y < navigationTop)
            .Distinct()
            .ToArray();
        if (controls.Length != 1)
            return false;
        var point = controls[0]!.Value;
        // Do not use a card's old coordinates after loading or another navigation.
        current = await Observe(job, path, ct);
        if (
            !PostCards(current, job).Any(p => XNode.DeepEquals(p, post))
            || !await ld.TapAsync(job.DeviceIndex, point.X, point.Y, path, ct)
        )
            return false;
        string viewer = "";
        for (var settle = 0; settle < 5; settle++)
        {
            await Delay(TimeSpan.FromMilliseconds(600), ct);
            viewer = await Observe(job, path, ct);
            if (!Safe(viewer))
                return false;
            if (CaptionAndAuthor(job, viewer))
                break;
            var viewerRoot = XElement.Parse(viewer);
            var viewerPrefix = Normalize(job.Caption);
            viewerPrefix = viewerPrefix[..Math.Min(24, viewerPrefix.Length)];
            var more =
                Labels(viewer)
                    .Any(s => s.Equals(job.TargetName, StringComparison.OrdinalIgnoreCase))
                && viewerPrefix.Length >= 12
                && Labels(viewer)
                    .Any(s => Normalize(s).StartsWith(viewerPrefix, StringComparison.Ordinal))
                    ? CaptionExpansionControls(viewerRoot, job.Caption)
                    : [];
            if (more.Length == 1)
            {
                var p = more[0];
                if (await ld.TapAsync(job.DeviceIndex, p.X, p.Y, path, ct))
                {
                    await Delay(TimeSpan.FromMilliseconds(500), ct);
                    viewer = await Observe(job, path, ct);
                    if (CaptionAndAuthor(job, viewer))
                        break;
                }
            }
        }
        var matched = CaptionAndAuthor(job, viewer);
        var back = ScreenTimeHierarchy.Control(viewer, "Back");
        var backPoint =
            back == null ? null : FacebookLayoutRegistry.UsableCenter(back, readOnlyList: true);
        if (
            backPoint == null
            || !await ld.TapAsync(job.DeviceIndex, backPoint.Value.X, backPoint.Value.Y, path, ct)
        )
            return false;
        await Delay(TimeSpan.FromMilliseconds(500), ct);
        // Reacquire the original timeline after Back; never scroll a viewer.
        var restored = await Observe(job, path, ct);
        return matched && Safe(restored) && PostCards(restored, job).Any();
    }

    internal static (int X, int Y)[] CaptionExpansionControls(XElement scope, string caption)
    {
        var prefix = Normalize(caption);
        prefix = prefix[..Math.Min(24, prefix.Length)];
        return scope
            .DescendantsAndSelf("node")
            .Where(n =>
                new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Any(s =>
                        Regex.IsMatch(s!.Trim(), @"^(?:See\s+)?more$", RegexOptions.IgnoreCase)
                        || (
                            prefix.Length >= 12
                            && !n.Descendants("node")
                                .Any(child =>
                                    new[]
                                    {
                                        (string?)child.Attribute("text"),
                                        (string?)child.Attribute("content-desc"),
                                    }.Any(label =>
                                        label != null
                                        && Regex.IsMatch(
                                            label.Trim(),
                                            @"^(?:See\s+)?more$",
                                            RegexOptions.IgnoreCase
                                        )
                                    )
                                )
                            && Normalize(s!).StartsWith(prefix, StringComparison.Ordinal)
                            && Regex.IsMatch(
                                s!.Trim(),
                                @"(?:…|\.{3})\s*(?:See\s+)?more$",
                                RegexOptions.IgnoreCase
                            )
                        )
                    )
            )
            .Select(n => FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .ToArray();
    }

    internal static XElement[] PostCards(string xml, PFlowJob job)
    {
        if (!Safe(xml))
            return [];
        return ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "More options for " + job.TargetName,
                    StringComparison.Ordinal
                )
                && (n.Attribute("content-desc")?.Value ?? "").EndsWith(
                    "post",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(n =>
                n.Ancestors("node")
                    .FirstOrDefault(a =>
                        a.Descendants("node")
                            .Count(c =>
                                (c.Attribute("content-desc")?.Value ?? "").StartsWith(
                                    "More options for ",
                                    StringComparison.Ordinal
                                )
                                && (c.Attribute("content-desc")?.Value ?? "").EndsWith(
                                    "post",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            ) == 1
                        && a.Descendants("node").Any(c => Exact(c, job.TargetName))
                        && a.Descendants("node")
                            .Any(c =>
                                c.Attribute("content-desc")?.Value == "Reel"
                                && c.Attribute("class")?.Value
                                    is "android.widget.FrameLayout"
                                        or "android.widget.Button"
                            )
                        && a.Descendants("node")
                            .Any(c =>
                                (
                                    c.Attribute("content-desc")?.Value
                                    ?? c.Attribute("text")?.Value
                                    ?? ""
                                ).Contains("Shared with: Public", StringComparison.Ordinal)
                            )
                    )
            )
            .Where(n => n != null)
            .Cast<XElement>()
            .Distinct()
            .ToArray();
    }

    private async Task<bool> ScrollProfile(
        PFlowJob job,
        string xml,
        string? path,
        CancellationToken ct,
        bool towardTop = false
    )
    {
        if (!Safe(xml))
            return false;
        var viewport = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
                && n.Attribute("scrollable")?.Value == "true"
            )
            .Select(Bounds)
            .Where(b => b is { Height: > 200, Width: > 200 })
            .OrderByDescending(b => b!.Height)
            .FirstOrDefault();
        if (viewport == null)
            return false;
        if (
            !await ld.SwipeAsync(
                job.DeviceIndex,
                viewport.X + viewport.Width / 2,
                viewport.Y + (towardTop ? viewport.Height / 3 : viewport.Height * 4 / 5),
                viewport.X + viewport.Width / 2,
                viewport.Y + (towardTop ? viewport.Height * 4 / 5 : viewport.Height / 3),
                450,
                path,
                ct
            )
        )
            return false;
        await Delay(TimeSpan.FromMilliseconds(600), ct);
        return true;
    }

    private static bool Exact(XElement node, string value) =>
        new[] { (string?)node.Attribute("text"), (string?)node.Attribute("content-desc") }.Any(s =>
            s == value
        );

    private static string Normalize(string value) =>
        Regex.Replace(value.Normalize(), @"\s+", " ").Trim();

    private static string[] Labels(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("package") == "com.facebook.katana" && Bounds(n) != null
            )
            .SelectMany(n =>
                new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
            )
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToArray();

    private static HashSet<string> Ids(string xml) =>
        Labels(xml)
            .SelectMany(s =>
                Regex
                    .Matches(
                        s,
                        @"(?:/(?:reel|videos|posts)/|(?:post_id|post-id|Post ID)\s*[:=]\s*)(\d+)"
                    )
                    .Select(m => m.Groups[1].Value)
            )
            .ToHashSet(StringComparer.Ordinal);
}
