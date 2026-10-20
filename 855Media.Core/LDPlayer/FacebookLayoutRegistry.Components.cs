using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class FacebookLayoutRegistry
{
    public static IReadOnlyList<string> SectionExpansionLabels =>
        CaptureCatalog().SectionExpansionLabels;

    internal static bool IsSectionExpansionLabel(string? label) =>
        CaptureCatalog().IsSectionExpansion(label);

    private static readonly FacebookLayoutRule PersonalSettingsExcluded = Rule(
        "not-personal-settings",
        "Editable, Page, or foreign settings are not personal-profile link evidence",
        o =>
            PFlowUiPrivacy.ContainsInput(o.Xml)
            || o.Has("Page Settings")
            || o.Has("Your Page link")
            || o.Has("Copy Page link")
    );

    private static IEnumerable<FacebookLayoutDefinition> ComponentDefinitions()
    {
        yield return new(
            "settings.meta-birthday-details.v1",
            FacebookScreenKind.MetaBirthdayDetails,
            [],
            [
                Rule(
                    "meta-birthday-readonly",
                    "Read-only Birthday summary; Edit and age confirmation are never actions",
                    o => PFlowMetaAccountLayout.IsBirthday(o.Xml)
                ),
            ],
            [Authentication, SwitcherOverlay, ComposerOverlay],
            []
        );
        yield return new(
            "settings.meta-account-entry.v1",
            FacebookScreenKind.MetaAccountSettings,
            [],
            [
                Rule(
                    "meta-settings",
                    "Settings with Meta Account entry and read-only search",
                    o => PFlowMetaAccountLayout.IsSettings(o.Xml)
                ),
            ],
            [Authentication, SwitcherOverlay, ComposerOverlay],
            [FacebookControlRole.OpenMetaAccount]
        );
        yield return new(
            "settings.meta-account-home.v1",
            FacebookScreenKind.MetaAccount,
            [],
            [
                Rule(
                    "meta-account-home",
                    "Meta Account with information/permissions navigation",
                    o => PFlowMetaAccountLayout.IsHome(o.Xml)
                ),
            ],
            [Authentication, SwitcherOverlay, ComposerOverlay],
            [FacebookControlRole.PersonalInformation]
        );
        yield return new(
            "settings.meta-information-permissions.v1",
            FacebookScreenKind.PersonalInformationPermissions,
            [],
            [
                Rule(
                    "meta-information",
                    "Read-only information/permissions screen with Birthday row",
                    o => PFlowMetaAccountLayout.IsInformation(o.Xml)
                ),
            ],
            [Authentication, SwitcherOverlay, ComposerOverlay],
            []
        );
        yield return new(
            "dashboard.creator-welcome.v1",
            FacebookScreenKind.CreatorDashboardWelcome,
            [],
            [
                Rule(
                    "creator-welcome-sheet",
                    "Creator Dashboard welcome sheet with both onboarding choices",
                    o => PFlowCreatorDashboardWelcome.Sheet(o.Xml) != null
                ),
            ],
            [Authentication, SwitcherOverlay, ComposerOverlay],
            [FacebookControlRole.Dismiss]
        );
        foreach (var promotion in new[] { false, true })
        {
            var withPromotion = promotion;
            yield return new(
                withPromotion ? "profile.details-verified-offer.v1" : "profile.details-sheet.v1",
                FacebookScreenKind.ProfileDetails,
                [],
                [
                    Rule(
                        "name-details-sheet",
                        "Named information sheet with Transparency entry",
                        IsNameDetailsSheet
                    ),
                    Rule(
                        "name-details-target",
                        "Details sheet belongs to the requested display name",
                        o =>
                            string.IsNullOrWhiteSpace(o.Context.TargetName)
                            || (
                                o.Has(o.Context.TargetName)
                                && o.Has("About " + o.Context.TargetName)
                            )
                    ),
                    Rule(
                        "verification-offer-variant",
                        "Optional inline Meta verification offer",
                        o => HasNameDetailsVerificationOffer(o) == withPromotion
                    ),
                ],
                [Authentication, SwitcherOverlay, Chooser, ComposerOverlay],
                [FacebookControlRole.Transparency]
            );
        }
        yield return new(
            "feed.email-update.v1",
            FacebookScreenKind.EmailUpdatePrompt,
            [],
            [
                Rule(
                    "email-update-card",
                    "Scoped Meta email reminder with Accounts Center action",
                    o => PFlowEmailUpdatePrompt.Card(o.Xml) != null
                ),
            ],
            [Authentication, SwitcherOverlay, ComposerOverlay],
            [FacebookControlRole.Dismiss]
        );
        yield return new(
            "switcher.in-progress.v1",
            FacebookScreenKind.IdentitySwitching,
            [],
            [
                Rule(
                    "identity-switch-in-progress",
                    "Facebook switching loader with no actionable navigation",
                    o => PFlowIdentitySwitchLayout.IsSwitching(o.Xml)
                ),
            ],
            [],
            []
        );
        yield return new(
            "profile.settings-overview.v1",
            FacebookScreenKind.ProfileSettings,
            [new("profile-settings-title", "Profile Settings")],
            [
                Rule(
                    "settings-before-link",
                    "Personal settings before the link row becomes exposed",
                    o =>
                        !o.Has("Your profile link")
                        && !o.Has("Copy profile link")
                        && PFlowNativeAccountNavigator.FindControl(o.Xml, "Back") != null
                ),
            ],
            [Authentication, SwitcherOverlay, Chooser, PersonalSettingsExcluded],
            [FacebookControlRole.CopyProfileLink]
        );
        yield return new(
            "profile.page-tools.v1",
            FacebookScreenKind.OwnProfile,
            [],
            [
                Rule(
                    "named-page-tools",
                    "Requested Page name with explicit professional/admin tools",
                    o =>
                        !IsNameDetailsSheet(o)
                        && !HasWeeklyProgressCard(o)
                        && !o.Test(TargetName)
                        && !string.IsNullOrWhiteSpace(o.Context.TargetName)
                        && PFlowAutoPostService.ComposerMatchesTarget(o.Xml, o.Context.TargetName)
                        && (
                            o.Has("Professional dashboard")
                            || o.Has("Business Dashboard")
                            || o.Has("Creator Dashboard")
                            || o.Has("Manage Page")
                        )
                ),
            ],
            [
                Authentication,
                SwitcherOverlay,
                Chooser,
                ComposerOverlay,
                Rule(
                    "not-page-profile",
                    "Menu, editable, and visitor views cannot confirm a Page",
                    o =>
                        AtxAgentClient.IsMenuPageInBloks(o.Screen)
                        || PFlowUiPrivacy.ContainsInput(o.Xml)
                        || o.Has("Admin quick access")
                ),
            ],
            [FacebookControlRole.Dashboard]
        );
        yield return new(
            "profile.weekly-progress.v1",
            FacebookScreenKind.OwnProfile,
            [],
            [
                Rule(
                    "weekly-progress-card",
                    "Inline weekly progress tasks on an own profile",
                    HasWeeklyProgressCard
                ),
                Rule(
                    "weekly-progress-owner",
                    "Own-profile evidence and requested name",
                    o =>
                        o.Test(TargetName)
                        || (
                            !string.IsNullOrWhiteSpace(o.Context.TargetName)
                            && PFlowAutoPostService.ComposerMatchesTarget(
                                o.Xml,
                                o.Context.TargetName
                            )
                            && o.Has("Creator Dashboard")
                        )
                ),
            ],
            [
                Authentication,
                SwitcherOverlay,
                Chooser,
                ComposerOverlay,
                Rule(
                    "not-profile",
                    "Menu and visitor views are not own profiles",
                    o =>
                        IsNameDetailsSheet(o)
                        || AtxAgentClient.IsMenuPageInBloks(o.Screen)
                        || o.Has("Admin quick access")
                ),
            ],
            [FacebookControlRole.Dashboard]
        );
        yield return new(
            "profile.settings-copy.v1",
            FacebookScreenKind.ProfileSettings,
            [new("profile-settings-title", "Profile Settings")],
            [
                Rule(
                    "copy-profile-link-layout",
                    "Personal settings with a Copy profile link row",
                    o => !o.Has("Your profile link") && o.Has("Copy profile link")
                ),
            ],
            [Authentication, SwitcherOverlay, Chooser, PersonalSettingsExcluded],
            [FacebookControlRole.CopyProfileLink]
        );
        foreach (var variant in new[] { "dashboard", "professional", "header", "muse-sidebar" })
        {
            var captured = variant;
            yield return new(
                "menu." + variant + ".v1",
                FacebookScreenKind.Menu,
                [],
                [
                    Rule(
                        "menu-drawer",
                        "Menu drawer, not background Feed or a shortcut list",
                        o => AtxAgentClient.IsMenuPageInBloks(o.Screen)
                    ),
                    Rule(
                        "active-header",
                        "Unique active profile header with requested name when supplied",
                        o => MenuProfileTarget(o) != null
                    ),
                    Rule(
                        "menu-variant",
                        "Dashboard label variants with optional section headings",
                        o =>
                            captured == "muse-sidebar" ? HasMenuMuseCard(o)
                            : HasMenuMuseCard(o) ? false
                            : captured == "professional" ? o.Has("Professional dashboard")
                            : captured == "dashboard"
                                ? o.Has("Dashboard") && !o.Has("Professional dashboard")
                            : !o.Has("Dashboard") && !o.Has("Professional dashboard")
                    ),
                ],
                [Authentication, SwitcherOverlay, Chooser],
                [
                    FacebookControlRole.ActiveProfile,
                    FacebookControlRole.Dashboard,
                    FacebookControlRole.ExpandSection,
                ]
            );
        }
        yield return new(
            "android.recents-clear.v1",
            FacebookScreenKind.AndroidRecents,
            [],
            [
                Rule(
                    "android-clear-control",
                    "Unique enabled Android-owned Clear all with valid bounds",
                    o => RecentAppsClearTargetCore(o.Xml) != null
                ),
            ],
            [],
            [FacebookControlRole.ClearRecentApps]
        );
        yield return new(
            "share.destination-chooser.v1",
            FacebookScreenKind.ShareChooser,
            [],
            [
                Rule(
                    "destination-chooser",
                    "Supported explicit share destination chooser",
                    o =>
                        PFlowShareDestination.IsChooser(o.Xml)
                        && o.Nodes.All(n =>
                            string.IsNullOrEmpty((string?)n.Attribute("package"))
                            || (string?)n.Attribute("package")
                                is "android"
                                    or "com.android.intentresolver"
                                    or "com.android.systemui"
                                    or "com.facebook.katana"
                        )
                ),
            ],
            [Authentication],
            []
        );
        foreach (
            var kind in Enum.GetValues<PFlowLoginScreenKind>()
                .Where(k =>
                    k
                        is not (
                            PFlowLoginScreenKind.Unsupported
                            or PFlowLoginScreenKind.OwnPersonalProfile
                        )
                )
        )
        {
            var captured = kind;
            yield return new(
                "authentication." + kind.ToString().ToLowerInvariant() + ".v1",
                FacebookScreenKind.Authentication,
                [],
                [
                    Rule(
                        "authentication-kind",
                        "Supported authentication/onboarding layout; no identity proof",
                        o => PFlowLoginScreen.Classify(o.Xml) == captured
                    ),
                ],
                [],
                captured switch
                {
                    PFlowLoginScreenKind.ContactsAccessIntro => [FacebookControlRole.Skip],
                    PFlowLoginScreenKind.LanguageSuggestion =>
                    [
                        FacebookControlRole.ContinueInEnglish,
                    ],
                    _ => [],
                }
            );
        }
    }

    private static bool IsNameDetailsSheet(FacebookLayoutObservation o) =>
        o.Has("To keep Facebook safe, we're showing information about people and their profiles.")
        && o.Has("Transparency and privacy policy")
        && o.Nodes.Any(n =>
            new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(v =>
                v != null
                && v.StartsWith("About ", StringComparison.Ordinal)
                && v.Length > 6
                && o.Has(v[6..])
            )
        );

    private static bool HasNameDetailsVerificationOffer(FacebookLayoutObservation o) =>
        o.Nodes.Any(n =>
            new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(v =>
                v != null && v.StartsWith("Get verified with Meta", StringComparison.Ordinal)
            )
        );

    private static bool HasWeeklyProgressCard(FacebookLayoutObservation o) =>
        o.Nodes.Any(card =>
            card.Attribute("package")?.Value == "com.facebook.katana"
            && card.Attribute("class")?.Value == "android.view.ViewGroup"
            && HasVisibleCardLabel(card, "Weekly progress")
            && HasVisibleCardLabel(card, "View all")
            && (
                HasVisibleCardLabel(card, "Create 3 new public posts")
                || HasVisibleCardLabel(card, "Get 3 new followers")
                || HasVisibleCardLabel(card, "Create 3 new public posts with photos")
            )
        );

    private static bool HasMenuMuseCard(FacebookLayoutObservation o) =>
        o.Nodes.Any(card =>
            card.Attribute("package")?.Value == "com.facebook.katana"
            && card.Attribute("class")?.Value == "android.view.ViewGroup"
            && !card.AncestorsAndSelf("node")
                .Any(n =>
                    n.Attribute("visible-to-user")?.Value == "false"
                    || n.Attribute("enabled")?.Value == "false"
                )
            && HasVisibleCardLabel(card, "Muse")
            && HasVisibleCardLabel(card, "Get app")
            && HasVisibleCardLabel(card, "Meet Muse, your personal AI agent.")
        );

    private static bool HasVisibleCardLabel(XElement card, string label) =>
        card.Descendants("node")
            .Any(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a =>
                        a.Attribute("visible-to-user")?.Value == "false"
                        || a.Attribute("enabled")?.Value == "false"
                    )
                && new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(
                    value => string.Equals(value?.Trim(), label, StringComparison.Ordinal)
                )
            );

    private static BloksElement? MenuProfileTarget(FacebookLayoutObservation o)
    {
        var screen = o.Screen;
        var expectedName = o.Context.TargetName;
        // Some installed drawers repeat the active-profile accessibility label
        // inside Create Facebook Page. That action is not a second profile header.
        var actionChildren = screen
            .AllElements.Where(e =>
                e.Matches("Create Facebook Page", false)
                || e.Matches("Create Page", false)
                || e.Matches("Log into another account", false)
            )
            .SelectMany(e => e.Flatten())
            .ToHashSet();
        if (string.IsNullOrWhiteSpace(expectedName))
        {
            var names = screen
                .AllElements.Where(e => e.Enabled && !actionChildren.Contains(e))
                .Select(e => e.ContentDesc)
                .Where(label =>
                    label.EndsWith(", see your profile", StringComparison.OrdinalIgnoreCase)
                    || label.EndsWith(", Profile", StringComparison.OrdinalIgnoreCase)
                )
                .Select(label =>
                    label.EndsWith(", Profile", StringComparison.OrdinalIgnoreCase)
                        ? label[..^9]
                        : label[..^18]
                )
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            if (names.Length != 1)
                return null;
            expectedName = names[0];
        }
        var headers = screen
            .AllElements.Where(e =>
                e.Enabled
                && !actionChildren.Contains(e)
                && e.Width > 0
                && e.Height > 0
                && (
                    MatchesMenuName(e, expectedName + ", see your profile")
                    || MatchesMenuName(e, expectedName + ", Profile")
                )
            )
            .GroupBy(e => e.Bounds)
            .Select(g => g.First())
            .Take(2)
            .ToArray();
        if (headers.Length != 1)
            return null;
        var header = headers[0];
        var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(1280).Max();
        var targets = screen
            .AllElements.Where(e =>
                e.Enabled
                && !actionChildren.Contains(e)
                && e.Width > 0
                && e.Height > 0
                && e.CenterY < height / 2
                && MatchesMenuName(e, expectedName)
                && e.Bounds.Left >= header.Bounds.Left
                && e.Bounds.Right <= header.Bounds.Right
                && e.Bounds.Top >= header.Bounds.Top
                && e.Bounds.Bottom <= header.Bounds.Bottom
            )
            .GroupBy(e => (e.CenterX, e.CenterY))
            .Select(g => g.OrderBy(e => e.Width * e.Height).First())
            .Take(2)
            .ToArray();
        return targets.Length == 1 ? targets[0] : null;
    }

    private static bool MatchesMenuName(BloksElement element, string label) =>
        new[] { element.Text, element.ContentDesc }.Any(value =>
            string.Equals(
                Regex.Replace(Regex.Replace(value ?? "", @"\s+", " ").Trim(), @"\s+,", ","),
                Regex.Replace(Regex.Replace(label, @"\s+", " ").Trim(), @"\s+,", ","),
                StringComparison.OrdinalIgnoreCase
            )
        );

    private static IEnumerable<(int X, int Y)> FindComponentControls(
        FacebookLayoutObservation o,
        FacebookControlRole role,
        FacebookScreenKind screen
    )
    {
        if (screen == FacebookScreenKind.Menu && role == FacebookControlRole.ActiveProfile)
        {
            if (MenuProfileTarget(o) is { } target)
                yield return (target.CenterX, target.CenterY);
        }
        else if (screen == FacebookScreenKind.Menu && role == FacebookControlRole.ExpandSection)
        {
            // Section context is mandatory: do not conflate a Photos/Stories See all with Menu expansion.
            if (string.IsNullOrWhiteSpace(o.Context.SectionLabel))
                yield break;
            var headers = o
                .Screen.AllElements.Where(e =>
                    e.Enabled && MatchesMenuName(e, o.Context.SectionLabel)
                )
                .ToArray();
            if (headers.Length != 1)
                yield break;
            foreach (
                var node in o.Nodes.Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && new[]
                    {
                        (string?)n.Attribute("text"),
                        (string?)n.Attribute("content-desc"),
                    }.Any(o.Catalog.IsSectionExpansion)
                )
            )
                if (
                    UsableCenter(node) is { } point
                    && point.Y >= headers[0].Bounds.Top
                    && point.Y <= headers[0].Bounds.Bottom + 80
                )
                    yield return point;
        }
        else if (
            screen == FacebookScreenKind.ProfileSettings
            && role == FacebookControlRole.CopyProfileLink
        )
        {
            var point =
                VisibleCopyControl(o.Xml, "Copy profile link")
                ?? (o.Has("Your profile link") ? VisibleCopyControl(o.Xml, "Copy link") : null);
            if (point.HasValue)
                yield return point.Value;
        }
        else if (
            screen == FacebookScreenKind.AndroidRecents
            && role == FacebookControlRole.ClearRecentApps
        )
        {
            var point = RecentAppsClearPoint(RecentAppsClearTargetCore(o.Xml));
            if (point.HasValue)
                yield return point.Value;
        }
    }

    internal static XElement? FindRecentAppsClearTarget(string? xml) =>
        Match(xml, FacebookScreenKind.AndroidRecents).Control(FacebookControlRole.ClearRecentApps)
        != null
            ? RecentAppsClearTargetCore(xml)
            : null;

    private static (int X, int Y)? VisibleCopyControl(string xml, string label)
    {
        var point = PFlowNativeAccountNavigator.FindControl(xml, label);
        if (point == null)
            return null;
        var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var controls = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && (
                    string.Equals(
                        (string?)n.Attribute("text"),
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        (string?)n.Attribute("content-desc"),
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                && (string?)n.Attribute("enabled") == "true"
                && (string?)n.Attribute("clickable") == "true"
            );
        foreach (var node in controls)
        {
            if (
                node.AncestorsAndSelf()
                    .Any(n =>
                        (string?)n.Attribute("visible-to-user") == "false"
                        || (string?)n.Attribute("enabled") == "false"
                    )
            )
                return null;
            var bounds = Regex.Match(
                (string?)node.Attribute("bounds") ?? "",
                @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
            );
            if (!bounds.Success)
                return null;
            var left = int.Parse(bounds.Groups[1].Value);
            var top = int.Parse(bounds.Groups[2].Value);
            var right = int.Parse(bounds.Groups[3].Value);
            var bottom = int.Parse(bounds.Groups[4].Value);
            // Accessibility reports offscreen rows as tiny clipped rectangles.
            // Never tap a sliver along the Android gesture/navigation edge.
            if (
                right - left < 32
                || bottom - top < 32
                || left < 0
                || right > width
                || top < 0
                || bottom > height - 16
            )
                return null;
            foreach (var ancestor in node.Ancestors().Where(n => n.Attribute("bounds") != null))
            {
                var viewport = Regex.Match(
                    (string?)ancestor.Attribute("bounds") ?? "",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                );
                if (
                    !viewport.Success
                    || left < int.Parse(viewport.Groups[1].Value)
                    || top < int.Parse(viewport.Groups[2].Value)
                    || right > int.Parse(viewport.Groups[3].Value)
                    || bottom > int.Parse(viewport.Groups[4].Value)
                )
                    return null;
            }
        }
        return point;
    }

    private static XElement? RecentAppsClearTargetCore(string? xml)
    {
        var candidates = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(node =>
            {
                var package = (string?)node.Attribute("package") ?? "";
                if (
                    package != "com.android.systemui"
                    && !package.StartsWith("com.android.launcher", StringComparison.Ordinal)
                )
                    return false;
                if (
                    node.AncestorsAndSelf()
                        .Any(n =>
                            (string?)n.Attribute("enabled") == "false"
                            || (string?)n.Attribute("visible-to-user") == "false"
                        )
                )
                    return false;
                var label = new[]
                {
                    (string?)node.Attribute("text"),
                    (string?)node.Attribute("content-desc"),
                }.Any(value =>
                    Regex.IsMatch(
                        value?.Trim() ?? "",
                        @"^(?:clear|close|dismiss) all(?: apps)?$",
                        RegexOptions.IgnoreCase
                    )
                );
                var id = (string?)node.Attribute("resource-id") ?? "";
                if (
                    !label
                    && !Regex.IsMatch(
                        id,
                        @"/(?:clear_all|clear_all_button|dismiss_all|dismiss_all_button)$"
                    )
                )
                    return false;
                // LDPlayer Android 9 exposes this functional System UI button as non-clickable.
                // Scope the exception to its observed class, resource ID, and Recents container.
                var legacyButton =
                    label
                    && package == "com.android.systemui"
                    && id == "com.android.systemui:id/button"
                    && (string?)node.Attribute("class") == "android.widget.TextView"
                    && node.Ancestors()
                        .Any(n =>
                            (string?)n.Attribute("resource-id")
                                == "com.android.systemui:id/recents_view"
                            && (string?)n.Attribute("package") == "com.android.systemui"
                        );
                return (ScreenTimeHierarchy.Center(node).HasValue || legacyButton)
                    && RecentAppsClearPoint(node).HasValue;
            })
            .GroupBy(node => RecentAppsClearPoint(node)!.Value)
            .Select(group => group.First())
            .Take(2)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static (int X, int Y)? RecentAppsClearPoint(XElement? node)
    {
        if (
            node == null
            || !Regex.IsMatch((string?)node.Attribute("bounds") ?? "", @"^\[\d+,\d+\]\[\d+,\d+\]$")
        )
            return null;
        var values = Regex
            .Matches((string?)node.Attribute("bounds") ?? "", @"\d+")
            .Select(m => int.TryParse(m.Value, out var value) ? value : -1)
            .ToArray();
        if (
            values.Length != 4
            || values.Any(v => v < 0)
            || values[2] <= values[0]
            || values[3] <= values[1]
        )
            return null;
        foreach (var ancestor in node.Ancestors().Where(n => n.Attribute("bounds") != null))
        {
            var bounds = Regex
                .Matches((string?)ancestor.Attribute("bounds") ?? "", @"\d+")
                .Select(m => int.TryParse(m.Value, out var value) ? value : -1)
                .ToArray();
            if (
                bounds.Length != 4
                || bounds[0] > values[0]
                || bounds[1] > values[1]
                || bounds[2] < values[2]
                || bounds[3] < values[3]
            )
                return null;
        }
        return ((int)(((long)values[0] + values[2]) / 2), (int)(((long)values[1] + values[3]) / 2));
    }
}
