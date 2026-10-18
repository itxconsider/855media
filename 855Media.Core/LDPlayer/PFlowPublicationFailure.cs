using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

internal sealed record PFlowPublicationFailure(string Message, string Attribute)
{
    private static string Normalize(string value) =>
        Regex.Replace(value.Replace('\u2019', '\''), @"\s+", " ").Trim();

    internal static PFlowPublicationFailure? Read(
        string? xml,
        string caption = "",
        ISet<string>? before = null
    )
    {
        var old = before?.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var node in ScreenTimeHierarchy.Nodes(xml))
        {
            if (
                node.AncestorsAndSelf()
                    .Any(a =>
                        a.Attribute("visible-to-user")?.Value == "false"
                        || a.Attribute("password")?.Value == "true"
                        || (a.Attribute("class")?.Value ?? "").Contains(
                            "EditText",
                            StringComparison.Ordinal
                        )
                        || (
                            a.Attribute("package")?.Value is { Length: > 0 } package
                            && package != "com.facebook.katana"
                        )
                    )
            )
                continue;
            if (
                !node.AncestorsAndSelf()
                    .Any(a => a.Attribute("package")?.Value == "com.facebook.katana")
            )
                continue;
            if (
                node.Attribute("bounds") != null
                && PFlowProfilePublicationVerifier.Bounds(node) == null
            )
                continue;
            PFlowPublicationFailure? match = null;
            foreach (var attribute in new[] { "text", "content-desc" })
            {
                var original = node.Attribute(attribute)?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(original))
                    continue;
                var normalized = Normalize(original);
                if (
                    normalized.Equals(Normalize(caption), StringComparison.OrdinalIgnoreCase)
                    || old?.Contains(normalized) == true
                )
                    continue;
                if (
                    Regex.IsMatch(
                        normalized,
                        @"^(?:Couldn't (?:post|share(?: your)? story|upload(?: your)? (?:reel|story|post))|Upload failed|Unable to publish)(?=$|[\s.!:;…-])",
                        RegexOptions.IgnoreCase
                    )
                )
                    if (match == null || original.Length > match.Message.Length)
                        match = new(original, attribute);
            }
            if (match != null)
                return match;
        }
        return null;
    }

    internal void Record(PFlowJob job)
    {
        job.PublicationErrorMessage = Message;
        job.PublicationErrorAttribute = Attribute;
        job.Error = Message;
        job.Evidence = $"Facebook publication error ({Attribute}): {Message}";
        job.ConfirmedUtc = null;
        // This records Facebook's reported failure, not independent proof that
        // a submitted/background upload can never publish later.
        job.NoPublicationVerified = false;
        job.State = PFlowState.Failed;
        job.LocalDeleteState = "Held for publication review";
        job.RemoteCleanupState = "Held for publication review";
        PFlowPublicationTracking.Transition(
            job,
            PFlowPublicationStage.PublicationError,
            $"Facebook UI ({Attribute}): {Regex.Replace(Message, @"\s+", " ")}"
        );
        job.CurrentStep = "Failed: " + Regex.Replace(Message, @"\s+", " ");
    }
}
