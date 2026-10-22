using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// UI corroborates a fresh Graph-bound session; names never establish UID ownership.
public static class PFlowSessionProfileUi
{
    private static string Normal(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    internal static bool SameName(string first, string second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && Normal(first).Equals(Normal(second), StringComparison.OrdinalIgnoreCase);

    public static bool UnambiguousMapping(
        FacebookPageTarget target,
        IEnumerable<FacebookPageTarget> accountTargets
    ) =>
        !string.IsNullOrWhiteSpace(target.PageName)
        && accountTargets
            .Where(t => t.UserUid == target.UserUid && SameName(t.PageName, target.PageName))
            .All(t =>
                t.PageId == target.PageId
                && t.PersonaUid == target.PersonaUid
                && t.IsPrimaryProfile == target.IsPrimaryProfile
            );

    private static bool Visible(XElement node) =>
        !node.AncestorsAndSelf()
            .Any(n =>
                (string?)n.Attribute("visible-to-user") == "false"
                || (string?)n.Attribute("enabled") == "false"
            );

    private static string[] Labels(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(Visible)
            .Where(n => (string?)n.Attribute("package") == "com.facebook.katana")
            .SelectMany(n =>
                new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
            )
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Normal(s!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static bool Safe(string xml) =>
        PFlowProfilePublicationVerifier.Safe(xml)
        && !Labels(xml)
            .Any(s =>
                s.Equals("Page Settings", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Profile settings", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Admin quick access", StringComparison.OrdinalIgnoreCase)
            )
        && !PFlowAutoPostService.HasComposerLayoutEvidence(xml, "Reel")
        && !PFlowConfirmation.HasPendingPublication(xml);

    internal static bool Contradicts(string xml, FacebookPageTarget target)
    {
        var labels = Labels(xml);
        foreach (var label in labels)
        {
            foreach (var prefix in new[] { "Posting as ", "Sharing as " })
                if (
                    label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && !SameName(label[prefix.Length..], target.PageName)
                )
                    return true;
            const string suffix = ", see your profile";
            if (
                label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && !SameName(label[..^suffix.Length], target.PageName)
            )
                return true;
        }
        // An already visible own profile for another actor must not be navigated past.
        var ownControls = labels.Any(s =>
            s.Equals("Edit profile", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Business Dashboard", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Creator Dashboard", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Professional dashboard", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Manage Page", StringComparison.OrdinalIgnoreCase)
        );
        var profilePicture = labels.Contains("Profile picture", StringComparer.OrdinalIgnoreCase);
        return profilePicture
            && (ownControls || labels.Contains("Cover Photo", StringComparer.OrdinalIgnoreCase))
            && (!labels.Any(s => SameName(s, target.PageName)) || !HasOwnControls(labels, target));
    }

    private static bool HasOwnControls(string[] labels, FacebookPageTarget target) =>
        target.IsPrimaryProfile
            ? labels.Contains("Edit profile", StringComparer.OrdinalIgnoreCase)
            : labels.Any(s =>
                s.Equals("Professional dashboard", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Business Dashboard", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Creator Dashboard", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Manage Page", StringComparison.OrdinalIgnoreCase)
            );

    internal static bool Confirms(string xml, FacebookPageTarget target)
    {
        if (!Safe(xml) || Contradicts(xml, target))
            return false;
        var labels = Labels(xml);
        if (!labels.Any(s => SameName(s, target.PageName)))
            return false;
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        if (AtxAgentClient.IsMenuPageInBloks(screen))
            return false;
        return HasOwnControls(labels, target)
            && (
                target.IsPrimaryProfile
                    ? PFlowNativeAccountNavigator.OwnProfile(xml)
                    : PFlowOwnProfileNavigation.IsOwnPageProfile(screen, target.PageName)
            );
    }
}
