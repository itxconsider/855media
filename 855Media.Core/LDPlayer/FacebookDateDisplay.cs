using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

public static class FacebookDateDisplay
{
    public static string Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";
        var date = Regex.Replace(
            value.Trim(),
            @"^(?:Joined(?: Facebook)?|Created|Page created|Born on|Birthday)\s*:?\s*",
            "",
            RegexOptions.IgnoreCase
        );
        var culture = CultureInfo.InvariantCulture;
        // Missing date components remain unknown rather than becoming day 1.
        if (
            DateTime.TryParseExact(
                date,
                new[] { "MMMM yyyy", "MMM yyyy", "MM/yyyy", "yyyy-MM" },
                culture,
                DateTimeStyles.None,
                out var month
            )
        )
            return month.ToString("'--/'MM/yy", culture);
        if (Regex.IsMatch(date, @"^\d{4}$"))
            return "--/--/" + date[2..];
        if (
            DateTime.TryParseExact(
                date,
                new[]
                {
                    "MMMM d, yyyy",
                    "MMM d, yyyy",
                    "MMMM d yyyy",
                    "d MMMM yyyy",
                    "d MMM yyyy",
                    "yyyy-MM-dd",
                    "dd/MM/yyyy",
                    "d/M/yyyy",
                    "dd/MM/yy",
                    "d/M/yy",
                },
                culture,
                DateTimeStyles.None,
                out var full
            )
        )
            return full.ToString("dd/MM/yy", culture);
        return date;
    }
}
