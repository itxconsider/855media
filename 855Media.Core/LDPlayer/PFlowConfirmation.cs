using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowUploadNotification(string Key, long Version, string Title, bool Ongoing)
{
    public bool Complete =>
        !Ongoing && Title.Equals("Facebook upload complete", StringComparison.OrdinalIgnoreCase);
}

public static class PFlowConfirmation
{
    internal static string NormalizeSuccessMessage(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ").TrimEnd('.', '!', ' ', '\u2026');

    internal static bool IsPublicationSuccess(string value, string type)
    {
        var noun =
            type == "Story" ? "story"
            : type == "Picture" ? "post"
            : "reel";
        var message = NormalizeSuccessMessage(value);
        return Regex.IsMatch(
            message,
            @"^(?:Your "
                + noun
                + @" (?:has been |was |is now )(?:successfully )?(?:shared|published|posted)|"
                + noun
                + @" (?:shared|published|posted)(?: successfully)?|Your "
                + noun
                + @" is (?:now )?live)$",
            RegexOptions.IgnoreCase
        );
    }

    // Leave the success banner/toast a short opportunity to arrive after upload completion.
    internal static TimeSpan DirectConfirmationGrace(TimeSpan window) =>
        TimeSpan.FromMilliseconds(Math.Min(8000, window.TotalMilliseconds / 4));

    internal static bool IsNotificationOnly(string evidence) =>
        evidence.StartsWith("Facebook media-upload notification:", StringComparison.Ordinal);

    internal static PFlowState RecordPublicationEvidence(PFlowJob job, string evidence)
    {
        job.Evidence = evidence;
        if (IsNotificationOnly(evidence))
        {
            job.ConfirmedUtc = null;
            job.Error =
                "Upload completed, but publication on the target was not verified. Review the Page before retrying; files were retained.";
            job.RemoteCleanupState = "Held for publication review";
            job.LocalDeleteState = "Held for publication review";
            return PFlowState.Unknown;
        }
        job.ConfirmedUtc = DateTime.UtcNow;
        return PFlowState.Confirmed;
    }

    public static bool HasActiveNotificationList(string? dump) =>
        dump != null && Regex.IsMatch(dump, @"(?m)^[ \t]*Notification List:[ \t]*\r?$");

    public static IReadOnlyList<PFlowUploadNotification> ReadUploadNotifications(
        string? dump,
        string package = "com.facebook.katana"
    )
    {
        if (string.IsNullOrWhiteSpace(dump))
            return [];
        var header = Regex.Match(dump, @"(?m)^[ \t]*Notification List:[ \t]*\r?$");
        if (!header.Success)
            return [];
        var section = dump[(header.Index + header.Length)..];
        var end = Regex.Match(section, @"(?m)^  \S");
        if (end.Success)
            section = section[..end.Index];
        var records = Regex.Matches(section, @"(?m)^\s*NotificationRecord\(");
        var result = new List<PFlowUploadNotification>();
        for (var i = 0; i < records.Count; i++)
        {
            var record = section[
                records[i].Index..(i + 1 < records.Count ? records[i + 1].Index : section.Length)
            ];
            if (!Regex.IsMatch(record, @"\bpkg=" + Regex.Escape(package) + @"(?:\s|$)"))
                continue;
            if (
                !record.Contains(
                    "channel=media_upload_notification_channel_id",
                    StringComparison.Ordinal
                )
            )
                continue;
            var key = Regex.Match(record, @"(?m)^\s*key=(\S+)").Groups[1].Value;
            var title = Regex
                .Match(record, @"(?m)^\s*android.title=String \(([^\r\n]*)\)\s*$")
                .Groups[1]
                .Value;
            long version = 0;
            foreach (Match time in Regex.Matches(record, @"\bm(?:Creation|Update)TimeMs=(\d+)"))
                if (long.TryParse(time.Groups[1].Value, out var timestamp))
                    version = Math.Max(version, timestamp);
            var flagsText = Regex.Match(record, @"\bflags=0x([0-9a-fA-F]+)").Groups[1].Value;
            var ongoing =
                long.TryParse(
                    flagsText,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var flags
                )
                && (flags & 2) != 0;
            if (key.Length > 0 && version > 0)
                result.Add(new(key, version, title.Trim(), ongoing));
        }
        return result;
    }

    public static string? NewUploadCompletion(
        IReadOnlyList<PFlowUploadNotification> before,
        IReadOnlyList<PFlowUploadNotification> after,
        bool composerClosed
    )
    {
        // Completion must belong to the only new upload after Share, with the composer exited.
        // If another upload was already active, notification-only attribution is ambiguous.
        if (!composerClosed || before.Any(n => n.Ongoing) || after.Any(n => n.Ongoing))
            return null;
        var fresh = after
            .Where(n =>
                n.Complete && !before.Any(old => old.Key == n.Key && old.Version >= n.Version)
            )
            .ToList();
        return fresh.Count == 1
            ? $"Facebook media-upload notification: {fresh[0].Title}; key={fresh[0].Key}; update={fresh[0].Version}; composer closed"
            : null;
    }
}
