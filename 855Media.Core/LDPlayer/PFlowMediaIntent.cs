using System;
using System.IO;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

internal static class PFlowMediaIntent
{
    public static string? MimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null,
        };

    public static string? ShareCommand(string path, string package, bool image)
    {
        var mime = MimeType(path);
        if (
            mime == null
            || !mime.StartsWith(image ? "image/" : "video/", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(package)
        )
            return null;
        // Invoke the installed Android resolver explicitly so a saved Always
        // preference cannot route the payload straight into a different composer.
        return "am start -n android/com.android.internal.app.ResolverActivity -a android.intent.action.SEND -t "
            + PFlowAutoPostService.ShellQuote(mime)
            + " --eu android.intent.extra.STREAM "
            + PFlowAutoPostService.ShellQuote("file://" + path)
            + " -p "
            + PFlowAutoPostService.ShellQuote(package);
    }

    public static string ScanCommand(string path) =>
        "am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d "
        + PFlowAutoPostService.ShellQuote("file://" + path);

    private static bool HasError(string output) =>
        Regex.IsMatch(
            output,
            @"(?im)^\s*(?:Error\b|Exception\b)|SecurityException|Permission Denial|unable to resolve Intent|ActivityNotFoundException|does not exist"
        );

    public static bool ShareStarted(string output) =>
        !string.IsNullOrWhiteSpace(output)
        && !HasError(output)
        && Regex.IsMatch(output, @"(?im)^\s*(?:Starting:\s*Intent|Status:\s*ok\b)");

    // Broadcast completion acknowledges the command; publication still requires separate UI evidence.
    public static bool ScanAcknowledged(string output) =>
        !string.IsNullOrWhiteSpace(output)
        && !HasError(output)
        && Regex.IsMatch(output, @"(?im)^\s*Broadcast completed:\s*result=-?\d+\b");
}
