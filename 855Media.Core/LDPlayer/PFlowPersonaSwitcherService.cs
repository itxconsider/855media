using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService : IPFlowPersonaSwitcherService
{
    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _nativeDelay;
    private readonly Func<DateTime> _nativeNow;

    public PFlowPersonaSwitcherService(ILDPlayerService ldPlayer, AtxAgentClient? atxClient = null)
        : this(ldPlayer, atxClient, Task.Delay, () => DateTime.UtcNow) { }

    internal PFlowPersonaSwitcherService(
        ILDPlayerService ldPlayer,
        AtxAgentClient? atxClient,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTime> now
    )
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
        _nativeDelay = delay;
        _nativeNow = now;
    }

    public Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return SwitchToPersonaCoreAsync(
            index,
            personaName,
            pageId: null,
            customPath,
            cancellationToken
        );
    }

    public Task<bool> SwitchToPersonaAsync(
        int index,
        FacebookPageTarget target,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return SelectNativeTargetAsync(index, target, customPath, cancellationToken);
    }

    public Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return SelectNativeTargetAsync(
            index,
            new FacebookPageTarget
            {
                PageName = personaName,
                PageId = pageId ?? string.Empty,
                Type = string.IsNullOrWhiteSpace(pageId) ? "Profile" : "Page",
            },
            customPath,
            cancellationToken
        );
    }

    public Task<bool> SwitchToPageByIdAsync(
        int index,
        string pageName,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return SwitchToPersonaAsync(index, pageName, pageId, customPath, cancellationToken);
    }

    private async Task<bool> SwitchToPersonaCoreAsync(
        int index,
        string personaName,
        string? pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(personaName))
            return false;

        personaName = personaName.Trim();
        var isPageTarget = !string.IsNullOrWhiteSpace(pageId);
        bool IsTargetActive(string? xml) =>
            isPageTarget
                ? IsPagePersonaActive(xml, personaName)
                : IsAlreadyOnTargetPersona(xml, personaName);

        // 1. Ensure Facebook Katana is in foreground
        var currentXml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(currentXml))
        {
            await _ldPlayer.LaunchAppAsync(
                index,
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
            await Task.Delay(2500, cancellationToken);
            currentXml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        }

        // TIER 0: Check if current screen already has target persona active
        if (IsTargetActive(currentXml))
        {
            if (IsAccountSwitcherOpen(currentXml))
            {
                await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
                await Task.Delay(800, cancellationToken);
            }
            return true;
        }

        // P-Flow uses the native identity switcher; public profile deep links do not establish persona identity.

        // TIER 2: Check if current screen has a direct switch button
        var directBtn = FindDirectSwitchButton(currentXml, personaName);
        if (directBtn != null)
        {
            var (dcx, dcy) = ParseNodeBoundsCenter(directBtn);
            if (dcx > 0 && dcy > 0)
            {
                await _ldPlayer.TapAsync(index, dcx, dcy, customPath, cancellationToken);
                await Task.Delay(5000, cancellationToken);
                currentXml = await _ldPlayer.DumpUiHierarchyAsync(
                    index,
                    customPath,
                    cancellationToken
                );
                if (IsTargetActive(currentXml))
                    return true;
            }
        }

        // TIER 3: Multi-variant Menu Navigation (using fb://menu deep link to bypass top/bottom tab differences)
        bool isSwitcherOpen = IsAccountSwitcherOpen(currentXml);
        if (!isSwitcherOpen)
        {
            await _ldPlayer.OpenDeepLinkAsync(
                index,
                "fb://menu",
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
            await Task.Delay(2500, cancellationToken);
            currentXml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);

            if (IsTargetActive(currentXml))
            {
                return true;
            }

            directBtn = FindDirectSwitchButton(currentXml, personaName);
            if (directBtn != null)
            {
                var (dcx, dcy) = ParseNodeBoundsCenter(directBtn);
                if (dcx > 0 && dcy > 0)
                {
                    await _ldPlayer.TapAsync(index, dcx, dcy, customPath, cancellationToken);
                    await Task.Delay(5000, cancellationToken);
                    currentXml = await _ldPlayer.DumpUiHierarchyAsync(
                        index,
                        customPath,
                        cancellationToken
                    );
                    if (IsTargetActive(currentXml))
                        return true;
                }
            }

            // Check Quick-switch avatar in Menu (1-tap switch!)
            var quickAvatar = FindQuickSwitchAvatarInMenu(currentXml, personaName);
            if (quickAvatar.HasValue)
            {
                await _ldPlayer.TapAsync(
                    index,
                    quickAvatar.Value.X,
                    quickAvatar.Value.Y,
                    customPath,
                    cancellationToken
                );
                await Task.Delay(5500, cancellationToken);
                currentXml = await _ldPlayer.DumpUiHierarchyAsync(
                    index,
                    customPath,
                    cancellationToken
                );
                if (IsTargetActive(currentXml))
                    return true;
            }

            // Check "Your shortcuts" section in Menu
            var shortcut = FindPageInMenuShortcuts(currentXml, personaName);
            if (shortcut.HasValue)
            {
                await _ldPlayer.TapAsync(
                    index,
                    shortcut.Value.X,
                    shortcut.Value.Y,
                    customPath,
                    cancellationToken
                );
                await Task.Delay(4000, cancellationToken);
                var afterShortcutXml = await _ldPlayer.DumpUiHierarchyAsync(
                    index,
                    customPath,
                    cancellationToken
                );
                if (IsTargetActive(afterShortcutXml))
                {
                    return true;
                }
                var shortcutSwitchBtn = FindDirectSwitchButton(afterShortcutXml, personaName);
                if (shortcutSwitchBtn != null)
                {
                    var (scx, scy) = ParseNodeBoundsCenter(shortcutSwitchBtn);
                    if (scx > 0 && scy > 0)
                    {
                        await _ldPlayer.TapAsync(index, scx, scy, customPath, cancellationToken);
                        await Task.Delay(5500, cancellationToken);
                        var switchedXml = await _ldPlayer.DumpUiHierarchyAsync(
                            index,
                            customPath,
                            cancellationToken
                        );
                        if (IsTargetActive(switchedXml))
                            return true;
                        afterShortcutXml = switchedXml;
                    }
                }
                if (!string.IsNullOrWhiteSpace(afterShortcutXml))
                {
                    currentXml = afterShortcutXml;
                }
            }

            if (!IsAccountSwitcherOpen(currentXml))
            {
                await ClickProfileSwitcherDropdownAsync(index, customPath, cancellationToken);
                await Task.Delay(2500, cancellationToken);
                currentXml = await _ldPlayer.DumpUiHierarchyAsync(
                    index,
                    customPath,
                    cancellationToken
                );
            }
        }

        // TIER 4: Search and tap target persona in bottom sheet (with scroll down if off-screen)
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (string.IsNullOrWhiteSpace(currentXml))
                break;

            var match = FindPersonaInSwitcher(currentXml, personaName);
            if (match.HasValue)
            {
                var (cx, cy, isAlreadySelected) = match.Value;
                if (isAlreadySelected)
                {
                    if (IsAccountSwitcherOpen(currentXml))
                    {
                        await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
                        await Task.Delay(800, cancellationToken);
                    }
                    return true;
                }

                await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                string? verifyXml = null;
                for (var verifyAttempt = 0; verifyAttempt < 8; verifyAttempt++)
                {
                    await Task.Delay(1000, cancellationToken);
                    verifyXml = await _ldPlayer.DumpUiHierarchyAsync(
                        index,
                        customPath,
                        cancellationToken
                    );

                    if (IsTargetActive(verifyXml))
                        return true;

                    var selectedAfterTap = FindPersonaInSwitcher(verifyXml, personaName);
                    if (
                        isPageTarget
                        && IsAccountSwitcherOpen(verifyXml)
                        && selectedAfterTap.HasValue
                        && selectedAfterTap.Value.IsSelected
                    )
                    {
                        await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                        verifyXml = await _ldPlayer.DumpUiHierarchyAsync(
                            index,
                            customPath,
                            cancellationToken
                        );
                        if (IsTargetActive(verifyXml))
                            return true;
                    }
                }

                if (IsAccountSwitcherOpen(verifyXml))
                    await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
                return false;
            }

            // Target not visible in current view: scroll down in the switcher bottom sheet
            if (attempt < 4 && IsAccountSwitcherOpen(currentXml))
            {
                var (sw, sh) = ParseScreenDimensions(currentXml);
                if (sw <= 0)
                    sw = 720;
                if (sh <= 0)
                    sh = 1280;
                await _ldPlayer.SwipeAsync(
                    index,
                    sw / 2,
                    (int)(sh * 0.78),
                    sw / 2,
                    (int)(sh * 0.40),
                    durationMs: 400,
                    customPath,
                    cancellationToken
                );
                await Task.Delay(1500, cancellationToken);
                currentXml = await _ldPlayer.DumpUiHierarchyAsync(
                    index,
                    customPath,
                    cancellationToken
                );
            }
            else
            {
                break;
            }
        }

        // If not found or couldn't switch, dismiss open sheet so screen isn't blocked
        if (IsAccountSwitcherOpen(currentXml))
        {
            await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
            await Task.Delay(800, cancellationToken);
        }

        return false;
    }

    /// <summary>Selects the personal identity through the native dropdown without restarting Facebook.</summary>
    public Task<bool> EnsureSwitchedToPersonalProfileAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(targetProfileName))
            return Task.FromResult(false);
        return SelectNativeTargetAsync(
            index,
            new FacebookPageTarget { Type = "Profile", PageName = targetProfileName },
            customPath,
            cancellationToken
        );
    }

    /// <summary>
    /// Step 5: Finds and clicks the Facebook Menu / Profile tab on the navigation bar.
    /// Handles feeds that lack a top-left hamburger button by querying the navigation bar via Bloks and UI hierarchy,
    /// identifying the rightmost Menu / Profile tab (which may have a blue selection bar when selected).
    /// </summary>
    public async Task<bool> OpenFacebookMenuFromFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Check if already on the Menu page via Bloks
        var bloksScreen = await _ldPlayer.DumpBloksScreenAsync(
            index,
            customPath,
            cancellationToken
        );
        if (bloksScreen != null && AtxAgentClient.IsMenuPageInBloks(bloksScreen))
        {
            return true;
        }

        // 2. Query Bloks for the Menu / Profile tab on the navigation bar
        if (bloksScreen != null && bloksScreen.AllElements.Count > 0)
        {
            var bloksTab = AtxAgentClient.FindMenuTabInBloks(bloksScreen);
            if (bloksTab.HasValue)
            {
                if (bloksTab.Value.IsSelected)
                {
                    // Already on Menu tab (highlighted by blue selection bar)
                    return true;
                }

                await _ldPlayer.TapAsync(
                    index,
                    bloksTab.Value.X,
                    bloksTab.Value.Y,
                    customPath,
                    cancellationToken
                );
                await Task.Delay(2000, cancellationToken);
                return true;
            }
        }

        // 3. Fallback to UI Hierarchy XML
        var feedXml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(feedXml))
        {
            // Check if already on Menu / Drawer page
            if (
                feedXml.Contains("Your shortcuts", StringComparison.OrdinalIgnoreCase)
                || feedXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
                || feedXml.Contains("Open profile switcher", StringComparison.OrdinalIgnoreCase)
                || feedXml.Contains("Switch profile", StringComparison.OrdinalIgnoreCase)
                || feedXml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            var docs = SafeParseXmlDocuments(feedXml);
            foreach (var doc in docs)
            {
                var allNodes = doc.Descendants("node").ToList();

                // 3a. Check for Hamburger Menu icon ☰: desc="Menu" or text="Menu" on header (x1 < 150, y1 < 250)
                var hamburger = allNodes.FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value?.Trim();
                    var text = n.Attribute("text")?.Value?.Trim();
                    var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
                    if (!string.Equals(label, "Menu", StringComparison.OrdinalIgnoreCase))
                        return false;

                    var b = n.Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(b))
                        return false;
                    var m = Regex.Match(b, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!m.Success)
                        return false;
                    int x1 = int.Parse(m.Groups[1].Value);
                    int y1 = int.Parse(m.Groups[2].Value);
                    return x1 < 150 && y1 < 250;
                });

                if (hamburger != null)
                {
                    var (hcx, hcy) = ParseNodeBoundsCenter(hamburger);
                    if (hcx > 0 && hcy > 0)
                    {
                        await _ldPlayer.TapAsync(index, hcx, hcy, customPath, cancellationToken);
                        await Task.Delay(2000, cancellationToken);
                        return true;
                    }
                }

                // 3b. Search by accessibility content-description for Tab 6:
                // "Menu, tab 6 of 6", "Menu, tab 5 of 5", "Profile, tab 6 of 6", "Account switcher, shortcuts, and settings menu"
                var menuTab = allNodes.FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value?.Trim();
                    var text = n.Attribute("text")?.Value?.Trim();
                    var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
                    if (string.IsNullOrWhiteSpace(label))
                        return false;

                    return label.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
                        || label.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                        || label.Contains(
                            "shortcuts, and settings menu",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || label.Contains("Account switcher", StringComparison.OrdinalIgnoreCase);
                });

                if (menuTab != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(menuTab);
                    if (cx > 0 && cy > 0)
                    {
                        await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                        await Task.Delay(2000, cancellationToken);
                        return true;
                    }
                }

                // 3c. Structural search in XML: Find bottom bar container and rightmost tab
                var (swDoc, shDoc) = ParseScreenDimensions(feedXml);
                if (swDoc <= 0)
                    swDoc = 720;
                if (shDoc <= 0)
                    shDoc = 1280;

                var bottomTabs = allNodes
                    .Where(n =>
                    {
                        if (n.Attribute("clickable")?.Value != "true")
                            return false;
                        var b = n.Attribute("bounds")?.Value;
                        if (string.IsNullOrWhiteSpace(b))
                            return false;
                        var m = Regex.Match(b, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                        if (!m.Success)
                            return false;
                        int y1 = int.Parse(m.Groups[2].Value);
                        return y1 > shDoc * 0.82;
                    })
                    .Select(n =>
                    {
                        var (cx, cy) = ParseNodeBoundsCenter(n);
                        bool isSel = n.Attribute("selected")?.Value == "true";
                        return (Node: n, CenterX: cx, CenterY: cy, IsSelected: isSel);
                    })
                    .Where(t => t.CenterX > 0 && t.CenterY > 0)
                    .OrderBy(t => t.CenterX)
                    .ToList();

                if (bottomTabs.Count >= 3)
                {
                    var rightmost = bottomTabs.Last();
                    await _ldPlayer.TapAsync(
                        index,
                        rightmost.CenterX,
                        rightmost.CenterY,
                        customPath,
                        cancellationToken
                    );
                    await Task.Delay(2000, cancellationToken);
                    return true;
                }
            }
        }

        // 4. Proportional fallback:
        var (sw, sh) = ParseScreenDimensions(feedXml);
        if (sw <= 0)
            sw = 720;
        if (sh <= 0)
            sh = 1280;

        // Try top-left hamburger first (approx 8% width, 7% height, e.g. 52, 92)
        await _ldPlayer.TapAsync(
            index,
            (int)(sw * 0.08),
            (int)(sh * 0.07),
            customPath,
            cancellationToken
        );
        await Task.Delay(2000, cancellationToken);

        var verifyMenu = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (
            !string.IsNullOrWhiteSpace(verifyMenu)
            && (
                verifyMenu.Contains("Your shortcuts", StringComparison.OrdinalIgnoreCase)
                || verifyMenu.Contains("Open profile switcher", StringComparison.OrdinalIgnoreCase)
                || verifyMenu.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        // Next try top-right tab 6 (approx 92% width, 14% height, e.g. 660, 180)
        await _ldPlayer.TapAsync(
            index,
            (int)(sw * 0.92),
            (int)(sh * 0.14),
            customPath,
            cancellationToken
        );
        await Task.Delay(2000, cancellationToken);
        return true;
    }

    /// <summary>
    /// Step 6: Clicks the dropdown profile list button (chevron / options button on the active profile card).
    /// </summary>
    public async Task<bool> ClickProfileSwitcherDropdownAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var menuXml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(menuXml))
        {
            var docs = SafeParseXmlDocuments(menuXml);
            foreach (var doc in docs)
            {
                var allNodes = doc.Descendants("node").ToList();

                // 1. Explicitly labeled dropdown / options / switcher button in upper profile card
                var dropdownNode = allNodes.FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value?.Trim();
                    var text = n.Attribute("text")?.Value?.Trim();
                    var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
                    if (string.IsNullOrWhiteSpace(label))
                        return false;

                    var bounds = n.Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(bounds))
                        return false;
                    var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!m.Success)
                        return false;
                    int y1 = int.Parse(m.Groups[2].Value);
                    int y2 = int.Parse(m.Groups[4].Value);
                    int x1 = int.Parse(m.Groups[1].Value);

                    if (
                        label.Contains("Search", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("Settings", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return false;
                    }

                    if (y1 >= 140 && y2 <= 380 && x1 >= 180)
                    {
                        if (
                            label.Equals("More options", StringComparison.OrdinalIgnoreCase)
                            || label.Equals("Switch profile", StringComparison.OrdinalIgnoreCase)
                            || label.Equals("Switch accounts", StringComparison.OrdinalIgnoreCase)
                            || label.Contains(
                                "Open profile switcher",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || label.Contains(
                                "See all profiles",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || label.Contains(
                                "Profiles and Pages",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || label.Contains(
                                "Your profiles and pages",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || label.Contains("switcher", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("chevron", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("dropdown", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("expand", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            return true;
                        }
                    }
                    return false;
                });

                if (dropdownNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(dropdownNode);
                    if (cx > 0 && cy > 0)
                    {
                        await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                        return true;
                    }
                }

                // 2. Any clickable ImageView / Button on the right side of the profile header card
                var headerRightBtn = allNodes.FirstOrDefault(n =>
                {
                    var bounds = n.Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(bounds))
                        return false;
                    var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!m.Success)
                        return false;
                    int x1 = int.Parse(m.Groups[1].Value);
                    int y1 = int.Parse(m.Groups[2].Value);
                    int y2 = int.Parse(m.Groups[4].Value);

                    var clickable = n.Attribute("clickable")?.Value == "true";
                    var cls = n.Attribute("class")?.Value ?? "";
                    return (
                            clickable
                            || cls.Contains("Image")
                            || cls.Contains("Button")
                            || cls.Contains("View")
                        )
                        && y1 >= 140
                        && y2 <= 380
                        && x1 >= 540;
                });

                if (headerRightBtn != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(headerRightBtn);
                    if (cx > 0 && cy > 0)
                    {
                        await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                        return true;
                    }
                }

                // 3. Check general switcher coordinates
                var switcherCoords = FindProfileSwitcherCoordinates(menuXml);
                if (switcherCoords.HasValue)
                {
                    await _ldPlayer.TapAsync(
                        index,
                        switcherCoords.Value.X,
                        switcherCoords.Value.Y,
                        customPath,
                        cancellationToken
                    );
                    return true;
                }
            }
        }

        // Proportional fallback: far-right of header card (88% width, 18.5% height for switcher chevron [v])
        var (swMenu, shMenu) = ParseScreenDimensions(menuXml);
        if (swMenu <= 0)
            swMenu = 720;
        if (shMenu <= 0)
            shMenu = 1280;
        await _ldPlayer.TapAsync(
            index,
            (int)(swMenu * 0.88),
            (int)(shMenu * 0.185),
            customPath,
            cancellationToken
        );
        return true;
    }

    /// <summary>
    /// Step 7: Queries the Bloks / UI hierarchy of the switcher bottom sheet to find the personal profile account.
    /// Distinguishes personal profile accounts (no "Page" badge) from Pages (which carry a "Page" badge/pill).
    /// </summary>
    public async Task<(int X, int Y)?> QueryPersonalProfileInSwitcherAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Try Bloks query first
        var bloksScreen = await _ldPlayer.DumpBloksScreenAsync(
            index,
            customPath,
            cancellationToken
        );
        if (bloksScreen != null && bloksScreen.AllElements.Count > 0)
        {
            var bloksCoords = AtxAgentClient.FindPersonalProfileCoordsInBloks(
                bloksScreen,
                targetProfileName
            );
            if (bloksCoords.HasValue)
            {
                return bloksCoords;
            }
        }

        // 2. Fallback to UI hierarchy XML
        var switcherXml = await _ldPlayer.DumpUiHierarchyAsync(
            index,
            customPath,
            cancellationToken
        );
        if (!string.IsNullOrWhiteSpace(switcherXml))
        {
            var profileNode = FindPersonalProfileNodeInSwitcher(switcherXml, targetProfileName);
            if (profileNode != null)
            {
                var (px, py) = ParseNodeBoundsCenter(profileNode);
                if (px > 0 && py > 0)
                {
                    return (px, py);
                }
            }
        }

        return null;
    }

    private static bool IsPagePersonaActive(string? xml, string personaName)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(personaName))
            return false;

        if (
            xml.Contains($"Switched to {personaName}", StringComparison.OrdinalIgnoreCase)
            || xml.Contains(
                $"Switched to {personaName.TrimEnd('…', '.', ' ').Trim()}",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return true;

        if (IsAccountSwitcherOpen(xml))
        {
            var selection = FindPersonaInSwitcher(xml, personaName);
            return selection.HasValue && selection.Value.IsSelected;
        }

        // A public Page profile can show the Page name and admin shortcuts while the
        // personal profile is still active. Require Page-management UI evidence too.
        if (
            xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Switch into your page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Switch into", StringComparison.OrdinalIgnoreCase)
        )
            return false;

        var hasPageManagementUi =
            xml.Contains("Professional dashboard", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Page dashboard", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Manage Page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Page settings", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Page access", StringComparison.OrdinalIgnoreCase);

        return hasPageManagementUi && IsAlreadyOnTargetPersona(xml, personaName);
    }

    public static bool IsAlreadyOnTargetPersona(string? xml, string personaName)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(personaName))
            return false;

        var cleanTarget = personaName.TrimEnd('…', '.', ' ').Trim();

        // 1. Check if Facebook toast or banner says "Switched to [Persona]"
        if (
            xml.Contains($"Switched to {cleanTarget}", StringComparison.OrdinalIgnoreCase)
            || xml.Contains($"Switched to {personaName}", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        // 2. If in switcher sheet, check if target persona is marked selected or checked
        if (IsAccountSwitcherOpen(xml))
        {
            var match = FindPersonaInSwitcher(xml, personaName);
            return match.HasValue && match.Value.IsSelected;
        }

        // 3. Check if top header card contains personaName (accounting for ellipsis/truncation)
        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var nodes = doc.Descendants("node").ToList();
            var activeHeaderNode = nodes.FirstOrDefault(n =>
            {
                var text = n.Attribute("text")?.Value?.Trim();
                var desc = n.Attribute("content-desc")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(text) ? text : desc;
                if (string.IsNullOrWhiteSpace(label))
                    return false;

                var (cx, cy) = ParseNodeBoundsCenter(n);
                if (cy > 0 && cy < 380)
                {
                    if (
                        IsIgnoredSwitcherHeaderOrAction(label)
                        || label.Contains("Search", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("Settings", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return false;
                    }

                    var cleanLabel = label.TrimEnd('…', '.', ' ').Trim();
                    if (string.Equals(cleanLabel, cleanTarget, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (
                        cleanLabel.Length >= 4
                        && (
                            cleanTarget.StartsWith(cleanLabel, StringComparison.OrdinalIgnoreCase)
                            || cleanLabel.StartsWith(
                                cleanTarget,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || cleanLabel.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)
                            || cleanTarget.Contains(cleanLabel, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        return true;
                    }

                    if (
                        label.Contains(
                            $"{cleanTarget}, see your profile",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || label.StartsWith($"{cleanTarget},", StringComparison.OrdinalIgnoreCase)
                        || label.Contains(
                            $"{cleanLabel}, see your profile",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return true;
                    }
                }
                return false;
            });

            if (activeHeaderNode != null)
                return true;
        }

        return false;
    }

    public static XElement? FindDirectSwitchButton(string? xml, string? targetProfileName = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var cleanTarget = targetProfileName?.TrimEnd('…', '.', ' ').Trim();
        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            foreach (var node in doc.Descendants("node"))
            {
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var text = node.Attribute("text")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                // 1. Generic Switch Buttons on page/profile views
                bool isGenericSwitch =
                    label.Equals("Switch", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch profile", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch accounts", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch account", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch to manage", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch to page", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch now", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("switch into", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("switch to manage", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("switch to this page", StringComparison.OrdinalIgnoreCase);

                if (isGenericSwitch)
                {
                    return node;
                }

                // 2. Target persona specific switch button or banner
                if (!string.IsNullOrWhiteSpace(cleanTarget))
                {
                    bool isTargetMatch =
                        label.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)
                        || cleanTarget.Contains(
                            label
                                .Replace(
                                    "Switch into your page",
                                    "",
                                    StringComparison.OrdinalIgnoreCase
                                )
                                .Replace(
                                    "Switch into your profile",
                                    "",
                                    StringComparison.OrdinalIgnoreCase
                                )
                                .Replace("Switch into", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("Switch to", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("Switch", "", StringComparison.OrdinalIgnoreCase)
                                .Trim(' ', ',', '.'),
                            StringComparison.OrdinalIgnoreCase
                        );

                    if (
                        isTargetMatch
                        && (
                            label.StartsWith("Switch", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("switch", StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        return node;
                    }
                }
                else if (
                    label.StartsWith("Switch to ", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Switch into ", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Switch ", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return node;
                }
            }
        }
        return null;
    }

    public static (int X, int Y)? FindQuickSwitchAvatarInMenu(
        string? menuXml,
        string targetPersonaName
    )
    {
        if (string.IsNullOrWhiteSpace(menuXml) || string.IsNullOrWhiteSpace(targetPersonaName))
            return null;

        var cleanTarget = targetPersonaName.TrimEnd('…', '.', ' ').Trim();
        var docs = SafeParseXmlDocuments(menuXml);
        foreach (var doc in docs)
        {
            var allNodes = doc.Descendants("node").ToList();
            foreach (var node in allNodes)
            {
                var text = node.Attribute("text")?.Value?.Trim();
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                var (cx, cy) = ParseNodeBoundsCenter(node);
                // Quick-switch avatar carousel in Menu is located in the upper screen zone (y: 80 to 450)
                if (cy > 80 && cy < 450)
                {
                    var cleanLabel = label.TrimEnd('…', '.', ' ').Trim();
                    if (
                        string.Equals(cleanLabel, cleanTarget, StringComparison.OrdinalIgnoreCase)
                        || cleanLabel.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)
                        || cleanTarget.Contains(cleanLabel, StringComparison.OrdinalIgnoreCase)
                        || cleanLabel.Contains(
                            $"Switch to {cleanTarget}",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return (cx, cy);
                    }
                }
            }
        }

        return null;
    }

    public static (int X, int Y)? FindPageInMenuShortcuts(string? menuXml, string targetPersonaName)
    {
        if (string.IsNullOrWhiteSpace(menuXml) || string.IsNullOrWhiteSpace(targetPersonaName))
            return null;

        var cleanTarget = targetPersonaName.TrimEnd('…', '.', ' ').Trim();
        var docs = SafeParseXmlDocuments(menuXml);
        foreach (var doc in docs)
        {
            var allNodes = doc.Descendants("node").ToList();
            foreach (var node in allNodes)
            {
                var text = node.Attribute("text")?.Value?.Trim();
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                if (IsIgnoredSwitcherHeaderOrAction(label))
                    continue;

                var cleanLabel = label.TrimEnd('…', '.', ' ').Trim();
                bool isMatch =
                    string.Equals(cleanLabel, cleanTarget, StringComparison.OrdinalIgnoreCase)
                    || (
                        cleanLabel.Length >= 4
                        && cleanTarget.StartsWith(cleanLabel, StringComparison.OrdinalIgnoreCase)
                    )
                    || (
                        cleanTarget.Length >= 4
                        && cleanLabel.StartsWith(cleanTarget, StringComparison.OrdinalIgnoreCase)
                    )
                    || cleanLabel.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)
                    || cleanTarget.Contains(cleanLabel, StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                    continue;

                var (cx, cy) = ParseNodeBoundsCenter(node);
                // "Your shortcuts" section in Menu is located below the header card (y: 280 to 650)
                if (cx > 0 && cy > 280 && cy < 650)
                {
                    return (cx, cy);
                }
            }
        }

        return null;
    }

    public static (int X, int Y, bool IsSelected)? FindPersonaInSwitcher(
        string? switcherXml,
        string personaName
    )
    {
        if (string.IsNullOrWhiteSpace(switcherXml) || string.IsNullOrWhiteSpace(personaName))
            return null;

        // Use exact row names and selection scoped to that row for the observed native sheet.
        if (FindNativeSwitcherRows(switcherXml).Count > 0)
            return FindNativeTargetInSwitcher(switcherXml, personaName, null);

        var cleanTarget = personaName.TrimEnd('…', '.', ' ').Trim();
        var docs = SafeParseXmlDocuments(switcherXml);
        foreach (var doc in docs)
        {
            var allNodes = doc.Descendants("node").ToList();
            var candidates =
                new List<(
                    XElement Node,
                    string Label,
                    bool IsExact,
                    bool IsSelected,
                    int X,
                    int Y
                )>();

            foreach (var node in allNodes)
            {
                var text = node.Attribute("text")?.Value?.Trim();
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(text) ? text : desc;
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                // Skip non-persona items
                if (
                    IsIgnoredSwitcherHeaderOrAction(label)
                    || IsNotificationSubtitle(label)
                    || string.Equals(label, "Page", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, "Page profile", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                var cleanLabel = label.TrimEnd('…', '.', ' ').Trim();
                bool isExact = string.Equals(
                    cleanLabel,
                    cleanTarget,
                    StringComparison.OrdinalIgnoreCase
                );
                bool isMatch =
                    isExact
                    || cleanLabel.StartsWith(cleanTarget, StringComparison.OrdinalIgnoreCase)
                    || cleanTarget.StartsWith(cleanLabel, StringComparison.OrdinalIgnoreCase)
                    || cleanLabel.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)
                    || cleanTarget.Contains(cleanLabel, StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                    continue;

                var (cx, cy) = ParseNodeBoundsCenter(node);
                if (cx <= 0 || cy <= 0)
                    continue;

                // Check if marked selected="true" or checked="true"
                bool isSelected =
                    node.Attribute("selected")?.Value == "true"
                    || node.Attribute("checked")?.Value == "true"
                    || node.Ancestors("node")
                        .Take(4)
                        .Any(a =>
                            a.Attribute("selected")?.Value == "true"
                            || a.Attribute("checked")?.Value == "true"
                            || a.Descendants("node")
                                .Any(d =>
                                    string.Equals(
                                        d.Attribute("content-desc")?.Value?.Trim(),
                                        "Selected",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    || string.Equals(
                                        d.Attribute("content-desc")?.Value?.Trim(),
                                        "Checked",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                        );

                candidates.Add((node, label, isExact, isSelected, cx, cy));
            }

            if (candidates.Count > 0)
            {
                var best = candidates.FirstOrDefault(c => c.IsExact);
                if (best.Node == null)
                {
                    best = candidates.FirstOrDefault(c =>
                        c.Label.StartsWith(cleanTarget, StringComparison.OrdinalIgnoreCase)
                    );
                    if (best.Node == null)
                    {
                        best = candidates[0];
                    }
                }
                return (best.X, best.Y, best.IsSelected);
            }
        }

        return null;
    }

    public static XElement? FindPersonalProfileNodeInSwitcher(
        string? switcherXml,
        string? targetProfileName = null
    )
    {
        if (string.IsNullOrWhiteSpace(switcherXml))
            return null;

        try
        {
            var docs = SafeParseXmlDocuments(switcherXml);
            foreach (var doc in docs)
            {
                var allNodes = doc.Descendants("node").ToList();

                // Candidate account rows
                var candidates =
                    new List<(
                        XElement Node,
                        XElement RowContainer,
                        string Name,
                        bool IsSelected,
                        int Top,
                        int Bottom
                    )>();

                for (int i = 0; i < allNodes.Count; i++)
                {
                    var text = allNodes[i].Attribute("text")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        text = allNodes[i].Attribute("content-desc")?.Value?.Trim();

                    if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                        continue;

                    if (IsIgnoredSwitcherHeaderOrAction(text) || IsNotificationSubtitle(text))
                        continue;

                    if (
                        string.Equals(text, "Page", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(text, "Page profile", StringComparison.OrdinalIgnoreCase)
                    )
                        continue;

                    // Parse node bounds
                    var bounds = allNodes[i].Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(bounds))
                        continue;

                    var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!match.Success)
                        continue;

                    int x1 = int.Parse(match.Groups[1].Value);
                    int y1 = int.Parse(match.Groups[2].Value);
                    int x2 = int.Parse(match.Groups[3].Value);
                    int y2 = int.Parse(match.Groups[4].Value);

                    // Skip items above y = 250 (header bar, search, status bar)
                    if (y1 < 250)
                        continue;

                    // Find enclosing row container among ancestors (ViewGroup/Button with wide bounds)
                    XElement rowContainer = allNodes[i];
                    var ancestors = allNodes[i].Ancestors("node").ToList();
                    foreach (var anc in ancestors)
                    {
                        var ancBounds = anc.Attribute("bounds")?.Value;
                        if (string.IsNullOrWhiteSpace(ancBounds))
                            continue;

                        var ancMatch = Regex.Match(ancBounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                        if (!ancMatch.Success)
                            continue;

                        int ax1 = int.Parse(ancMatch.Groups[1].Value);
                        int ay1 = int.Parse(ancMatch.Groups[2].Value);
                        int ax2 = int.Parse(ancMatch.Groups[3].Value);
                        int ay2 = int.Parse(ancMatch.Groups[4].Value);
                        int aw = ax2 - ax1;
                        int ah = ay2 - ay1;

                        if (aw > 400 && ah is >= 40 and <= 200)
                        {
                            rowContainer = anc;
                            break;
                        }
                    }

                    // Check if this row represents a Page by inspecting rowContainer and its descendants/siblings
                    bool isPage = rowContainer
                        .DescendantsAndSelf()
                        .Any(d =>
                        {
                            var dt = d.Attribute("text")?.Value?.Trim();
                            var dd = d.Attribute("content-desc")?.Value?.Trim();
                            return string.Equals(dt, "Page", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(
                                    dt,
                                    "Page profile",
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(dd, "Page", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(
                                    dd,
                                    "Page profile",
                                    StringComparison.OrdinalIgnoreCase
                                );
                        });

                    if (!isPage)
                    {
                        int rowTop = y1 - 20;
                        int rowBottom = y2 + 20;
                        isPage = allNodes.Any(other =>
                        {
                            if (other == allNodes[i])
                                return false;

                            var ob = other.Attribute("bounds")?.Value;
                            if (string.IsNullOrWhiteSpace(ob))
                                return false;

                            var om = Regex.Match(ob, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                            if (!om.Success)
                                return false;

                            int oy =
                                (int.Parse(om.Groups[2].Value) + int.Parse(om.Groups[4].Value)) / 2;
                            if (oy < rowTop || oy > rowBottom)
                                return false;

                            var ot = other.Attribute("text")?.Value?.Trim();
                            var od = other.Attribute("content-desc")?.Value?.Trim();
                            return string.Equals(ot, "Page", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(
                                    ot,
                                    "Page profile",
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(od, "Page", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(
                                    od,
                                    "Page profile",
                                    StringComparison.OrdinalIgnoreCase
                                );
                        });
                    }

                    if (isPage)
                        continue;

                    // Check if selected
                    bool isSelected =
                        rowContainer.Attribute("selected")?.Value == "true"
                        || rowContainer.Attribute("checked")?.Value == "true"
                        || allNodes[i].Attribute("selected")?.Value == "true"
                        || allNodes[i].Attribute("checked")?.Value == "true"
                        || rowContainer
                            .DescendantsAndSelf()
                            .Any(d =>
                                d.Attribute("selected")?.Value == "true"
                                || d.Attribute("checked")?.Value == "true"
                                || string.Equals(
                                    d.Attribute("content-desc")?.Value?.Trim(),
                                    "Selected",
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(
                                    d.Attribute("content-desc")?.Value?.Trim(),
                                    "Checked",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );

                    candidates.Add((allNodes[i], rowContainer, text, isSelected, y1, y2));
                }

                if (candidates.Count == 0)
                    continue;

                // 1. If targetProfileName is specified and matches a candidate:
                if (!string.IsNullOrWhiteSpace(targetProfileName))
                {
                    var matchedCandidate = candidates.FirstOrDefault(c =>
                        c.Name.Contains(targetProfileName, StringComparison.OrdinalIgnoreCase)
                        || targetProfileName.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
                    );

                    if (matchedCandidate != default)
                    {
                        if (matchedCandidate.IsSelected)
                            return null;

                        return matchedCandidate.Node;
                    }
                }

                // 2. Fallback: targetProfileName not specified OR not found in switcher:
                // Return the first candidate that is not selected (unselected personal profile)
                var unselectedCandidate = candidates.FirstOrDefault(c => !c.IsSelected);
                if (unselectedCandidate != default)
                {
                    return unselectedCandidate.Node;
                }
            }
        }
        catch { }

        return null;
    }

    public static (int X, int Y)? FindProfileSwitcherCoordinates(string? menuXml)
    {
        if (string.IsNullOrWhiteSpace(menuXml))
            return null;

        try
        {
            var docs = SafeParseXmlDocuments(menuXml);
            foreach (var doc in docs)
            {
                var allNodes = doc.Descendants("node").ToList();

                // 1. Check for a node with content-desc containing switch / profiles / chevron
                var switcherNode = allNodes.FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value;
                    if (string.IsNullOrWhiteSpace(desc))
                        return false;

                    if (
                        desc.Contains("Search", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("Settings", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return false;
                    }

                    return desc.Contains("switch", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("profile", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("account", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("chevron", StringComparison.OrdinalIgnoreCase);
                });

                if (switcherNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(switcherNode);
                    if (cx > 0 && cy > 0)
                        return (cx, cy);
                }

                // 2. Check for clickable image or button in upper header card on the far right side
                var upperRightClickable = allNodes.FirstOrDefault(n =>
                {
                    var bounds = n.Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(bounds))
                        return false;

                    var desc = n.Attribute("content-desc")?.Value ?? "";
                    var text = n.Attribute("text")?.Value ?? "";
                    if (
                        desc.Contains("Search", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("Settings", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("Search", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("Settings", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return false;
                    }

                    var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!m.Success)
                        return false;

                    int x1 = int.Parse(m.Groups[1].Value);
                    int y1 = int.Parse(m.Groups[2].Value);
                    int x2 = int.Parse(m.Groups[3].Value);
                    int y2 = int.Parse(m.Groups[4].Value);

                    var clickable = n.Attribute("clickable")?.Value == "true";
                    var cls = n.Attribute("class")?.Value ?? "";

                    // In Menu, user card header is y: 140 to 380, switcher chevron is on the far right x >= 540
                    return (
                            clickable
                            || cls.Contains("Image")
                            || cls.Contains("Button")
                            || cls.Contains("View")
                        )
                        && y1 >= 140
                        && y2 <= 380
                        && x1 >= 540;
                });

                if (upperRightClickable != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(upperRightClickable);
                    if (cx > 0 && cy > 0)
                        return (cx, cy);
                }
            }
        }
        catch { }

        return null;
    }

    public static bool IsAccountSwitcherOpen(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        // 1. Textual clues across English and international variants
        if (
            xml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Create Page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Accounts Center", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Go to Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Open profile switcher", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Switch to another profile", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Profiles and Pages", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Your profiles and pages", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("See all profiles", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Log into another account", StringComparison.OrdinalIgnoreCase)
            || (
                xml.Contains("Dismiss", StringComparison.OrdinalIgnoreCase)
                && xml.Contains("Page", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        // 2. Structural clues: bottom sheet or modal container with multiple selectable account items
        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var nodes = doc.Descendants("node").ToList();
            bool hasBottomSheetContainer = nodes.Any(n =>
            {
                var res = n.Attribute("resource-id")?.Value ?? "";
                var cls = n.Attribute("class")?.Value ?? "";
                return res.Contains("bottom_sheet", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("switcher", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("sheet", StringComparison.OrdinalIgnoreCase)
                    || cls.Contains("BottomSheet", StringComparison.OrdinalIgnoreCase);
            });

            if (hasBottomSheetContainer)
                return true;

            int selectableCount = nodes.Count(n =>
                n.Attribute("checked")?.Value == "true" || n.Attribute("selected")?.Value == "true"
            );
            if (
                selectableCount >= 1
                && (
                    xml.Contains("Page", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Profile", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    public static (int Width, int Height) ParseScreenDimensions(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return (0, 0);

        // Hierarchy dumps may list the 48px Android status bar before the Facebook window.
        // Use all visible window bounds; the first [0,0] rectangle is not the display size.
        var width = 0;
        var height = 0;
        foreach (Match bounds in Regex.Matches(xml, @"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]"))
        {
            if (int.TryParse(bounds.Groups[3].Value, out var right))
                width = Math.Max(width, right);
            if (int.TryParse(bounds.Groups[4].Value, out var bottom))
                height = Math.Max(height, bottom);
        }
        return (width, height);
    }

    public static (int X, int Y) ParseNodeBoundsCenter(XElement node)
    {
        var bounds = node.Attribute("bounds")?.Value;
        if (string.IsNullOrWhiteSpace(bounds))
            return (0, 0);

        var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
        if (!m.Success)
            return (0, 0);

        int x1 = int.Parse(m.Groups[1].Value);
        int y1 = int.Parse(m.Groups[2].Value);
        int x2 = int.Parse(m.Groups[3].Value);
        int y2 = int.Parse(m.Groups[4].Value);

        return ((x1 + x2) / 2, (y1 + y2) / 2);
    }

    public static List<XDocument> SafeParseXmlDocuments(string? raw)
    {
        var docs = new List<XDocument>();
        if (string.IsNullOrWhiteSpace(raw))
            return docs;

        var matches = Regex.Matches(
            raw,
            @"<hierarchy[\s\S]*?<\/hierarchy>",
            RegexOptions.IgnoreCase
        );
        if (matches.Count > 0)
        {
            foreach (Match m in matches)
            {
                try
                {
                    docs.Add(XDocument.Parse(m.Value));
                }
                catch { }
            }
        }
        else
        {
            try
            {
                docs.Add(XDocument.Parse(raw));
            }
            catch { }
        }

        return docs;
    }

    public static bool IsIgnoredSwitcherHeaderOrAction(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return true;

        var t = text.Trim();
        return t.Equals("Menu", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Your shortcuts", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All shortcuts", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Create Page", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Accounts Center", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Go to Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Open profile switcher", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
            || t.Equals("See more", StringComparison.OrdinalIgnoreCase)
            || t.Equals("See less", StringComparison.OrdinalIgnoreCase)
            || t.Equals("See all profiles", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Log into another account", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Log in to another account", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Create new profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("See your profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Settings & privacy", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Help & support", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Page", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Meta", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Search", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Switch Profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Switch profile", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNotificationSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.Trim().TrimStart('•', ' ', '\t', '-', '·');
        return Regex.IsMatch(trimmed, @"^\d+\+?\s+notification", RegexOptions.IgnoreCase);
    }
}
