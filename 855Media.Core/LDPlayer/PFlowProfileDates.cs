using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

/// <summary>Reads dates from profile sections, never arbitrary post or friend-card text.</summary>
internal static class PFlowProfileDates
{
    private const string Month =
        @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";
    private const string FullDate =
        @"(?:MONTH\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+MONTH\s+\d{4}|\d{4}-\d{1,2}-\d{1,2}|\d{1,2}/\d{1,2}/\d{4})";

    internal static bool HasFullDate(string? value) => !string.IsNullOrEmpty(Date(value));

    private static string Date(string? value)
    {
        var match = Regex.Match(
            value ?? "",
            FullDate.Replace("MONTH", Month),
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
            return string.Empty;
        return
            DateTime.TryParse(
                match.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var date
            )
            && date.Year >= 1900
            && date.Date <= DateTime.Today
            ? match.Value.Trim()
            : string.Empty;
    }

    private static string Label(BloksElement element) =>
        (string.IsNullOrWhiteSpace(element.Text) ? element.ContentDesc : element.Text).Trim();

    internal static bool MatchesName(BloksScreenInfo screen, string name) =>
        !string.IsNullOrWhiteSpace(name)
        && screen.AllElements.Any(e =>
            e.Text.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
            || e.ContentDesc.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
        );

    internal static string Birthdate(BloksScreenInfo screen, bool confirmedProfileContext = false)
    {
        foreach (
            var header in screen.AllElements.Where(e =>
                e.Matches("Personal details", false) || e.Matches("At a glance", false)
            )
        )
        {
            var bottom = screen
                .AllElements.Where(e =>
                    e.Bounds.Top > header.Bounds.Top
                    && new[]
                    {
                        "Friends",
                        "All posts",
                        "Posts",
                        "Photos",
                        "Featured",
                        "People you may know",
                    }.Any(label => e.Matches(label, false))
                )
                .Select(e => e.Bounds.Top)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            var candidates = screen
                .AllElements.Where(e => e.CenterY > header.CenterY && e.CenterY < bottom)
                .Select(Label)
                .Where(label =>
                    Regex.IsMatch(
                        label,
                        @"^(?:Born\s+|Birthday\s*[:,]?\s*|Date of birth\s*[:,]?\s*)?"
                            + FullDate.Replace("MONTH", Month)
                            + @"$",
                        RegexOptions.IgnoreCase
                    )
                )
                .Select(Date)
                .Where(date => date.Length > 0)
                .Distinct()
                .ToArray();
            if (candidates.Length == 1)
                return candidates[0];
        }
        // A scroll can clip the At a glance heading while its explicit Born row
        // remains above Friends. Only a previously confirmed own profile may
        // use that continuation, and never past Posts or suggestion cards.
        if (confirmedProfileContext)
        {
            var friendsTop = screen
                .AllElements.Where(e => e.Matches("Friends", false))
                .Select(e => e.Bounds.Top)
                .DefaultIfEmpty(-1)
                .Min();
            foreach (
                var row in screen.AllElements.Where(e =>
                    e.Bounds.Top < friendsTop
                    && Regex.IsMatch(
                        Label(e),
                        @"^Born\s+" + FullDate.Replace("MONTH", Month) + "$",
                        RegexOptions.IgnoreCase
                    )
                )
            )
            {
                if (
                    !screen.AllElements.Any(e =>
                        e.Bounds.Top < row.Bounds.Top
                        && new[] { "All posts", "Posts", "People you may know" }.Any(label =>
                            e.Matches(label, false)
                        )
                    )
                )
                    return Date(Label(row));
            }
        }
        return string.Empty;
    }

    internal static string CreationDate(
        BloksScreenInfo screen,
        bool profile,
        string expectedName,
        bool confirmedTransparencyContext = false
    )
    {
        var title = profile ? "Profile transparency" : "Page transparency";
        if (
            !screen.AllElements.Any(e => e.Matches(title, false))
            || (!confirmedTransparencyContext && !MatchesName(screen, expectedName))
        )
            return string.Empty;
        foreach (
            var row in screen.AllElements.Where(e =>
                Regex.IsMatch(
                    Label(e),
                    profile
                        ? @"^Profile created(?:\s*[:,]\s*.*)?$"
                        : @"^(?:Page created|Created(?:\s*[-–:]\s*.*)?)$",
                    RegexOptions.IgnoreCase
                )
            )
        )
        {
            var inline = Date(Label(row));
            if (inline.Length > 0)
                return inline;
            // Facebook's History row renders its date immediately below the event.
            // Stop at the next event, so name changes and Pro mode dates cannot leak in.
            var nextEvent = screen
                .AllElements.Where(e =>
                    e.Bounds.Top >= row.Bounds.Bottom
                    && Regex.IsMatch(
                        Label(e),
                        @"^(?:Changed name|Professional mode|Ads from|People who manage|Profile created|Created\b)",
                        RegexOptions.IgnoreCase
                    )
                )
                .Select(e => e.Bounds.Top)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            var dates = screen
                .AllElements.Where(e =>
                    e.Bounds.Top >= row.Bounds.Bottom
                    && e.Bounds.Top < Math.Min(nextEvent, row.Bounds.Bottom + 100)
                    && Math.Abs(e.Bounds.Left - row.Bounds.Left) <= 32
                )
                .Select(Label)
                .Where(label =>
                    Regex.IsMatch(
                        label,
                        "^" + FullDate.Replace("MONTH", Month) + "$",
                        RegexOptions.IgnoreCase
                    )
                )
                .Select(Date)
                .Where(date => date.Length > 0)
                .Distinct()
                .ToArray();
            if (dates.Length == 1)
                return dates[0];
        }
        return string.Empty;
    }
}
