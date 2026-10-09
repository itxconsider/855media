using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Clean Architecture implementation of UI hierarchy inspection, XPath/XML element clicking,
/// kinetic swipes, touch taps, Unicode text typing, and hardware buttons on LDPlayer instances.
/// </summary>
public class LDPlayerUiAutomationService : ILDPlayerUiAutomationService
{
    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;
    private readonly IAdbKeyboardService? _adbKeyboard;

    public LDPlayerUiAutomationService(
        ILDPlayerService ldPlayer,
        AtxAgentClient? atxClient = null,
        IAdbKeyboardService? adbKeyboard = null
    )
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
        _adbKeyboard = adbKeyboard;
    }

    public async Task<string?> DumpUiHierarchyAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Fast path: Query atx-agent on-device daemon via HTTP (~15ms)
        if (_atxClient != null)
        {
            var atxDump = await _atxClient.DumpHierarchyAsync(index, cancellationToken);
            if (
                !string.IsNullOrWhiteSpace(atxDump)
                && atxDump.Contains("<hierarchy", StringComparison.OrdinalIgnoreCase)
            )
            {
                return atxDump;
            }
        }

        // 2. Standard ADB fallback path (~2.5s)
        await _ldPlayer.ExecuteAdbAsync(
            index,
            "shell uiautomator dump /sdcard/window_dump.xml",
            customPath,
            cancellationToken
        );

        var xmlContent = await _ldPlayer.ExecuteAdbAsync(
            index,
            "shell cat /sdcard/window_dump.xml",
            customPath,
            cancellationToken
        );

        return xmlContent.Contains("<hierarchy", StringComparison.OrdinalIgnoreCase)
            ? xmlContent
            : null;
    }

    public async Task<bool> ClickElementByTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ClickElementAsync(
                index,
                text: text,
                cancellationToken: cancellationToken
            );
            if (ok)
                return true;
        }

        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        return await ClickNodeInXmlAsync(
            index,
            xml,
            n =>
                string.Equals(n.Attribute("text")?.Value, text, StringComparison.OrdinalIgnoreCase),
            customPath,
            cancellationToken
        );
    }

    public async Task<bool> ClickElementByResourceIdAsync(
        int index,
        string resourceId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ClickElementAsync(
                index,
                resourceId: resourceId,
                cancellationToken: cancellationToken
            );
            if (ok)
                return true;
        }

        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        return await ClickNodeInXmlAsync(
            index,
            xml,
            n =>
            {
                var id = n.Attribute("resource-id")?.Value;
                return id != null
                    && (
                        id.Equals(resourceId, StringComparison.OrdinalIgnoreCase)
                        || id.EndsWith("/" + resourceId, StringComparison.OrdinalIgnoreCase)
                    );
            },
            customPath,
            cancellationToken
        );
    }

    public async Task<bool> ClickElementByDescAsync(
        int index,
        string contentDesc,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ClickElementAsync(
                index,
                contentDesc: contentDesc,
                cancellationToken: cancellationToken
            );
            if (ok)
                return true;
        }

        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        return await ClickNodeInXmlAsync(
            index,
            xml,
            n =>
                string.Equals(
                    n.Attribute("content-desc")?.Value,
                    contentDesc,
                    StringComparison.OrdinalIgnoreCase
                ),
            customPath,
            cancellationToken
        );
    }

    public async Task<bool> InputTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(text))
            return true;

        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var atxSent = await _atxClient.InputTextAsync(index, text, cancellationToken);
            if (atxSent)
                return true;
        }

        // Check & enable ADB Keyboard for full Unicode/Emoji/linebreaks support
        if (_adbKeyboard != null)
        {
            bool isInstalled = await _adbKeyboard.IsAdbKeyboardInstalledAsync(
                index,
                customPath,
                cancellationToken
            );
            if (!isInstalled)
            {
                await _adbKeyboard.InstallAdbKeyboardAsync(
                    index,
                    null,
                    customPath,
                    cancellationToken
                );
                isInstalled = await _adbKeyboard.IsAdbKeyboardInstalledAsync(
                    index,
                    customPath,
                    cancellationToken
                );
            }

            if (isInstalled)
            {
                await _adbKeyboard.EnableAdbKeyboardAsync(index, customPath, cancellationToken);
                var sent = await _adbKeyboard.InputTextAdbKeyboardAsync(
                    index,
                    text,
                    clearFirst: false,
                    customPath,
                    cancellationToken
                );
                if (sent)
                    return true;
            }
        }

        // Fallback: ADB shell input text (safe escaping for special characters)
        var escaped = text.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "\\'")
            .Replace(" ", "%s")
            .Replace("#", "\\#")
            .Replace("&", "\\&")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("<", "\\<")
            .Replace(">", "\\>")
            .Replace(";", "\\;")
            .Replace("|", "\\|")
            .Replace("$", "\\$");

        await _ldPlayer.ExecuteAdbAsync(
            index,
            $"shell input text \"{escaped}\"",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> SwipeAsync(
        int index,
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMs = 350,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        await _ldPlayer.ExecuteAdbAsync(
            index,
            $"shell input swipe {startX} {startY} {endX} {endY} {durationMs}",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> TapAsync(
        int index,
        int x,
        int y,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var tapped = await _atxClient.TapAsync(index, x, y, cancellationToken);
            if (tapped)
                return true;
        }

        await _ldPlayer.ExecuteAdbAsync(
            index,
            $"shell input tap {x} {y}",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> SendKeyAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.SendKeyAsync(index, keyCode, cancellationToken);
            if (ok)
                return true;
        }

        await _ldPlayer.ExecuteAdbAsync(
            index,
            $"shell input keyevent {keyCode}",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> PressHomeAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.PressHomeAsync(index, cancellationToken);
        }

        return await SendKeyAsync(index, 3, customPath, cancellationToken); // KEYCODE_HOME = 3
    }

    public async Task<bool> PressBackAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.PressBackAsync(index, cancellationToken);
        }

        return await SendKeyAsync(index, 4, customPath, cancellationToken); // KEYCODE_BACK = 4
    }

    public async Task<bool> PressRecentAppsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.PressRecentAppsAsync(index, cancellationToken);
        }

        return await SendKeyAsync(index, 187, customPath, cancellationToken); // KEYCODE_APP_SWITCH = 187
    }

    private async Task<bool> ClickNodeInXmlAsync(
        int index,
        string xml,
        Func<XElement, bool> predicate,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var node = doc.Descendants("node").FirstOrDefault(predicate);
            if (node != null)
            {
                var bounds = node.Attribute("bounds")?.Value;
                if (!string.IsNullOrEmpty(bounds))
                {
                    var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (match.Success)
                    {
                        int x1 = int.Parse(match.Groups[1].Value);
                        int y1 = int.Parse(match.Groups[2].Value);
                        int x2 = int.Parse(match.Groups[3].Value);
                        int y2 = int.Parse(match.Groups[4].Value);
                        return await TapAsync(
                            index,
                            (x1 + x2) / 2,
                            (y1 + y2) / 2,
                            customPath,
                            cancellationToken
                        );
                    }
                }
            }
        }
        catch { }
        return false;
    }
}
