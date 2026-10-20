using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// The caller must have verified the personal actor. This route only reads
// settings; editable birthday screens are never accepted or submitted.
internal static class PFlowPersonalBirthdayReader
{
    private static bool MatchesLabel(string? value, string label) =>
        string.Equals(value?.Trim(), label, StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(
            value?.Trim() ?? "",
            "^" + Regex.Escape(label) + @",\s*button\s+\d+\s+of\s+\d+$",
            RegexOptions.IgnoreCase
        )
        || label == "Settings & privacy"
            && Regex.IsMatch(
                value?.Trim() ?? "",
                @"^Settings (?:and|&) privacy(?:, header\. Section is (?:collapsed|expanded)\. Double-tap to (?:expand|collapse) the section\.)?$",
                RegexOptions.IgnoreCase
            );

    private static bool MenuSurface(string xml, BloksScreenInfo screen) =>
        AtxAgentClient.IsMenuPageInBloks(screen) || Has(xml, "Close menu.", "Close menu");

    internal static (int X, int Y)? ProfileControl(string xml, string name)
    {
        if (Blocked(xml))
            return null;
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        var points = nodes
            .Where(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(
                    v =>
                        string.Equals(v?.Trim(), name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            v?.Trim(),
                            name + ", Facebook",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
            )
            .Select(n =>
                n.AncestorsAndSelf("node")
                    .FirstOrDefault(a => (string?)a.Attribute("clickable") == "true")
            )
            .Where(n =>
                n != null
                && !n.DescendantsAndSelf("node")
                    .Any(a =>
                        string.Equals(
                            (string?)a.Attribute("text"),
                            "Instagram",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            (string?)a.Attribute("content-desc"),
                            "Instagram",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                && n.DescendantsAndSelf("node")
                    .Any(a =>
                        new[]
                        {
                            (string?)a.Attribute("text"),
                            (string?)a.Attribute("content-desc"),
                        }.Any(v =>
                            string.Equals(v?.Trim(), "Facebook", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                v?.Trim(),
                                name + ", Facebook",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
            )
            .Select(n => FacebookLayoutRegistry.UsableCenter(n!, readOnlyList: true))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return points.Length == 1 ? points[0] : null;
    }

    internal static (int X, int Y)? Control(string xml, params string[] labels)
    {
        var points = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && labels.Any(l =>
                    new[]
                    {
                        (string?)n.Attribute("text"),
                        (string?)n.Attribute("content-desc"),
                    }.Any(v => MatchesLabel(v, l))
                )
            )
            // Installed Settings drawers mark a passive ViewGroup around the
            // RecyclerView disabled while their explicit child buttons are enabled.
            .Select(n => FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return points.Length == 1 ? points[0] : null;
    }

    private static bool Own(BloksScreenInfo screen, string name) =>
        !Blocked(screen.RawXml)
        && !AtxAgentClient.IsMenuPageInBloks(screen)
        && PFlowProfileDates.MatchesName(screen, name)
        && screen.AllElements.Any(e => e.Matches("Edit profile", false));

    private static bool Has(string? xml, params string[] labels) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                && labels.Any(l =>
                    new[]
                    {
                        (string?)n.Attribute("text"),
                        (string?)n.Attribute("content-desc"),
                    }.Any(v => MatchesLabel(v, l))
                )
            );

    private static bool Blocked(string? xml) =>
        (PFlowUiPrivacy.ContainsInput(xml) && !PFlowMetaAccountLayout.IsSettings(xml))
        || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        || PFlowIdentitySwitchLayout.IsSwitching(xml)
        || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        || ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("package") is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            );

    internal static string ReadDate(string xml, bool personalDetailsConfirmed)
    {
        if (
            personalDetailsConfirmed
            && !Blocked(xml)
            && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.MetaBirthdayDetails).IsMatch
        )
            return PFlowMetaAccountLayout.ReadBirthday(xml);
        if (
            !personalDetailsConfirmed
            || Blocked(xml)
            || (
                Has(xml, "Your information and permissions")
                && !FacebookLayoutRegistry
                    .Match(xml, FacebookScreenKind.PersonalInformationPermissions)
                    .IsMatch
            )
            || Has(xml, "Instagram", "Choose a profile", "Select a profile")
            || Has(xml, "Profiles") && !Has(xml, "Personal details", "Personal information")
            || !Has(
                xml,
                "Personal details",
                "Personal information",
                "Birthday",
                "Date of birth",
                "Birth date"
            )
            || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
            || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelsFeed).IsMatch
        )
            return "";
        string[] Labels(XElement n) =>
            new[]
            {
                (string?)n.Attribute("text") ?? "",
                (string?)n.Attribute("content-desc") ?? "",
            };
        bool BirthdayLabel(string v) =>
            Regex.IsMatch(
                v.Trim(),
                @"^(?:Birthday|Date of birth|Birth date)(?:$|\s*[:,])",
                RegexOptions.IgnoreCase
            );
        var dates = new System.Collections.Generic.HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );
        var (_, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        foreach (
            var label in ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && !n.AncestorsAndSelf("node")
                        .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                    && Labels(n).Any(BirthdayLabel)
                )
        )
        {
            foreach (var value in Labels(label).Where(BirthdayLabel))
            {
                var date = PFlowProfileDates.ValidDate(value);
                if (date.Length > 0)
                    dates.Add(date);
            }
            // A sibling date must belong to the same compact field row, never
            // a nearby join date, another account, or arbitrary Feed content.
            foreach (var row in label.Ancestors("node").Take(3))
            {
                var bounds = Regex.Match(
                    (string?)row.Attribute("bounds") ?? "",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                );
                if (
                    !bounds.Success
                    || height <= 0
                    || int.Parse(bounds.Groups[4].Value) - int.Parse(bounds.Groups[2].Value)
                        > height / 4
                )
                    continue;
                foreach (
                    var n in row.Descendants("node")
                        .Where(n =>
                            !n.AncestorsAndSelf("node")
                                .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                        )
                )
                foreach (var value in Labels(n))
                {
                    var date = PFlowProfileDates.ValidDate(value);
                    if (date.Length > 0 && date == value.Trim())
                        dates.Add(date);
                }
                break;
            }
        }
        return dates.Count == 1 ? dates.Single() : "";
    }

    internal static async Task<string> ReadAsync(
        ILDPlayerService ld,
        int index,
        string name,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= PFlowFetchPacing.DelayAsync;
        ct.ThrowIfCancellationRequested();
        var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        if (Blocked(xml))
            return "";
        if (
            !Own(AtxAgentClient.ParseBloksHierarchy(xml), name)
            && !await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                index,
                name,
                path,
                s => Own(s, name),
                ct,
                delay: delay
            )
        )
            return "";
        var entered = false;
        var personal = false;
        var profilesOpened = false;
        var menuOpened = false;
        var headerReveals = 0;
        var settingsOpened = false;
        var sectionAttempts = 0;
        var birthdayOpened = false;
        var birthdayProfileSelected = false;
        var scrolls = 0;
        var reason = "route-unavailable";
        try
        {
            var acknowledged = await ld.OpenDeepLinkAsync(
                index,
                BloksConstants.AccountsCenterDeepLink,
                BloksConstants.KatanaPackage,
                path,
                ct
            );
            await PFlowFetchTrace.WriteAsync(
                index,
                "personal.birthday.open",
                $"deepLinkAcknowledged={acknowledged}",
                ct
            );
            for (var observation = 0; observation < 32; observation++)
            {
                ct.ThrowIfCancellationRequested();
                await delay(TimeSpan.FromMilliseconds(500), ct);
                xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                if (Blocked(xml))
                {
                    reason = "blocked-or-editable-screen";
                    return "";
                }
                var screen = AtxAgentClient.ParseBloksHierarchy(xml);
                var center =
                    (
                        FacebookLayoutRegistry.Match(xml, FacebookScreenKind.MetaAccount).IsMatch
                        || Has(xml, "Accounts Center", "Accounts Centre")
                        || Has(xml, "Profiles and personal details")
                            && Has(
                                xml,
                                "Manage your connected experiences and account settings across Meta technologies. Learn more"
                            )
                    )
                    && !MenuSurface(xml, screen)
                    && !Has(xml, "Settings & privacy");
                entered |= center;
                if (
                    profilesOpened
                    && Has(xml, "Personal details", "Personal information")
                    && Control(xml, "Personal details", "Personal information") == null
                )
                    personal = true;
                if (observation is 0 or 5 or 15)
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.birthday.route",
                        $"observation={observation}; accountsCenter={center}; menu={MenuSurface(xml, screen)}; menuControl={Control(xml, "Menu", "Account switcher, shortcuts, and settings menu") != null}; nodes={screen.AllElements.Count}",
                        ct
                    );
                if (personal)
                {
                    var date = ReadDate(xml, true);
                    if (date.Length > 0)
                    {
                        reason = "verified-read-only-field";
                        return date;
                    }
                }
                async Task<bool> Tap(params string[] labels)
                {
                    var p = Control(xml, labels);
                    if (!p.HasValue)
                        return false;
                    var acknowledged = await ld.TapAsync(index, p.Value.X, p.Value.Y, path, ct);
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.birthday.navigate",
                        $"control={labels[0]}; acknowledged={acknowledged}",
                        ct
                    );
                    if (acknowledged)
                        await delay(TimeSpan.FromMilliseconds(1000), ct);
                    return acknowledged;
                }
                if (
                    center
                    && !personal
                    && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.MetaAccount).IsMatch
                    && await Tap("Your information and permissions")
                )
                {
                    personal = true;
                    continue;
                }
                if (center && !profilesOpened && await Tap("Profiles and personal details"))
                {
                    profilesOpened = true;
                    continue;
                }
                if (
                    birthdayOpened
                    && !birthdayProfileSelected
                    && ProfileControl(xml, name) is { } ownProfile
                )
                {
                    if (await ld.TapAsync(index, ownProfile.X, ownProfile.Y, path, ct))
                    {
                        personal = true;
                        birthdayProfileSelected = true;
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "personal.birthday.profile-selected",
                            "matchingFacebookProfile=true",
                            ct
                        );
                        continue;
                    }
                }
                if (center && !personal && await Tap("Personal details", "Personal information"))
                {
                    personal = true;
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.birthday.personal-details",
                        "observedControl=true",
                        ct
                    );
                    continue;
                }
                if (
                    personal
                    && !birthdayOpened
                    && Has(xml, "Personal details", "Personal information")
                    && await Tap("Birthday", "Date of birth", "Birth date")
                )
                {
                    birthdayOpened = true;
                    continue;
                }
                if (observation < 5)
                    continue; // Allow lazy Accounts Center content to arrive.
                if (!entered)
                {
                    if (
                        await Tap(
                            "Meta Account",
                            "Accounts Center",
                            "See more in Accounts Center",
                            "See more in Accounts Centre"
                        )
                    )
                    {
                        entered = true;
                        continue;
                    }
                    if (!menuOpened && !MenuSurface(xml, screen))
                    {
                        var menu = Control(
                            xml,
                            "Menu",
                            "Account switcher, shortcuts, and settings menu"
                        );
                        if (menu.HasValue)
                            menuOpened = await ld.TapAsync(
                                index,
                                menu.Value.X,
                                menu.Value.Y,
                                path,
                                ct
                            );
                        else if (
                            headerReveals < 3
                            && PFlowPersonaSwitcherService.CanRevealNativeNavigation(screen)
                        )
                        {
                            var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(
                                xml
                            );
                            if (
                                width > 0
                                && height > 0
                                && await ld.SwipeAsync(
                                    index,
                                    width / 2,
                                    height / 3,
                                    width / 2,
                                    height * 4 / 5,
                                    350,
                                    path,
                                    ct
                                )
                            )
                            {
                                headerReveals++;
                                await PFlowFetchTrace.WriteAsync(
                                    index,
                                    "personal.birthday.reveal-navigation",
                                    $"attempt={headerReveals}; observedBrowseScreen=true",
                                    ct
                                );
                            }
                        }
                        continue;
                    }
                    if (!settingsOpened && MenuSurface(xml, screen) && await Tap("Settings"))
                    {
                        settingsOpened = true;
                        continue;
                    }
                    if (
                        sectionAttempts < 2
                        && MenuSurface(xml, screen)
                        && !ScreenTimeHierarchy
                            .Nodes(xml)
                            .Any(n =>
                                ((string?)n.Attribute("content-desc") ?? "").StartsWith(
                                    "Settings and privacy, header. Section is expanded.",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                        && await Tap("Settings & privacy")
                    )
                    {
                        sectionAttempts++;
                        continue;
                    }
                }
                if (
                    scrolls < 3
                    && (
                        center
                        || MenuSurface(xml, screen)
                        || Has(xml, "Settings", "Personal details", "Personal information")
                        || profilesOpened
                    )
                )
                {
                    var (w, h) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                    if (
                        w > 0
                        && h > 0
                        && await ld.SwipeAsync(index, w / 2, h * 4 / 5, w / 2, h / 2, 350, path, ct)
                    )
                        scrolls++;
                }
            }
            reason =
                personal ? "birthday-not-exposed"
                : entered ? "personal-details-not-exposed"
                : "route-unavailable";
            await PFlowDiagnostics.CaptureAsync(ld, index, "personal-birthday-unavailable", path);
            return "";
        }
        finally
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "personal.birthday.lookup-result",
                $"reason={reason}; personalDetailsOpened={personal}; scrolls={scrolls}",
                ct
            );
            if (!ct.IsCancellationRequested)
            {
                try
                {
                    // Unwind only the observed settings route; never save an edit.
                    for (var back = 0; back < 4; back++)
                    {
                        xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                        if (
                            Blocked(xml)
                            || !Has(
                                xml,
                                "Accounts Center",
                                "Accounts Centre",
                                "Meta Account",
                                "Your information and permissions",
                                "Profiles and personal details",
                                "Personal details",
                                "Personal information",
                                "Birthday",
                                "Settings & privacy",
                                "Settings"
                            )
                        )
                            break;
                        if (Control(xml, "Back") != null)
                        {
                            if (!await ld.PressBackAsync(index, path, ct))
                                break;
                        }
                        else if (
                            entered
                            && !MenuSurface(xml, AtxAgentClient.ParseBloksHierarchy(xml))
                            && Has(
                                xml,
                                "Accounts Center",
                                "Accounts Centre",
                                "Meta Account",
                                "Profiles and personal details"
                            )
                            && Control(xml, "Close") is { } close
                        )
                        {
                            if (!await ld.TapAsync(index, close.X, close.Y, path, ct))
                                break;
                        }
                        else
                            break;
                        await delay(TimeSpan.FromMilliseconds(400), ct);
                    }
                    var restored =
                        !Blocked(xml)
                        && await PFlowOwnProfileNavigation.OpenAsync(
                            ld,
                            index,
                            name,
                            path,
                            s =>
                                !AtxAgentClient.IsMenuPageInBloks(s)
                                && PFlowProfileDates.MatchesName(s, name)
                                && s.AllElements.Any(e => e.Matches("Edit profile", false)),
                            ct,
                            delay: delay
                        );
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.birthday.return",
                        $"ownProfileRestored={restored}",
                        ct
                    );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.birthday.return-failed",
                        $"errorType={ex.GetType().Name}; partialDataRetained=true",
                        ct
                    );
                }
            }
        }
    }
}
