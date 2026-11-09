using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowPhotoGalleryEntry
{
    private static bool Label(XElement n, string text) =>
        string.Equals(n.Attribute("text")?.Value, text, StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            n.Attribute("content-desc")?.Value,
            text,
            StringComparison.OrdinalIgnoreCase
        );

    // Facebook marks only this noninteractive category wrapper disabled, while
    // its individual Gallery/GIF/Live buttons remain explicitly enabled.
    private static bool CategoryWrapper(XElement n) =>
        CompactCategoryWrapper(n)
        || n.Attribute("class")?.Value == "android.view.ViewGroup"
            && n.Attribute("clickable")?.Value == "false"
            && (
                Label(n, "Add to your post")
                || string.IsNullOrEmpty(n.Attribute("content-desc")?.Value)
                    && string.IsNullOrEmpty(n.Attribute("text")?.Value)
                    && n.Elements("node")
                        .Any(c =>
                            c.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            && c.Attribute("bounds")?.Value == n.Attribute("bounds")?.Value
                        )
            )
            && n.Parent is { } parent
            && (
                parent.Attribute("class")?.Value == "android.widget.AbsListView"
                    && Label(parent, "Add to your post")
                || parent.Attribute("class")?.Value == "android.view.ViewGroup"
                    && parent.Attribute("clickable")?.Value == "false"
                    && parent.Attribute("enabled")?.Value == "true"
                    && new[] { "Gallery", "GIF", "Live" }.All(label =>
                        n.Descendants("node")
                            .Any(c =>
                                c.Attribute("class")?.Value == "android.widget.Button"
                                && Label(c, label)
                                && c.Attribute("enabled")?.Value == "true"
                                && c.Attribute("clickable")?.Value == "true"
                            )
                    )
            )
            && n.Attribute("bounds")?.Value is { Length: > 0 } bounds
            && bounds == parent.Attribute("bounds")?.Value;

    // New post's compact icon bar has a narrower RecyclerView than its parent
    // footer. Only its passive wrapper is disabled; individual actions retain
    // their own enabled state (Live may be disabled).
    private static bool CompactCategoryWrapper(XElement n) =>
        n.Attribute("class")?.Value == "android.view.ViewGroup"
        && n.Attribute("package")?.Value == "com.facebook.katana"
        && n.Attribute("clickable")?.Value == "false"
        && string.IsNullOrEmpty(n.Attribute("text")?.Value)
        && string.IsNullOrEmpty(n.Attribute("content-desc")?.Value)
        && n.Elements("node").Count() == 1
        && n.Elements("node").Single() is { } list
        && list.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
        && list.Attribute("bounds")?.Value == n.Attribute("bounds")?.Value
        && list.Attribute("enabled")?.Value == "true"
        && new[] { "Gallery", "AI images", "GIF", "Life update", "Live" }.All(label =>
            list.Elements("node")
                .Count(c =>
                    c.Attribute("class")?.Value == "android.widget.Button" && Label(c, label)
                ) == 1
        )
        && n.Parent is { } parent
        && parent.Attribute("class")?.Value == "android.view.ViewGroup"
        && parent.Attribute("enabled")?.Value == "true"
        && parent.Attribute("clickable")?.Value == "false";

    internal static (int X, int Y)? GalleryPoint(string xml)
    {
        var buttons = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && Label(n, "Gallery")
                && n.Attribute("enabled")?.Value == "true"
                && n.Attribute("clickable")?.Value == "true"
                && PFlowPhotoComposerLayout.Visible(n)
            )
            .Take(2)
            .ToArray();
        if (buttons.Length != 1)
            return null;
        var button = buttons[0];
        if (
            button
                .Ancestors()
                .Any(n => n.Attribute("enabled")?.Value == "false" && !CategoryWrapper(n))
        )
            return null;
        // Validate the entire clipping chain in a read-only clone. Never enable
        // an actual Android control or relax the shared selector's disabled guard.
        var root = new XElement(button.AncestorsAndSelf().Last());
        var copies = root.DescendantsAndSelf("node")
            .Where(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && Label(n, "Gallery")
                && PFlowPhotoComposerLayout.Visible(n)
                && n.Attribute("bounds")?.Value == button.Attribute("bounds")?.Value
            )
            .Take(2)
            .ToArray();
        if (copies.Length != 1)
            return null;
        var copy = copies[0];
        foreach (
            var wrapper in copy.Ancestors()
                .Where(n => n.Attribute("enabled")?.Value == "false" && CategoryWrapper(n))
        )
            wrapper.SetAttributeValue("enabled", "true");
        return FacebookLayoutRegistry.UsableCenter(copy);
    }

    // A modern New post can expose its caption before its Gallery category loads.
    // Only an actual legacy Create post with a usable media action may take this route.
    internal static XElement? LegacyMediaControl(string xml, string target)
    {
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(PFlowPhotoComposerLayout.Visible)
            .ToArray();
        if (
            nodes.Any(n => Label(n, "New post"))
            || !nodes.Any(n => Label(n, "Create post"))
            || !PFlowAutoPostService.IsComposer(xml, "Picture")
            || !PFlowPhotoComposerLayout.CaptionMatches(xml, "")
            || !PFlowPhotoComposerLayout.DraftTargetMatches(xml, target)
            || PFlowPhotoComposerLayout.ActorContradicts(xml, target)
        )
            return null;
        return PFlowPhotoGroupLayout.Control(xml, "Photo/video", "Photo");
    }

    internal static bool IsEmptyEntry(string xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            !nodes.Any()
            || nodes.Any(n =>
                PFlowPhotoComposerLayout.Visible(n)
                && (
                    n.Attribute("password")?.Value == "true"
                    || n.Attribute("package")?.Value is { Length: > 0 } p
                        && p != "com.facebook.katana"
                        && p != "com.android.systemui"
                )
            )
        )
            return false;
        if (
            !nodes.Any(n => PFlowPhotoComposerLayout.Visible(n) && Label(n, "New post"))
            || nodes.Any(n =>
                PFlowPhotoComposerLayout.Visible(n)
                && (
                    Label(n, "Select multiple")
                    || Label(n, "Remove photo")
                    || Label(n, "Edit photo")
                    || n.Attribute("class")?.Value == "android.widget.ImageView"
                        && Label(n, "Photo")
                )
            )
        )
            return false;
        return PFlowPhotoComposerLayout.CaptionMatches(xml, "") && GalleryPoint(xml) != null;
    }
}
