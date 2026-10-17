using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public enum FacebookScreenKind
{
    Switcher,
    OwnProfile,
    ProfileSettings,
    Dashboard,
    Analytics,
    ReelComposer,
    PhotoComposer,
    NewsFeed,
    ReelsFeed,
    CreateMenu,
    StoryPicker,
    StoryAlbums,
    StoryEditor,
    OwnStoryViewer,
    Menu,
    AndroidRecents,
    ShareChooser,
    Authentication,
    PostActionBar,
    AppliedReaction,
    ReactionPicker,
    CommentSheet,
    PublishedComment,
    ReelPicker,
    ReelEditor,
    IdentitySwitching,
    EmailUpdatePrompt,
    ProfileDetails,
}

public enum FacebookLayoutStatus
{
    Matched,
    Unknown,
    Blocked,
    Ambiguous,
}

public enum FacebookControlRole
{
    Dismiss,
    Dashboard,
    Analytics,
    Publish,
    Caption,
    Create,
    Story,
    ActiveProfile,
    ExpandSection,
    CopyProfileLink,
    ClearRecentApps,
    Like,
    Comment,
    AppliedReaction,
    ReactionLike,
    ReactionLove,
    ReactionCare,
    ReactionHaha,
    ReactionWow,
    ReactionSad,
    ReactionAngry,
    CommentInput,
    SendComment,
    Reel,
    Continue,
    Skip,
    ContinueInEnglish,
    Transparency,
}

public sealed record FacebookLayoutContext(
    string? TargetName = null,
    string? TargetUid = null,
    string? PostType = null,
    string? SectionLabel = null,
    string? CommentText = null
);

// A match belongs to one captured hierarchy. Controls must not be reused after navigation.
public sealed class FacebookLayoutObservation
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Xml { get; }
    public BloksScreenInfo Screen => _screen.Value;
    public FacebookLayoutContext Context { get; }
    internal XElement[] Nodes => _nodes.Value;
    private readonly Lazy<BloksScreenInfo> _screen;
    private readonly Lazy<XElement[]> _nodes;
    internal BloksScreenInfo? ParsedScreen { get; }
    internal FacebookLayoutCatalog Catalog { get; }
    private readonly ConcurrentDictionary<FacebookLayoutRule, bool> _rules = new();

    internal bool Test(FacebookLayoutRule rule)
    {
        return _rules.GetOrAdd(rule, static (r, observation) => r.Test(observation), this);
    }

    private readonly Lazy<string?> _switcherVariant;
    private readonly Lazy<bool> _feedEvidence;
    private readonly Lazy<bool> _reelsEvidence;
    private readonly Lazy<bool> _scrolledFeed;
    private readonly Lazy<bool> _fullscreenReels;
    internal string? SwitcherVariant => _switcherVariant.Value;
    internal bool FeedEvidence => _feedEvidence.Value;
    internal bool ReelsEvidence => _reelsEvidence.Value;
    internal bool ScrolledFeed => _scrolledFeed.Value;
    internal bool FullscreenReels => _fullscreenReels.Value;

    internal bool Has(string label) =>
        Screen.AllElements.Any(e => e.Matches(label, partialMatch: false));

    public FacebookLayoutObservation(string? xml, FacebookLayoutContext? context = null)
        : this(xml, context, null) { }

    public static FacebookLayoutObservation FromScreen(
        BloksScreenInfo screen,
        FacebookLayoutContext? context = null
    ) => new(screen.RawXml, context, FacebookHierarchyCache.CanReuse(screen) ? screen : null);

    internal static FacebookLayoutObservation ForCatalog(
        string? xml,
        FacebookLayoutContext? context,
        FacebookLayoutCatalog catalog
    ) => new(xml, context, null, catalog);

    private FacebookLayoutObservation(
        string? xml,
        FacebookLayoutContext? context,
        BloksScreenInfo? parsed,
        FacebookLayoutCatalog? catalog = null
    )
    {
        Xml = xml ?? "";
        Catalog = catalog ?? FacebookLayoutRegistry.CaptureCatalog();
        Context = context ?? new();
        ParsedScreen = parsed;
        _screen = new(() => parsed ?? AtxAgentClient.ParseBloksHierarchy(Xml));
        _nodes = new(() => ScreenTimeHierarchy.Nodes(Xml));
        _switcherVariant = new(() => PFlowPersonaSwitcherService.ReadSwitcherLayoutVariant(Xml));
        _feedEvidence = new(() => ScreenTimeService.HasScreenEvidence(Xml, "Feed"));
        _reelsEvidence = new(() => ScreenTimeService.HasScreenEvidence(Xml, "Reels"));
        _scrolledFeed = new(() => ScreenTimeNavigation.IsScrolledFeed(Xml));
        _fullscreenReels = new(() => ScreenTimeNavigation.IsFullscreenReels(Xml));
    }
}

public sealed record FacebookLayoutRule(
    string Id,
    string Description,
    Func<FacebookLayoutObservation, bool> Test
);

public sealed record FacebookLabelRequirement(string Id, params string[] Alternatives);

public sealed record FacebookLayoutDefinition(
    string Id,
    FacebookScreenKind Screen,
    IReadOnlyList<FacebookLabelRequirement> RequiredLabels,
    IReadOnlyList<FacebookLayoutRule> RequiredStructure,
    IReadOnlyList<FacebookLayoutRule> Exclusions,
    IReadOnlyList<FacebookControlRole> ControlRoles
);

public sealed record FacebookLayoutEvaluation(
    string VariantId,
    FacebookScreenKind Screen,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> ExcludedBy
)
{
    public bool Matches => MissingEvidence.Count == 0 && ExcludedBy.Count == 0;
}

public sealed record FacebookLayoutControl(FacebookControlRole Role, int X, int Y);

// Screen recognition does not establish a specific post target for public actions.
public sealed record FacebookBrowseEvidence(bool InteractionReady, string? FeedViewport);

public sealed record FacebookLayoutMatch(
    Guid ObservationId,
    FacebookLayoutStatus Status,
    FacebookScreenKind? Screen,
    string? VariantId,
    FacebookLayoutContext Context,
    IReadOnlyList<FacebookLayoutControl> Controls,
    IReadOnlyList<FacebookLayoutEvaluation> Candidates,
    IReadOnlyList<string> MissingControls,
    FacebookBrowseEvidence? Browse = null
)
{
    public bool IsMatch => Status == FacebookLayoutStatus.Matched;

    public FacebookLayoutControl? Control(FacebookControlRole role) =>
        IsMatch ? Controls.SingleOrDefault(c => c.Role == role) : null;

    // Rule IDs only: no account names, UIDs, captions, or raw screen text in diagnostics.
    public string Diagnostic =>
        $"status={Status}; screen={Screen}; variant={VariantId ?? "none"}; "
        + $"controls={string.Join(",", Controls.Select(c => c.Role))}; missingControls={string.Join(",", MissingControls)}; "
        + string.Join(
            "; ",
            Candidates.Select(c =>
                $"{c.VariantId}:missing=[{string.Join(",", c.MissingEvidence)}],excluded=[{string.Join(",", c.ExcludedBy)}]"
            )
        );
}

/// <summary>Versioned C# layout definitions. Recognition grants no UID or publication proof.</summary>
public static partial class FacebookLayoutRegistry
{
    private static FacebookLayoutRule Rule(
        string id,
        string description,
        Func<FacebookLayoutObservation, bool> test
    ) => new(id, description, test);

    private static readonly FacebookLayoutRule Authentication = Rule(
        "authentication",
        "Account authentication or checkpoint screen",
        o => PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(o.Xml))
    );
    private static readonly FacebookLayoutRule Loading = Rule(
        "loading",
        "Visible progress indicator",
        o =>
            o.Screen.AllElements.Any(e =>
                e.ClassName.Contains("ProgressBar", StringComparison.OrdinalIgnoreCase)
            )
    );
    private static readonly FacebookLayoutRule SwitcherOverlay = Rule(
        "switcher-overlay",
        "An account switcher covers the screen",
        o => o.SwitcherVariant != null
    );
    private static readonly FacebookLayoutRule Chooser = Rule(
        "share-chooser",
        "Android/Facebook share destination chooser",
        o => PFlowShareDestination.IsChooser(o.Xml)
    );
    private static readonly FacebookLayoutRule ComposerOverlay = Rule(
        "composer",
        "A post or Reel editor is active",
        o => PFlowAutoPostService.HasComposerLayoutEvidence(o.Xml, "Reel") || o.Has("Create post")
    );
    private static readonly FacebookLayoutRule StoryOverlay = Rule(
        "story-overlay",
        "Story creation or album selection covers the Feed",
        o =>
            PFlowStoryLayout.IsCreateMenu(o)
            || PFlowStoryLayout.IsPicker(o)
            || PFlowStoryLayout.IsAlbums(o)
            || PFlowStoryLayout.IsEditor(o)
            || PFlowStoryLayout.IsOwnViewer(o)
    );
    private static readonly FacebookLayoutRule TargetName = Rule(
        "expected-profile",
        "Expected name and own-profile controls",
        o =>
            string.IsNullOrWhiteSpace(o.Context.TargetName)
                ? o.Has("Edit profile")
                    || o.Has("Edit profile picture")
                    || o.Has("Manage Page")
                    || o.Has("Edit Page")
                : LDPlayerService.HasExpectedOwnProfileEvidence(
                    o.Screen,
                    o.Context.TargetUid ?? "",
                    o.Context.TargetName
                )
    );

    internal static IReadOnlyList<FacebookLayoutDefinition> BuiltInDefinitions { get; } =
        Array.AsReadOnly(
            new[]
            {
                Switcher("switcher.typed.v1", "typed"),
                Switcher("switcher.untyped.v1", "untyped"),
                Switcher("switcher.footer.v1", "footer"),
                new FacebookLayoutDefinition(
                    "profile.own.v1",
                    FacebookScreenKind.OwnProfile,
                    [],
                    [TargetName],
                    [
                        Authentication,
                        SwitcherOverlay,
                        Chooser,
                        ComposerOverlay,
                        Rule(
                            "profile-details-overlay",
                            "Name-details sheet is not the underlying own profile",
                            IsNameDetailsSheet
                        ),
                        Rule(
                            "weekly-progress-variant",
                            "Weekly progress uses its own profile variant",
                            HasWeeklyProgressCard
                        ),
                        Rule(
                            "menu",
                            "Menu content is not an own profile",
                            o => AtxAgentClient.IsMenuPageInBloks(o.Screen)
                        ),
                        Rule(
                            "visitor-profile",
                            "Visitor/admin access view",
                            o =>
                                o.Screen.AllElements.Any(e =>
                                    e.Matches("Admin quick access", false)
                                    || e.Text.Contains(
                                        "Switch into",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                        ),
                    ],
                    [FacebookControlRole.Dashboard]
                ),
                new FacebookLayoutDefinition(
                    "profile.settings-link.v1",
                    FacebookScreenKind.ProfileSettings,
                    [
                        new("profile-settings-title", "Profile Settings"),
                        new("profile-link-label", "Your profile link"),
                    ],
                    [],
                    [Authentication, SwitcherOverlay, Chooser, PersonalSettingsExcluded],
                    [FacebookControlRole.CopyProfileLink]
                ),
                DashboardLayout("dashboard.analytics-tab.v1", "tab"),
                DashboardLayout("dashboard.analytics-section.v1", "section"),
                DashboardLayout("dashboard.analytics-dual-entry.v1", "dual"),
                Analytics("analytics.summary-cards.v1", true),
                Analytics("analytics.metrics.v1", false),
                Composer("composer.reel-settings.v1", FacebookScreenKind.ReelComposer, true),
                Composer("composer.reel-caption.v1", FacebookScreenKind.ReelComposer, false),
                BrowseLayout("feed.home.v1", FacebookScreenKind.NewsFeed, false),
                BrowseLayout("feed.stacked-create.v1", FacebookScreenKind.NewsFeed, false, true),
                BrowseLayout("feed.scrolled.v1", FacebookScreenKind.NewsFeed, true),
                BrowseLayout("reels.tabbed.v1", FacebookScreenKind.ReelsFeed, false),
                BrowseLayout("reels.fullscreen.v1", FacebookScreenKind.ReelsFeed, true),
                StoryLayout(
                    "create.menu.v1",
                    FacebookScreenKind.CreateMenu,
                    PFlowStoryLayout.IsCreateMenu,
                    FacebookControlRole.Story,
                    FacebookControlRole.Reel
                ),
                StoryLayout(
                    "reel.picker.v1",
                    FacebookScreenKind.ReelPicker,
                    PFlowReelLayout.IsPicker
                ),
                StoryLayout(
                    "reel.picker-tabbed.v1",
                    FacebookScreenKind.ReelPicker,
                    PFlowReelLayout.IsTabbedPicker
                ),
                StoryLayout(
                    "reel.editor.v1",
                    FacebookScreenKind.ReelEditor,
                    PFlowReelLayout.IsEditor,
                    FacebookControlRole.Continue
                ),
                StoryLayout(
                    "story.picker.v1",
                    FacebookScreenKind.StoryPicker,
                    PFlowStoryLayout.IsPicker
                ),
                StoryLayout(
                    "story.albums.v1",
                    FacebookScreenKind.StoryAlbums,
                    PFlowStoryLayout.IsAlbums
                ),
                StoryLayout(
                    "story.editor.v1",
                    FacebookScreenKind.StoryEditor,
                    PFlowStoryLayout.IsEditor,
                    FacebookControlRole.Publish
                ),
                StoryLayout(
                    "story.own-viewer.v1",
                    FacebookScreenKind.OwnStoryViewer,
                    PFlowStoryLayout.IsOwnViewer
                ),
                new FacebookLayoutDefinition(
                    "composer.photo.v1",
                    FacebookScreenKind.PhotoComposer,
                    [],
                    [
                        Rule(
                            "photo-composer",
                            "Photo composer labels",
                            o =>
                                PFlowAutoPostService.HasComposerLayoutEvidence(o.Xml, "Picture")
                                && (
                                    o.Has("Create post")
                                    || o.Nodes.Any(n =>
                                        (n.Attribute("class")?.Value ?? "").Contains(
                                            "EditText",
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                    )
                                )
                        ),
                    ],
                    [Authentication, Chooser, SwitcherOverlay, Loading],
                    [FacebookControlRole.Publish, FacebookControlRole.Caption]
                ),
            }
                .Concat(ComponentDefinitions())
                .Concat(InteractionDefinitions())
                .ToArray()
        );

    private static FacebookLayoutDefinition StoryLayout(
        string id,
        FacebookScreenKind screen,
        Func<FacebookLayoutObservation, bool> predicate,
        params FacebookControlRole[] controls
    ) =>
        new(
            id,
            screen,
            [],
            [Rule(id, "Observed Story navigation structure", predicate)],
            [
                Authentication,
                screen == FacebookScreenKind.OwnStoryViewer
                    ? Rule(
                        "loading",
                        "Loading outside the Story playback timer",
                        o =>
                            o.Nodes.Any(n =>
                                (n.Attribute("class")?.Value ?? "").Contains(
                                    "ProgressBar",
                                    StringComparison.Ordinal
                                ) && !PFlowStoryLayout.IsPlaybackProgress(n)
                            )
                    )
                    : Loading,
                SwitcherOverlay,
                Chooser,
                ComposerOverlay,
            ],
            controls
        );

    private static FacebookLayoutDefinition BrowseLayout(
        string id,
        FacebookScreenKind screen,
        bool alternate,
        bool stackedCreate = false
    ) =>
        new(
            id,
            screen,
            [],
            [
                Rule(
                    "browse-destination",
                    "Recognized destination with no conflicting tab or blocking overlay",
                    o => screen == FacebookScreenKind.NewsFeed ? o.FeedEvidence : o.ReelsEvidence
                ),
                Rule(
                    "browse-create-cards",
                    "Stacked story/reel creation cards are a distinct Feed variant",
                    o =>
                        screen != FacebookScreenKind.NewsFeed
                        || PFlowReelLayout.HasStackedCreateCards(o) == stackedCreate
                ),
                Rule(
                    "browse-variant",
                    "Home/scrolled Feed or tabbed/fullscreen Reels structure",
                    o =>
                        stackedCreate
                        || (
                            screen == FacebookScreenKind.NewsFeed
                                ? o.ScrolledFeed
                                : o.FullscreenReels
                        ) == alternate
                ),
            ],
            [
                Authentication,
                SwitcherOverlay,
                Chooser,
                ComposerOverlay,
                StoryOverlay,
                Rule(
                    "browse-loading",
                    "Destination is loading without recognized content",
                    o =>
                        Loading.Test(o)
                        && !(
                            screen == FacebookScreenKind.NewsFeed
                                ? o.ScrolledFeed
                                : o.FullscreenReels
                                    || ScreenTimeNavigation.ReelObservationKey(o.Xml) != null
                        )
                ),
            ],
            screen == FacebookScreenKind.NewsFeed
                ? [FacebookControlRole.Create, FacebookControlRole.Reel, FacebookControlRole.Story]
                : []
        );

    private static FacebookLayoutDefinition Switcher(string id, string variant) =>
        new(
            id,
            FacebookScreenKind.Switcher,
            [],
            [
                Rule(
                    "switcher-" + variant,
                    "Dismissible identity rows with supported selection and scroll structure",
                    o => o.SwitcherVariant == variant
                ),
            ],
            [Authentication, Chooser],
            [FacebookControlRole.Dismiss]
        );

    private static FacebookLayoutDefinition Analytics(string id, bool summaryCards) =>
        new(
            id,
            FacebookScreenKind.Analytics,
            [new("analytics-title", "Analytics")],
            [
                Rule(
                    "analytics-ready",
                    "Analytics title and loaded metrics/empty-state content",
                    o =>
                        LDPlayerService.HasAnalyticsContentEvidence(o.Screen)
                        && !DashboardHasHeader(o)
                ),
                Rule(
                    "analytics-variant",
                    "Summary cards or standard metrics layout",
                    o => o.Has("Recent content") == summaryCards
                ),
            ],
            [Authentication, Loading, SwitcherOverlay, Chooser, ComposerOverlay],
            []
        );

    private static FacebookLayoutDefinition Composer(
        string id,
        FacebookScreenKind screen,
        bool settings
    ) =>
        new(
            id,
            screen,
            [],
            [
                Rule(
                    "reel-composer",
                    "Reel caption or settings labels",
                    o => PFlowAutoPostService.HasComposerLayoutEvidence(o.Xml, "Reel")
                ),
                Rule(
                    "reel-variant",
                    "Settings or caption layout",
                    o => o.Has("Reel Settings") == settings
                ),
            ],
            [Authentication, Chooser, SwitcherOverlay, Loading],
            [FacebookControlRole.Publish, FacebookControlRole.Caption]
        );

    public static FacebookLayoutMatch Match(
        string? xml,
        FacebookScreenKind? expected = null,
        FacebookLayoutContext? context = null
    )
    {
        return MatchCatalog(CaptureCatalog(), xml, expected, context);
    }

    public static FacebookLayoutMatch MatchScreen(
        BloksScreenInfo screen,
        FacebookScreenKind? expected = null,
        FacebookLayoutContext? context = null
    ) => Match(FacebookLayoutObservation.FromScreen(screen, context), expected);

    public static FacebookLayoutMatch Match(
        FacebookLayoutObservation observation,
        FacebookScreenKind? expected = null
    ) =>
        Evaluate(
            observation,
            observation.Catalog.Definitions.Where(d =>
                expected == null ? !IsInteractionComponent(d.Screen) : d.Screen == expected
            )
        );

    internal static FacebookLayoutMatch Evaluate(
        FacebookLayoutObservation o,
        IEnumerable<FacebookLayoutDefinition> definitions
    )
    {
        using var scope = FacebookHierarchyCache.Enter();
        FacebookHierarchyCache.Seed(o.ParsedScreen);
        var definitionsArray = definitions.ToArray();
        var packages = o
            .Nodes.Select(n => n.Attribute("package")?.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();
        var foreign =
            packages.Length > 0
            && !packages.Contains("com.facebook.katana", StringComparer.Ordinal);
        var switching = PFlowIdentitySwitchLayout.IsSwitching(o.Xml);
        var evaluations = definitionsArray
            .Select(d => new FacebookLayoutEvaluation(
                d.Id,
                d.Screen,
                (
                    o.Nodes.Length == 0
                        ? new[] { "hierarchy-empty-or-invalid" }
                        : Array.Empty<string>()
                )
                    .Concat(
                        d.RequiredLabels.Where(l => !l.Alternatives.Any(o.Has)).Select(l => l.Id)
                    )
                    .Concat(d.RequiredStructure.Where(r => !o.Test(r)).Select(r => r.Id))
                    .ToArray(),
                (
                    foreign
                    && d.Screen
                        is not (
                            FacebookScreenKind.AndroidRecents
                            or FacebookScreenKind.ShareChooser
                        )
                        ? new[] { "foreign-app" }
                        : Array.Empty<string>()
                )
                    .Concat(d.Exclusions.Where(o.Test).Select(r => r.Id))
                    .Concat(
                        switching && d.Screen != FacebookScreenKind.IdentitySwitching
                            ? new[] { "identity-switch-in-progress" }
                            : Array.Empty<string>()
                    )
                    .ToArray()
            ))
            .ToArray();
        var activeVersions = evaluations
            .Where(e => e.Matches)
            .GroupBy(e => o.Catalog.FindOrigin(e.VariantId)?.BaseId ?? e.VariantId)
            .SelectMany(group =>
            {
                var version = group.Max(e => o.Catalog.FindOrigin(e.VariantId)?.Version ?? 0);
                return group.Where(e =>
                    (o.Catalog.FindOrigin(e.VariantId)?.Version ?? 0) == version
                );
            })
            .Select(e => e.VariantId)
            .ToHashSet(StringComparer.Ordinal);
        evaluations = evaluations
            .Select(e =>
                e.Matches && !activeVersions.Contains(e.VariantId)
                    ? e with
                    {
                        ExcludedBy = e
                            .ExcludedBy.Append("superseded-by-matching-version")
                            .ToArray(),
                    }
                    : e
            )
            .ToArray();
        var matched = evaluations.Where(e => e.Matches).ToArray();
        var winner =
            matched.Length == 1 ? definitionsArray.Single(d => d.Id == matched[0].VariantId) : null;
        var controls = new List<FacebookLayoutControl>();
        var missing = new List<string>();
        if (winner != null)
            foreach (var role in winner.ControlRoles)
            {
                var points = ConfiguredControls(o, role, winner).Distinct().ToArray();
                if (points.Length == 1)
                    controls.Add(new(role, points[0].X, points[0].Y));
                else
                    missing.Add(role + (points.Length == 0 ? ":unavailable" : ":ambiguous"));
            }
        var status =
            winner != null ? FacebookLayoutStatus.Matched
            : matched.Length > 1 ? FacebookLayoutStatus.Ambiguous
            : foreign || evaluations.Any(e => e.ExcludedBy.Contains("authentication"))
                ? FacebookLayoutStatus.Blocked
            : FacebookLayoutStatus.Unknown;
        return new(
            o.Id,
            status,
            winner?.Screen,
            winner?.Id,
            o.Context,
            controls.AsReadOnly(),
            Array.AsReadOnly(evaluations),
            missing.AsReadOnly(),
            winner?.Screen is FacebookScreenKind.NewsFeed or FacebookScreenKind.ReelsFeed
                ? new FacebookBrowseEvidence(
                    !ScreenTimeNavigation.IsBrowseOnlyCard(
                        o.Xml,
                        winner.Screen == FacebookScreenKind.NewsFeed ? "Feed" : "Reels"
                    ),
                    winner.Screen == FacebookScreenKind.NewsFeed
                        ? ScreenTimeNavigation.FeedViewportSignature(o.Xml)
                        : null
                )
                : null
        );
    }

    private static IEnumerable<(int X, int Y)> FindControls(
        FacebookLayoutObservation o,
        FacebookControlRole role,
        FacebookScreenKind screen
    )
    {
        if (screen == FacebookScreenKind.EmailUpdatePrompt)
        {
            if (
                role == FacebookControlRole.Dismiss
                && PFlowEmailUpdatePrompt.DismissControl(o.Xml) is { } control
            )
                yield return (control.X, control.Y);
            yield break;
        }
        if (IsInteractionComponent(screen))
        {
            foreach (var point in FindInteractionControls(o, role, screen))
                yield return point;
            yield break;
        }
        if (
            role
            is FacebookControlRole.ActiveProfile
                or FacebookControlRole.ExpandSection
                or FacebookControlRole.CopyProfileLink
                or FacebookControlRole.ClearRecentApps
        )
        {
            foreach (var point in FindComponentControls(o, role, screen))
                yield return point;
            yield break;
        }
        if (screen == FacebookScreenKind.NewsFeed && role == FacebookControlRole.Reel)
        {
            var entry = PFlowReelLayout.CreateEntry(o.Xml);
            if (entry != null && PFlowStoryLayout.ControlCenter(entry) is { } point)
                yield return point;
            yield break;
        }
        if (screen == FacebookScreenKind.Dashboard && role == FacebookControlRole.Analytics)
        {
            var routes = DashboardAnalyticsRoutes(o);
            var preferred = routes.Any(r => r.Tab) ? routes.Where(r => r.Tab) : routes;
            foreach (var route in preferred)
                yield return (route.X, route.Y);
            yield break;
        }
        if (role == FacebookControlRole.Caption)
        {
            var field = PFlowAutoPostService.FindPFlowCaptionField(o.Xml);
            if (field != null && UsableCenter(field) is { } center)
                yield return center;
            yield break;
        }
        var labels = role switch
        {
            FacebookControlRole.Dismiss => new[] { "Dismiss" },
            FacebookControlRole.Dashboard => new[]
            {
                "Professional dashboard",
                "Creator Dashboard",
                "Business Dashboard",
                "Professional tools",
                "Dashboard",
            },
            FacebookControlRole.Analytics => new[] { "Analytics" },
            FacebookControlRole.Transparency => new[] { "Transparency and privacy policy" },
            FacebookControlRole.Reel => new[] { "Reel", "Create reel" },
            FacebookControlRole.Continue => new[] { "Next", "Continue" },
            FacebookControlRole.Skip => new[] { "Skip" },
            FacebookControlRole.ContinueInEnglish => new[] { "Continue in English (US)" },
            FacebookControlRole.Create => new[]
            {
                "Create, Double tap to create a new post, story, or reel",
                "Create",
            },
            FacebookControlRole.Story when screen == FacebookScreenKind.NewsFeed => new[]
            {
                "Create story",
            },
            FacebookControlRole.Story => new[] { "Story" },
            FacebookControlRole.Publish when screen == FacebookScreenKind.StoryEditor => new[]
            {
                "Share",
            },
            FacebookControlRole.Publish when screen == FacebookScreenKind.PhotoComposer => new[]
            {
                "Post",
                "Publish",
            },
            FacebookControlRole.Publish => new[] { "Share now", "Share reel", "Share" },
            _ => Array.Empty<string>(),
        };
        foreach (
            var node in o.Nodes.Where(n =>
                n.Attribute("enabled")?.Value != "false"
                && n.Attribute("visible-to-user")?.Value != "false"
                && (
                    role is not (FacebookControlRole.Skip or FacebookControlRole.ContinueInEnglish)
                    || n.Attribute("package")?.Value == "com.facebook.katana"
                )
            )
        )
        {
            var matches = new[]
            {
                node.Attribute("text")?.Value,
                node.Attribute("content-desc")?.Value,
            }.Any(v =>
                v != null
                && labels.Any(label =>
                    string.Equals(v.Trim(), label, StringComparison.OrdinalIgnoreCase)
                    || role == FacebookControlRole.Analytics
                        && Regex.IsMatch(
                            v.Trim(),
                            @"^Analytics,\s*(?:tab\s*)?\d+\s+of\s+\d+$",
                            RegexOptions.IgnoreCase
                        )
                )
            );
            if (!matches)
                continue;
            var owner = node.AncestorsAndSelf("node")
                .FirstOrDefault(n => n.Attribute("clickable")?.Value == "true");
            if (
                owner == null
                || owner.Attribute("enabled")?.Value == "false"
                || owner.Attribute("visible-to-user")?.Value == "false"
            )
                continue;
            if (UsableCenter(node) is { } point)
                yield return point;
        }
    }

    internal static (int X, int Y)? UsableCenter(
        XElement node,
        bool storyPicker = false,
        bool storyTray = false,
        bool readOnlyList = false
    )
    {
        var point = ScreenTimeHierarchy.Center(node);
        if (point is not { X: > 0, Y: > 0 })
            return null;
        foreach (var ancestor in node.AncestorsAndSelf("node"))
        {
            if (
                ancestor.Attribute("enabled")?.Value == "false"
                    && !(
                        readOnlyList
                        && ancestor != node
                        && ancestor.Attribute("class")?.Value == "android.view.ViewGroup"
                        && ancestor.Attribute("clickable")?.Value == "false"
                        && ancestor
                            .Elements("node")
                            .Any(n =>
                                n.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            )
                        && node.Ancestors("node")
                            .Any(n =>
                                n.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            )
                    )
                    && !(
                        storyTray
                        && ancestor != node
                        && ancestor.Attribute("class")?.Value == "android.view.ViewGroup"
                        && ancestor.Attribute("clickable")?.Value == "false"
                        && ancestor
                            .Elements("node")
                            .Any(n =>
                                n.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            )
                        && ancestor
                            .Ancestors("node")
                            .Any(n => n.Attribute("content-desc")?.Value == "Story tray")
                    )
                    && !(
                        storyPicker
                        && ancestor != node
                        && ancestor.Attribute("class")?.Value == "android.view.ViewGroup"
                        && ancestor.Attribute("clickable")?.Value == "false"
                        && ancestor
                            .Elements("node")
                            .Any(n => n.Attribute("class")?.Value == "android.widget.GridView")
                    )
                || ancestor.Attribute("visible-to-user")?.Value == "false"
            )
                return null;
            // A hierarchy can retain offscreen controls inside a scroll container.
            var bounds = Regex.Match(
                ancestor.Attribute("bounds")?.Value ?? "",
                @"^\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]$"
            );
            if (!bounds.Success)
                continue;
            var values = new int[4];
            for (var i = 0; i < 4; i++)
                if (!int.TryParse(bounds.Groups[i + 1].Value, out values[i]))
                    return null;
            if (values[2] <= values[0] || values[3] <= values[1])
                continue;
            // The labelled child need not contain the centre of its clickable parent.
            if (ancestor == node && ancestor.Attribute("clickable")?.Value != "true")
                continue;
            if (
                point.Value.X < values[0]
                || point.Value.X >= values[2]
                || point.Value.Y < values[1]
                || point.Value.Y >= values[3]
            )
                return null;
        }
        return point;
    }
}
