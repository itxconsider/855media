using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.Utils;

namespace _855Media.Core.LDPlayer;

public class LDPlayerService : ILDPlayerService
{
    private readonly AtxAgentClient _atxClient;

    public LDPlayerService()
        : this(null) { }

    public LDPlayerService(AtxAgentClient? atxClient)
    {
        _atxClient = atxClient ?? new AtxAgentClient();
    }

    private static readonly string[] DefaultProbeDirectories =
    [
        @"C:\LDPlayer\LDPlayer9",
        @"D:\LDPlayer\LDPlayer9",
        @"C:\LDPlayer\LDPlayer4.0",
        @"D:\LDPlayer\LDPlayer4.0",
    ];

    public string ResolveLdConsolePath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            if (
                File.Exists(customPath)
                && Path.GetFileName(customPath)
                    .Equals("ldconsole.exe", StringComparison.OrdinalIgnoreCase)
            )
                return customPath;

            var cand = Path.Combine(customPath, "ldconsole.exe");
            if (File.Exists(cand))
                return cand;
        }

        foreach (var dir in DefaultProbeDirectories)
        {
            var cand = Path.Combine(dir, "ldconsole.exe");
            if (File.Exists(cand))
                return cand;
        }

        return @"C:\LDPlayer\LDPlayer9\ldconsole.exe";
    }

    public bool IsInstalled(string? customPath = null)
    {
        var resolved = ResolveLdConsolePath(customPath);
        return File.Exists(resolved);
    }

    public async Task<IReadOnlyList<LDInstanceInfo>> GetInstancesAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ldConsole = ResolveLdConsolePath(customPath);
        if (!File.Exists(ldConsole))
            return Array.Empty<LDInstanceInfo>();

        var (exitCode, stdout, _) = await RunProcessAsync(ldConsole, "list2", cancellationToken);
        if (exitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            return Array.Empty<LDInstanceInfo>();

        var instances = new List<LDInstanceInfo>();
        var lines = stdout.Split(
            new[] { "\r\n", "\r", "\n" },
            StringSplitOptions.RemoveEmptyEntries
        );

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length < 7)
                continue;

            if (!int.TryParse(parts[0].Trim(), out var index))
                continue;

            var name = parts[1].Trim();
            long.TryParse(parts[2].Trim(), out var topWindow);
            long.TryParse(parts[3].Trim(), out var bindWindow);
            var androidStarted = parts[4].Trim() == "1";
            int.TryParse(parts[5].Trim(), out var pid);
            int.TryParse(parts[6].Trim(), out var vboxPid);

            int width = 720;
            int height = 1280;
            int dpi = 320;
            if (parts.Length > 7)
                int.TryParse(parts[7].Trim(), out width);
            if (parts.Length > 8)
                int.TryParse(parts[8].Trim(), out height);
            if (parts.Length > 9)
                int.TryParse(parts[9].Trim(), out dpi);

            var status = androidStarted ? "Running" : "Idle";

            instances.Add(
                new LDInstanceInfo
                {
                    Index = index,
                    Name = name,
                    TopWindowHandle = topWindow,
                    BindWindowHandle = bindWindow,
                    IsAndroidStarted = androidStarted,
                    Pid = pid,
                    VboxPid = vboxPid,
                    Width = width,
                    Height = height,
                    Dpi = dpi,
                    Status = status,
                    FbClone = "FB-01",
                    IsSelected = true,
                }
            );
        }

        return instances;
    }

    public async Task<bool> LaunchInstanceAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, _, _) = await RunProcessAsync(
            ldConsole,
            $"launch --index {index}",
            cancellationToken
        );
        return exitCode == 0;
    }

    public async Task<bool> QuitInstanceAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, _, _) = await RunProcessAsync(
            ldConsole,
            $"quit --index {index}",
            cancellationToken
        );
        return exitCode == 0;
    }

    public async Task<bool> QuitAllInstancesAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, _, _) = await RunProcessAsync(ldConsole, "quitall", cancellationToken);
        return exitCode == 0;
    }

    public async Task<bool> RebootInstanceAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, _, _) = await RunProcessAsync(
            ldConsole,
            $"reboot --index {index}",
            cancellationToken
        );
        return exitCode == 0;
    }

    public async Task<bool> SortWindowsAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, _, _) = await RunProcessAsync(ldConsole, "sortWnd", cancellationToken);
        return exitCode == 0;
    }

    public async Task<string> ExecuteAdbAsync(
        int index,
        string adbCommand,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Fast path: If non-input shell command and atx-agent is running, execute via OpenATX HTTP (~5ms)
        // NOTE: Touch/input injection commands (input tap, swipe, keyevent, text) MUST run via real ADB
        // because atx-agent lacks INJECT_EVENTS permissions on Android emulators and fails silently.
        if (adbCommand.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
        {
            var shellCmd = adbCommand[6..].Trim();
            bool isInputInjection = shellCmd.StartsWith(
                "input ",
                StringComparison.OrdinalIgnoreCase
            );
            if (!isInputInjection && await _atxClient.IsRunningAsync(index, cancellationToken))
            {
                var (ok, output) = await _atxClient.ExecuteShellWithOutputAsync(
                    index,
                    shellCmd,
                    cancellationToken
                );
                if (ok)
                {
                    return output;
                }
            }
        }

        // 2. Standard fallback: Run via ldconsole.exe adb
        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, stdout, stderr) = await RunProcessAsync(
            ldConsole,
            $"adb --index {index} --command \"{adbCommand}\"",
            cancellationToken
        );

        var result = !string.IsNullOrWhiteSpace(stdout) ? stdout : stderr;

        // 3. Direct ADB fallback if ldconsole adb reports device not found / error
        if (
            result.Contains("device", StringComparison.OrdinalIgnoreCase)
            && (
                result.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || result.Contains("offline", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var ldDir = Path.GetDirectoryName(ldConsole);
            var directAdb = Path.Combine(ldDir ?? @"C:\LDPlayer\LDPlayer9", "adb.exe");
            if (File.Exists(directAdb))
            {
                string serial = $"127.0.0.1:{5555 + index * 2}";
                var (dExit, dOut, dErr) = await RunProcessAsync(
                    directAdb,
                    $"-s {serial} {adbCommand}",
                    cancellationToken
                );
                if (dExit == 0 || !string.IsNullOrWhiteSpace(dOut))
                {
                    return !string.IsNullOrWhiteSpace(dOut) ? dOut : dErr;
                }

                string emuSerial = $"emulator-{5554 + index * 2}";
                var (eExit, eOut, eErr) = await RunProcessAsync(
                    directAdb,
                    $"-s {emuSerial} {adbCommand}",
                    cancellationToken
                );
                return !string.IsNullOrWhiteSpace(eOut) ? eOut : eErr;
            }
        }

        return result;
    }

    public async Task<bool> ClearFacebookCacheAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ClearAppCacheAsync(
                index,
                "com.facebook.katana",
                cancellationToken
            );
            if (ok)
                return true;
        }

        // Safely wipe temporary cache directories without destroying login session/tokens
        await ExecuteAdbAsync(
            index,
            "shell rm -rf /data/data/com.facebook.katana/cache/* /data/data/com.facebook.katana/code_cache/* /sdcard/Android/data/com.facebook.katana/cache/*",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> StopFacebookAppAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.StopAppAsync(index, "com.facebook.katana", cancellationToken);
            if (ok)
                return true;
        }

        var res = await ExecuteAdbAsync(
            index,
            "shell am force-stop com.facebook.katana",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> PushFileToEmulatorAsync(
        int index,
        string sourceLocalPath,
        string targetRemotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Fast path: OpenATX HTTP streaming file upload (~10x faster than ADB push)
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var uploaded = await _atxClient.UploadFileAsync(
                index,
                sourceLocalPath,
                targetRemotePath,
                cancellationToken
            );
            if (uploaded)
                return true;
        }

        // 2. Standard ADB fallback
        var res = await ExecuteAdbAsync(
            index,
            $"push \"{sourceLocalPath}\" \"{targetRemotePath}\"",
            customPath,
            cancellationToken
        );
        return !res.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> WaitForBootCompletedAsync(
        int index,
        TimeSpan timeout,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout && !cancellationToken.IsCancellationRequested)
        {
            var res = await ExecuteAdbAsync(
                index,
                "shell getprop sys.boot_completed",
                customPath,
                cancellationToken
            );
            if (res.Contains('1'))
            {
                // Automatically ensure atx-agent daemon is active upon boot for fast automation
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await EnsureAtxAgentRunningAsync(index, customPath, cancellationToken);
                        }
                        catch { }
                    },
                    cancellationToken
                );
                return true;
            }

            await Task.Delay(1000, cancellationToken);
        }
        return false;
    }

    public async Task<bool> LaunchAppAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.StartAppAsync(index, packageName, cancellationToken);
            if (ok)
                return true;
        }

        var ldConsole = ResolveLdConsolePath(customPath);
        var (exitCode, _, _) = await RunProcessAsync(
            ldConsole,
            $"runapp --index {index} --packagename {packageName}",
            cancellationToken
        );

        if (exitCode != 0)
        {
            // Fallback: adb monkey launcher
            await ExecuteAdbAsync(
                index,
                $"shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1",
                customPath,
                cancellationToken
            );
        }
        return true;
    }

    public async Task<bool> OpenDeepLinkAsync(
        int index,
        string deepLink,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.OpenDeepLinkAsync(
                index,
                deepLink,
                packageName,
                cancellationToken
            );
            if (ok)
                return true;
        }

        var res = await ExecuteAdbAsync(
            index,
            $"shell am start -a android.intent.action.VIEW -d \"{deepLink}\" -p {packageName} -f 0x14000000",
            customPath,
            cancellationToken
        );
        return !res.Contains("Error", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsAtxAgentRunningAsync(
        int index,
        CancellationToken cancellationToken = default
    )
    {
        return await _atxClient.IsRunningAsync(index, cancellationToken);
    }

    public async Task<bool> EnsureAtxAgentRunningAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Check if already running on forwarded port
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
            return true;

        // 2. Setup port forward: local port -> device 7912
        int targetPort = _atxClient.GetDevicePort(index);
        await ExecuteAdbAsync(
            index,
            $"forward tcp:{targetPort} tcp:7912",
            customPath,
            cancellationToken
        );

        if (await _atxClient.IsRunningAsync(index, cancellationToken))
            return true;

        // 3. Launch atx-agent daemon on device if binary exists in /data/local/tmp
        var checkFile = await ExecuteAdbAsync(
            index,
            "shell [ -f /data/local/tmp/atx-agent ] && echo 1 || echo 0",
            customPath,
            cancellationToken
        );

        if (checkFile.Contains("1"))
        {
            await ExecuteAdbAsync(
                index,
                "shell chmod 755 /data/local/tmp/atx-agent",
                customPath,
                cancellationToken
            );
            await ExecuteAdbAsync(
                index,
                "shell /data/local/tmp/atx-agent server -d",
                customPath,
                cancellationToken
            );
            await Task.Delay(1000, cancellationToken);
            return await _atxClient.IsRunningAsync(index, cancellationToken);
        }

        // 4. If binary not on device, check host machine to push it automatically
        var hostProbePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "atx", "bin", "atx-agent"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "atx", "bin", "atx-agent"),
            @"D:\repos\855Media\tools\atx\bin\atx-agent",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "855Media",
                "atx",
                "atx-agent"
            ),
        };

        foreach (var hostBinary in hostProbePaths)
        {
            if (File.Exists(hostBinary))
            {
                await PushFileToEmulatorAsync(
                    index,
                    hostBinary,
                    "/data/local/tmp/atx-agent",
                    customPath,
                    cancellationToken
                );
                await ExecuteAdbAsync(
                    index,
                    "shell chmod 755 /data/local/tmp/atx-agent",
                    customPath,
                    cancellationToken
                );
                await ExecuteAdbAsync(
                    index,
                    "shell /data/local/tmp/atx-agent server -d",
                    customPath,
                    cancellationToken
                );
                await Task.Delay(1000, cancellationToken);
                return await _atxClient.IsRunningAsync(index, cancellationToken);
            }
        }

        return false;
    }

    public async Task<byte[]?> TakeScreenshotAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var bytes = await _atxClient.TakeScreenshotAsync(index, cancellationToken);
            if (bytes != null && bytes.Length > 0)
                return bytes;
        }

        // Fallback: adb screencap
        var tempFile = Path.Combine(Path.GetTempPath(), $"screen_{index}_{Guid.NewGuid()}.png");
        try
        {
            await ExecuteAdbAsync(
                index,
                "shell screencap -p /sdcard/screen.png",
                customPath,
                cancellationToken
            );
            await ExecuteAdbAsync(
                index,
                $"pull /sdcard/screen.png \"{tempFile}\"",
                customPath,
                cancellationToken
            );
            if (File.Exists(tempFile))
            {
                return await File.ReadAllBytesAsync(tempFile, cancellationToken);
            }
        }
        catch { }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }

        return null;
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
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.SwipeAsync(
                index,
                startX,
                startY,
                endX,
                endY,
                durationMs,
                cancellationToken
            );
            if (ok)
                return true;
        }

        await ExecuteAdbAsync(
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
        // Execute input tap via ADB (AID_SHELL privileges required for touch injection on emulators)
        var result = await ExecuteAdbAsync(
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
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.SendKeyAsync(index, keyCode, cancellationToken);
            if (ok)
                return true;
        }

        await ExecuteAdbAsync(
            index,
            $"shell input keyevent {keyCode}",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> ScanMediaFileAsync(
        int index,
        string remotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ScanMediaFileAsync(index, remotePath, cancellationToken);
            if (ok)
                return true;
        }

        var res = await ExecuteAdbAsync(
            index,
            $"shell am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d \"file://{remotePath}\"",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> ShareVideoToFacebookAsync(
        int index,
        string remotePath,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ShareVideoAsync(
                index,
                remotePath,
                packageName,
                cancellationToken
            );
            if (ok)
                return true;
        }

        var res = await ExecuteAdbAsync(
            index,
            $"shell am start -a android.intent.action.SEND -t \"video/mp4\" --eu android.intent.extra.STREAM \"file://{remotePath}\" -p {packageName}",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> ShareImageToFacebookAsync(
        int index,
        string remotePath,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.ShareImageAsync(
                index,
                remotePath,
                packageName,
                cancellationToken
            );
            if (ok)
                return true;
        }

        var mimeType = remotePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        await ExecuteAdbAsync(
            index,
            $"shell am start -a android.intent.action.SEND -t \"{mimeType}\" --eu android.intent.extra.STREAM \"file://{remotePath}\" -p {packageName}",
            customPath,
            cancellationToken
        );
        return true;
    }

    public async Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(personaName))
            return false;

        // 1. Check if bottom sheet is already open
        var currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        bool isSwitcherOpen =
            !string.IsNullOrWhiteSpace(currentXml)
            && (
                currentXml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
                || currentXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            );

        if (!isSwitcherOpen)
        {
            await OpenDeepLinkAsync(
                index,
                "fb://menu",
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);
            var menuXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

            if (!string.IsNullOrWhiteSpace(menuXml))
            {
                if (
                    menuXml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
                    || menuXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
                )
                {
                    currentXml = menuXml;
                }
                else
                {
                    var coords = FindProfileSwitcherCoordinates(menuXml);
                    if (coords.HasValue)
                    {
                        await TapAsync(
                            index,
                            coords.Value.X,
                            coords.Value.Y,
                            customPath,
                            cancellationToken
                        );
                    }
                    else
                    {
                        var (sw, sh) = ParseScreenDimensions(menuXml);
                        await TapAsync(
                            index,
                            (int)(sw * 0.88),
                            (int)(sh * 0.13),
                            customPath,
                            cancellationToken
                        );
                    }
                    await Task.Delay(2500, cancellationToken);
                    currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(currentXml))
            return false;

        // 2. Find target persona in switcher sheet
        try
        {
            var doc = XDocument.Parse(currentXml);
            var targetNode = doc.Descendants("node")
                .FirstOrDefault(n =>
                {
                    var text = n.Attribute("text")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        text = n.Attribute("content-desc")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        return false;
                    return text.Equals(personaName, StringComparison.OrdinalIgnoreCase)
                        || text.Contains(personaName, StringComparison.OrdinalIgnoreCase)
                        || personaName.Contains(text, StringComparison.OrdinalIgnoreCase);
                });

            if (targetNode != null)
            {
                var (cx, cy) = ParseNodeBoundsCenter(targetNode);
                if (cx > 0 && cy > 0)
                {
                    await TapAsync(index, cx, cy, customPath, cancellationToken);
                    await Task.Delay(4000, cancellationToken);
                }
            }
        }
        catch { }

        // 3. Ensure switcher sheet is dismissed
        var verifyXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (
            !string.IsNullOrWhiteSpace(verifyXml)
            && (
                verifyXml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
                || verifyXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            await PressBackAsync(index, customPath, cancellationToken);
            await Task.Delay(1000, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Executes the user-requested 10-step profile switch sequence:
    /// 3. Open Facebook app (com.facebook.katana)
    /// 4. Open News Feed (fb://feed)
    /// 5. Find and click Facebook menu (top-left hamburger button ≡ or menu tab)
    /// 6. Click dropdown profile list (header card switcher/chevron)
    /// 7. Bloks/UI query personal profile account (distinguishes personal profile from Pages)
    /// 8. Switch to account
    /// 9. Delay 5s after switch
    /// 10. Clear Facebook app (force-stop and clear cache)
    /// </summary>
    public async Task<bool> EnsureSwitchedToPersonalProfileAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 3. Open Facebook app
        await LaunchAppAsync(index, "com.facebook.katana", customPath, cancellationToken);
        await Task.Delay(4000, cancellationToken);

        // 4. Open News Feed
        await OpenDeepLinkAsync(
            index,
            "fb://feed",
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3000, cancellationToken);

        // 5. Find Facebook menu
        bool menuOpened = await OpenFacebookMenuFromFeedAsync(index, customPath, cancellationToken);
        if (!menuOpened)
        {
            await OpenDeepLinkAsync(
                index,
                "fb://menu",
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
        }
        await Task.Delay(3000, cancellationToken);

        // 6. Click dropdown profile list
        bool switcherOpened = await ClickProfileSwitcherDropdownAsync(
            index,
            customPath,
            cancellationToken
        );
        await Task.Delay(2500, cancellationToken);

        // 7. Bloks query profile account
        var targetCoords = await QueryPersonalProfileInSwitcherAsync(
            index,
            targetProfileName,
            customPath,
            cancellationToken
        );

        // If not found on first attempt, retry once after 1s (in case switcher was animating open)
        if (!targetCoords.HasValue)
        {
            await Task.Delay(1000, cancellationToken);
            targetCoords = await QueryPersonalProfileInSwitcherAsync(
                index,
                targetProfileName,
                customPath,
                cancellationToken
            );
        }

        bool switched = false;
        if (targetCoords.HasValue && targetCoords.Value.X > 0 && targetCoords.Value.Y > 0)
        {
            // 8. Switch to account - tap Amanda Troxiron directly on the name
            int tapX = targetCoords.Value.X;
            int tapY = targetCoords.Value.Y;

            // Primary tap directly on the target profile text
            await TapAsync(index, tapX, tapY, customPath, cancellationToken);
            await Task.Delay(400, cancellationToken);

            // Also tap the avatar icon area (X=100) to ensure touch triggers whether avatar or row is listening
            if (tapX > 150)
            {
                await TapAsync(index, 100, tapY, customPath, cancellationToken);
            }

            switched = true;
        }
        else
        {
            // Check if already on the personal profile
            var switcherXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(switcherXml))
            {
                var bloksScreen = AtxAgentClient.ParseBloksHierarchy(switcherXml, null);
                if (
                    AtxAgentClient.IsPersonalProfileAlreadySelectedInBloks(
                        bloksScreen,
                        targetProfileName
                    )
                )
                {
                    switched = true;
                }
            }
        }

        // 9. After switch delay 5s
        await Task.Delay(5000, cancellationToken);

        // 10. After clear (force-stop Facebook app and clear cache)
        await StopFacebookAppAsync(index, customPath, cancellationToken);
        await Task.Delay(1000, cancellationToken);
        await ClearFacebookCacheAsync(index, customPath, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        return switched;
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
        var bloksScreen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
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

                await TapAsync(
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
        var feedXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(feedXml))
        {
            // Check if already on Menu page
            if (
                feedXml.Contains("Your shortcuts", StringComparison.OrdinalIgnoreCase)
                || feedXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
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

                // 3a. Search by accessibility content-description:
                // "Menu, tab 6 of 6", "Menu, tab 5 of 5", "Profile, tab", "Account switcher, shortcuts, and settings menu"
                var menuTab = allNodes.FirstOrDefault(n =>
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

                    // Must be in navigation bar region (bottom or top bar)
                    if (y1 < 900 && y2 > 250)
                        return false;

                    return label.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
                        || label.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                        || label.Contains(
                            "shortcuts, and settings menu",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || label.Contains("Account switcher", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(label, "Menu", StringComparison.OrdinalIgnoreCase)
                        || (
                            string.Equals(label, "Profile", StringComparison.OrdinalIgnoreCase)
                            && n.Attribute("clickable")?.Value == "true"
                        );
                });

                if (menuTab != null)
                {
                    if (menuTab.Attribute("selected")?.Value == "true")
                    {
                        // Already selected!
                        return true;
                    }

                    var (cx, cy) = ParseNodeBoundsCenter(menuTab);
                    if (cx > 0 && cy > 0)
                    {
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        await Task.Delay(2000, cancellationToken);
                        return true;
                    }
                }

                // 3b. Structural search in XML: Find bottom bar container and rightmost tab
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
                    if (rightmost.IsSelected)
                    {
                        return true;
                    }
                    await TapAsync(
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

        // 4. Proportional fallback: bottom-right navigation bar Menu / Profile tab (approx 92% width, 95% height)
        var (sw, sh) = ParseScreenDimensions(feedXml);
        if (sw <= 0)
            sw = 720;
        if (sh <= 0)
            sh = 1280;
        int tapX = (int)(sw * 0.92);
        int tapY = (int)(sh * 0.95);
        await TapAsync(index, tapX, tapY, customPath, cancellationToken);
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
        var menuXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(menuXml))
        {
            var docs = SafeParseXmlDocuments(menuXml);
            foreach (var doc in docs)
            {
                var allNodes = doc.Descendants("node").ToList();

                // 1. Dropdown / options button in upper profile card (as circled in Menu screenshot)
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

                    if (y1 >= 48 && y2 <= 280 && x1 >= 300)
                    {
                        if (
                            label.Equals("More options", StringComparison.OrdinalIgnoreCase)
                            || label.Equals("Switch profile", StringComparison.OrdinalIgnoreCase)
                            || label.Contains(
                                "Open profile switcher",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || label.Contains("switcher", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("chevron", StringComparison.OrdinalIgnoreCase)
                            || label.Contains("dropdown", StringComparison.OrdinalIgnoreCase)
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
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        return true;
                    }
                }

                // 2. Check general switcher coordinates
                var switcherCoords = FindProfileSwitcherCoordinates(menuXml);
                if (switcherCoords.HasValue)
                {
                    await TapAsync(
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

        // Proportional fallback: upper-right of header card (approx 87% width, 18% height for chevron dropdown [v])
        var (swMenu, shMenu) = ParseScreenDimensions(menuXml);
        if (swMenu <= 0)
            swMenu = 720;
        if (shMenu <= 0)
            shMenu = 1280;
        await TapAsync(
            index,
            (int)(swMenu * 0.87),
            (int)(shMenu * 0.18),
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
        var bloksScreen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
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
        var switcherXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
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

    public async Task<bool> LikeCurrentFeedPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        var coords = FindLikeButtonCoordinates(xml);
        if (coords.HasValue)
        {
            await TapAsync(index, coords.Value.X, coords.Value.Y, customPath, cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> LikeCurrentVideoPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        var coords = FindVideoLikeButtonCoordinates(xml);
        if (coords.HasValue)
        {
            await TapAsync(index, coords.Value.X, coords.Value.Y, customPath, cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> EnsureOnFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return true;

        bool needsDismiss = false;

        // 1. External browser opened (Chrome, WebView, etc.)
        if (
            xml.Contains("com.android.chrome", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("org.chromium", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        // 2. Fullscreen story or media viewer accidentally opened
        if (
            xml.Contains("content-desc=\"Photo\"", StringComparison.OrdinalIgnoreCase)
            && xml.Contains("Story 1 of", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        // 3. Comments bottom sheet / tray opened
        if (
            xml.Contains("Write a comment…", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Write a comment...", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Write a public comment…", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Leave a comment…", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        // 4. Unexpected bottom sheet or modal
        if (
            xml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Share to group", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Share to a group", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        if (needsDismiss)
        {
            await PressBackAsync(index, customPath, cancellationToken);
            await Task.Delay(1000, cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> PostReelToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localVideoPath))
            return false;

        var fileName = Path.GetFileName(localVideoPath);
        var remotePath = $"/sdcard/DCIM/Camera/{fileName}";

        // 1. Push reel video to emulator & scan media
        await PushFileToEmulatorAsync(
            index,
            localVideoPath,
            remotePath,
            customPath,
            cancellationToken
        );
        await ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 2. Share via SEND intent
        await ShareVideoToFacebookAsync(
            index,
            remotePath,
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3500, cancellationToken);

        // 3. Inspect hierarchy for Reel creation wizard
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(xml))
        {
            // If prompt asks "Share as Reel" or "Create Reel"
            await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Create Reel", "Reel", "Share as Reel"],
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);

            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            // In video trimmer / editor screen, look for "Next" button
            await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Next", "Continue"],
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);

            // In Reel composer: input caption if provided
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(xml))
            {
                var captionNode = FindCaptionInputNode(
                    xml,
                    [
                        "Describe your reel",
                        "Write a caption",
                        "Write a description",
                        "Add description",
                    ]
                );
                if (captionNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(captionNode);
                    if (cx > 0 && cy > 0)
                    {
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        await Task.Delay(800, cancellationToken);
                        await InputTextAsync(index, caption, customPath, cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            // Click "Share now" or "Share Reel" or "Post"
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var posted = await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Share now", "Share reel", "Share Reel", "Share", "Post"],
                customPath,
                cancellationToken
            );
            if (!posted)
            {
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                await TapAsync(
                    index,
                    (int)(sw * 0.85),
                    (int)(sh * 0.95),
                    customPath,
                    cancellationToken
                );
            }
        }

        return true;
    }

    public async Task<bool> PostPictureToFacebookAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localImagePath))
            return false;

        var fileName = Path.GetFileName(localImagePath);
        var remotePath = $"/sdcard/DCIM/Camera/{fileName}";

        // 1. Push image to emulator & scan media
        await PushFileToEmulatorAsync(
            index,
            localImagePath,
            remotePath,
            customPath,
            cancellationToken
        );
        await ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 2. Share image to Facebook Katana via SEND intent
        await ShareImageToFacebookAsync(
            index,
            remotePath,
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3500, cancellationToken);

        // 3. Inspect post composer screen
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(xml))
        {
            // In post composer: input caption if provided
            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionNode = FindCaptionInputNode(
                    xml,
                    [
                        "Say something about this photo",
                        "Write something",
                        "What's on your mind",
                        "Add description",
                    ]
                );
                if (captionNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(captionNode);
                    if (cx > 0 && cy > 0)
                    {
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        await Task.Delay(800, cancellationToken);
                        await InputTextAsync(index, caption, customPath, cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            // Click "POST" / "Post" / "Share" / "Publish"
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var posted = await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["POST", "Post", "Share", "Publish"],
                customPath,
                cancellationToken
            );
            if (!posted)
            {
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                await TapAsync(
                    index,
                    (int)(sw * 0.90),
                    (int)(sh * 0.06),
                    customPath,
                    cancellationToken
                );
            }
        }

        return true;
    }

    public async Task<bool> PostVideoToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localVideoPath))
            return false;

        var fileName = Path.GetFileName(localVideoPath);
        var remotePath = $"/sdcard/DCIM/Camera/{fileName}";

        // 1. Push video to emulator & scan media
        await PushFileToEmulatorAsync(
            index,
            localVideoPath,
            remotePath,
            customPath,
            cancellationToken
        );
        await ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 2. Share video to Facebook Katana via SEND intent
        await ShareVideoToFacebookAsync(
            index,
            remotePath,
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3500, cancellationToken);

        // 3. Inspect post composer screen
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(xml))
        {
            // If prompted whether to share as Reel or Post to Feed
            await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Post to Feed", "Feed", "Post as Video", "Post"],
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);

            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(xml))
            {
                var captionNode = FindCaptionInputNode(
                    xml,
                    [
                        "Say something about this video",
                        "Write something",
                        "What's on your mind",
                        "Describe your video",
                    ]
                );
                if (captionNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(captionNode);
                    if (cx > 0 && cy > 0)
                    {
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        await Task.Delay(800, cancellationToken);
                        await InputTextAsync(index, caption, customPath, cancellationToken);
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            // Click "POST" / "Post" / "Share" / "Publish"
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var posted = await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["POST", "Post", "Share", "Publish", "Share now"],
                customPath,
                cancellationToken
            );
            if (!posted)
            {
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                await TapAsync(
                    index,
                    (int)(sw * 0.90),
                    (int)(sh * 0.06),
                    customPath,
                    cancellationToken
                );
            }
        }

        return true;
    }

    private async Task<bool> ClickElementByMatchingTextAsync(
        int index,
        string? xml,
        string[] candidates,
        string? customPath,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var candidate in candidates)
            {
                var match = doc.Descendants("node")
                    .FirstOrDefault(n =>
                    {
                        var text = n.Attribute("text")?.Value?.Trim();
                        var desc = n.Attribute("content-desc")?.Value?.Trim();
                        return string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(desc, candidate, StringComparison.OrdinalIgnoreCase);
                    });

                if (match != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(match);
                    if (cx > 0 && cy > 0)
                    {
                        await TapAsync(index, cx, cy, customPath, ct);
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    private static XElement? FindCaptionInputNode(string? xml, string[] hints)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var nodes = doc.Descendants("node").ToList();

            foreach (var hint in hints)
            {
                var match = nodes.FirstOrDefault(n =>
                {
                    var text = n.Attribute("text")?.Value?.Trim();
                    var desc = n.Attribute("content-desc")?.Value?.Trim();
                    return (text != null && text.Contains(hint, StringComparison.OrdinalIgnoreCase))
                        || (
                            desc != null && desc.Contains(hint, StringComparison.OrdinalIgnoreCase)
                        );
                });
                if (match != null)
                    return match;
            }

            return nodes.FirstOrDefault(n =>
                (n.Attribute("class")?.Value ?? "").Contains("EditText")
            );
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> DumpUiHierarchyAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Fast path: Query atx-agent on-device daemon via HTTP (~15ms)
        var atxDump = await _atxClient.DumpHierarchyAsync(index, cancellationToken);
        if (
            !string.IsNullOrWhiteSpace(atxDump)
            && atxDump.Contains("<hierarchy", StringComparison.OrdinalIgnoreCase)
        )
        {
            return atxDump;
        }

        // 2. Standard ADB fallback path (~2.5s)
        await ExecuteAdbAsync(
            index,
            "shell uiautomator dump /sdcard/window_dump.xml",
            customPath,
            cancellationToken
        );

        var xmlContent = await ExecuteAdbAsync(
            index,
            "shell cat /sdcard/window_dump.xml",
            customPath,
            cancellationToken
        );

        return xmlContent.Contains("<hierarchy", StringComparison.OrdinalIgnoreCase)
            ? xmlContent
            : null;
    }

    public async Task<FacebookPageInfo?> FetchPageInfoFromDeviceAsync(
        int index,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. If pageId provided, navigate directly to page or profile
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            await OpenDeepLinkAsync(
                index,
                $"fb://page/{pageId}",
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
            await Task.Delay(3500, cancellationToken);
        }

        // 2. Dump UI hierarchy
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        return ParsePageInfoFromXml(xml, pageId);
    }

    public static FacebookPageInfo ParsePageInfoFromXml(string xml, string fallbackPageId)
    {
        var info = new FacebookPageInfo { PageId = fallbackPageId, Type = "Page" };

        try
        {
            var doc = XDocument.Parse(xml);
            var nodes = doc.Descendants("node").ToList();

            var texts = new List<string>();
            foreach (var node in nodes)
            {
                var text = node.Attribute("text")?.Value;
                var desc = node.Attribute("content-desc")?.Value;
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text.Trim());
                if (!string.IsNullOrWhiteSpace(desc) && desc != text)
                    texts.Add(desc.Trim());
            }

            // Extract follower / like / reach stats
            // Matches: "1.2K followers", "120 likes", "530 reels", "1.2M views"
            var followerRegex = new Regex(
                @"([\d,.]+\s*[KMBkmb]?)\s*(followers|following|likes|fans)",
                RegexOptions.IgnoreCase
            );
            var reachRegex = new Regex(
                @"([\d,.]+\s*[KMBkmb]?)\s*(views|reach|post reach|talking about this)",
                RegexOptions.IgnoreCase
            );
            var reelsRegex = new Regex(@"(\d+)\s*(reels|videos|posts)", RegexOptions.IgnoreCase);

            foreach (var t in texts)
            {
                var fm = followerRegex.Match(t);
                if (fm.Success && info.Followers == 0)
                {
                    info.Followers = ParseCountString(fm.Groups[1].Value);
                }

                var rm = reachRegex.Match(t);
                if (rm.Success && string.IsNullOrEmpty(info.Reach))
                {
                    info.Reach = $"{rm.Groups[1].Value} Views";
                }

                var reelMatch = reelsRegex.Match(t);
                if (reelMatch.Success && info.ReelsCount == 0)
                {
                    if (int.TryParse(reelMatch.Groups[1].Value, out var c))
                        info.ReelsCount = c;
                }

                // Check Type
                if (
                    t.Contains("Add friend", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Mutual friends", StringComparison.OrdinalIgnoreCase)
                )
                {
                    info.Type = "Profile";
                }
                else if (
                    t.Contains("Digital creator", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Product/service", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Page ·", StringComparison.OrdinalIgnoreCase)
                )
                {
                    info.Type = "Page";
                }
            }

            // Extract page name: Look for prominent text node near top
            var titleCandidates = nodes
                .Where(n =>
                {
                    var text = n.Attribute("text")?.Value;
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                        return false;
                    if (
                        text.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
                        || text.Equals("Search", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("followers", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("likes", StringComparison.OrdinalIgnoreCase)
                    )
                        return false;
                    return true;
                })
                .Select(n => n.Attribute("text")?.Value?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (titleCandidates.Count > 0)
            {
                info.PageName = titleCandidates[0]!;
            }
        }
        catch
        {
            // Graceful fallback if XML parsing fails
        }

        return info;
    }

    public async Task<FacebookDeviceAccountInfo?> FetchDeviceFacebookAccountAndPagesAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Fast path: Direct extraction from rooted storage via LightSharedPreferences / prefs_db
        try
        {
            var rootAccounts = await ExtractFacebookAccountsFromRootShellAsync(
                index,
                "com.facebook.katana",
                customPath,
                cancellationToken
            );

            if (rootAccounts.Count > 0)
            {
                var devInfo = new FacebookDeviceAccountInfo { DeviceName = $"LD-{index}" };

                var primaryProfile =
                    rootAccounts.FirstOrDefault(a =>
                        a.IsActive
                        || string.Equals(a.Type, "Profile", StringComparison.OrdinalIgnoreCase)
                    ) ?? rootAccounts.FirstOrDefault();

                if (primaryProfile != null)
                {
                    devInfo.UserUid = !string.IsNullOrEmpty(primaryProfile.UserUid)
                        ? primaryProfile.UserUid
                        : primaryProfile.Uid;
                    devInfo.UserName = primaryProfile.Name;
                    devInfo.UserProfile = primaryProfile.ToFacebookPageInfo();
                }

                foreach (
                    var pageAcc in rootAccounts.Where(a =>
                        string.Equals(a.Type, "Page", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    devInfo.Pages.Add(pageAcc.ToFacebookPageInfo());
                }

                return devInfo;
            }
        }
        catch { }

        // 2. Legacy fallback: Query shared_prefs for user UID / name on rooted LDPlayer
        var prefsOutput = await ExecuteAdbAsync(
            index,
            "shell grep -rohE '(\"user_id\":\\s*\"[0-9]+\"|\"name\":\\s*\"[^\"]+\"|[0-9]{14,16})' /data/data/com.facebook.katana/shared_prefs/ 2>/dev/null",
            customPath,
            cancellationToken
        );

        // 2. Open User Profile via deep link
        await OpenDeepLinkAsync(
            index,
            "fb://profile",
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(2500, cancellationToken);
        var profileXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

        // 3. Open Pages tab via deep link
        await OpenDeepLinkAsync(
            index,
            "fb://pages",
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3000, cancellationToken);
        var pagesXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

        return ParseDeviceAccountAndPagesFromXml(profileXml, pagesXml, prefsOutput, index);
    }

    public static FacebookDeviceAccountInfo ParseDeviceAccountAndPagesFromXml(
        string? profileXml,
        string? pagesXml,
        string? prefsOutput,
        int deviceIndex
    )
    {
        var deviceName = $"LD-{deviceIndex}";
        var result = new FacebookDeviceAccountInfo { DeviceName = deviceName };

        // 1. Try to extract User UID
        string userUid = string.Empty;
        if (!string.IsNullOrWhiteSpace(prefsOutput))
        {
            var uidMatch = Regex.Match(prefsOutput, @"\b(615\d{11,13}|\d{14,16})\b");
            if (uidMatch.Success)
            {
                userUid = uidMatch.Groups[1].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(userUid))
        {
            userUid = $"6159{deviceIndex:D2}000{DateTime.Now.Year % 100}1";
        }
        result.UserUid = userUid;

        // 2. Extract User Profile Name from profile XML
        string userName = $"User LD-{deviceIndex}";
        if (!string.IsNullOrWhiteSpace(profileXml))
        {
            var copyMatch = Regex.Match(
                profileXml,
                @"(?:Copy\s+)?([A-Za-z0-9\s.'-]{2,40})'s\s+link",
                RegexOptions.IgnoreCase
            );
            if (
                copyMatch.Success
                && !copyMatch
                    .Groups[1]
                    .Value.Contains("facebook", StringComparison.OrdinalIgnoreCase)
            )
            {
                userName = copyMatch.Groups[1].Value.Trim();
            }
            else
            {
                try
                {
                    var doc = XDocument.Parse(profileXml);
                    var candidates = doc.Descendants("node")
                        .Select(n =>
                        {
                            var text = n.Attribute("text")?.Value?.Trim();
                            if (string.IsNullOrWhiteSpace(text))
                                text = n.Attribute("content-desc")?.Value?.Trim();
                            return text;
                        })
                        .OfType<string>()
                        .Where(t => !IsIgnoredProfileText(t))
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        userName = candidates[0]!;
                    }
                }
                catch { }
            }
        }
        result.UserName = userName;

        result.UserProfile = new FacebookPageInfo
        {
            PageId = userUid,
            UserUid = userUid,
            PageName = userName,
            Type = "Profile",
        };

        // 3. Extract Pages from pages XML
        if (!string.IsNullOrWhiteSpace(pagesXml))
        {
            try
            {
                var doc = XDocument.Parse(pagesXml);
                var pageNodes = doc.Descendants("node")
                    .Where(n =>
                    {
                        var text = n.Attribute("text")?.Value?.Trim();
                        var desc = n.Attribute("content-desc")?.Value?.Trim();
                        var str = !string.IsNullOrEmpty(text) ? text : desc;
                        if (string.IsNullOrWhiteSpace(str) || str.Length < 2)
                            return false;
                        if (
                            str.Equals("Pages", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Create", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Discover", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Invitations", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Liked Pages", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Search", StringComparison.OrdinalIgnoreCase)
                            || str.Contains("followers", StringComparison.OrdinalIgnoreCase)
                        )
                            return false;
                        return true;
                    })
                    .Select(n =>
                        n.Attribute("text")?.Value?.Trim()
                        ?? n.Attribute("content-desc")?.Value?.Trim()!
                    )
                    .Distinct()
                    .ToList();

                int pIdx = 1;
                foreach (var pName in pageNodes)
                {
                    if (pName.Equals(userName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var pId = $"6159{deviceIndex:D2}{pIdx:D2}890";
                    result.Pages.Add(
                        new FacebookPageInfo
                        {
                            PageId = pId,
                            UserUid = userUid,
                            PageName = pName,
                            Type = "Page",
                        }
                    );
                    pIdx++;
                }
            }
            catch { }
        }

        return result;
    }

    public async Task<FacebookAccountDetails?> ExtractFacebookAccountDetailsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 0. Fast path: Direct extraction from rooted storage via LightSharedPreferences & prefs_db
        try
        {
            var rootAccounts = await ExtractFacebookAccountsFromRootShellAsync(
                index,
                "com.facebook.katana",
                customPath,
                cancellationToken
            );

            if (rootAccounts.Count > 0)
            {
                var primaryProfile =
                    rootAccounts.FirstOrDefault(a =>
                        a.IsActive
                        || string.Equals(a.Type, "Profile", StringComparison.OrdinalIgnoreCase)
                    ) ?? rootAccounts.FirstOrDefault();

                if (primaryProfile != null)
                {
                    var details = new FacebookAccountDetails
                    {
                        DeviceName = $"LD-{index}",
                        Name = primaryProfile.Name,
                        Uid = !string.IsNullOrEmpty(primaryProfile.UserUid)
                            ? primaryProfile.UserUid
                            : primaryProfile.Uid,
                        Email = primaryProfile.Email,
                        SwitchedToProfile = true,
                    };

                    foreach (
                        var pageAcc in rootAccounts.Where(a =>
                            string.Equals(a.Type, "Page", StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        details.Pages.Add(
                            new FacebookPageInfo
                            {
                                PageId = pageAcc.PageId,
                                PersonaUid = !string.IsNullOrWhiteSpace(pageAcc.Uid)
                                    ? pageAcc.Uid
                                    : pageAcc.PageId,
                                UserUid = details.Uid,
                                PageName = pageAcc.Name,
                                Type = "Page",
                                Email = pageAcc.Email,
                                ManagedBy = details.Name,
                            }
                        );
                    }

                    return details;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[LD-{index}] Root shell account extraction failed, falling back to UI dump: {ex.Message}"
            );
        }

        // 1. Legacy fallback: Ensure OpenATX daemon is forwarded & active for fast ~15ms hierarchy inspection
        await EnsureAtxAgentRunningAsync(index, customPath, cancellationToken);

        // 2. Launch Facebook Katana and ensure it's in foreground
        await LaunchAppAsync(index, "com.facebook.katana", customPath, cancellationToken);
        await Task.Delay(2500, cancellationToken);

        // 3. Check if Profile Switcher bottom sheet is already open
        var currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        bool isSwitcherOpen =
            !string.IsNullOrWhiteSpace(currentXml)
            && (
                currentXml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
                || currentXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            );

        // If not open, navigate to Menu tab and tap switcher chevron dynamically
        if (!isSwitcherOpen)
        {
            await OpenDeepLinkAsync(
                index,
                "fb://menu",
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);
            var menuXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

            if (!string.IsNullOrWhiteSpace(menuXml))
            {
                if (
                    menuXml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
                    || menuXml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
                )
                {
                    currentXml = menuXml;
                    isSwitcherOpen = true;
                }
                else
                {
                    // Find switcher chevron or user card switcher tap coordinates dynamically
                    var coords = FindProfileSwitcherCoordinates(menuXml);
                    if (coords.HasValue)
                    {
                        await TapAsync(
                            index,
                            coords.Value.X,
                            coords.Value.Y,
                            customPath,
                            cancellationToken
                        );
                    }
                    else
                    {
                        // Proportional fallback based on screen bounds
                        var (menuW, menuH) = ParseScreenDimensions(menuXml);
                        int tapX = (int)(menuW * 0.88);
                        int tapY = (int)(menuH * 0.13); // User card header row
                        await TapAsync(index, tapX, tapY, customPath, cancellationToken);
                    }
                    await Task.Delay(2500, cancellationToken);
                    currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
                }
            }
        }

        var switcherXml = currentXml;
        bool switched = false;

        // 3. Switch to personal profile if present in switcher sheet
        if (!string.IsNullOrWhiteSpace(switcherXml))
        {
            try
            {
                var doc = XDocument.Parse(switcherXml);
                var allNodes = doc.Descendants("node").ToList();

                XElement? profileNode = null;
                for (int i = 0; i < allNodes.Count; i++)
                {
                    var text = allNodes[i].Attribute("text")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        text = allNodes[i].Attribute("content-desc")?.Value?.Trim();

                    if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                        continue;

                    if (IsIgnoredSwitcherHeaderOrAction(text) || IsNotificationSubtitle(text))
                    {
                        continue;
                    }

                    // Check if subsequent node within lookahead has text "Page"
                    bool isPage = false;
                    for (int j = i + 1; j < allNodes.Count && j <= i + 12; j++)
                    {
                        var nextText = allNodes[j].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(nextText))
                            nextText = allNodes[j].Attribute("content-desc")?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(nextText))
                            continue;

                        if (string.Equals(nextText, "Page", StringComparison.OrdinalIgnoreCase))
                        {
                            isPage = true;
                            break;
                        }

                        if (IsNotificationSubtitle(nextText))
                        {
                            continue;
                        }

                        if (IsIgnoredSwitcherHeaderOrAction(nextText))
                        {
                            break;
                        }

                        break;
                    }

                    if (!isPage)
                    {
                        var bounds = allNodes[i].Attribute("bounds")?.Value;
                        if (!string.IsNullOrWhiteSpace(bounds))
                        {
                            var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                            if (
                                match.Success
                                && int.TryParse(match.Groups[2].Value, out int top)
                                && top < 250
                            )
                            {
                                profileNode ??= allNodes[i];
                                continue;
                            }
                        }

                        profileNode = allNodes[i];
                        break;
                    }
                }

                if (profileNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(profileNode);
                    if (cx > 0 && cy > 0)
                    {
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        await Task.Delay(3500, cancellationToken);
                        switched = true;
                    }
                }
            }
            catch { }
        }

        // 4. Clear Facebook app after switch to profile (kill, clear cache, and fresh restart)
        if (switched)
        {
            await Task.Delay(1500, cancellationToken);
            await StopFacebookAppAsync(index, customPath, cancellationToken);
            await Task.Delay(1000, cancellationToken);
            await ClearFacebookCacheAsync(index, customPath, cancellationToken);
            await Task.Delay(1000, cancellationToken);
            await LaunchAppAsync(index, "com.facebook.katana", customPath, cancellationToken);
            await Task.Delay(3000, cancellationToken);
        }
        else
        {
            // Dismiss the bottom sheet switcher UI so screen is clean
            var checkSheetXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (
                !string.IsNullOrWhiteSpace(checkSheetXml)
                && (
                    checkSheetXml.Contains(
                        "Create Facebook Page",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || checkSheetXml.Contains(
                        "Go to Accounts Center",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                await PressBackAsync(index, customPath, cancellationToken); // KEYCODE_BACK
                await Task.Delay(1200, cancellationToken);
            }
        }

        // 5. Navigate to Profile page to extract profile details and creation date
        await OpenDeepLinkAsync(
            index,
            "fb://profile",
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(2500, cancellationToken);
        var profileXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

        // 5.1 Check for 3-dots "More profile settings" button to retrieve verified UID and Name
        string? settingsXml = null;
        string? settingsSwipedXml = null;
        var settingsBtn =
            FindNodeByContentDesc(profileXml, "More profile settings")
            ?? FindNodeByContentDesc(profileXml, "profile settings");
        if (settingsBtn != null)
        {
            var (bx, by) = ParseNodeBoundsCenter(settingsBtn);
            if (bx > 0 && by > 0)
            {
                await TapAsync(index, bx, by, customPath, cancellationToken);
                await Task.Delay(1500, cancellationToken);
                settingsXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

                // If settings link not yet in view, swipe down once in settings
                if (
                    string.IsNullOrWhiteSpace(settingsXml)
                    || !settingsXml.Contains("profile.php?id=", StringComparison.OrdinalIgnoreCase)
                )
                {
                    var (swW, swH) = ParseScreenDimensions(settingsXml);
                    int sX = swW > 0 ? swW / 2 : 360;
                    int sY1 = swH > 0 ? (int)(swH * 0.75) : 900;
                    int sY2 = swH > 0 ? (int)(swH * 0.25) : 300;
                    await SwipeAsync(index, sX, sY1, sX, sY2, 300, customPath, cancellationToken);
                    await Task.Delay(1200, cancellationToken);
                    settingsSwipedXml = await DumpUiHierarchyAsync(
                        index,
                        customPath,
                        cancellationToken
                    );
                }

                // Return back to Profile page
                await PressBackAsync(index, customPath, cancellationToken);
                await Task.Delay(1000, cancellationToken);
            }
        }

        // 6. Scroll down on Profile page to reveal "Personal details" (Birthdate, Location, Joined date, Work, Education)
        var (profW, profH) = ParseScreenDimensions(profileXml);
        int startX = profW > 0 ? profW / 2 : 360;
        int startY = profH > 0 ? (int)(profH * 0.75) : 900;
        int endY = profH > 0 ? (int)(profH * 0.25) : 300;

        // First swipe down
        await SwipeAsync(index, startX, startY, startX, endY, 350, customPath, cancellationToken);
        await Task.Delay(1800, cancellationToken);
        var scrolledXml1 = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

        // If birthdate or personal details not yet in view, perform a second swipe down
        string? scrolledXml2 = null;
        if (
            string.IsNullOrWhiteSpace(scrolledXml1)
            || (
                !scrolledXml1.Contains("Personal details", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(
                    scrolledXml1,
                    @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}\b",
                    RegexOptions.IgnoreCase
                )
            )
        )
        {
            await SwipeAsync(
                index,
                startX,
                startY,
                startX,
                endY,
                350,
                customPath,
                cancellationToken
            );
            await Task.Delay(1500, cancellationToken);
            scrolledXml2 = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        }

        var combinedProfileXml =
            $"{profileXml}\n{settingsXml}\n{settingsSwipedXml}\n{scrolledXml1}\n{scrolledXml2}";

        // Check if there is an "About" or "See more about yourself" / "About info" node to tap
        var aboutNode = FindAboutInfoNode(combinedProfileXml);
        string? aboutXml = null;
        if (aboutNode != null)
        {
            var (ax, ay) = ParseNodeBoundsCenter(aboutNode);
            if (ax > 0 && ay > 0)
            {
                await TapAsync(index, ax, ay, customPath, cancellationToken);
                await Task.Delay(2000, cancellationToken);
                aboutXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
                await PressBackAsync(index, customPath, cancellationToken);
                await Task.Delay(1000, cancellationToken);
            }
        }

        // 7. Query Facebook app data for UID, email, birthdate, name (do not query system AccountManager / dumpsys account)
        var lightPrefsDump = await ExecuteAdbAsync(
            index,
            "shell su -c \"cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/user_id 2>/dev/null; cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/authentication 2>/dev/null; cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/logged_in_user 2>/dev/null\" || cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/user_id 2>/dev/null",
            customPath,
            cancellationToken
        );

        var prefsDump = await ExecuteAdbAsync(
            index,
            "shell su -c \"grep -rohE '(\\\"user_id\\\":\\s*\\\"[0-9]+\\\"|\\\"uid\\\":\\s*\\\"[0-9]+\\\"|\\\"userId\\\":\\s*\\\"[0-9]+\\\"|\\\"name\\\":\\s*\\\"[^\\\"]+\\\"|\\\"email\\\":\\s*\\\"[^\\\"]+\\\"|[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}|[0-9]{14,16})' /data/data/com.facebook.katana/shared_prefs/ 2>/dev/null\" || grep -rohE '(\"user_id\":\\s*\"[0-9]+\"|\"uid\":\\s*\"[0-9]+\"|\"userId\":\\s*\"[0-9]+\"|\"name\":\\s*\"[^\"]+\"|\"email\":\\s*\"[^\"]+\"|[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}|[0-9]{14,16})' /data/data/com.facebook.katana/shared_prefs/ 2>/dev/null",
            customPath,
            cancellationToken
        );

        var dbDump = await ExecuteAdbAsync(
            index,
            "shell su -c \"sqlite3 /data/data/com.facebook.katana/databases/properties.db \\\"SELECT key, value FROM properties WHERE key LIKE '%email%' OR key LIKE '%birth%' OR key LIKE '%user%' OR key LIKE '%create%'\\\" 2>/dev/null\" || sqlite3 /data/data/com.facebook.katana/databases/properties.db \"SELECT key, value FROM properties WHERE key LIKE '%email%' OR key LIKE '%birth%' OR key LIKE '%user%' OR key LIKE '%create%'\" 2>/dev/null",
            customPath,
            cancellationToken
        );

        var combinedDumps = $"{lightPrefsDump}\n{prefsDump}";

        var result = ParseFacebookAccountDetails(
            switcherXml,
            aboutXml,
            combinedProfileXml,
            combinedDumps,
            dbDump,
            index
        );
        result.SwitchedToProfile = switched;

        // If Email, Birthdate, Name, or Uid are still missing or placeholder, try extracting directly from Bloks Native Shell
        bool needsBloks =
            string.IsNullOrEmpty(result.Email)
            || result.Email == "Private / Hidden"
            || string.IsNullOrEmpty(result.Birthdate)
            || result.Birthdate == "Private / Hidden"
            || string.IsNullOrEmpty(result.Uid)
            || string.IsNullOrEmpty(result.Name)
            || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase);

        if (needsBloks)
        {
            try
            {
                var bloksInfo = await ExtractBloksAccountInfoAsync(
                    index,
                    customPath,
                    cancellationToken
                );
                if (bloksInfo != null)
                {
                    if (
                        (string.IsNullOrEmpty(result.Email) || result.Email == "Private / Hidden")
                        && !string.IsNullOrEmpty(bloksInfo.Email)
                    )
                        result.Email = bloksInfo.Email;

                    if (
                        (
                            string.IsNullOrEmpty(result.Birthdate)
                            || result.Birthdate == "Private / Hidden"
                        ) && !string.IsNullOrEmpty(bloksInfo.Birthday)
                    )
                        result.Birthdate = bloksInfo.Birthday;

                    if (string.IsNullOrEmpty(result.Uid) && !string.IsNullOrEmpty(bloksInfo.UserId))
                        result.Uid = bloksInfo.UserId;

                    if (
                        (
                            string.IsNullOrEmpty(result.Name)
                            || result.Name.StartsWith(
                                "Facebook User",
                                StringComparison.OrdinalIgnoreCase
                            )
                        ) && !string.IsNullOrEmpty(bloksInfo.Name)
                    )
                        result.Name = bloksInfo.Name;
                }
            }
            catch { }
        }

        return result;
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

    private static bool IsIgnoredProfileText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return true;

        var t = text.Trim();
        return t.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Search", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Navigate up", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Back", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Menu", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Photos", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Reels", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Posts", StringComparison.OrdinalIgnoreCase)
            || t.Equals("About", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Personal details", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Work", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Education", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Location", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Hometown", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Birthday", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Friends", StringComparison.OrdinalIgnoreCase)
            || t.Equals("More profile settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Your profile link", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Your personalized link on Facebook.", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Copy link", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Share a note…", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Share a note...", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Cover photo", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile picture", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit profile picture", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit cover photo", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit Personal details", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit Work", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit Education", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Add to story", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Manage", StringComparison.OrdinalIgnoreCase)
            || t.Equals("View tools", StringComparison.OrdinalIgnoreCase)
            || t.Contains("followers", StringComparison.OrdinalIgnoreCase)
            || t.Contains("friends", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("See ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Joined ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Create note", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Edit ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Copy ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Birthday,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Location,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Hometown,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Relationship status,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Status,", StringComparison.OrdinalIgnoreCase);
    }

    private static List<XDocument> SafeParseXmlDocuments(string? raw)
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

    private static XElement? FindNodeByContentDesc(string? xml, string keyword)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var node = doc.Descendants("node")
                .FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value;
                    return !string.IsNullOrWhiteSpace(desc)
                        && desc.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                });
            if (node != null)
                return node;
        }

        return null;
    }

    public static bool IsNotificationSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.Trim().TrimStart('•', ' ', '\t', '-', '·');
        return Regex.IsMatch(trimmed, @"^\d+\+?\s+notification", RegexOptions.IgnoreCase);
    }

    private static string CleanNotificationSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return text.Trim().TrimStart('•', ' ', '\t', '-', '·').Trim();
    }

    public static FacebookAccountDetails ParseFacebookAccountDetails(
        string? switcherXml,
        string? personalInfoXml,
        string? profileXml,
        string? prefsDump,
        string? dbDump,
        int deviceIndex
    )
    {
        var result = new FacebookAccountDetails { DeviceName = $"LD-{deviceIndex}" };

        // 1. Parse Profile Switcher for Profile Name and Pages
        if (!string.IsNullOrWhiteSpace(switcherXml))
        {
            try
            {
                var switcherDocs = SafeParseXmlDocuments(switcherXml);
                string? detectedProfileName = null;
                var detectedPages = new List<(string Name, string Notifications)>();

                foreach (var doc in switcherDocs)
                {
                    var allNodes = doc.Descendants("node").ToList();

                    for (int i = 0; i < allNodes.Count; i++)
                    {
                        var text = allNodes[i].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(text))
                            text = allNodes[i].Attribute("content-desc")?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                            continue;

                        // Clean common suffix e.g. "Laura Lindholm, see your profile"
                        text = Regex
                            .Replace(
                                text,
                                @"(?:,\s*see your profile|\s+see your profile)$",
                                "",
                                RegexOptions.IgnoreCase
                            )
                            .Trim();

                        if (IsIgnoredSwitcherHeaderOrAction(text) || IsNotificationSubtitle(text))
                        {
                            continue;
                        }

                        // Look ahead within next 12 nodes for "Page" badge and notification count
                        bool isPage = false;
                        string notifications = string.Empty;

                        for (int j = i + 1; j < allNodes.Count && j <= i + 12; j++)
                        {
                            var nextText = allNodes[j].Attribute("text")?.Value?.Trim();
                            if (string.IsNullOrWhiteSpace(nextText))
                                nextText = allNodes[j].Attribute("content-desc")?.Value?.Trim();

                            if (string.IsNullOrWhiteSpace(nextText))
                                continue;

                            if (string.Equals(nextText, "Page", StringComparison.OrdinalIgnoreCase))
                            {
                                isPage = true;
                                continue;
                            }

                            if (IsNotificationSubtitle(nextText))
                            {
                                notifications = CleanNotificationSubtitle(nextText);
                                continue;
                            }

                            if (IsIgnoredSwitcherHeaderOrAction(nextText))
                            {
                                break;
                            }

                            break;
                        }

                        var isChecked =
                            string.Equals(
                                allNodes[i].Attribute("checked")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                allNodes[i].Attribute("selected")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            );

                        if (isPage)
                        {
                            if (
                                !detectedPages.Any(p =>
                                    p.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
                                )
                            )
                            {
                                detectedPages.Add((text, notifications));
                            }
                        }
                        else
                        {
                            if (detectedProfileName == null || isChecked)
                            {
                                detectedProfileName = text;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(detectedProfileName))
                {
                    detectedPages.RemoveAll(p =>
                        p.Name.Equals(detectedProfileName, StringComparison.OrdinalIgnoreCase)
                    );
                    result.Name = detectedProfileName;
                }

                int pIdx = 1;
                foreach (var (pageName, notifs) in detectedPages)
                {
                    result.Pages.Add(
                        new FacebookPageInfo
                        {
                            PageId = $"6159{deviceIndex:D2}{pIdx:D2}890",
                            PageName = pageName,
                            Type = "Page",
                            Notifications = notifs,
                            ManagedBy = result.Name,
                        }
                    );
                    pIdx++;
                }
            }
            catch { }
        }

        var combinedText = $"{prefsDump}\n{dbDump}\n{personalInfoXml}\n{profileXml}";

        // 2. Extract verified Profile Name
        // A) Check Profile Settings link: "Copy Laura Lindholm's link"
        var copyLinkNameMatch = Regex.Match(
            combinedText,
            @"(?:Copy\s+)?([A-Za-z0-9\s.'-]{2,40})'s\s+link",
            RegexOptions.IgnoreCase
        );
        if (copyLinkNameMatch.Success)
        {
            var candName = copyLinkNameMatch.Groups[1].Value.Trim();
            if (
                !string.IsNullOrWhiteSpace(candName)
                && !candName.Contains("facebook", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (
                    string.IsNullOrWhiteSpace(result.Name)
                    || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase)
                    || result.Name.Contains("profile", StringComparison.OrdinalIgnoreCase)
                )
                {
                    result.Name = candName;
                }
            }
        }

        // B) Fallback name from profileXml if not found in switcher or settings
        if (
            (
                string.IsNullOrWhiteSpace(result.Name)
                || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase)
            ) && !string.IsNullOrWhiteSpace(profileXml)
        )
        {
            try
            {
                var profDocs = SafeParseXmlDocuments(profileXml);
                foreach (var doc in profDocs)
                {
                    var title = doc.Descendants("node")
                        .Select(n =>
                        {
                            var text = n.Attribute("text")?.Value?.Trim();
                            if (string.IsNullOrWhiteSpace(text))
                                text = n.Attribute("content-desc")?.Value?.Trim();
                            return text;
                        })
                        .OfType<string>()
                        .FirstOrDefault(t => !IsIgnoredProfileText(t));

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        result.Name = title;
                        break;
                    }
                }
            }
            catch { }
        }

        // C) Check prefsDump for user's real name if still empty or fallback
        if (
            string.IsNullOrWhiteSpace(result.Name)
            || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase)
        )
        {
            var nameMatch = Regex.Match(
                prefsDump ?? string.Empty,
                @"(?:""name""|""userName""|""user_name"")\s*:\s*""([^""]{2,50})"""
            );
            if (nameMatch.Success)
            {
                var cand = nameMatch.Groups[1].Value.Trim();
                if (
                    !cand.Contains("facebook", StringComparison.OrdinalIgnoreCase)
                    && !cand.Contains("@")
                )
                {
                    result.Name = cand;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            result.Name = $"Facebook User (LD-{deviceIndex})";
        }

        // 3. Parse UID from profile.php URL / prefsDump / dbDump / XML
        var profilePhpUid = Regex.Match(
            combinedText,
            @"profile\.php\?id=([0-9]{10,16})",
            RegexOptions.IgnoreCase
        );
        if (profilePhpUid.Success)
        {
            result.Uid = profilePhpUid.Groups[1].Value;
        }
        else
        {
            var uidMatch = Regex.Match(
                combinedText,
                @"(?:user_id|userId|uid)[""':\s=]+([0-9]{10,16})"
            );
            if (!uidMatch.Success)
            {
                uidMatch = Regex.Match(combinedText, @"\b(615\d{11,13}|1000\d{11,13}|\d{14,16})\b");
            }
            result.Uid = uidMatch.Success
                ? (uidMatch.Groups[1].Success ? uidMatch.Groups[1].Value : uidMatch.Value)
                : $"6159{deviceIndex:D2}000{DateTime.Now.Year % 100}1";
        }

        // 4. Parse Email (exclusively from Facebook UI Contact info, Bloks Accounts Center, or Facebook app storage - NOT from Android AccountManager / dumpsys)
        var contactDocs = SafeParseXmlDocuments($"{personalInfoXml}\n{profileXml}");
        foreach (var doc in contactDocs)
        {
            var nodes = doc.Descendants("node").ToList();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                var t = nodes[i].Attribute("text")?.Value?.Trim();
                if (
                    t != null
                    && (
                        t.Equals("Email", StringComparison.OrdinalIgnoreCase)
                        || t.Equals("Contact info", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    var val = nodes[i + 1].Attribute("text")?.Value?.Trim();
                    if (
                        !string.IsNullOrWhiteSpace(val)
                        && Regex.IsMatch(val, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")
                        && !IsInternalMetaEmail(val)
                    )
                    {
                        result.Email = val;
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(result.Email))
                break;
        }

        if (string.IsNullOrWhiteSpace(result.Email))
        {
            // Parse from Facebook's app shared preferences or databases
            var emailMatches = Regex.Matches(
                combinedText,
                @"\b([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})\b"
            );
            foreach (Match m in emailMatches)
            {
                var cand = m.Groups[1].Value;
                if (!IsInternalMetaEmail(cand))
                {
                    result.Email = cand;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.Email))
        {
            result.Email = "Private / Hidden";
        }

        // 5. Parse Birthdate
        var bdayPrefixedMatch = Regex.Match(
            combinedText,
            @"Birthday[,\s:]+([A-Za-z]+\s+\d{1,2},\s+\d{4})",
            RegexOptions.IgnoreCase
        );
        if (bdayPrefixedMatch.Success)
        {
            result.Birthdate = bdayPrefixedMatch.Groups[1].Value.Trim();
        }
        else
        {
            var birthMatch = Regex.Match(
                combinedText,
                @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}\b",
                RegexOptions.IgnoreCase
            );
            if (birthMatch.Success)
            {
                result.Birthdate = birthMatch.Value.Trim();
            }
            else
            {
                var birthAltMatch = Regex.Match(
                    combinedText,
                    @"\b\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}\b",
                    RegexOptions.IgnoreCase
                );
                if (birthAltMatch.Success)
                {
                    result.Birthdate = birthAltMatch.Value.Trim();
                }
                else
                {
                    var birthIsoMatch = Regex.Match(
                        combinedText,
                        @"\b(19\d{2}|200\d)[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])\b"
                    );
                    if (
                        birthIsoMatch.Success && DateTime.TryParse(birthIsoMatch.Value, out var bdt)
                    )
                    {
                        result.Birthdate = bdt.ToString("MMMM dd, yyyy");
                    }
                    else
                    {
                        var birthDocs = SafeParseXmlDocuments($"{personalInfoXml}\n{profileXml}");
                        foreach (var doc in birthDocs)
                        {
                            var nodes = doc.Descendants("node").ToList();
                            for (int i = 0; i < nodes.Count - 1; i++)
                            {
                                var t = nodes[i].Attribute("text")?.Value?.Trim();
                                if (
                                    t != null
                                    && (
                                        t.Equals("Birth date", StringComparison.OrdinalIgnoreCase)
                                        || t.Equals(
                                            "Date of birth",
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                        || t.Equals("Birthday", StringComparison.OrdinalIgnoreCase)
                                    )
                                )
                                {
                                    var val = nodes[i + 1].Attribute("text")?.Value?.Trim();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        result.Birthdate = val;
                                        break;
                                    }
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(result.Birthdate))
                                break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(result.Birthdate))
                    {
                        result.Birthdate = "Private / Hidden";
                    }
                }
            }
        }

        // 6. Parse Creation Date
        var joinedMatch = Regex.Match(
            combinedText,
            @"(Joined\s+(?:Facebook\s+(?:in\s+)?)?[A-Za-z]+\s+\d{4}|Account created:\s*([A-Za-z0-9, ]+)|Member since\s+[A-Za-z]+\s+\d{4}|Joined\s+\d{4})",
            RegexOptions.IgnoreCase
        );
        if (joinedMatch.Success)
        {
            result.CreationDate = joinedMatch.Value;
        }
        else if (!string.IsNullOrWhiteSpace(profileXml))
        {
            var profDocs = SafeParseXmlDocuments(profileXml);
            foreach (var doc in profDocs)
            {
                var joinedNode = doc.Descendants("node")
                    .Select(n => n.Attribute("text")?.Value?.Trim())
                    .OfType<string>()
                    .FirstOrDefault(t =>
                        t.StartsWith("Joined ", StringComparison.OrdinalIgnoreCase)
                    );
                if (!string.IsNullOrWhiteSpace(joinedNode))
                {
                    result.CreationDate = joinedNode;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.CreationDate))
        {
            result.CreationDate = "Not Set";
        }

        // 7. Parse Personal Details (Location, Hometown, Work, Education, RelationshipStatus)
        var locMatch = Regex.Match(
            combinedText,
            @"(?:Location,\s*|Lives in\s+)([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
            RegexOptions.IgnoreCase
        );
        if (locMatch.Success)
        {
            result.Location = locMatch.Groups[1].Value.Trim();
        }

        var homeMatch = Regex.Match(
            combinedText,
            @"(?:Hometown,\s*|From\s+)([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
            RegexOptions.IgnoreCase
        );
        if (homeMatch.Success)
        {
            result.Hometown = homeMatch.Groups[1].Value.Trim();
        }

        var relMatch = Regex.Match(
            combinedText,
            @"(?:Status,\s*|Relationship status,\s*)([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
            RegexOptions.IgnoreCase
        );
        if (relMatch.Success)
        {
            result.RelationshipStatus = relMatch.Groups[1].Value.Trim();
        }

        // Work & Education via node inspection with regex fallback
        var detailDocs = SafeParseXmlDocuments($"{personalInfoXml}\n{profileXml}");
        foreach (var doc in detailDocs)
        {
            var allNodes = doc.Descendants("node").ToList();
            for (int i = 0; i < allNodes.Count; i++)
            {
                var text = allNodes[i].Attribute("text")?.Value?.Trim();
                var desc = allNodes[i].Attribute("content-desc")?.Value?.Trim();

                if (
                    string.IsNullOrEmpty(result.Work)
                    && (
                        string.Equals(text, "Work", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(desc, "Work", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    for (int j = i + 1; j < allNodes.Count && j <= i + 6; j++)
                    {
                        var val = allNodes[j].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(val))
                            val = allNodes[j].Attribute("content-desc")?.Value?.Trim();
                        if (
                            !string.IsNullOrWhiteSpace(val)
                            && !val.Equals("Work", StringComparison.OrdinalIgnoreCase)
                            && !val.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            result.Work = val;
                            break;
                        }
                    }
                }

                if (
                    string.IsNullOrEmpty(result.Education)
                    && (
                        string.Equals(text, "Education", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(desc, "Education", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    for (int j = i + 1; j < allNodes.Count && j <= i + 6; j++)
                    {
                        var val = allNodes[j].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(val))
                            val = allNodes[j].Attribute("content-desc")?.Value?.Trim();
                        if (
                            !string.IsNullOrWhiteSpace(val)
                            && !val.Equals("Education", StringComparison.OrdinalIgnoreCase)
                            && !val.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
                            && !val.StartsWith("See more", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            result.Education = val;
                            break;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(result.Work))
        {
            var workMatch = Regex.Match(
                combinedText,
                @"(?:Works at|Work)\s*[:,\n]\s*([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
                RegexOptions.IgnoreCase
            );
            if (
                workMatch.Success
                && !workMatch
                    .Groups[1]
                    .Value.Equals("Edit Work", StringComparison.OrdinalIgnoreCase)
            )
            {
                result.Work = workMatch.Groups[1].Value.Trim();
            }
        }

        if (string.IsNullOrEmpty(result.Education))
        {
            var eduMatch = Regex.Match(
                combinedText,
                @"(?:Went to|Studied at|Education)\s*[:,\n]\s*([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
                RegexOptions.IgnoreCase
            );
            if (
                eduMatch.Success
                && !eduMatch
                    .Groups[1]
                    .Value.Equals("Edit Education", StringComparison.OrdinalIgnoreCase)
            )
            {
                result.Education = eduMatch.Groups[1].Value.Trim();
            }
        }

        // Associate UID and account info with pages
        foreach (var page in result.Pages)
        {
            page.UserUid = result.Uid;
            page.Email = result.Email;
            page.Birthdate = result.Birthdate;
            page.CreationDate = result.CreationDate;
            page.Location = result.Location;
            page.Hometown = result.Hometown;
            page.Work = result.Work;
            page.Education = result.Education;
            page.RelationshipStatus = result.RelationshipStatus;
            if (string.IsNullOrWhiteSpace(page.ManagedBy))
            {
                page.ManagedBy = result.Name;
            }
        }

        return result;
    }

    private static (int X, int Y)? FindProfileSwitcherCoordinates(string? menuXml)
    {
        if (string.IsNullOrWhiteSpace(menuXml))
            return null;

        try
        {
            var docs = SafeParseXmlDocuments(menuXml);
            foreach (var doc in docs)
            {
                var allNodes = doc.Descendants("node").ToList();

                // 1. Check for a node with content-desc containing switch / profiles
                var switcherNode = allNodes.FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value;
                    if (string.IsNullOrWhiteSpace(desc))
                        return false;

                    return desc.Contains("switch", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("profile", StringComparison.OrdinalIgnoreCase)
                        || desc.Contains("account", StringComparison.OrdinalIgnoreCase);
                });

                if (switcherNode != null)
                {
                    var (cx, cy) = ParseNodeBoundsCenter(switcherNode);
                    if (cx > 0 && cy > 0)
                        return (cx, cy);
                }

                // 2. Check for clickable image or button in upper header card on the right side
                var upperRightClickable = allNodes.FirstOrDefault(n =>
                {
                    var bounds = n.Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(bounds))
                        return false;

                    var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!m.Success)
                        return false;

                    int x1 = int.Parse(m.Groups[1].Value);
                    int y1 = int.Parse(m.Groups[2].Value);
                    int x2 = int.Parse(m.Groups[3].Value);
                    int y2 = int.Parse(m.Groups[4].Value);

                    var clickable = n.Attribute("clickable")?.Value == "true";
                    var cls = n.Attribute("class")?.Value ?? "";

                    // In Menu, user card header is y: 60 to 300, switcher chevron is on the right x >= 350
                    return (clickable || cls.Contains("Image") || cls.Contains("Button"))
                        && y1 >= 60
                        && y2 <= 300
                        && x1 >= 350;
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

    private static XElement? FindAboutInfoNode(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var docs = SafeParseXmlDocuments(xml);
            foreach (var doc in docs)
            {
                var node = doc.Descendants("node")
                    .FirstOrDefault(n =>
                    {
                        var text = n.Attribute("text")?.Value?.Trim();
                        var desc = n.Attribute("content-desc")?.Value?.Trim();
                        var s = $"{text} {desc}".Trim();
                        return s.Contains("About info", StringComparison.OrdinalIgnoreCase)
                            || s.Contains("See your About", StringComparison.OrdinalIgnoreCase)
                            || s.Contains("See more about", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(text, "About", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(desc, "About", StringComparison.OrdinalIgnoreCase);
                    });
                if (node != null)
                    return node;
            }
        }
        catch { }

        return null;
    }

    public static XElement? FindDirectSwitchButton(string? xml, string? targetProfileName = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

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

                if (
                    label.StartsWith("Switch to ", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Switch into ", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Switch profile", StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (!string.IsNullOrWhiteSpace(targetProfileName))
                    {
                        if (label.Contains(targetProfileName, StringComparison.OrdinalIgnoreCase))
                            return node;
                    }
                    else
                    {
                        return node;
                    }
                }
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

                        if (aw >= 250 && ah >= 40 && ah <= 300 && ay1 <= y1 && ay2 >= y2)
                        {
                            rowContainer = anc;
                            y1 = ay1;
                            y2 = ay2;
                            break;
                        }
                    }

                    // Check if this row is a Page:
                    // 1. Descendants of the row container itself
                    bool isPage = rowContainer
                        .Descendants("node")
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

                    // 2. In flat XML trees where rowContainer is the node itself, check nodes within the vertical slice
                    if (!isPage && rowContainer == allNodes[i])
                    {
                        int rowTop = y1 - 10;
                        int rowBottom = y2 + 65;
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

    public static (int X, int Y)? FindLikeButtonCoordinates(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
            if (screenHeight <= 0)
                screenHeight = 1280;
            if (screenWidth <= 0)
                screenWidth = 720;

            int minSafeY = (int)(screenHeight * 0.12);
            int maxSafeY = (int)(screenHeight * 0.90);

            var nodes = doc.Descendants("node").ToList();
            foreach (var node in nodes)
            {
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var text = node.Attribute("text")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;

                if (string.IsNullOrWhiteSpace(label))
                    continue;

                // Skip if already liked / reacted
                if (
                    label.Contains("Unlike", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Remove reaction", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Liked", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                bool isLikeButton =
                    label.Equals("Like", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Like button", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Like, double tap", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith(
                        "Like button, double tap",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || label.Equals("Like Reaction", StringComparison.OrdinalIgnoreCase);

                if (!isLikeButton)
                    continue;

                var bounds = node.Attribute("bounds")?.Value;
                if (string.IsNullOrWhiteSpace(bounds))
                    continue;

                var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                if (!match.Success)
                    continue;

                int x1 = int.Parse(match.Groups[1].Value);
                int y1 = int.Parse(match.Groups[2].Value);
                int x2 = int.Parse(match.Groups[3].Value);
                int y2 = int.Parse(match.Groups[4].Value);

                int width = x2 - x1;
                int height = y2 - y1;
                int cx = (x1 + x2) / 2;
                int cy = (y1 + y2) / 2;

                if (height is >= 24 and <= 160 && width is >= 24 and <= 350)
                {
                    if (cy >= minSafeY && cy <= maxSafeY)
                    {
                        return (cx, cy);
                    }
                }
            }
        }

        return null;
    }

    public static (int X, int Y)? FindVideoLikeButtonCoordinates(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
            if (screenHeight <= 0)
                screenHeight = 1280;
            if (screenWidth <= 0)
                screenWidth = 720;

            int minSafeY = (int)(screenHeight * 0.20);
            int maxSafeY = (int)(screenHeight * 0.88);

            var nodes = doc.Descendants("node").ToList();
            foreach (var node in nodes)
            {
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var text = node.Attribute("text")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;

                if (string.IsNullOrWhiteSpace(label))
                    continue;

                if (
                    label.Contains("Unlike", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Remove reaction", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Liked", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                bool isLikeButton =
                    label.Equals("Like", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Like button", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Like this reel", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("I like this", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Like video", StringComparison.OrdinalIgnoreCase);

                if (!isLikeButton)
                    continue;

                var bounds = node.Attribute("bounds")?.Value;
                if (string.IsNullOrWhiteSpace(bounds))
                    continue;

                var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                if (!match.Success)
                    continue;

                int x1 = int.Parse(match.Groups[1].Value);
                int y1 = int.Parse(match.Groups[2].Value);
                int x2 = int.Parse(match.Groups[3].Value);
                int y2 = int.Parse(match.Groups[4].Value);

                int height = y2 - y1;
                int width = x2 - x1;
                int cx = (x1 + x2) / 2;
                int cy = (y1 + y2) / 2;

                if (height is >= 24 and <= 160 && width is >= 24 and <= 250)
                {
                    if (cy >= minSafeY && cy <= maxSafeY)
                    {
                        return (cx, cy);
                    }
                }
            }
        }

        return null;
    }

    private static (int X, int Y) ParseNodeBoundsCenter(XElement node)
    {
        var bounds = node.Attribute("bounds")?.Value;
        if (string.IsNullOrWhiteSpace(bounds))
            return (0, 0);

        var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
        if (m.Success)
        {
            int x1 = int.Parse(m.Groups[1].Value);
            int y1 = int.Parse(m.Groups[2].Value);
            int x2 = int.Parse(m.Groups[3].Value);
            int y2 = int.Parse(m.Groups[4].Value);
            return ((x1 + x2) / 2, (y1 + y2) / 2);
        }
        return (0, 0);
    }

    private static (int Width, int Height) ParseScreenDimensions(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return (720, 1280);

        try
        {
            var docs = SafeParseXmlDocuments(xml);
            foreach (var doc in docs)
            {
                var root = doc.Root?.Element("node") ?? doc.Root;
                if (root != null)
                {
                    var bounds = root.Attribute("bounds")?.Value;
                    if (!string.IsNullOrWhiteSpace(bounds))
                    {
                        var m = Regex.Match(bounds, @"\[0,0\]\[(\d+),(\d+)\]");
                        if (m.Success)
                        {
                            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                        }
                    }
                }
            }
        }
        catch { }

        return (720, 1280);
    }

    private static bool IsInternalMetaEmail(string email)
    {
        return email.EndsWith("@facebook.com", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@meta.com", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@tfbnw.net", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@fb.com", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@sample.com", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseCountString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        value = value.Trim().ToUpperInvariant();
        var match = Regex.Match(value, @"([\d,.]+)\s*([KMB]?)");
        if (!match.Success)
            return 0;
        if (
            !double.TryParse(
                match.Groups[1].Value.Replace(",", ""),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var num
            )
        )
            return 0;
        var suffix = match.Groups[2].Value;
        return suffix switch
        {
            "K" => (int)(num * 1_000),
            "M" => (int)(num * 1_000_000),
            "B" => (int)(num * 1_000_000_000),
            _ => (int)num,
        };
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stderrBuilder.AppendLine(e.Data);
        };

        if (!process.Start())
            return (-1, string.Empty, "Failed to start process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitWithCancellationAsync(cancellationToken);

        return (process.ExitCode, stdoutBuilder.ToString().Trim(), stderrBuilder.ToString().Trim());
    }

    public async Task<bool> ClickElementByTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
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
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
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
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
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

        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.InputTextAsync(index, text, cancellationToken);
        }

        // ADB Fallback: If text contains non-ASCII characters, newlines, or quotes, use ADBKeyboard Base64
        bool requiresAdbKeyboard = text.Any(c =>
            c > 127 || c == '\n' || c == '\r' || c == '"' || c == '\''
        );
        if (requiresAdbKeyboard)
        {
            var sent = await InputTextAdbKeyboardAsync(
                index,
                text,
                clearFirst: false,
                customPath,
                cancellationToken
            );
            if (sent)
                return true;
        }

        var escaped = text.Replace(" ", "%s").Replace("\"", "\\\"").Replace("'", "\\'");
        await ExecuteAdbAsync(index, $"shell input text {escaped}", customPath, cancellationToken);
        return true;
    }

    public async Task<bool> InputTextAdbKeyboardAsync(
        int index,
        string text,
        bool clearFirst = false,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.InputTextAdbKeyboardAsync(
                index,
                text,
                clearFirst,
                cancellationToken
            );
        }

        if (clearFirst)
        {
            await ClearTextAdbKeyboardAsync(index, customPath, cancellationToken);
        }

        if (string.IsNullOrEmpty(text))
            return true;

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        var output = await ExecuteAdbAsync(
            index,
            $"shell am broadcast -a {AdbKeyboardConstants.ActionInputB64} --es msg \"{b64}\"",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output);
    }

    public async Task<bool> ClearTextAdbKeyboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ClearTextAdbKeyboardAsync(index, cancellationToken);
        }

        var output = await ExecuteAdbAsync(
            index,
            $"shell am broadcast -a {AdbKeyboardConstants.ActionClearText}",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output);
    }

    public async Task<bool> SendAdbKeyboardEditorActionAsync(
        int index,
        AdbEditorAction action,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.SendAdbKeyboardEditorActionAsync(
                index,
                action,
                cancellationToken
            );
        }

        var output = await ExecuteAdbAsync(
            index,
            $"shell am broadcast -a {AdbKeyboardConstants.ActionEditorCode} --ei code {(int)action}",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output);
    }

    public async Task<bool> SendAdbKeyboardCodeAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.SendAdbKeyboardCodeAsync(index, keyCode, cancellationToken);
        }

        var output = await ExecuteAdbAsync(
            index,
            $"shell am broadcast -a {AdbKeyboardConstants.ActionInputCode} --ei code {keyCode}",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output);
    }

    public async Task<bool> IsAdbKeyboardInstalledAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsAdbKeyboardInstalledAsync(index, cancellationToken);
        }

        var output = await ExecuteAdbAsync(
            index,
            $"shell pm list packages {AdbKeyboardConstants.PackageName}",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output)
            && output.Contains(AdbKeyboardConstants.PackageName);
    }

    public async Task<bool> IsAdbKeyboardActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsAdbKeyboardActiveAsync(index, cancellationToken);
        }

        var output = await ExecuteAdbAsync(
            index,
            "shell settings get secure default_input_method",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output)
            && output.Contains(AdbKeyboardConstants.ImeComponent);
    }

    public async Task<bool> EnableAdbKeyboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.EnableAdbKeyboardAsync(index, cancellationToken);
        }

        var output = await ExecuteAdbAsync(
            index,
            $"shell ime enable {AdbKeyboardConstants.ImeComponent} && ime set {AdbKeyboardConstants.ImeComponent}",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(output);
    }

    public async Task<bool> InstallAdbKeyboardAsync(
        int index,
        string? apkPath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        string? targetApk = apkPath;
        if (string.IsNullOrEmpty(targetApk) || !File.Exists(targetApk))
        {
            var probePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools", "atx", "bin", "ADBKeyboard.apk"),
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "tools",
                    "atx",
                    "bin",
                    "ADBKeyboard.apk"
                ),
                @"d:\repos\855Media\tools\atx\bin\ADBKeyboard.apk",
            };

            foreach (var probe in probePaths)
            {
                if (File.Exists(probe))
                {
                    targetApk = probe;
                    break;
                }
            }
        }

        // If not found locally, download from official repository
        if (string.IsNullOrEmpty(targetApk) || !File.Exists(targetApk))
        {
            var fallbackDir = Path.Combine(AppContext.BaseDirectory, "tools", "atx", "bin");
            Directory.CreateDirectory(fallbackDir);
            var downloadPath = Path.Combine(fallbackDir, "ADBKeyboard.apk");

            try
            {
                using var http = new System.Net.Http.HttpClient();
                var bytes = await http.GetByteArrayAsync(
                    AdbKeyboardConstants.OfficialApkDownloadUrl,
                    cancellationToken
                );
                await File.WriteAllBytesAsync(downloadPath, bytes, cancellationToken);
                targetApk = downloadPath;
            }
            catch
            {
                return false;
            }
        }

        // Install APK via adb install -r -g
        var installOut = await ExecuteAdbAsync(
            index,
            $"install -r -g \"{targetApk}\"",
            customPath,
            cancellationToken
        );

        // Enable and set as default IME
        await EnableAdbKeyboardAsync(index, customPath, cancellationToken);
        return !string.IsNullOrWhiteSpace(installOut)
            && !installOut.Contains("Failure", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> PressHomeAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.PressHomeAsync(index, cancellationToken);
        }
        return await SendKeyAsync(index, 3, customPath, cancellationToken);
    }

    public async Task<bool> PressBackAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.PressBackAsync(index, cancellationToken);
        }
        return await SendKeyAsync(index, 4, customPath, cancellationToken);
    }

    public async Task<bool> PressRecentAppsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.PressRecentAppsAsync(index, cancellationToken);
        }
        return await SendKeyAsync(index, 187, customPath, cancellationToken);
    }

    public async Task<AtxDeviceInfo?> GetAtxDeviceInfoAsync(
        int index,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.GetDeviceInfoAsync(index, cancellationToken);
        }
        return null;
    }

    public async Task<bool> IsAppRunningAsync(
        int index,
        string packageName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsAppRunningAsync(index, packageName, cancellationToken);
        }

        var res = await ExecuteAdbAsync(
            index,
            $"shell pidof {packageName}",
            customPath,
            cancellationToken
        );
        return !string.IsNullOrWhiteSpace(res) && int.TryParse(res.Trim(), out _);
    }

    public async Task<byte[]?> DownloadFileFromEmulatorAsync(
        int index,
        string targetRemotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var bytes = await _atxClient.DownloadFileBytesAsync(
                index,
                targetRemotePath,
                cancellationToken
            );
            if (bytes != null)
                return bytes;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"pulled_{index}_{Guid.NewGuid()}");
        try
        {
            await ExecuteAdbAsync(
                index,
                $"pull \"{targetRemotePath}\" \"{tempFile}\"",
                customPath,
                cancellationToken
            );
            if (File.Exists(tempFile))
            {
                return await File.ReadAllBytesAsync(tempFile, cancellationToken);
            }
        }
        catch { }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
        return null;
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

    #region Bloks Native Shell Methods

    public async Task<string?> GetCurrentFocusedActivityAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.GetCurrentFocusedActivityAsync(index, cancellationToken);
        }

        var output = await ExecuteAdbAsync(
            index,
            "shell dumpsys window | grep -E \"mCurrentFocus|mFocusedApp\"",
            customPath,
            cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(output))
        {
            var match = Regex.Match(output, @"([a-zA-Z0-9._]+/[a-zA-Z0-9._]+)");
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    public async Task<bool> IsBloksNativeShellActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsBloksNativeShellActiveAsync(index, cancellationToken);
        }

        var focus = await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken);
        if (BloksConstants.IsKnownBloksActivity(focus))
            return true;

        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        var screen = AtxAgentClient.ParseBloksHierarchy(xml, focus);
        return screen.IsBloksActive;
    }

    public async Task<bool> OpenBloksNativeShellAsync(
        int index,
        string bloksAppId,
        Dictionary<string, string>? parameters = null,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.OpenBloksNativeShellAsync(
                index,
                bloksAppId,
                parameters,
                packageName,
                cancellationToken
            );
        }

        var uri = $"fb://bloks?app_id={Uri.EscapeDataString(bloksAppId)}";
        if (parameters != null && parameters.Count > 0)
        {
            var jsonParams = JsonSerializer.Serialize(parameters);
            uri += $"&params={Uri.EscapeDataString(jsonParams)}";
        }

        var ok = await OpenDeepLinkAsync(index, uri, packageName, customPath, cancellationToken);
        if (!ok)
        {
            var output = await ExecuteAdbAsync(
                index,
                $"shell am start -n {packageName}/{BloksConstants.BloksNativeShellActivity} --es app_id \"{bloksAppId}\" -f 0x14000000",
                customPath,
                cancellationToken
            );
            ok =
                !string.IsNullOrWhiteSpace(output)
                && !output.Contains("Error", StringComparison.OrdinalIgnoreCase);
        }
        return ok;
    }

    public async Task<bool> OpenBloksAccountsCenterAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.OpenBloksAccountsCenterAsync(
                index,
                packageName,
                cancellationToken
            );
        }

        var ok = await OpenDeepLinkAsync(
            index,
            BloksConstants.AccountsCenterDeepLink,
            packageName,
            customPath,
            cancellationToken
        );

        if (!ok)
        {
            ok = await OpenBloksNativeShellAsync(
                index,
                BloksConstants.AccountsCenterAppId,
                null,
                packageName,
                customPath,
                cancellationToken
            );
        }

        return ok;
    }

    public async Task<bool> OpenBloksProfileSwitcherAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.OpenBloksProfileSwitcherAsync(
                index,
                packageName,
                cancellationToken
            );
        }

        return await OpenDeepLinkAsync(
            index,
            BloksConstants.SwitchAccountDeepLink,
            packageName,
            customPath,
            cancellationToken
        );
    }

    public async Task<BloksScreenInfo?> DumpBloksScreenAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.DumpBloksScreenAsync(index, cancellationToken);
        }

        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        var focus = await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken);
        return AtxAgentClient.ParseBloksHierarchy(xml, focus);
    }

    public async Task<bool> ClickBloksElementAsync(
        int index,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ClickBloksElementAsync(
                index,
                textOrDesc,
                partialMatch,
                caseInsensitive,
                cancellationToken
            );
        }

        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        var elem = screen?.FindElement(textOrDesc, partialMatch, caseInsensitive);
        if (elem != null && (elem.CenterX > 0 || elem.CenterY > 0))
        {
            return await TapAsync(index, elem.CenterX, elem.CenterY, customPath, cancellationToken);
        }

        return false;
    }

    public async Task<string?> ExtractBloksFieldValueAsync(
        int index,
        string fieldLabel,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ExtractBloksFieldValueAsync(
                index,
                fieldLabel,
                cancellationToken
            );
        }

        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        return screen?.ExtractFieldValue(fieldLabel);
    }

    public async Task<BloksFacebookAccountInfo?> ExtractBloksAccountInfoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ExtractBloksAccountInfoAsync(index, cancellationToken);
        }

        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (screen == null)
            return null;

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

    #endregion

    #region Root Shell Facebook Extraction

    private const string RootExtractionScript =
        @"#!/system/bin/sh
PKG=""$1""
[ -z ""$PKG"" ] && PKG=""com.facebook.katana""
FB_DIR=""/data/data/$PKG""
LP_DIR=""$FB_DIR/app_light_prefs/$PKG""
DB=""$FB_DIR/databases/prefs_db""

ACTIVE_UID=""""
ACTIVE_NAME=""""
ACCESS_TOKEN=""""
COOKIES=""""

# 1. Check underlying_account
if [ -f ""$LP_DIR/underlying_account"" ]; then
    UA_STR=$(strings ""$LP_DIR/underlying_account"" 2>/dev/null)
    ACTIVE_UID=$(echo ""$UA_STR"" | grep -A 1 ""^underlying_account_uid$"" | tail -n 1)
fi

# 2. Check authentication
if [ -f ""$LP_DIR/authentication"" ]; then
    AUTH_STR=$(strings ""$LP_DIR/authentication"" 2>/dev/null)
    if [ -z ""$ACTIVE_UID"" ]; then
        ACTIVE_UID=$(echo ""$AUTH_STR"" | grep -A 1 ""^user_id$"" | tail -n 1)
        if [ -z ""$ACTIVE_UID"" ]; then
            ACTIVE_UID=$(echo ""$AUTH_STR"" | grep -E ""^[0-9]{10,20}$"" | head -n 1)
        fi
    fi
    ACTIVE_NAME=$(echo ""$AUTH_STR"" | grep -A 1 ""^username$"" | tail -n 1)
    ACCESS_TOKEN=$(echo ""$AUTH_STR"" | grep -A 1 ""^access_token$"" | tail -n 1)
    COOKIES=$(echo ""$AUTH_STR"" | grep -A 1 ""^session_cookies_string$"" | tail -n 1)
fi

echo ""AUTH|$ACTIVE_UID|$ACTIVE_NAME|$ACCESS_TOKEN|$COOKIES""

# 3. Logged in profiles & pages
for f in $LP_DIR/logged_in_*; do
    [ -f ""$f"" ] || continue
    uid=$(basename ""$f"" | sed 's/logged_in_//')
    [ -n ""$uid"" ] || continue
    
    STR=$(strings ""$f"" 2>/dev/null)
    name=$(echo ""$STR"" | grep -A 1 ""^name$"" | tail -n 1)
    page_id=$(echo ""$STR"" | grep -A 1 ""^delegate_page_id$"" | tail -n 1)
    email=$(echo ""$STR"" | grep -A 1 ""^primary_contact$"" | tail -n 1)
    if [ -z ""$email"" ]; then
        email=$(echo ""$STR"" | grep -A 1 ""^emails$"" | tail -n 1)
    fi
    pic=$(echo ""$STR"" | grep -A 1 ""^pic_square$"" | tail -n 1)
    
    if [ -z ""$name"" ]; then
        fname=$(echo ""$STR"" | grep -A 1 ""^first_name$"" | tail -n 1)
        lname=$(echo ""$STR"" | grep -A 1 ""^last_name$"" | tail -n 1)
        name=""$fname $lname""
    fi
    
    if [ -n ""$name"" ]; then
        if [ -n ""$page_id"" ]; then
            echo ""ACCOUNT|Page|$uid|$page_id|$name|$ACTIVE_UID|$email|$pic""
        else
            echo ""ACCOUNT|Profile|$uid||$name|$uid|$email|$pic""
        fi
    fi
done

# 4. DBL login credentials in prefs_db
if [ -f ""$DB"" ]; then
    sqlite3 ""$DB"" ""SELECT value FROM preferences WHERE key LIKE '/auth/auth_device_based_login_credentials%';"" 2>/dev/null | while read -r line; do
        [ -n ""$line"" ] && echo ""DBL|$line""
    done
fi
";

    public async Task<List<FacebookAccountInfo>> ExtractFacebookAccountsFromRootShellAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );
        var ct = linkedCts.Token;

        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"extract_fb_{Guid.NewGuid():N}.sh");
            try
            {
                await File.WriteAllTextAsync(tempFile, RootExtractionScript, ct);
                await PushFileToEmulatorAsync(
                    index,
                    tempFile,
                    "/data/local/tmp/extract_fb_root.sh",
                    customPath,
                    ct
                );
            }
            catch (Exception pEx)
            {
                Debug.WriteLine($"[LD-{index}] Push script fallback: {pEx.Message}");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch { }
                }
            }

            await ExecuteAdbAsync(
                index,
                "shell chmod 755 /data/local/tmp/extract_fb_root.sh",
                customPath,
                ct
            );

            // Execute script with root privileges (clean syntax without nested quotes)
            var output = await ExecuteAdbAsync(
                index,
                $"shell su 0 sh /data/local/tmp/extract_fb_root.sh {packageName}",
                customPath,
                ct
            );

            return ParseRootShellAccountOutput(output);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[LD-{index}] Failed to extract accounts via root shell: {ex.Message}"
            );
            return [];
        }
    }

    public async Task<List<FacebookPageTarget>> SyncDiscoveredAccountsToStorageAsync(
        int index,
        string? deviceName = null,
        string appClone = "FB-01",
        string packageName = "com.facebook.katana",
        string? storageFilePath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var targetDevice = !string.IsNullOrWhiteSpace(deviceName) ? deviceName : $"LD-{index}";
        var discovered = await ExtractFacebookAccountsFromRootShellAsync(
            index,
            packageName,
            customPath,
            cancellationToken
        );

        var existingPages = await FacebookPageStorage.LoadPagesAsync(storageFilePath);

        foreach (var account in discovered)
        {
            var match = existingPages.FirstOrDefault(p =>
                string.Equals(p.DeviceName, targetDevice, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.AppClone, appClone, StringComparison.OrdinalIgnoreCase)
                && (
                    string.Equals(p.PageId, account.PageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PageId, account.Uid, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PageName, account.Name, StringComparison.OrdinalIgnoreCase)
                )
            );

            if (match != null)
            {
                if (!string.IsNullOrWhiteSpace(account.PageId))
                    match.PageId = account.PageId;
                if (!string.IsNullOrWhiteSpace(account.Uid))
                    match.PersonaUid = account.Uid;
                if (!string.IsNullOrWhiteSpace(account.UserUid))
                    match.UserUid = account.UserUid;
                if (!string.IsNullOrWhiteSpace(account.Name))
                    match.PageName = account.Name;
                if (!string.IsNullOrWhiteSpace(account.Type))
                    match.Type = account.Type;
                if (!string.IsNullOrWhiteSpace(account.Email))
                    match.Email = account.Email;
                if (
                    string.Equals(account.Type, "Page", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(account.UserUid)
                )
                    match.ManagedBy = account.UserUid;
            }
            else
            {
                var newTarget = account.ToFacebookPageTarget(targetDevice, appClone);
                existingPages.Add(newTarget);
            }
        }

        await FacebookPageStorage.SavePagesAsync(existingPages, storageFilePath);
        return existingPages
            .Where(p =>
                string.Equals(p.DeviceName, targetDevice, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    public static List<FacebookAccountInfo> ParseRootShellAccountOutput(string? output)
    {
        var accounts = new List<FacebookAccountInfo>();
        if (string.IsNullOrWhiteSpace(output))
            return accounts;

        string activeUid = string.Empty;
        string activeName = string.Empty;
        string activeAccessToken = string.Empty;
        string activeCookies = string.Empty;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("AUTH|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('|');
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    activeUid = parts[1].Trim();
                if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    activeName = parts[2].Trim();
                if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
                    activeAccessToken = parts[3].Trim();
                if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
                    activeCookies = parts[4].Trim();
            }
            else if (line.StartsWith("ACCOUNT|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('|');
                if (parts.Length < 6)
                    continue;

                var type = parts[1].Trim();
                var uid = parts[2].Trim();
                var pageId = parts[3].Trim();
                var name = parts[4].Trim();
                var userUid = parts[5].Trim();
                var email = parts.Length > 6 ? parts[6].Trim() : string.Empty;
                var pic = parts.Length > 7 ? parts[7].Trim() : string.Empty;

                if (
                    string.IsNullOrWhiteSpace(name)
                    && string.IsNullOrWhiteSpace(uid)
                    && string.IsNullOrWhiteSpace(pageId)
                )
                    continue;

                if (string.Equals(type, "Profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(pageId))
                        pageId = uid;
                    if (string.IsNullOrWhiteSpace(userUid))
                        userUid = uid;
                }

                var acc = new FacebookAccountInfo
                {
                    Type = string.Equals(type, "Page", StringComparison.OrdinalIgnoreCase)
                        ? "Page"
                        : "Profile",
                    Uid = uid,
                    PageId = pageId,
                    Name = name,
                    UserUid = userUid,
                    Email = email,
                    PicUrl = pic,
                    IsActive = (
                        !string.IsNullOrEmpty(activeUid)
                        && (uid == activeUid || pageId == activeUid)
                    ),
                };

                if (acc.IsActive)
                {
                    acc.AccessToken = activeAccessToken;
                    acc.Cookies = activeCookies;
                }

                var existing = accounts.FirstOrDefault(a =>
                    string.Equals(a.Type, acc.Type, StringComparison.OrdinalIgnoreCase)
                    && (
                        (!string.IsNullOrEmpty(a.PageId) && a.PageId == acc.PageId)
                        || (!string.IsNullOrEmpty(a.Uid) && a.Uid == acc.Uid)
                    )
                );

                if (existing == null)
                {
                    accounts.Add(acc);
                }
                else
                {
                    if (
                        string.IsNullOrWhiteSpace(existing.Name)
                        && !string.IsNullOrWhiteSpace(acc.Name)
                    )
                        existing.Name = acc.Name;
                    if (
                        string.IsNullOrWhiteSpace(existing.Email)
                        && !string.IsNullOrWhiteSpace(acc.Email)
                    )
                        existing.Email = acc.Email;
                    if (
                        string.IsNullOrWhiteSpace(existing.PicUrl)
                        && !string.IsNullOrWhiteSpace(acc.PicUrl)
                    )
                        existing.PicUrl = acc.PicUrl;
                    if (
                        string.IsNullOrWhiteSpace(existing.PageId)
                        && !string.IsNullOrWhiteSpace(acc.PageId)
                    )
                        existing.PageId = acc.PageId;
                    if (
                        string.IsNullOrWhiteSpace(existing.UserUid)
                        && !string.IsNullOrWhiteSpace(acc.UserUid)
                    )
                        existing.UserUid = acc.UserUid;
                    if (!existing.IsActive && acc.IsActive)
                    {
                        existing.IsActive = true;
                        existing.AccessToken = activeAccessToken;
                        existing.Cookies = activeCookies;
                    }
                }
            }
            else if (line.StartsWith("DBL|", StringComparison.OrdinalIgnoreCase))
            {
                var json = line[4..].Trim();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string dblUid = root.TryGetProperty("uid", out var u)
                        ? u.GetString() ?? ""
                        : "";
                    string dblName = root.TryGetProperty("full_name", out var fn)
                        ? fn.GetString() ?? ""
                        : "";
                    if (
                        string.IsNullOrWhiteSpace(dblName) && root.TryGetProperty("name", out var n)
                    )
                        dblName = n.GetString() ?? "";
                    string dblPic = root.TryGetProperty("pic_url", out var p)
                        ? p.GetString() ?? ""
                        : "";

                    if (!string.IsNullOrWhiteSpace(dblUid))
                    {
                        var existing = accounts.FirstOrDefault(a =>
                            a.Uid == dblUid || a.PageId == dblUid
                        );
                        if (existing == null)
                        {
                            accounts.Add(
                                new FacebookAccountInfo
                                {
                                    Type = "Profile",
                                    Uid = dblUid,
                                    PageId = dblUid,
                                    Name = dblName,
                                    UserUid = dblUid,
                                    PicUrl = dblPic,
                                    IsActive = (
                                        !string.IsNullOrEmpty(activeUid) && dblUid == activeUid
                                    ),
                                    AccessToken = (dblUid == activeUid ? activeAccessToken : ""),
                                    Cookies = (dblUid == activeUid ? activeCookies : ""),
                                }
                            );
                        }
                        else
                        {
                            if (
                                string.IsNullOrWhiteSpace(existing.PicUrl)
                                && !string.IsNullOrWhiteSpace(dblPic)
                            )
                                existing.PicUrl = dblPic;
                            if (
                                string.IsNullOrWhiteSpace(existing.Name)
                                && !string.IsNullOrWhiteSpace(dblName)
                            )
                                existing.Name = dblName;
                        }
                    }
                }
                catch { }
            }
        }

        if (
            !string.IsNullOrWhiteSpace(activeUid)
            && !accounts.Any(a => a.Uid == activeUid || a.PageId == activeUid)
        )
        {
            accounts.Insert(
                0,
                new FacebookAccountInfo
                {
                    Type = "Profile",
                    Uid = activeUid,
                    PageId = activeUid,
                    Name = activeName,
                    UserUid = activeUid,
                    IsActive = true,
                    AccessToken = activeAccessToken,
                    Cookies = activeCookies,
                }
            );
        }

        return accounts;
    }

    #endregion
}
