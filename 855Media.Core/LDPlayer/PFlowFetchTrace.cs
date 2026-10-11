using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Small persistent trace for Facebook root-shell and UI discovery. Values such
/// as email addresses, long account IDs, and full dates are redacted before
/// writing so the log explains the route without duplicating account data.
/// </summary>
public static class PFlowFetchTrace
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private static readonly Regex EmailPattern = new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    private static readonly Regex LongNumberPattern = new(@"\b\d{8,}\b", RegexOptions.Compiled);
    private static readonly Regex FullDatePattern = new(
        @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{1,2},?\s+\d{4}\b|\b\d{1,2}\s+(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string LogFilePath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "855Media",
                "logs"
            );
            return Path.Combine(directory, "facebook-fetch.log");
        }
    }

    public static async Task WriteAsync(
        int deviceIndex,
        string step,
        string? detail = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var path = LogFilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var message = Redact(detail ?? string.Empty);
            var line =
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [LD-{deviceIndex}] {Redact(step)}"
                + (message.Length == 0 ? string.Empty : $" | {message}")
                + Environment.NewLine;

            await WriteGate.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(path, line, cancellationToken);
            }
            finally
            {
                WriteGate.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PFlowFetchTrace] Could not write trace: {ex.Message}");
        }
    }

    public static string SummarizeScreen(BloksScreenInfo? screen)
    {
        if (screen == null)
            return "screen=null";

        var labels = new[]
        {
            "login",
            "joined facebook",
            "birthday",
            "date of birth",
            "personal details",
            "professional dashboard",
            "edit profile",
            "add to story",
            "analytics",
            "page transparency",
            "date created",
            "followers",
            "views",
        };
        var allText = string.Join(" ", screen.VisibleTexts.Concat(screen.ContentDescriptions));
        var found = string.Join(
            ",",
            Array.FindAll(
                labels,
                label => allText.Contains(label, StringComparison.OrdinalIgnoreCase)
            )
        );

        return $"activity={screen.CurrentActivity}; elements={screen.AllElements.Count}; textNodes={screen.VisibleTexts.Count}; labels=[{found}]";
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var safe = EmailPattern.Replace(value, "[email]");
        safe = FullDatePattern.Replace(safe, "[date]");
        safe = LongNumberPattern.Replace(safe, "[id]");
        return safe.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
