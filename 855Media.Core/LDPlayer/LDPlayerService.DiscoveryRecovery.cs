using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    private async Task<bool> OpenPageSwitcherFromCurrentScreenAsync(
        int index,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress
    )
    {
        var current = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (current == null)
            return false;
        if (
            !IsAccountSwitcherOpen(current.RawXml)
            && !AtxAgentClient.IsMenuPageInBloks(current)
            && FindProfileHeaderSwitcherButton(current.RawXml) == null
        )
            await OpenDeepLinkAsync(
                index,
                "fb://feed",
                BloksConstants.KatanaPackage,
                customPath,
                cancellationToken
            );
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var reveals = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
            if (screen == null)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }
            if (IsFacebookLoginGate(screen.CurrentActivity, screen))
                return false;
            if (IsAccountSwitcherOpen(screen.RawXml))
                return true;
            if (
                (
                    AtxAgentClient.IsMenuPageInBloks(screen)
                    || FindProfileHeaderSwitcherButton(screen.RawXml) != null
                )
                && await ClickProfileSwitcherDropdownAsync(
                    index,
                    null,
                    customPath,
                    cancellationToken
                )
            )
            {
                var sheetWait = System.Diagnostics.Stopwatch.StartNew();
                while (sheetWait.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await Task.Delay(400, cancellationToken);
                    if (
                        IsAccountSwitcherOpen(
                            await DumpUiHierarchyAsync(index, customPath, cancellationToken)
                        )
                    )
                        return true;
                }
                return false;
            }
            var navigation = FindAuthenticatedProfileNavigationTarget(screen);
            if (navigation != null)
                await TapAsync(
                    index,
                    navigation.CenterX,
                    navigation.CenterY,
                    customPath,
                    cancellationToken
                );
            else if (
                screen.AllElements.Any(element =>
                    element.Enabled
                    && element.Clickable
                    && (
                        element.ContentDesc.StartsWith(
                            "Menu, tab",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || element.Text.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
                        || element.Matches("Menu", partialMatch: false)
                        || element.Matches(
                            "Account switcher, shortcuts, and settings menu",
                            partialMatch: false
                        )
                    )
                )
            )
                await OpenFacebookMenuFromFeedAsync(index, customPath, cancellationToken);
            else if (
                screen.AllElements.Any(e => e.Matches("Story tray", partialMatch: false))
                && reveals++ < 3
            )
            {
                var (width, height) = ParseScreenDimensions(screen.RawXml);
                if (width > 0 && height > 0)
                    await SwipeAsync(
                        index,
                        width / 2,
                        height / 3,
                        width / 2,
                        height * 3 / 4,
                        350,
                        customPath,
                        cancellationToken
                    );
            }
            await Task.Delay(500, cancellationToken);
        }
        if (progress != null)
            await progress("The current screen did not expose the account switcher.");
        await PFlowFetchTrace.WriteAsync(
            index,
            "page.switcher.navigation.timeout",
            PFlowFetchTrace.SummarizeScreen(
                await DumpBloksScreenAsync(index, customPath, cancellationToken)
            ),
            cancellationToken
        );
        return false;
    }

    public async Task<FacebookPageInfo?> ExtractPageInfoFromUiAsync(
        int index,
        string pageUid,
        string? pageId = null,
        string? expectedPageName = null,
        string? expectedProfileUid = null,
        string? expectedProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<string, Task>? progress = null,
        bool ownerAlreadyVerified = false
    )
    {
        if (
            string.IsNullOrWhiteSpace(expectedProfileUid)
            || string.IsNullOrWhiteSpace(expectedProfileName)
        )
            return null;
        // Shared native switching owns navigation recovery. Keep Fetch's date,
        // statistics and root-owner checks without restarting again on UI failure.
        return await ExtractPageInfoCoreAsync(
            index,
            pageUid,
            pageId,
            expectedPageName,
            expectedProfileUid,
            expectedProfileName,
            customPath,
            cancellationToken,
            progress,
            ownerAlreadyVerified
        );
    }

    private async Task<bool> OpenCurrentOwnProfileAsync(
        int index,
        string expectedName,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress
    )
    {
        // Let the closed sheet or profile transition settle before choosing a route.
        await Task.Delay(700, cancellationToken);
        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (
            screen != null
            && !AtxAgentClient.IsMenuPageInBloks(screen)
            && IsExpectedOwnProfileVisible(screen, string.Empty, expectedName)
        )
            return true;
        if (screen == null || !AtxAgentClient.IsMenuPageInBloks(screen))
        {
            await OpenDeepLinkAsync(
                index,
                "fb://feed",
                BloksConstants.KatanaPackage,
                customPath,
                cancellationToken
            );
            var ready = await WaitForAuthenticatedFacebookScreenAsync(
                index,
                customPath,
                cancellationToken,
                progress
            );
            if (!ready.Ready)
                return false;
            screen = AtxAgentClient.ParseBloksHierarchy(ready.Xml);
        }
        if (
            !AtxAgentClient.IsMenuPageInBloks(screen)
            && IsExpectedOwnProfileVisible(screen, string.Empty, expectedName)
        )
            return true;
        return await OpenAuthenticatedProfileFromNavigationAsync(
            index,
            screen,
            expectedName,
            customPath,
            cancellationToken,
            progress
        );
    }

    internal static BloksElement? FindMenuProfileNameTarget(
        BloksScreenInfo screen,
        string expectedName
    )
    {
        if (IsAccountSwitcherOpen(screen.RawXml))
            return null;
        if (!AtxAgentClient.IsMenuPageInBloks(screen))
            return null;
        var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(1280).Max();
        return screen
            .AllElements.Where(e =>
                e.Enabled
                && e.Width > 0
                && e.Height > 0
                && e.CenterY < height / 2
                && e.Matches(expectedName, partialMatch: false)
            )
            .OrderBy(e => e.Width * e.Height)
            .FirstOrDefault();
    }

    private static bool IsSuccessfulInputCommand(string output) =>
        !Regex.IsMatch(
            output,
            @"SecurityException|Permission Denial|(?:^|\n)\s*error:|device.*(?:not found|offline)|no devices/emulators|/system/bin/sh:.*(?:not found|syntax error)",
            RegexOptions.IgnoreCase
        );

    internal static XElement? FindRecentAppsClearTarget(string? xml)
    {
        return SafeParseXmlDocuments(xml)
            .SelectMany(doc => doc.Descendants("node"))
            .FirstOrDefault(node =>
            {
                // Only accept controls owned by Android's Recents surface.
                var package = node.Attribute("package")?.Value ?? "";
                if (
                    !package.Contains("launcher", StringComparison.OrdinalIgnoreCase)
                    && !package.Equals("com.android.systemui", StringComparison.OrdinalIgnoreCase)
                )
                    return false;
                if (node.Attribute("enabled")?.Value == "false")
                    return false;
                var labels = new[]
                {
                    node.Attribute("text")?.Value,
                    node.Attribute("content-desc")?.Value,
                };
                var id = node.Attribute("resource-id")?.Value ?? "";
                var (x, y) = ParseNodeBoundsCenter(node);
                return x > 0
                    && y > 0
                    && (
                        labels.Any(label =>
                            Regex.IsMatch(
                                label?.Trim() ?? "",
                                @"^(?:clear|close|dismiss) all(?: apps)?$",
                                RegexOptions.IgnoreCase
                            )
                        )
                        || Regex.IsMatch(
                            id,
                            @"/(?:clear_all|clear_all_button|dismiss_all|dismiss_all_button)$",
                            RegexOptions.IgnoreCase
                        )
                    );
            });
    }

    // Use the name immediately above profile counts, below the avatar. Never
    // infer a name from arbitrary Feed text, or treat this as identity proof.
    internal static string? ReadProfileHeaderName(string? xml)
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        var avatar = screen.AllElements.FirstOrDefault(e => e.Matches("Profile picture", false));
        if (avatar == null)
            return null;
        var counts = screen
            .AllElements.Where(e =>
                Regex.IsMatch(
                    string.IsNullOrWhiteSpace(e.Text) ? e.ContentDesc : e.Text,
                    @"^\s*[\d,.]+\s*[KMB]?\s*(?:friends|followers|posts)\b",
                    RegexOptions.IgnoreCase
                )
                && e.Bounds.Top >= avatar.Bounds.Bottom
            )
            .OrderBy(e => e.Bounds.Top)
            .FirstOrDefault();
        if (counts == null)
            return null;
        var headerElements = screen
            .AllElements.Where(e =>
                e.Bounds.Top >= avatar.Bounds.Bottom
                && e.Bounds.Bottom <= counts.Bounds.Top + 2
                && !string.IsNullOrWhiteSpace(
                    string.IsNullOrWhiteSpace(e.Text) ? e.ContentDesc : e.Text
                )
            )
            .ToList();
        var titleTop = headerElements.Select(e => e.Bounds.Top).DefaultIfEmpty(-1).Min();
        var names = headerElements
            .Where(e => e.Bounds.Top == titleTop)
            .Select(e => (string.IsNullOrWhiteSpace(e.Text) ? e.ContentDesc : e.Text).Trim())
            .Where(name =>
                name.Length >= 2
                && name.Length <= 100
                && !IsIgnoredProfileText(name)
                && !IsIgnoredSwitcherHeaderOrAction(name)
                && !name.Equals("Get verified", StringComparison.OrdinalIgnoreCase)
                && !name.Contains('…')
                && !name.Contains('@')
                && Regex.IsMatch(name, @"^\p{L}[\p{L}\p{M}\s.'’\-]+$")
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 1 ? names[0] : null;
    }

    private async Task<string?> ResolvePersonalProfileNameAsync(
        int index,
        string accountUid,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress
    )
    {
        if (string.IsNullOrWhiteSpace(accountUid))
            return null;
        if (progress != null)
            await progress("Personal account: resolving the missing name from its UID profile.");
        await LaunchAppAsync(index, BloksConstants.KatanaPackage, customPath, cancellationToken);
        await OpenDeepLinkAsync(
            index,
            $"fb://profile/{Uri.EscapeDataString(accountUid)}",
            BloksConstants.KatanaPackage,
            customPath,
            cancellationToken
        );
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(45))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var name = ReadProfileHeaderName(xml);
            if (!string.IsNullOrWhiteSpace(name))
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "personal.name.resolved",
                    "UID profile header supplied a name; authenticated switcher verification is still required.",
                    cancellationToken
                );
                return name;
            }
            await Task.Delay(750, cancellationToken);
        }
        await PFlowFetchTrace.WriteAsync(
            index,
            "personal.name.unavailable",
            "The UID profile did not expose an unambiguous name; Page lookup cannot verify the owner.",
            cancellationToken
        );
        return null;
    }
}
