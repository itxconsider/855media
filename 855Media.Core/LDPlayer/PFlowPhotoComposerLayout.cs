using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowPhotoComposerLayout
{
    internal static XElement? CaptionEditorDone(string xml)
    {
        if (
            !Safe(xml)
            || !Has(xml, "Add text")
            || Has(xml, "New post")
            || Has(xml, "Post settings")
        )
            return null;
        var fields = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                Visible(n)
                && PFlowUiPrivacy.IsEditableClass(n.Attribute("class")?.Value)
                && n.Attribute("enabled")?.Value != "false"
            )
            .ToArray();
        if (fields.Length != 1)
            return null;
        var done = Header(xml, "Done");
        return
            done?.Attribute("class")?.Value == "android.widget.Button"
            && FacebookLayoutRegistry.UsableCenter(done) != null
            ? done
            : null;
    }

    internal static bool Visible(XElement node) =>
        !node.AncestorsAndSelf().Any(n => n.Attribute("visible-to-user")?.Value == "false");

    private static bool Safe(string xml) =>
        ScreenTimeHierarchy.Nodes(xml).Any()
        && !ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                Visible(n)
                && (
                    n.Attribute("password")?.Value == "true"
                    || n.Attribute("package")?.Value is { Length: > 0 } p
                        && p != "com.facebook.katana"
                        && p != "com.android.systemui"
                )
            );

    private static bool Has(string xml, string label) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                Visible(n)
                && (
                    string.Equals(
                        n.Attribute("text")?.Value,
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        n.Attribute("content-desc")?.Value,
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );

    internal static XElement? CaptionField(string xml)
    {
        var fields = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                Visible(n)
                && n.Attribute("enabled")?.Value != "false"
                && (
                    n.Attribute("class")?.Value == "android.widget.EditText"
                    || n.Attribute("class")?.Value == "android.widget.AutoCompleteTextView"
                        && Has(xml, "New post")
                        && n.Attribute("clickable")?.Value == "true"
                )
                && n.Attribute("password")?.Value != "true"
                && (n.Attribute("package")?.Value is null or "" or "com.facebook.katana")
            )
            .Take(2)
            .ToArray();
        return fields.Length == 1 ? fields[0] : null;
    }

    internal static XElement? Header(string xml, string label, bool enabled = true)
    {
        var candidates = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                Visible(n)
                && (
                    string.Equals(
                        n.Attribute("text")?.Value,
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        n.Attribute("content-desc")?.Value,
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            .Select(n =>
                n.AncestorsAndSelf()
                    .FirstOrDefault(a => a.Attribute("clickable")?.Value == "true" && Visible(a))
            )
            .Where(n =>
                n != null
                && (
                    !enabled
                    || !n.AncestorsAndSelf().Any(a => a.Attribute("enabled")?.Value == "false")
                )
            )
            .Distinct()
            .Where(n =>
                ScreenTimeHierarchy.Center(n!) is { X: > 0, Y: > 0 } point
                && (
                    point.Y < 250
                    || label.Equals("Next", StringComparison.OrdinalIgnoreCase)
                        && Has(xml, "New post")
                        && CaptionField(xml) != null
                        && n!.Attribute("class")?.Value == "android.widget.Button"
                        && point.Y
                            > PFlowPersonaSwitcherService.ParseScreenDimensions(xml).Height * 3 / 4
                        && FacebookLayoutRegistry.UsableCenter(n) != null
                )
            )
            .Take(2)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static bool IsDraft(string xml) =>
        Safe(xml)
        && Has(xml, "New post")
        && !Has(xml, "Select multiple")
        && !Has(xml, "Post settings")
        && CaptionField(xml) != null
        && Header(xml, "Next", enabled: false) != null
        && (
            Has(xml, "Photo")
            || Has(xml, "Remove photo")
            || Has(xml, "Edit photo")
            || ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n =>
                    Visible(n)
                    && n.Attribute("class")?.Value == "android.widget.ImageView"
                    && System.Text.RegularExpressions.Regex.IsMatch(
                        n.Attribute("content-desc")?.Value ?? "",
                        @"^Photo [1-9]\d* of [1-9]\d*, expand photo$"
                    )
                )
        );

    internal static bool IsSettings(string xml) =>
        Safe(xml)
        && Has(xml, "Post settings")
        && !Has(xml, "New post")
        && !Has(xml, "Select multiple")
        && CaptionField(xml) == null
        && Header(xml, "Share", enabled: false) != null
        && new[]
        {
            "Post audience",
            "Who can comment",
            "Scheduling options",
            "Share to story",
            "Share to your profile",
            "Add AI label",
        }.Count(s => Has(xml, s)) >= 2;

    internal static XElement? ReviewPost(string xml)
    {
        var controls = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                Visible(n)
                && n.Attribute("class")?.Value == "android.widget.Button"
                && (
                    n.Attribute("content-desc")?.Value == "Post"
                    || n.Attribute("text")?.Value == "Post"
                )
                && n.Attribute("clickable")?.Value == "true"
                && FacebookLayoutRegistry.UsableCenter(n) is { Y: > 250 }
            )
            .Take(2)
            .ToArray();
        return controls.Length == 1 ? controls[0] : null;
    }

    internal static bool IsReview(string xml) =>
        Safe(xml)
        && Has(xml, "New post")
        && CaptionField(xml) == null
        && !Has(xml, "Select multiple")
        && !Has(xml, "Next")
        && Has(xml, "Just now")
        && ReviewPost(xml) != null
        && ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                Visible(n)
                && n.Attribute("class")?.Value
                    is "android.widget.ImageView"
                        or "android.widget.Button"
                && (
                    (n.Attribute("content-desc")?.Value ?? "") == "Photo"
                    || System.Text.RegularExpressions.Regex.IsMatch(
                        n.Attribute("content-desc")?.Value ?? "",
                        @"^Photo [1-9]\d* of [1-9]\d*(?:, expand photo)?$"
                    )
                )
            );

    internal static bool ReviewTargetMatches(string xml, string target) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                Visible(n)
                && n.Attribute("class")?.Value == "android.widget.Button"
                && PFlowNameMatch.Same(n.Attribute("text")?.Value ?? "", target)
                && FacebookLayoutRegistry.UsableCenter(n) is { Y: > 100 and < 350 }
            )
            .Take(2)
            .Count() == 1;

    internal static bool CaptionMatches(string xml, string caption)
    {
        if (CaptionField(xml) is not { } field)
            return false;
        if (!string.IsNullOrWhiteSpace(caption))
            return PFlowAutoPostService.CaptionTextMatches(field.ToString(), caption);
        var text = field.Attribute("text")?.Value ?? "";
        return string.IsNullOrWhiteSpace(text)
            || text.StartsWith("What's on your mind", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(
                "Say something about this photo",
                StringComparison.OrdinalIgnoreCase
            );
    }

    internal static bool ActorContradicts(string xml, string target) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(Visible)
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value ?? "", n.Attribute("content-desc")?.Value ?? "" }
            )
            .Any(s =>
                (
                    s.StartsWith("Posting as ", StringComparison.OrdinalIgnoreCase)
                    || s.StartsWith("Sharing as ", StringComparison.OrdinalIgnoreCase)
                ) && !PFlowNameMatch.Same(s[11..], target)
            );

    internal static bool DraftTargetMatches(string xml, string target) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                Visible(n)
                && !n.AncestorsAndSelf()
                    .Any(a => PFlowUiPrivacy.IsEditableClass(a.Attribute("class")?.Value))
            )
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value ?? "", n.Attribute("content-desc")?.Value ?? "" }
            )
            .Any(s =>
                PFlowNameMatch.Same(s, target)
                || PFlowNameMatch.Same(s, "Posting as " + target)
                || PFlowNameMatch.Same(s, "Sharing as " + target)
            );
}
