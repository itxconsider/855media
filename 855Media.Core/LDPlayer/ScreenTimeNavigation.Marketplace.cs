using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static partial class ScreenTimeNavigation
{
    private static bool IsMarketplaceVisibleNode(XElement node) =>
        node.Attribute("package")?.Value == "com.facebook.katana"
        && node.AncestorsAndSelf("node").All(n => n.Attribute("visible-to-user")?.Value != "false");

    internal static bool IsMarketplaceAgeGate(string? xml)
    {
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        )
            return false;
        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(IsMarketplaceVisibleNode)
            .SelectMany(Labels)
            .ToArray();
        return labels.Any(label =>
                label.Equals("Marketplace", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Marketplace, Heading", StringComparison.OrdinalIgnoreCase)
            )
            && labels.Contains("Age confirmation needed", StringComparer.OrdinalIgnoreCase)
            && labels.Contains("Confirm your age", StringComparer.OrdinalIgnoreCase);
    }

    internal static async Task ExitMarketplaceAgeGateAsync(
        ILDPlayerService ld,
        int index,
        string xml,
        string? path,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!IsMarketplaceAgeGate(xml))
            throw new InvalidOperationException(
                "Marketplace age gate is not verified; no Back input made."
            );
        if (!await ld.SendKeyAsync(index, 4, path, ct))
            throw new InvalidOperationException(
                "ScreenTime could not leave the Marketplace age gate; Back was not acknowledged."
            );
        // A successful key command alone does not prove that the gate closed.
        // Send Back once, then observe; never press Confirm your age.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await delay(TimeSpan.FromMilliseconds(300), ct);
            ct.ThrowIfCancellationRequested();
            var after = await ld.DumpUiHierarchyAsync(index, path, ct);
            if (IsMarketplaceAgeGate(after))
                continue;
            if (
                ScreenTimeHierarchy.Nodes(after).Any(IsMarketplaceVisibleNode)
                && !PFlowUiPrivacy.ContainsInput(after)
                && !IsBlockingScreen(after, "Marketplace")
            )
                return;
        }
        throw new InvalidOperationException(
            "ScreenTime could not verify leaving the Marketplace age gate; next action was not started."
        );
    }

    // These surfaces can retain the Marketplace heading underneath a sheet or gate.
    // Their buttons do not authorize age confirmation, location changes or messages.
    private static bool IsMarketplaceNonBrowsingScreen(string? xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(IsMarketplaceVisibleNode)
            .SelectMany(Labels)
            .Any(label =>
                new[]
                {
                    "Age confirmation needed",
                    "Confirm your age",
                    "Choose a location",
                    "Message seller",
                }.Contains(label, StringComparer.OrdinalIgnoreCase)
            );

    private static bool IsMarketplaceBrowseTab(string? xml, XElement tab)
    {
        if (
            !IsMarketplaceVisibleNode(tab)
            || !Regex.IsMatch(
                tab.Attribute("content-desc")?.Value ?? "",
                @"^(?:For you|Local), tab(?:,|$)",
                RegexOptions.IgnoreCase
            )
        )
            return false;

        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(IsMarketplaceVisibleNode)
            .SelectMany(Labels)
            .ToArray();
        // Ignore only the two internal browsing tabs when their Marketplace layout
        // is present. A selected global Home/Reels/etc. tab still conflicts.
        return labels.Any(label =>
                label.Equals("Marketplace", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Marketplace, Heading", StringComparison.OrdinalIgnoreCase)
            )
            && labels.Any(label =>
                Regex.IsMatch(label, @"^For you, tab(?:,|$)", RegexOptions.IgnoreCase)
            )
            && labels.Any(label =>
                Regex.IsMatch(label, @"^Local, tab(?:,|$)", RegexOptions.IgnoreCase)
            );
    }
}
