using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>Presentation only. Original status remains the source of job decisions.</summary>
public static class PFlowStatusText
{
    public static string Summary(string? status)
    {
        var raw = (status ?? "").Trim();
        var step = raw;
        if (
            step.StartsWith("P-Flow [", StringComparison.OrdinalIgnoreCase)
            && step.IndexOf(']') is var end
            && end > 0
        )
            step = step[(end + 1)..].TrimStart(' ', ':');
        var failed =
            step.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
            || step.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
        string Short(string text) => failed ? "Failed · " + text : text;
        if (step.StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase))
            return "Published";
        if (step.StartsWith("Scheduled", StringComparison.OrdinalIgnoreCase))
            return "Scheduled";
        if (step.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            return "Needs review · check publication";
        if (step.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase))
            return "Cancelled";
        if (step.Contains("phone verification", StringComparison.OrdinalIgnoreCase))
            return "Blocked · verify phone";
        if (step.Contains("No selected account tasks", StringComparison.OrdinalIgnoreCase))
            return "Skipped · no account tasks";
        if (step.Contains("No verified target/file job", StringComparison.OrdinalIgnoreCase))
            return "Skipped · no ready posts";
        if (step.Contains("Daily capacity", StringComparison.OrdinalIgnoreCase))
            return "Skipped · daily limit";
        if (step.Contains("could not switch", StringComparison.OrdinalIgnoreCase))
            return Short("cannot switch account");
        if (step.Contains("name/type", StringComparison.OrdinalIgnoreCase))
            return Short("account or Page not matched");
        if (step.Contains("copyright scan", StringComparison.OrdinalIgnoreCase))
            return Short("copyright check");
        if (step.Contains("Refreshing Home", StringComparison.OrdinalIgnoreCase))
            return Short("returning to Feed");
        if (step.Contains("caption", StringComparison.OrdinalIgnoreCase))
            return Short("caption " + (failed ? "not ready" : "entry"));
        if (step.Contains("upload", StringComparison.OrdinalIgnoreCase))
            return Short(failed ? "upload needs attention" : "uploading / processing");
        if (step.StartsWith("All done", StringComparison.OrdinalIgnoreCase))
            return "Finished";
        // Keep unknown/new messages useful, bounded and lossless in Details.
        step = step.Replace('\r', ' ').Replace('\n', ' ');
        return step.Length <= 76 ? step : step[..73] + "…";
    }

    public static string NextStep(string? status)
    {
        var text = (status ?? "").Trim();
        if (text.StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase))
            return "Published. No retry is needed.";
        if (text.Contains("phone verification", StringComparison.OrdinalIgnoreCase))
            return "Complete Facebook's phone verification before retrying this target.";
        if (text.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            return "Check Facebook and record the outcome in History before retrying this post.";
        if (
            text.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
        )
            return "Read the full error below and check Facebook. Review History for any submitted post before retrying.";
        if (text.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase))
            return "Stopped. Review any submitted post in History before retrying.";
        if (text.Contains("No selected account tasks", StringComparison.OrdinalIgnoreCase))
            return "Enable the account's tasks and prepare posts for this device.";
        return "Follow the current step. Full messages below are kept for this app session.";
    }
}

/// <summary>Bounded in-memory UI trail; durable posting evidence stays in History.</summary>
public sealed class PFlowStatusTrail
{
    private readonly Queue<string> _entries = new();

    public void Record(string status)
    {
        _entries.Enqueue($"{DateTimeOffset.Now:HH:mm:ss zzz}  {status}");
        while (_entries.Count > 20)
            _entries.Dequeue();
    }

    public string Details(string status) =>
        PFlowStatusText.NextStep(status)
        + "\n\nCurrent message\n"
        + status
        + "\n\nRecent steps · this app session · newest first\n"
        + string.Join("\n", _entries.Reverse());
}
