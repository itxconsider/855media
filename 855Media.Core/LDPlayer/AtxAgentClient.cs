using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// High-performance HTTP client for OpenATX on-device daemon (atx-agent).
/// Communicates over HTTP port 7912 (forwarded to localhost) for single-digit ms UI hierarchy dumps,
/// fast element interaction, shell commands, and HTTP file uploads/downloads.
/// </summary>
public class AtxAgentClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly int _basePort;
    private bool _disposed;

    public const int DefaultBasePort = 7912;

    public AtxAgentClient(int basePort = DefaultBasePort, HttpClient? httpClient = null)
    {
        _basePort = basePort;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public int GetDevicePort(int deviceIndex) => _basePort + deviceIndex;

    public string GetDeviceBaseUrl(int deviceIndex) =>
        $"http://127.0.0.1:{GetDevicePort(deviceIndex)}";

    public async Task<bool> IsRunningAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/version";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> DumpHierarchyAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/dump/hierarchy";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var trimmed = raw.TrimStart();
            if (
                trimmed.StartsWith("<hierarchy", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            )
            {
                return raw;
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("result", out var resultProp))
                {
                    var xml = resultProp.GetString();
                    if (
                        !string.IsNullOrWhiteSpace(xml)
                        && xml.Contains("<hierarchy", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return xml;
                    }
                }
            }
            catch { }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> TapAsync(
        int deviceIndex,
        int x,
        int y,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(deviceIndex, $"input tap {x} {y}", cancellationToken);
    }

    public async Task<bool> SwipeAsync(
        int deviceIndex,
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMs = 350,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"input swipe {startX} {startY} {endX} {endY} {durationMs}",
            cancellationToken
        );
    }

    public async Task<bool> SendKeyAsync(
        int deviceIndex,
        int keyCode,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(deviceIndex, $"input keyevent {keyCode}", cancellationToken);
    }

    public async Task<bool> PressHomeAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        return await SendKeyAsync(deviceIndex, 3, cancellationToken); // KEYCODE_HOME
    }

    public async Task<bool> PressBackAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        return await SendKeyAsync(deviceIndex, 4, cancellationToken); // KEYCODE_BACK
    }

    public async Task<bool> PressRecentAppsAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        return await SendKeyAsync(deviceIndex, 187, cancellationToken); // KEYCODE_APP_SWITCH
    }

    public async Task<bool> WakeUpAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        return await SendKeyAsync(deviceIndex, 224, cancellationToken); // KEYCODE_WAKEUP
    }

    public async Task<bool> InputTextAsync(
        int deviceIndex,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(text))
            return true;

        // If text contains non-ASCII characters (e.g. Khmer, Cyrillic, Chinese, Emoji), newlines, or quotes,
        // use ADBKeyboard Base64 broadcast for 100% lossless delivery
        bool requiresAdbKeyboard = text.Any(c =>
            c > 127 || c == '\n' || c == '\r' || c == '"' || c == '\''
        );
        if (requiresAdbKeyboard)
        {
            var b64Ok = await InputTextAdbKeyboardAsync(
                deviceIndex,
                text,
                clearFirst: false,
                cancellationToken
            );
            if (b64Ok)
                return true;
        }

        var escaped = text.Replace(" ", "%s").Replace("\"", "\\\"").Replace("'", "\\'");
        return await ExecuteShellAsync(deviceIndex, $"input text {escaped}", cancellationToken);
    }

    #region ADB Keyboard Integration

    /// <summary>
    /// Inputs text via ADBKeyboard Base64 broadcast (ADB_INPUT_B64).
    /// Supports 100% of Unicode characters (Khmer, Asian scripts, Emojis), quotes, newlines, and symbols.
    /// </summary>
    public async Task<bool> InputTextAdbKeyboardAsync(
        int deviceIndex,
        string text,
        bool clearFirst = false,
        CancellationToken cancellationToken = default
    )
    {
        if (clearFirst)
        {
            await ClearTextAdbKeyboardAsync(deviceIndex, cancellationToken);
        }

        if (string.IsNullOrEmpty(text))
            return true;

        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
        return await ExecuteShellAsync(
            deviceIndex,
            $"am broadcast -a {AdbKeyboardConstants.ActionInputB64} --es msg \"{b64}\"",
            cancellationToken
        );
    }

    /// <summary>
    /// Clears the currently focused text field using ADBKeyboard broadcast (ADB_CLEAR_TEXT).
    /// </summary>
    public async Task<bool> ClearTextAdbKeyboardAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am broadcast -a {AdbKeyboardConstants.ActionClearText}",
            cancellationToken
        );
    }

    /// <summary>
    /// Sends an IME editor action (Search, Send, Done, Go, Next) via ADBKeyboard broadcast (ADB_EDITOR_CODE).
    /// </summary>
    public async Task<bool> SendAdbKeyboardEditorActionAsync(
        int deviceIndex,
        AdbEditorAction action,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am broadcast -a {AdbKeyboardConstants.ActionEditorCode} --ei code {(int)action}",
            cancellationToken
        );
    }

    /// <summary>
    /// Sends a key code through ADBKeyboard (ADB_INPUT_CODE).
    /// </summary>
    public async Task<bool> SendAdbKeyboardCodeAsync(
        int deviceIndex,
        int keyCode,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am broadcast -a {AdbKeyboardConstants.ActionInputCode} --ei code {keyCode}",
            cancellationToken
        );
    }

    /// <summary>
    /// Checks whether ADBKeyboard is installed on the emulator instance.
    /// </summary>
    public async Task<bool> IsAdbKeyboardInstalledAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var (ok, output) = await ExecuteShellWithOutputAsync(
            deviceIndex,
            $"pm list packages {AdbKeyboardConstants.PackageName}",
            cancellationToken
        );
        return ok && output.Contains(AdbKeyboardConstants.PackageName);
    }

    /// <summary>
    /// Checks whether ADBKeyboard is currently the default active IME on the emulator.
    /// </summary>
    public async Task<bool> IsAdbKeyboardActiveAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var (ok, output) = await ExecuteShellWithOutputAsync(
            deviceIndex,
            "settings get secure default_input_method",
            cancellationToken
        );
        return ok && output.Contains(AdbKeyboardConstants.ImeComponent);
    }

    /// <summary>
    /// Enables and sets ADBKeyboard as the active Input Method Editor (IME).
    /// </summary>
    public async Task<bool> EnableAdbKeyboardAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"ime enable {AdbKeyboardConstants.ImeComponent} && ime set {AdbKeyboardConstants.ImeComponent}",
            cancellationToken
        );
    }

    /// <summary>
    /// Gets the component name of the currently active IME (e.g. com.android.adbkeyboard/.AdbIME).
    /// </summary>
    public async Task<string?> GetCurrentImeAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var (ok, output) = await ExecuteShellWithOutputAsync(
            deviceIndex,
            "settings get secure default_input_method",
            cancellationToken
        );
        return ok && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    /// <summary>
    /// Sets the active Input Method Editor (IME) on the emulator.
    /// </summary>
    public async Task<bool> SetImeAsync(
        int deviceIndex,
        string imeComponent,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(deviceIndex, $"ime set {imeComponent}", cancellationToken);
    }

    #endregion

    public async Task<(bool Success, string Output)> ExecuteShellWithOutputAsync(
        int deviceIndex,
        string command,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/shell";
            using var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("command", command),
            ]);
            using var resp = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return (false, string.Empty);

            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
                return (true, string.Empty);

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("output", out var outputProp))
                {
                    var outputStr = outputProp.GetString() ?? string.Empty;
                    bool isErr =
                        doc.RootElement.TryGetProperty("exitCode", out var code)
                        && code.GetInt32() != 0;
                    if (
                        isErr
                        || outputStr.Contains(
                            "SecurityException",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || outputStr.Contains(
                            "Permission Denial",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return (false, outputStr);
                    }
                    return (true, outputStr);
                }
            }
            catch { }

            return (true, raw);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    public async Task<bool> ExecuteShellAsync(
        int deviceIndex,
        string command,
        CancellationToken cancellationToken = default
    )
    {
        var (ok, _) = await ExecuteShellWithOutputAsync(deviceIndex, command, cancellationToken);
        return ok;
    }

    public async Task<bool> StartAppAsync(
        int deviceIndex,
        string packageName,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"monkey -p {packageName} -c android.intent.category.LAUNCHER 1",
            cancellationToken
        );
    }

    public async Task<bool> StopAppAsync(
        int deviceIndex,
        string packageName,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am force-stop {packageName}",
            cancellationToken
        );
    }

    public async Task<bool> ClearAppCacheAsync(
        int deviceIndex,
        string packageName,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"rm -rf /data/data/{packageName}/cache/* /data/data/{packageName}/code_cache/* /sdcard/Android/data/{packageName}/cache/*",
            cancellationToken
        );
    }

    public async Task<bool> OpenDeepLinkAsync(
        int deviceIndex,
        string deepLink,
        string packageName = "com.facebook.katana",
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am start -a android.intent.action.VIEW -d \"{deepLink}\" -p {packageName} -f 0x14000000",
            cancellationToken
        );
    }

    public async Task<bool> ScanMediaFileAsync(
        int deviceIndex,
        string remotePath,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d \"file://{remotePath}\"",
            cancellationToken
        );
    }

    public async Task<bool> ShareVideoAsync(
        int deviceIndex,
        string remotePath,
        string packageName = "com.facebook.katana",
        CancellationToken cancellationToken = default
    )
    {
        return await ExecuteShellAsync(
            deviceIndex,
            $"am start -a android.intent.action.SEND -t \"video/mp4\" --eu android.intent.extra.STREAM \"file://{remotePath}\" -p {packageName}",
            cancellationToken
        );
    }

    public async Task<bool> ShareImageAsync(
        int deviceIndex,
        string remotePath,
        string packageName = "com.facebook.katana",
        CancellationToken cancellationToken = default
    )
    {
        var mimeType = remotePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";
        return await ExecuteShellAsync(
            deviceIndex,
            $"am start -a android.intent.action.SEND -t \"{mimeType}\" --eu android.intent.extra.STREAM \"file://{remotePath}\" -p {packageName}",
            cancellationToken
        );
    }

    public async Task<byte[]?> TakeScreenshotAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/screenshot";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UploadFileAsync(
        int deviceIndex,
        string localFilePath,
        string remoteTargetPath,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localFilePath))
            return false;

        try
        {
            var cleanTarget = remoteTargetPath.TrimStart('/');
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/upload/{cleanTarget}";

            using var content = new MultipartFormDataContent();
            await using var fileStream = File.OpenRead(localFilePath);
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                "application/octet-stream"
            );

            var fileName = Path.GetFileName(localFilePath);
            content.Add(streamContent, "file", fileName);

            using var resp = await _httpClient.PostAsync(url, content, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<byte[]?> DownloadFileBytesAsync(
        int deviceIndex,
        string remoteFilePath,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var cleanPath = remoteFilePath.TrimStart('/');
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/raw/{cleanPath}";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> DownloadFileTextAsync(
        int deviceIndex,
        string remoteFilePath,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var bytes = await DownloadFileBytesAsync(
                deviceIndex,
                remoteFilePath,
                cancellationToken
            );
            return bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAppRunningAsync(
        int deviceIndex,
        string packageName,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/pidof/{packageName}";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AtxDeviceInfo?> GetDeviceInfoAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var url = $"{GetDeviceBaseUrl(deviceIndex)}/info";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var info = new AtxDeviceInfo
            {
                Serial = root.TryGetProperty("serial", out var s) ? s.GetString() ?? "" : "",
                Brand = root.TryGetProperty("brand", out var b) ? b.GetString() ?? "" : "",
                Model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                Sdk = root.TryGetProperty("sdk", out var sdk) ? sdk.GetInt32() : 0,
                AgentVersion = root.TryGetProperty("agentVersion", out var av)
                    ? av.GetString() ?? ""
                    : "",
            };

            if (root.TryGetProperty("display", out var disp))
            {
                if (disp.TryGetProperty("width", out var w))
                    info.DisplayWidth = w.GetInt32();
                if (disp.TryGetProperty("height", out var h))
                    info.DisplayHeight = h.GetInt32();
            }

            if (root.TryGetProperty("battery", out var bat))
            {
                if (bat.TryGetProperty("level", out var l))
                    info.BatteryLevel = l.GetInt32();
            }

            if (root.TryGetProperty("memory", out var mem))
            {
                if (mem.TryGetProperty("around", out var a))
                    info.MemoryAround = a.GetString() ?? "";
            }

            return info;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Found, int X, int Y)> FindElementBoundsAsync(
        int deviceIndex,
        string? text = null,
        string? resourceId = null,
        string? contentDesc = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await DumpHierarchyAsync(deviceIndex, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return (false, 0, 0);

        try
        {
            var doc = XDocument.Parse(xml);
            var node = doc.Descendants("node")
                .FirstOrDefault(n =>
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        var nodeText = n.Attribute("text")?.Value;
                        if (
                            nodeText != null
                            && nodeText.Equals(text, StringComparison.OrdinalIgnoreCase)
                        )
                            return true;
                    }
                    if (!string.IsNullOrEmpty(resourceId))
                    {
                        var nodeResId = n.Attribute("resource-id")?.Value;
                        if (
                            nodeResId != null
                            && (
                                nodeResId.Equals(resourceId, StringComparison.OrdinalIgnoreCase)
                                || nodeResId.EndsWith(
                                    "/" + resourceId,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                        )
                            return true;
                    }
                    if (!string.IsNullOrEmpty(contentDesc))
                    {
                        var nodeDesc = n.Attribute("content-desc")?.Value;
                        if (
                            nodeDesc != null
                            && nodeDesc.Equals(contentDesc, StringComparison.OrdinalIgnoreCase)
                        )
                            return true;
                    }
                    return false;
                });

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
                        return (true, (x1 + x2) / 2, (y1 + y2) / 2);
                    }
                }
            }
        }
        catch { }

        return (false, 0, 0);
    }

    public async Task<bool> ClickElementAsync(
        int deviceIndex,
        string? text = null,
        string? resourceId = null,
        string? contentDesc = null,
        CancellationToken cancellationToken = default
    )
    {
        var (found, x, y) = await FindElementBoundsAsync(
            deviceIndex,
            text,
            resourceId,
            contentDesc,
            cancellationToken
        );
        if (!found)
            return false;

        return await TapAsync(deviceIndex, x, y, cancellationToken);
    }

    public async Task<bool> WaitForElementAsync(
        int deviceIndex,
        string? text = null,
        string? resourceId = null,
        string? contentDesc = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < limit && !cancellationToken.IsCancellationRequested)
        {
            var (found, _, _) = await FindElementBoundsAsync(
                deviceIndex,
                text,
                resourceId,
                contentDesc,
                cancellationToken
            );
            if (found)
                return true;

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    #region Bloks Native Shell Integration

    /// <summary>
    /// Parses an accessibility hierarchy XML into structured BloksScreenInfo and BloksElement tree.
    /// </summary>
    public static BloksScreenInfo ParseBloksHierarchy(string? xml, string? currentActivity = null)
    {
        var screen = new BloksScreenInfo
        {
            CurrentActivity = currentActivity ?? string.Empty,
            RawXml = xml,
            IsBloksActive = BloksConstants.IsKnownBloksActivity(currentActivity),
        };

        if (string.IsNullOrWhiteSpace(xml))
            return screen;

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
                return screen;

            BloksElement ParseNode(XElement elem)
            {
                var item = new BloksElement
                {
                    Text = elem.Attribute("text")?.Value ?? string.Empty,
                    ContentDesc = elem.Attribute("content-desc")?.Value ?? string.Empty,
                    ClassName = elem.Attribute("class")?.Value ?? string.Empty,
                    ResourceId = elem.Attribute("resource-id")?.Value,
                    Clickable = elem.Attribute("clickable")?.Value == "true",
                    Scrollable = elem.Attribute("scrollable")?.Value == "true",
                    Selected = elem.Attribute("selected")?.Value == "true",
                    Checked = elem.Attribute("checked")?.Value == "true",
                    Enabled = elem.Attribute("enabled")?.Value != "false",
                };

                var boundsStr = elem.Attribute("bounds")?.Value;
                if (!string.IsNullOrEmpty(boundsStr))
                {
                    var match = Regex.Match(boundsStr, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (match.Success)
                    {
                        item.Bounds = (
                            int.Parse(match.Groups[1].Value),
                            int.Parse(match.Groups[2].Value),
                            int.Parse(match.Groups[3].Value),
                            int.Parse(match.Groups[4].Value)
                        );
                    }
                }

                foreach (var childNode in elem.Elements("node"))
                {
                    item.Children.Add(ParseNode(childNode));
                }

                return item;
            }

            foreach (var childNode in root.Elements("node"))
            {
                screen.RootElements.Add(ParseNode(childNode));
            }

            screen.AllElements = screen.RootElements.SelectMany(r => r.Flatten()).ToList();

            if (!screen.IsBloksActive)
            {
                screen.IsBloksActive = screen.AllElements.Any(e =>
                    (
                        !string.IsNullOrEmpty(e.ClassName)
                        && e.ClassName.Contains("bloks", StringComparison.OrdinalIgnoreCase)
                    )
                    || (
                        !string.IsNullOrEmpty(e.ResourceId)
                        && e.ResourceId.Contains("bloks", StringComparison.OrdinalIgnoreCase)
                    )
                    || e.Matches("Accounts Center", partialMatch: true)
                    || e.Matches("Personal details", partialMatch: true)
                );
            }

            foreach (var elem in screen.AllElements)
            {
                if (
                    !string.IsNullOrWhiteSpace(elem.Text)
                    && !screen.VisibleTexts.Contains(elem.Text)
                )
                {
                    screen.VisibleTexts.Add(elem.Text);
                }
                if (
                    !string.IsNullOrWhiteSpace(elem.ContentDesc)
                    && !screen.ContentDescriptions.Contains(elem.ContentDesc)
                )
                {
                    screen.ContentDescriptions.Add(elem.ContentDesc);
                }
            }
        }
        catch { }

        return screen;
    }

    /// <summary>
    /// Retrieves the current foreground package and activity via dumpsys window / top.
    /// </summary>
    public async Task<string?> GetCurrentFocusedActivityAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var (ok, output) = await ExecuteShellWithOutputAsync(
            deviceIndex,
            "dumpsys window | grep -E \"mCurrentFocus|mFocusedApp\"",
            cancellationToken
        );

        if (ok && !string.IsNullOrWhiteSpace(output))
        {
            var match = Regex.Match(output, @"([a-zA-Z0-9._]+/[a-zA-Z0-9._]+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        var (topOk, topOutput) = await ExecuteShellWithOutputAsync(
            deviceIndex,
            "dumpsys activity top | grep -m 1 \"ACTIVITY \"",
            cancellationToken
        );

        if (topOk && !string.IsNullOrWhiteSpace(topOutput))
        {
            var match = Regex.Match(topOutput, @"ACTIVITY\s+([a-zA-Z0-9._]+/[a-zA-Z0-9._]+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether Meta's Bloks Native Shell (BloksNativeShellActivity, BloksHostedActivity) is active in the foreground.
    /// </summary>
    public async Task<bool> IsBloksNativeShellActiveAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var focus = await GetCurrentFocusedActivityAsync(deviceIndex, cancellationToken);
        if (BloksConstants.IsKnownBloksActivity(focus))
            return true;

        var screen = await DumpBloksScreenAsync(deviceIndex, cancellationToken);
        return screen.IsBloksActive;
    }

    /// <summary>
    /// Navigates directly to a Bloks screen by its App ID (e.g. Accounts Center modular screen) via deep link or am start.
    /// </summary>
    public async Task<bool> OpenBloksNativeShellAsync(
        int deviceIndex,
        string bloksAppId,
        Dictionary<string, string>? parameters = null,
        string packageName = "com.facebook.katana",
        CancellationToken cancellationToken = default
    )
    {
        var uri = $"fb://bloks?app_id={Uri.EscapeDataString(bloksAppId)}";
        if (parameters != null && parameters.Count > 0)
        {
            var jsonParams = JsonSerializer.Serialize(parameters);
            uri += $"&params={Uri.EscapeDataString(jsonParams)}";
        }

        var started = await OpenDeepLinkAsync(deviceIndex, uri, packageName, cancellationToken);
        if (!started)
        {
            started = await ExecuteShellAsync(
                deviceIndex,
                $"am start -n {packageName}/{BloksConstants.BloksNativeShellActivity} --es app_id \"{bloksAppId}\" -f 0x14000000",
                cancellationToken
            );
        }

        return started;
    }

    /// <summary>
    /// Opens Meta Accounts Center hosted inside Bloks Native Shell.
    /// </summary>
    public async Task<bool> OpenBloksAccountsCenterAsync(
        int deviceIndex,
        string packageName = "com.facebook.katana",
        CancellationToken cancellationToken = default
    )
    {
        var ok = await OpenDeepLinkAsync(
            deviceIndex,
            BloksConstants.AccountsCenterDeepLink,
            packageName,
            cancellationToken
        );

        if (!ok)
        {
            ok = await OpenBloksNativeShellAsync(
                deviceIndex,
                BloksConstants.AccountsCenterAppId,
                null,
                packageName,
                cancellationToken
            );
        }

        return ok;
    }

    /// <summary>
    /// Opens the Bloks Profile / Account switcher sheet.
    /// </summary>
    public async Task<bool> OpenBloksProfileSwitcherAsync(
        int deviceIndex,
        string packageName = "com.facebook.katana",
        CancellationToken cancellationToken = default
    )
    {
        return await OpenDeepLinkAsync(
            deviceIndex,
            BloksConstants.SwitchAccountDeepLink,
            packageName,
            cancellationToken
        );
    }

    /// <summary>
    /// Dumps the current UI hierarchy and parses all Bloks elements, bounds, visible texts, and attributes.
    /// </summary>
    public async Task<BloksScreenInfo> DumpBloksScreenAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await DumpHierarchyAsync(deviceIndex, cancellationToken);
        var focus = await GetCurrentFocusedActivityAsync(deviceIndex, cancellationToken);
        return ParseBloksHierarchy(xml, focus);
    }

    /// <summary>
    /// Finds a single element in the Bloks screen hierarchy matching the specified query.
    /// </summary>
    public async Task<BloksElement?> FindBloksElementAsync(
        int deviceIndex,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        CancellationToken cancellationToken = default
    )
    {
        var screen = await DumpBloksScreenAsync(deviceIndex, cancellationToken);
        return screen.FindElement(textOrDesc, partialMatch, caseInsensitive);
    }

    /// <summary>
    /// Finds all elements in the Bloks screen hierarchy matching the specified query.
    /// </summary>
    public async Task<List<BloksElement>> FindAllBloksElementsAsync(
        int deviceIndex,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        CancellationToken cancellationToken = default
    )
    {
        var screen = await DumpBloksScreenAsync(deviceIndex, cancellationToken);
        return screen.FindAllElements(textOrDesc, partialMatch, caseInsensitive);
    }

    /// <summary>
    /// Taps the center of a Bloks element identified by text or content-desc.
    /// </summary>
    public async Task<bool> ClickBloksElementAsync(
        int deviceIndex,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        CancellationToken cancellationToken = default
    )
    {
        var elem = await FindBloksElementAsync(
            deviceIndex,
            textOrDesc,
            partialMatch,
            caseInsensitive,
            cancellationToken
        );
        if (elem == null || (elem.CenterX <= 0 && elem.CenterY <= 0))
            return false;

        return await TapAsync(deviceIndex, elem.CenterX, elem.CenterY, cancellationToken);
    }

    /// <summary>
    /// Identifies the personal profile account in a Bloks profile switcher sheet and returns its click coordinates.
    /// Distinguishes personal profile accounts (no "Page" badge) from Pages (which display a "Page" badge/pill).
    /// </summary>
    public static (int X, int Y)? FindPersonalProfileCoordsInBloks(
        BloksScreenInfo screen,
        string? targetProfileName = null
    )
    {
        if (screen == null || screen.AllElements.Count == 0)
            return null;

        var all = screen.AllElements;
        int screenWidth =
            screen.RootElements.FirstOrDefault()?.Bounds.Right > 0
                ? screen.RootElements.First().Bounds.Right
                : 720;
        int defaultCenterX = screenWidth / 2;

        var candidates =
            new List<(BloksElement Elem, string Name, bool IsSelected, int CenterX, int CenterY)>();

        foreach (var elem in all)
        {
            var text = !string.IsNullOrWhiteSpace(elem.Text)
                ? elem.Text.Trim()
                : elem.ContentDesc.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                continue;

            if (elem.CenterY < 250 || elem.Height < 15 || elem.Width < 30)
                continue;

            // Skip ignored headers, actions, notifications, and badges
            if (
                LDPlayerService.IsIgnoredSwitcherHeaderOrAction(text)
                || LDPlayerService.IsNotificationSubtitle(text)
                || string.Equals(text, "Page", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "Page profile", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            int rowTop = elem.Bounds.Top - 15;
            int rowBottom = elem.Bounds.Bottom + 55;

            // Check if this element or its row contains "Page" badge
            bool isPage = elem.Flatten()
                .Any(c =>
                    string.Equals(c.Text?.Trim(), "Page", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        c.ContentDesc?.Trim(),
                        "Page",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        c.Text?.Trim(),
                        "Page profile",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (!isPage)
            {
                // In flat element lists, check other elements whose CenterY is strictly within [rowTop, rowBottom]
                isPage = all.Any(other =>
                    other != elem
                    && other.CenterY >= rowTop
                    && other.CenterY <= rowBottom
                    && (
                        string.Equals(
                            other.Text?.Trim(),
                            "Page",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            other.ContentDesc?.Trim(),
                            "Page",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            other.Text?.Trim(),
                            "Page profile",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                );
            }

            if (isPage)
                continue;

            bool isSelected =
                elem.Selected
                || elem.Checked
                || elem.Flatten().Any(c => c.Selected || c.Checked)
                || all.Any(other =>
                    other.CenterY >= rowTop
                    && other.CenterY <= rowBottom
                    && (
                        other.Selected
                        || other.Checked
                        || string.Equals(
                            other.ContentDesc?.Trim(),
                            "Selected",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            other.ContentDesc?.Trim(),
                            "Checked",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                );

            int cx = elem.CenterX;
            int cy = elem.CenterY;

            candidates.Add((elem, text, isSelected, cx, cy));
        }

        if (candidates.Count == 0)
            return null;

        // 1. If targetProfileName is provided and matches a candidate:
        if (!string.IsNullOrWhiteSpace(targetProfileName))
        {
            var match = candidates.FirstOrDefault(c =>
                c.Name.Contains(targetProfileName, StringComparison.OrdinalIgnoreCase)
                || targetProfileName.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (match != default)
            {
                if (match.IsSelected)
                    return null;

                return (match.CenterX, match.CenterY);
            }
        }

        // 2. Fallback: targetProfileName not provided or not matched in switcher:
        // Return the first candidate that is not selected (unselected personal profile)
        var unselected = candidates.FirstOrDefault(c => !c.IsSelected);
        if (unselected != default)
        {
            return (unselected.CenterX, unselected.CenterY);
        }

        return null;
    }

    /// <summary>
    /// Checks if the personal profile is already selected in the Bloks profile switcher sheet.
    /// </summary>
    public static bool IsPersonalProfileAlreadySelectedInBloks(
        BloksScreenInfo screen,
        string? targetProfileName = null
    )
    {
        if (screen == null || screen.AllElements.Count == 0)
            return false;

        var all = screen.AllElements;
        var selectedItem = all.FirstOrDefault(e => (e.Selected || e.Checked) && e.CenterY >= 250);
        if (selectedItem == null)
            return false;

        var text = !string.IsNullOrWhiteSpace(selectedItem.Text)
            ? selectedItem.Text.Trim()
            : selectedItem.ContentDesc.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!string.IsNullOrWhiteSpace(targetProfileName))
        {
            return text.Contains(targetProfileName, StringComparison.OrdinalIgnoreCase)
                || targetProfileName.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        // Check if the selected item has a "Page" badge
        bool isPage = all.Any(other =>
            Math.Abs(other.CenterY - selectedItem.CenterY) <= 80
            && (
                string.Equals(other.Text?.Trim(), "Page", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    other.ContentDesc?.Trim(),
                    "Page",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    other.Text?.Trim(),
                    "Page profile",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        );

        return !isPage;
    }

    /// <summary>
    /// Identifies the Menu / Profile Tab on the Facebook navigation bar from BloksScreenInfo.
    /// Handles accounts and pages that do not have a top hamburger button.
    /// Returns the tap coordinates (X, Y) and whether the tab is currently selected (highlighted by blue selection bar).
    /// </summary>
    public static (int X, int Y, bool IsSelected)? FindMenuTabInBloks(
        BloksScreenInfo screen,
        int screenWidth = 720,
        int screenHeight = 1280
    )
    {
        if (screen == null || screen.AllElements.Count == 0)
            return null;

        var all = screen.AllElements;

        // 1. Direct search by accessibility content-desc or text:
        // Examples across Facebook app versions:
        // - "Menu, tab 6 of 6", "Menu, tab 5 of 5"
        // - "Profile, tab 6 of 6", "Profile, tab 5 of 5"
        // - "Account switcher, shortcuts, and settings menu"
        // - "Menu" or "Profile" with Clickable == true
        var directMatch = all.FirstOrDefault(e =>
        {
            var desc = e.ContentDesc?.Trim() ?? string.Empty;
            var text = e.Text?.Trim() ?? string.Empty;
            var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;
            if (string.IsNullOrWhiteSpace(label))
                return false;

            // Must be near navigation bar (bottom or top bar), not inside feed scroll body
            bool isNavBarArea =
                (e.CenterY > screenHeight * 0.78) || (e.CenterY < screenHeight * 0.22);
            if (!isNavBarArea)
                return false;

            return label.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                || label.Contains(
                    "shortcuts, and settings menu",
                    StringComparison.OrdinalIgnoreCase
                )
                || label.Contains("Account switcher", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(label, "Menu", StringComparison.OrdinalIgnoreCase) && e.Clickable)
                || (
                    string.Equals(label, "Profile", StringComparison.OrdinalIgnoreCase)
                    && e.Clickable
                );
        });

        if (directMatch != null && directMatch.CenterX > 0 && directMatch.CenterY > 0)
        {
            return (directMatch.CenterX, directMatch.CenterY, directMatch.Selected);
        }

        // 2. Structural search on bottom navigation bar:
        // Identify clickable tab buttons in the bottom bar region (Y > 80% screen height, height 25..160)
        var navBarTabs = all.Where(e =>
                e.Clickable && e.CenterY > screenHeight * 0.80 && e.Height >= 25 && e.Height <= 160
            )
            .OrderBy(e => e.CenterX)
            .ToList();

        if (navBarTabs.Count >= 3)
        {
            // The Menu / Profile tab is always the rightmost tab in the navigation bar
            var rightmostTab = navBarTabs.Last();
            return (rightmostTab.CenterX, rightmostTab.CenterY, rightmostTab.Selected);
        }

        return null;
    }

    /// <summary>
    /// Checks whether the screen is currently displaying the Facebook Menu page.
    /// (e.g. contains "Your shortcuts", "Go to Accounts Center", "Create Facebook Page", or header "Menu").
    /// </summary>
    public static bool IsMenuPageInBloks(BloksScreenInfo screen)
    {
        if (screen == null || screen.AllElements.Count == 0)
            return false;

        var all = screen.AllElements;
        return all.Any(e =>
            e.Matches("Your shortcuts", partialMatch: true)
            || e.Matches("Go to Accounts Center", partialMatch: true)
            || e.Matches("Switch profile", partialMatch: true)
            || e.Matches("Create Facebook Page", partialMatch: true)
            || (e.Matches("Menu", partialMatch: false) && e.CenterY < 180)
        );
    }

    /// <summary>
    /// Repeatedly scrolls up or down until the target Bloks element is found and clicks it.
    /// </summary>
    public async Task<bool> ScrollAndClickBloksElementAsync(
        int deviceIndex,
        string textOrDesc,
        int maxSwipes = 5,
        bool scrollDown = true,
        CancellationToken cancellationToken = default
    )
    {
        for (int i = 0; i < maxSwipes; i++)
        {
            if (
                await ClickBloksElementAsync(
                    deviceIndex,
                    textOrDesc,
                    cancellationToken: cancellationToken
                )
            )
                return true;

            int startY = scrollDown ? 900 : 300;
            int endY = scrollDown ? 300 : 900;
            await SwipeAsync(deviceIndex, 360, startY, 360, endY, 350, cancellationToken);
            await Task.Delay(800, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Extracts a paired field value next to or below a specified label in the Bloks screen (e.g. "Contact info", "Birthday").
    /// </summary>
    public async Task<string?> ExtractBloksFieldValueAsync(
        int deviceIndex,
        string fieldLabel,
        CancellationToken cancellationToken = default
    )
    {
        var screen = await DumpBloksScreenAsync(deviceIndex, cancellationToken);
        return screen.ExtractFieldValue(fieldLabel);
    }

    /// <summary>
    /// Extracts Facebook account details (Email, Birthday, Profiles, User ID) directly from Meta's Bloks Accounts Center screen.
    /// </summary>
    public async Task<BloksFacebookAccountInfo> ExtractBloksAccountInfoAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var screen = await DumpBloksScreenAsync(deviceIndex, cancellationToken);
        var info = new BloksFacebookAccountInfo
        {
            AccountsCenterLoaded =
                screen.IsBloksActive
                || screen.VisibleTexts.Any(t =>
                    t.Contains("Accounts Center", StringComparison.OrdinalIgnoreCase)
                ),
        };

        var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        foreach (var text in screen.VisibleTexts)
        {
            var match = emailRegex.Match(text);
            if (match.Success)
            {
                info.Email = match.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(info.Email))
        {
            var contactVal =
                screen.ExtractFieldValue("Contact info") ?? screen.ExtractFieldValue("Email");
            if (!string.IsNullOrEmpty(contactVal))
            {
                var match = emailRegex.Match(contactVal);
                if (match.Success)
                    info.Email = match.Value;
            }
        }

        var birthdayVal =
            screen.ExtractFieldValue("Birthday") ?? screen.ExtractFieldValue("Date of birth");
        if (!string.IsNullOrEmpty(birthdayVal))
        {
            info.Birthday = birthdayVal;
        }

        var profileElem =
            screen.FindElement("Profiles", partialMatch: false)
            ?? screen.FindElement("Accounts", partialMatch: false);
        if (profileElem != null)
        {
            var profileVal = screen.ExtractFieldValue("Profiles");
            if (!string.IsNullOrEmpty(profileVal))
            {
                info.Name = profileVal;
                info.Profiles.Add(profileVal);
            }
        }

        var uidRegex = new Regex(@"\b([0-9]{14,16})\b");
        foreach (var text in screen.VisibleTexts)
        {
            var match = uidRegex.Match(text);
            if (match.Success)
            {
                info.UserId = match.Groups[1].Value;
                break;
            }
        }

        return info;
    }

    /// <summary>
    /// Polls the UI hierarchy until a Bloks element matching the query is visible.
    /// </summary>
    public async Task<bool> WaitForBloksElementAsync(
        int deviceIndex,
        string textOrDesc,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < limit && !cancellationToken.IsCancellationRequested)
        {
            var elem = await FindBloksElementAsync(
                deviceIndex,
                textOrDesc,
                cancellationToken: cancellationToken
            );
            if (elem != null)
                return true;

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
