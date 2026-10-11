using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Represents a parsed UI element from Meta's Bloks Native Shell hierarchy.
/// In Bloks, standard Android resource-ids are typically absent; elements are defined
/// by text, content-desc, accessibility traits, and pixel bounding boxes [x1,y1][x2,y2].
/// </summary>
public class BloksElement
{
    public string Text { get; set; } = string.Empty;
    public string ContentDesc { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public (int Left, int Top, int Right, int Bottom) Bounds { get; set; }
    public int CenterX => (Bounds.Left + Bounds.Right) / 2;
    public int CenterY => (Bounds.Top + Bounds.Bottom) / 2;
    public int Width => Math.Max(0, Bounds.Right - Bounds.Left);
    public int Height => Math.Max(0, Bounds.Bottom - Bounds.Top);
    public bool Clickable { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Scrollable { get; set; }
    public bool Selected { get; set; }
    public bool Checked { get; set; }
    public List<BloksElement> Children { get; set; } = [];

    public bool Matches(string query, bool partialMatch = true, bool caseInsensitive = true)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var comparison = caseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.IsNullOrEmpty(Text))
        {
            if (
                partialMatch ? Text.IndexOf(query, comparison) >= 0 : Text.Equals(query, comparison)
            )
                return true;
        }

        if (!string.IsNullOrEmpty(ContentDesc))
        {
            if (
                partialMatch
                    ? ContentDesc.IndexOf(query, comparison) >= 0
                    : ContentDesc.Equals(query, comparison)
            )
                return true;
        }

        if (!string.IsNullOrEmpty(ResourceId))
        {
            if (
                partialMatch
                    ? ResourceId.IndexOf(query, comparison) >= 0
                    : ResourceId.Equals(query, comparison)
            )
                return true;
        }

        return false;
    }

    public IEnumerable<BloksElement> Flatten()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var descendant in child.Flatten())
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>
/// Detailed inspection state of Meta's Bloks Native Shell screen on an emulator device.
/// </summary>
public class BloksScreenInfo
{
    public bool IsBloksActive { get; set; }
    public string CurrentActivity { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public List<BloksElement> RootElements { get; set; } = [];
    public List<BloksElement> AllElements { get; set; } = [];
    public List<string> VisibleTexts { get; set; } = [];
    public List<string> ContentDescriptions { get; set; } = [];
    public string? RawXml { get; set; }

    public BloksElement? FindElement(
        string query,
        bool partialMatch = true,
        bool caseInsensitive = true
    )
    {
        return AllElements.FirstOrDefault(e => e.Matches(query, partialMatch, caseInsensitive));
    }

    public List<BloksElement> FindAllElements(
        string query,
        bool partialMatch = true,
        bool caseInsensitive = true
    )
    {
        return AllElements.Where(e => e.Matches(query, partialMatch, caseInsensitive)).ToList();
    }

    /// <summary>
    /// In Bloks forms (e.g. Accounts Center / Personal details), a row consists of a label
    /// (e.g., "Contact info", "Birthday", "Email", "Name") and a neighboring element containing the value.
    /// This method finds the label element and extracts the most relevant value from siblings or proximate elements.
    /// </summary>
    public string? ExtractFieldValue(string fieldLabel)
    {
        var labelElem = FindElement(fieldLabel, partialMatch: true, caseInsensitive: true);
        if (labelElem == null)
            return null;

        // Look for other elements located immediately beneath or beside the label element
        var candidates = AllElements
            .Where(e => e != labelElem && !string.IsNullOrWhiteSpace(e.Text))
            .Where(e => !e.Matches(fieldLabel, partialMatch: true, caseInsensitive: true))
            .OrderBy(e =>
            {
                int dy = e.CenterY - labelElem.CenterY;
                int dx = Math.Abs(e.CenterX - labelElem.CenterX);
                int score = (dy >= 0 && dy < 150 ? dy : Math.Abs(dy) * 5) + dx;
                return score;
            })
            .ToList();

        return candidates.FirstOrDefault()?.Text?.Trim();
    }
}

/// <summary>
/// Account information extracted from Meta's Bloks Native Shell (Accounts Center, Profiles).
/// </summary>
public class BloksFacebookAccountInfo
{
    public string? UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Birthday { get; set; }
    public List<string> Profiles { get; set; } = [];
    public List<string> Pages { get; set; } = [];
    public bool AccountsCenterLoaded { get; set; }
}

/// <summary>
/// Well-known Bloks activity names, component identifiers, and deep link URI schemes for Meta Katana.
/// </summary>
public static class BloksConstants
{
    public const string KatanaPackage = "com.facebook.katana";

    // Known Bloks host activities in com.facebook.katana
    public const string BloksNativeShellActivity =
        "com.facebook.bloks.hosted.activity.BloksNativeShellActivity";
    public const string BloksHostedActivity =
        "com.facebook.bloks.hosted.activity.BloksHostedActivity";
    public const string BloksHostedFragmentActivity =
        "com.facebook.bloks.hosted.activity.BloksHostedFragmentActivity";
    public const string FbBloksActivity = "com.facebook.katana.activity.FbBloksActivity";
    public const string FbBloksNativeShellActivity =
        "com.facebook.bloks.facebook.nativeshell.FbBloksNativeShellActivity";
    public const string BloksScreenQueryActivity =
        "com.bloks.foa.screen.genericcontainer.BloksScreenQueryGenericContainerActivity";
    public const string BloksScreenQueryTransparentActivity =
        "com.bloks.foa.screen.genericcontainer.BloksScreenQueryGenericContainerTransparentActivity";

    // Candidate Bloks App IDs. The installed Facebook version must be checked
    // before using an internal query as a production data source.
    public const string AccountsCenterAppId = "com.bloks.www.fx.settings.modular.screen";
    public const string ProfilesAppId = "com.bloks.www.fx.settings.profiles";
    public const string PersonalDetailsAppId = "com.bloks.www.fx.settings.personal.details";
    public const string IdentityConfirmationAppId =
        "com.bloks.www.fx.settings.identity.confirmation";
    public const string PagesManagerAppId = "com.bloks.www.pages.management";
    public const string ReelsComposerAppId = "com.bloks.www.reels.creation";

    // Known Deep Link schemes that resolve into Bloks Native Shell
    public const string AccountsCenterDeepLink = "fb://accounts_center";
    public const string SwitchAccountDeepLink = "fb://switch_account";
    public const string SettingsDeepLink = "fb://settings";
    public const string ProfileDeepLink = "fb://profile";
    public const string PagesDeepLink = "fb://pages";
    public const string ProfessionalDashboardDeepLink = "fb://professional_dashboard";

    public static readonly string[] KnownBloksActivities =
    [
        BloksNativeShellActivity,
        BloksHostedActivity,
        BloksHostedFragmentActivity,
        FbBloksActivity,
        FbBloksNativeShellActivity,
        BloksScreenQueryActivity,
        BloksScreenQueryTransparentActivity,
        "BloksNativeShellActivity",
        "BloksHostedActivity",
        "BloksHostedFragmentActivity",
        "com.facebook.bloks",
    ];

    public static bool IsKnownBloksActivity(string? activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
            return false;
        return KnownBloksActivities.Any(k =>
            activityName.Contains(k, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static string CreateBloksScreenQueryUri(
        string appId,
        IReadOnlyDictionary<string, string>? serverParameters = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var payload = JsonSerializer.Serialize(
            new
            {
                server_params = serverParameters ?? new Dictionary<string, string>(),
                client_input_params = new Dictionary<string, string>(),
            }
        );
        return $"fb://bloks_screen_query?app_id={Uri.EscapeDataString(appId)}&bloks_screen_params={Uri.EscapeDataString(payload)}";
    }
}

public enum BloksQueryScreenState
{
    AwaitingContent,
    NotBloks,
    Unchanged,
    DemoShell,
    Error,
    Ready,
}

/// <summary>
/// Checks navigation only. Callers must still verify the requested identity and
/// destination fields before saving any data from an internal screen query.
/// </summary>
public static class BloksQueryScreenValidator
{
    public static BloksQueryScreenState Inspect(
        BloksScreenInfo? screen,
        BloksScreenInfo? previousScreen = null
    )
    {
        if (screen == null || string.IsNullOrWhiteSpace(screen.RawXml))
            return BloksQueryScreenState.AwaitingContent;

        var labels = ContentLabels(screen).ToList();
        if (labels.Count == 0)
            return BloksQueryScreenState.AwaitingContent;

        var examples = labels.Count(label =>
            label.Contains("Screen Query Example", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Screen Query With Preloaded", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Open Screen Query", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Company Design System", StringComparison.OrdinalIgnoreCase)
        );
        if (
            examples >= 2
            || (
                examples > 0
                && labels.Any(label =>
                    label.Equals("Bloks Native Shell", StringComparison.OrdinalIgnoreCase)
                )
            )
        )
            return BloksQueryScreenState.DemoShell;

        var failures = new[]
        {
            "Something went wrong",
            "Unable to load",
            "Couldn't load",
            "Could not load",
            "Query failed",
            "Unsupported query",
            "Invalid app_id",
            "Invalid app id",
        };
        if (
            labels.Any(label =>
                failures.Any(failure => label.Contains(failure, StringComparison.OrdinalIgnoreCase))
            )
        )
            return BloksQueryScreenState.Error;

        if (!screen.IsBloksActive)
            return BloksQueryScreenState.NotBloks;

        if (
            previousScreen != null
            && screen.CurrentActivity.Equals(
                previousScreen.CurrentActivity,
                StringComparison.Ordinal
            )
            && ContentLabels(screen)
                .SequenceEqual(ContentLabels(previousScreen), StringComparer.Ordinal)
        )
            return BloksQueryScreenState.Unchanged;

        // A title and navigation controls can appear before query content loads.
        // Loading placeholders must disappear before the caller inspects fields.
        var loadingLabels = new[]
        {
            "Loading",
            "Loading...",
            "Loading…",
            "Please wait",
            "Please wait...",
            "Please wait…",
        };
        var navigationLabels = new[] { "Back", "Navigate up", "Close", "More options" };
        if (
            labels.Any(label => loadingLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            || labels.Count(label =>
                !navigationLabels.Contains(label, StringComparer.OrdinalIgnoreCase)
            ) < 2
        )
            return BloksQueryScreenState.AwaitingContent;

        return BloksQueryScreenState.Ready;
    }

    private static IEnumerable<string> ContentLabels(BloksScreenInfo screen) =>
        screen
            .VisibleTexts.Concat(screen.ContentDescriptions)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => Regex.Replace(label.Trim(), @"\s+", " "))
            .Where(label => !Regex.IsMatch(label, @"^\d{1,2}:\d{2}$"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal);
}
