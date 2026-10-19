using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal sealed record ScreenTimeReelReactionTarget(string Key, XElement Control, bool Applied)
{
    internal static ScreenTimeReelReactionTarget? Read(string? xml)
    {
        if (
            string.IsNullOrWhiteSpace(xml)
            || !ScreenTimeService.IsScreen(xml, "Reels")
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || ScreenTimeNavigation.IsReelsChaptersSheet(xml)
            || ScreenTimeNavigation.IsReelsInterruption(xml)
            || ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Reels")
        )
            return null;
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && !n.AncestorsAndSelf()
                    .Any(a =>
                        a.Attribute("visible-to-user")?.Value == "false"
                        || a.Attribute("class")?.Value == "android.widget.EditText"
                    )
            )
            .ToArray();
        var labels = nodes
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }
            )
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Regex.Replace(s!, @"\s+", " ").Trim())
            .Distinct()
            .ToArray();
        var authors = labels
            .Select(s =>
                Regex.Match(s, @"^View (.+)['’]s reels(?:, verified)?$", RegexOptions.IgnoreCase)
            )
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
        if (authors.Length != 1)
            return null;
        var (w, h) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var controls = nodes
            .Where(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("clickable")?.Value == "true"
                && n.Attribute("enabled")?.Value != "false"
                && Regex.IsMatch(
                    n.Attribute("content-desc")?.Value ?? "",
                    @"^\d[\d.,KMBkmb ]* reactions?$"
                )
            )
            .Select(n => (Node: n, Bounds: Bounds(n)))
            .Where(v => v.Bounds.HasValue)
            .ToArray();
        // Facebook exposes the icon and its count as adjacent buttons with the same label.
        // Only the upper button is a reaction control; the lower opens the reactions list.
        if (w <= 0 || h <= 0 || controls.Length != 2)
            return null;
        var ordered = controls.OrderBy(v => v.Bounds!.Value.Top).ToArray();
        var top = ordered[0];
        var bottom = ordered[1];
        var a = top.Bounds!.Value;
        var b = bottom.Bounds!.Value;
        if (
            top.Node.Attribute("content-desc")?.Value
                != bottom.Node.Attribute("content-desc")?.Value
            || a.Left < w * 0.7
            || a.Right > w
            || a.Top < 0
            || b.Bottom > h
            || a.Left != b.Left
            || a.Right != b.Right
            || b.Top - a.Bottom > 30
            || b.Top <= a.Top
            || a.Bottom - a.Top < 40
            || a.Bottom - a.Top <= b.Bottom - b.Top
        )
            return null;
        var content = ScreenTimeNavigation.ReelContentKey(xml) ?? "";
        var key =
            "reel-reaction:"
            + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(authors[0] + "|" + content))
            );
        return new(
            key,
            top.Node,
            top.Node.Attribute("selected")?.Value == "true"
                || top.Node.Attribute("checked")?.Value == "true"
        );
    }

    private static (int Left, int Top, int Right, int Bottom)? Bounds(XElement n)
    {
        var m = Regex.Match(
            n.Attribute("bounds")?.Value ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        if (!m.Success)
            return null;
        var v = Enumerable.Range(1, 4).Select(i => int.Parse(m.Groups[i].Value)).ToArray();
        return v[2] > v[0] && v[3] > v[1] ? (v[0], v[1], v[2], v[3]) : null;
    }
}
