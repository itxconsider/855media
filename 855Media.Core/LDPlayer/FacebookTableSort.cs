using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

public static class FacebookTableSort
{
    public static long CreationDateKey(string? value)
    {
        var display = FacebookDateDisplay.Format(value);
        var match = Regex.Match(display, @"^(?<d>\d{2}|--)/(?<m>\d{2}|--)/(?<y>\d{2})$");
        if (!match.Success)
            return -1;
        var yearMatch = Regex.Match(value ?? "", @"\b\d{4}\b");
        var year = yearMatch.Success
            ? int.Parse(yearMatch.Value)
            : 2000 + int.Parse(match.Groups["y"].Value);
        int Part(string key) => int.TryParse(match.Groups[key].Value, out var number) ? number : 0;
        return year * 10000L + Part("m") * 100L + Part("d");
    }

    public static long MediaCountKey(int available, int total) =>
        available < 0 || total < 0 ? -1 : ((long)available << 32) + (uint)total;
}
