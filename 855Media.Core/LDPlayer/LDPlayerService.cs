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

public partial class LDPlayerService : ILDPlayerService
{
    private readonly AtxAgentClient _atxClient;

    public LDPlayerService()
        : this(null, null, null, null, null, null, null, null) { }

    public LDPlayerService(AtxAgentClient? atxClient)
        : this(atxClient, null, null, null, null, null, null, null) { }

    private static readonly string[] DefaultProbeDirectories =
    [
        @"C:\LDPlayer\LDPlayer9",
        @"D:\LDPlayer\LDPlayer9",
        @"C:\LDPlayer\LDPlayer14",
        @"D:\LDPlayer\LDPlayer14",
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

    public static bool IsKeyboardBroadcastCommand(string shellCommand) =>
        Regex.IsMatch(
            shellCommand.Trim(),
            @"^am\s+broadcast\b.*\bADB_(?:INPUT|CLEAR|EDITOR)_",
            RegexOptions.IgnoreCase
        );

    public virtual async Task<string> ExecuteAdbAsync(
        int index,
        string adbCommand,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            adbCommand.StartsWith("shell ", StringComparison.OrdinalIgnoreCase)
            && (
                IsKeyboardBroadcastCommand(adbCommand[6..])
                || Regex.IsMatch(adbCommand[6..], @"^input\s", RegexOptions.IgnoreCase)
            )
        )
            return await ExecuteDirectDeviceAdbAsync(
                index,
                adbCommand,
                customPath,
                cancellationToken
            );

        // 1. Fast path: If non-input shell command and atx-agent is running, execute via OpenATX HTTP (~5ms)
        // NOTE: Touch/input injection commands (input tap, swipe, keyevent, text) MUST run via real ADB
        // because atx-agent lacks INJECT_EVENTS permissions on Android emulators and fails silently.
        if (adbCommand.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
        {
            var shellCmd = adbCommand[6..].Trim();
            if (Regex.IsMatch(shellCmd, @"^su(?:\s|$)", RegexOptions.IgnoreCase))
                return await ExecuteDirectDeviceAdbAsync(
                    index,
                    adbCommand,
                    customPath,
                    cancellationToken,
                    rootCommand: true
                );
            // Root commands must use ADB's shell identity. ATX runs in a
            // different process context and can wait for a separate su grant
            // after boot, exhausting discovery's timeout without any output.
            if (
                CanExecuteThroughAtxShell(shellCmd)
                && await _atxClient.IsRunningAsync(index, cancellationToken)
            )
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

        // ldconsole can silently discard host commands such as `forward`,
        // and truncate output. Use the same device serial for input and dumps.
        return await ExecuteDirectDeviceAdbAsync(index, adbCommand, customPath, cancellationToken);
    }

    internal static bool CanExecuteThroughAtxShell(string shellCommand) =>
        !Regex.IsMatch(
            shellCommand.Trim(),
            @"^(?:input|su|run-as)(?:\s|$)",
            RegexOptions.IgnoreCase
        );

    private async Task<string> ExecuteDirectDeviceAdbAsync(
        int index,
        string adbCommand,
        string? customPath,
        CancellationToken cancellationToken,
        bool rootCommand = false
    )
    {
        var adbPath = Path.Combine(
            Path.GetDirectoryName(ResolveLdConsolePath(customPath))!,
            "adb.exe"
        );
        string Failure(string message)
        {
            if (rootCommand)
                throw new RootShellUnavailableException(message);
            return $"error: {message}";
        }
        if (!File.Exists(adbPath))
            return Failure($"ADB was not found at {adbPath}.");

        // ldconsole truncates binary LightSharedPreferences output. Read it
        // through adb.exe itself so account names and delegate IDs survive.
        foreach (
            var serial in new[] { $"emulator-{5554 + index * 2}", $"127.0.0.1:{5555 + index * 2}" }
        )
        {
            var (exitCode, stdout, stderr) = await RunProcessAsync(
                adbPath,
                $"-s {serial} {adbCommand}",
                cancellationToken
            );
            var output = !string.IsNullOrWhiteSpace(stdout) ? stdout : stderr;
            if (exitCode == 0)
                return output;
            if (
                !output.Contains("not found", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("offline", StringComparison.OrdinalIgnoreCase)
            )
                return Failure($"ADB command failed on LD-{index}: {output.Trim()}");
        }
        return Failure($"ADB could not reach LD-{index}; wait for Android to boot.");
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
        var res = await ExecuteDirectDeviceAdbAsync(
            index,
            "shell am force-stop com.facebook.katana",
            customPath,
            cancellationToken
        );
        return IsSuccessfulInputCommand(res);
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
            if (string.Equals(res.Trim(), "1", StringComparison.Ordinal))
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
        if (
            packageName.StartsWith("com.facebook.", StringComparison.OrdinalIgnoreCase)
            && !await WaitForFacebookReadyAsync(
                index,
                TimeSpan.FromSeconds(120),
                customPath,
                cancellationToken
            )
        )
            return false;

        if (await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            var ok = await _atxClient.StartAppAsync(index, packageName, cancellationToken);
            if (ok)
                return true;
        }

        var output = await ExecuteDirectDeviceAdbAsync(
            index,
            $"shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1",
            customPath,
            cancellationToken
        );
        return IsSuccessfulInputCommand(output)
            && !output.Contains("No activities found", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> OpenDeepLinkAsync(
        int index,
        string deepLink,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            packageName.StartsWith("com.facebook.", StringComparison.OrdinalIgnoreCase)
            && !await WaitForFacebookReadyAsync(
                index,
                TimeSpan.FromSeconds(120),
                customPath,
                cancellationToken
            )
        )
            return false;

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
        // Bind this port to the requested instance before accepting a daemon.
        // A stale forward must not return another instance's hierarchy.
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

        if (string.Equals(checkFile.Trim(), "1", StringComparison.Ordinal))
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
        var output = await ExecuteAdbAsync(
            index,
            $"shell input swipe {startX} {startY} {endX} {endY} {durationMs}",
            customPath,
            cancellationToken
        );
        return IsSuccessfulInputCommand(output);
    }

    public async Task<bool> HoldAndSwipeAsync(
        int index,
        int startX,
        int startY,
        int endX,
        int endY,
        int holdMs,
        int movementTargetMs,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !await _atxClient.IsRunningAsync(index, cancellationToken)
            && !await EnsureAtxAgentRunningAsync(index, customPath, cancellationToken)
        )
            return false;
        // Do not substitute an ADB swipe: it cannot preserve the separate stationary hold.
        return await _atxClient.HoldAndSwipeAsync(
            index,
            startX,
            startY,
            endX,
            endY,
            holdMs,
            movementTargetMs,
            cancellationToken
        );
    }

    public async Task<bool> TapAsync(
        int index,
        int x,
        int y,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var tapHierarchy = _atxClient.IsTapTracingEnabled
            ? await DumpUiHierarchyAsync(index, customPath, cancellationToken)
            : null;

        // Execute input tap via ADB (AID_SHELL privileges required for touch injection on emulators)
        var output = await ExecuteAdbAsync(
            index,
            $"shell input tap {x} {y}",
            customPath,
            cancellationToken
        );
        var success = !Regex.IsMatch(
            output,
            @"SecurityException|Permission Denial|(?:^|\n)\s*error:|device.*(?:not found|offline)|no devices/emulators|/system/bin/sh:.*(?:not found|syntax error)",
            RegexOptions.IgnoreCase
        );
        _atxClient.ReportTapObserved(index, x, y, tapHierarchy, "ADB", success);
        return success;
    }

    public async Task<bool> SendKeyAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // Key injection needs the same ADB shell privileges as touch injection.
        var output = await ExecuteAdbAsync(
            index,
            $"shell input keyevent {keyCode}",
            customPath,
            cancellationToken
        );
        return IsSuccessfulInputCommand(output);
    }

    public async Task<bool> ScanMediaFileAsync(
        int index,
        string remotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            await _atxClient.IsRunningAsync(index, cancellationToken)
            && await _atxClient.ScanMediaFileAsync(index, remotePath, cancellationToken)
        )
            return true;
        cancellationToken.ThrowIfCancellationRequested();
        var output = await ExecuteAdbAsync(
            index,
            "shell " + PFlowMediaIntent.ScanCommand(remotePath),
            customPath,
            cancellationToken
        );
        return PFlowMediaIntent.ScanAcknowledged(output);
    }

    public async Task<bool> ShareVideoToFacebookAsync(
        int index,
        string remotePath,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var command = PFlowMediaIntent.ShareCommand(remotePath, packageName, false);
        if (command == null)
            return false;
        if (
            await _atxClient.IsRunningAsync(index, cancellationToken)
            && await _atxClient.ShareVideoAsync(index, remotePath, packageName, cancellationToken)
        )
            return true;
        cancellationToken.ThrowIfCancellationRequested();
        var output = await ExecuteAdbAsync(
            index,
            "shell " + command,
            customPath,
            cancellationToken
        );
        return PFlowMediaIntent.ShareStarted(output);
    }

    public async Task<bool> DeleteFileFromEmulatorAsync(
        int index,
        string remotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(remotePath))
            return true;

        try
        {
            if (await _atxClient.IsRunningAsync(index, cancellationToken))
            {
                await _atxClient.ExecuteShellAsync(
                    index,
                    $"rm -f \"{remotePath}\"",
                    cancellationToken
                );
            }
            else
            {
                await ExecuteAdbAsync(
                    index,
                    $"shell rm -f \"{remotePath}\"",
                    customPath,
                    cancellationToken
                );
            }

            await ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[LD-{index}] Failed to delete remote file '{remotePath}': {ex.Message}"
            );
            return false;
        }
    }

    // Upload inactivity is progress only; P-Flow requires separate publication evidence.
    public Task<bool> WaitForFacebookUploadCompletedAsync(
        int index,
        string? remotePath = null,
        TimeSpan? timeout = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _autoPostService.WaitForUploadCompletedAsync(
            index,
            remotePath,
            timeout,
            customPath,
            cancellationToken
        );

    private static bool IsFacebookUploadNotificationActive(string notifDumpsys)
    {
        if (string.IsNullOrWhiteSpace(notifDumpsys))
            return false;

        using var reader = new StringReader(notifDumpsys);
        string? line;
        bool inActiveRecord = false;
        bool isKatana = false;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains("NotificationRecord("))
            {
                inActiveRecord = true;
                isKatana = line.Contains("pkg=com.facebook.katana");
                if (isKatana && line.Contains("channel=media_upload_notification_channel_id"))
                    return true;
            }
            else if (inActiveRecord)
            {
                if (
                    string.IsNullOrWhiteSpace(line)
                    || line.StartsWith("  NotificationList")
                    || line.StartsWith("  mArchive")
                )
                {
                    inActiveRecord = false;
                    isKatana = false;
                }
                else if (isKatana)
                {
                    if (
                        line.Contains("media_upload_notification_channel_id")
                        || (
                            line.Contains("flags=0x6a")
                            || line.Contains("flags=0x62")
                            || line.Contains("flags=0x40")
                        )
                        || (
                            line.Contains("Posting")
                            || line.Contains("Uploading")
                            || line.Contains("Finishing up")
                        )
                    )
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public async Task<bool> ShareImageToFacebookAsync(
        int index,
        string remotePath,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var command = PFlowMediaIntent.ShareCommand(remotePath, packageName, true);
        if (command == null)
            return false;
        if (
            await _atxClient.IsRunningAsync(index, cancellationToken)
            && await _atxClient.ShareImageAsync(index, remotePath, packageName, cancellationToken)
        )
            return true;
        cancellationToken.ThrowIfCancellationRequested();
        var output = await ExecuteAdbAsync(
            index,
            "shell " + command,
            customPath,
            cancellationToken
        );
        return PFlowMediaIntent.ShareStarted(output);
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

        personaName = personaName.Trim();

        // 1. Inspect current screen
        var currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(currentXml))
        {
            await LaunchAppAsync(index, "com.facebook.katana", customPath, cancellationToken);
            await Task.Delay(2000, cancellationToken);
            currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        }

        // 1a. Check if already on the target persona
        if (IsAlreadyOnTargetPersona(currentXml, personaName))
        {
            if (IsAccountSwitcherOpen(currentXml))
            {
                await PressBackAsync(index, customPath, cancellationToken);
                await Task.Delay(800, cancellationToken);
            }
            return await WaitForPersonaActiveAsync(
                index,
                personaName,
                customPath,
                cancellationToken
            );
        }

        // 1b. Check if direct switch button exists on the current screen (e.g. "Switch to [PageName]")
        var directBtn = FindDirectSwitchButton(currentXml, personaName);
        if (directBtn != null)
        {
            var (dcx, dcy) = ParseNodeBoundsCenter(directBtn);
            if (dcx > 0 && dcy > 0)
            {
                await TapAsync(index, dcx, dcy, customPath, cancellationToken);
                return await WaitForPersonaActiveAsync(
                    index,
                    personaName,
                    customPath,
                    cancellationToken
                );
            }
        }

        // 2. Open Menu and then profile switcher sheet if not already open
        bool isSwitcherOpen = IsAccountSwitcherOpen(currentXml);
        if (!isSwitcherOpen)
        {
            await OpenFacebookMenuFromFeedAsync(index, customPath, cancellationToken);
            await Task.Delay(2000, cancellationToken);
            currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);

            if (IsAlreadyOnTargetPersona(currentXml, personaName))
            {
                return await WaitForPersonaActiveAsync(
                    index,
                    personaName,
                    customPath,
                    cancellationToken
                );
            }

            directBtn = FindDirectSwitchButton(currentXml, personaName);
            if (directBtn != null)
            {
                var (dcx, dcy) = ParseNodeBoundsCenter(directBtn);
                if (dcx > 0 && dcy > 0)
                {
                    await TapAsync(index, dcx, dcy, customPath, cancellationToken);
                    return await WaitForPersonaActiveAsync(
                        index,
                        personaName,
                        customPath,
                        cancellationToken
                    );
                }
            }

            if (!IsAccountSwitcherOpen(currentXml))
            {
                await ClickProfileSwitcherDropdownAsync(
                    index,
                    personaName,
                    customPath,
                    cancellationToken
                );
                await Task.Delay(2500, cancellationToken);
                currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            }
        }

        // 3. Search and tap target persona in switcher sheet (with vertical swipe if off-screen)
        for (int attempt = 0; attempt < 4; attempt++)
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
                        await PressBackAsync(index, customPath, cancellationToken);
                        await Task.Delay(800, cancellationToken);
                    }
                    return await WaitForPersonaActiveAsync(
                        index,
                        personaName,
                        customPath,
                        cancellationToken
                    );
                }

                await TapAsync(index, cx, cy, customPath, cancellationToken);
                await Task.Delay(5500, cancellationToken);

                var verifyXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
                if (IsAccountSwitcherOpen(verifyXml))
                {
                    await PressBackAsync(index, customPath, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                }
                return await WaitForPersonaActiveAsync(
                    index,
                    personaName,
                    customPath,
                    cancellationToken
                );
            }

            // Target not visible in current view: scroll down in the switcher bottom sheet
            if (attempt < 3 && IsAccountSwitcherOpen(currentXml))
            {
                var (sw, sh) = ParseScreenDimensions(currentXml);
                if (sw <= 0)
                    sw = 720;
                if (sh <= 0)
                    sh = 1280;
                await SwipeAsync(
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
                currentXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            }
            else
            {
                break;
            }
        }

        // If not found or couldn't switch, dismiss open sheet so screen isn't blocked
        if (IsAccountSwitcherOpen(currentXml))
        {
            await PressBackAsync(index, customPath, cancellationToken);
            await Task.Delay(800, cancellationToken);
        }

        return false;
    }

    private async Task<bool> WaitForPersonaActiveAsync(
        int index,
        string personaName,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (IsAlreadyOnTargetPersona(xml, personaName))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }

        return false;
    }

    /// <summary>Verify the native personal identity once and retain the Facebook session.</summary>
    public Task<bool> EnsureSwitchedToPersonalProfileAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _personaSwitcher.EnsureSwitchedToPersonalProfileAsync(
            index,
            targetProfileName,
            customPath,
            cancellationToken
        );

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
        // Feed content can appear before the navigation bar after a persona switch.
        // Wait for the actual Menu/Profile control instead of failing on that first dump.
        for (var navigationWait = 0; navigationWait < 20; navigationWait++)
        {
            if (
                bloksScreen != null
                && (
                    AtxAgentClient.IsMenuPageInBloks(bloksScreen)
                    || AtxAgentClient.FindMenuTabInBloks(bloksScreen).HasValue
                )
            )
                break;
            await Task.Delay(400, cancellationToken);
            bloksScreen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        }
        if (bloksScreen != null && AtxAgentClient.IsMenuPageInBloks(bloksScreen))
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "menu.screen.already-open",
                PFlowFetchTrace.SummarizeScreen(bloksScreen),
                cancellationToken
            );
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
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "menu.tab.already-selected",
                        $"x={bloksTab.Value.X}; y={bloksTab.Value.Y}",
                        cancellationToken
                    );
                    return true;
                }

                await PFlowFetchTrace.WriteAsync(
                    index,
                    "menu.tab.tap-bloks",
                    $"x={bloksTab.Value.X}; y={bloksTab.Value.Y}; selected={bloksTab.Value.IsSelected}",
                    cancellationToken
                );
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
                        await TapAsync(index, hcx, hcy, customPath, cancellationToken);
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
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
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
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "menu.tab.tap-rightmost",
                        $"x={rightmost.CenterX}; y={rightmost.CenterY}; selected={rightmost.IsSelected}; candidates={bottomTabs.Count}",
                        cancellationToken
                    );
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

        await PFlowFetchTrace.WriteAsync(
            index,
            "menu.tab.not-found",
            PFlowFetchTrace.SummarizeScreen(
                AtxAgentClient.ParseBloksHierarchy(
                    feedXml,
                    await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken)
                )
            ),
            cancellationToken
        );
        return false;
    }

    /// <summary>
    /// Step 6: Clicks the dropdown profile list button (chevron / options button on the active profile card).
    /// </summary>
    public async Task<bool> ClickProfileSwitcherDropdownAsync(
        int index,
        string? profileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var menuXml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(menuXml))
        {
            // When we know which signed-in profile should be active, bind the
            // tap to its own header row. Generic "profile/account" nodes and
            // fixed coordinates can land on a public profile or cover photo.
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                var headerButton = FindProfileHeaderSwitcherButton(menuXml, profileName);
                if (headerButton != null)
                {
                    var (headerX, headerY) = ParseNodeBoundsCenter(headerButton);
                    if (headerX > 0 && headerY > 0)
                    {
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "switcher.dropdown.tap-profile-header",
                            $"x={headerX}; y={headerY}",
                            cancellationToken
                        );
                        await TapAsync(index, headerX, headerY, customPath, cancellationToken);
                        return true;
                    }
                }
            }

            var hierarchyText = string.Join(
                " ",
                SafeParseXmlDocuments(menuXml)
                    .SelectMany(doc => doc.Descendants("node"))
                    .Select(node =>
                        $"{node.Attribute("text")?.Value} {node.Attribute("content-desc")?.Value}"
                    )
            );
            var isOwnProfileScreen =
                hierarchyText.Contains("Edit profile", StringComparison.OrdinalIgnoreCase)
                || hierarchyText.Contains(
                    "Professional dashboard",
                    StringComparison.OrdinalIgnoreCase
                )
                || hierarchyText.Contains("Add to story", StringComparison.OrdinalIgnoreCase);
            if (
                isOwnProfileScreen
                || AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(menuXml))
            )
            {
                var activeHeaderButton = FindProfileHeaderSwitcherButton(menuXml, null);
                if (activeHeaderButton != null)
                {
                    var (activeX, activeY) = ParseNodeBoundsCenter(activeHeaderButton);
                    if (activeX > 0 && activeY > 0)
                    {
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "switcher.dropdown.tap-active-profile-header",
                            $"x={activeX}; y={activeY}",
                            cancellationToken
                        );
                        await TapAsync(index, activeX, activeY, customPath, cancellationToken);
                        return true;
                    }
                }
            }

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

                    if (y1 >= 48 && y2 <= 350 && x1 >= 200)
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
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "switcher.dropdown.tap-labeled-control",
                            $"x={cx}; y={cy}",
                            cancellationToken
                        );
                        await TapAsync(index, cx, cy, customPath, cancellationToken);
                        return true;
                    }
                }
            }
        }

        await PFlowFetchTrace.WriteAsync(
            index,
            "switcher.dropdown.not-found",
            $"profileNameKnown={!string.IsNullOrWhiteSpace(profileName)}; {PFlowFetchTrace.SummarizeScreen(AtxAgentClient.ParseBloksHierarchy(menuXml, null))}",
            cancellationToken
        );
        return false;
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
        if (PFlowEmailUpdatePrompt.Card(xml) != null)
        {
            await PFlowEmailUpdatePrompt.DismissAsync(
                this,
                index,
                xml,
                customPath,
                cancellationToken
            );
            return true;
        }

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

    // Legacy signatures lack a verified target/history boundary. P-Flow owns publication.
    public Task<bool> PostReelToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _autoPostService.PostReelAsync(
            index,
            localVideoPath,
            caption,
            customPath,
            cancellationToken
        );

    public Task<bool> PostPictureToFacebookAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _autoPostService.PostPictureAsync(
            index,
            localImagePath,
            caption,
            customPath,
            cancellationToken
        );

    public Task<bool> PostVideoToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _autoPostService.PostVideoAsync(
            index,
            localVideoPath,
            caption,
            customPath,
            cancellationToken
        );

    private async Task<bool> ClickElementByMatchingTextAsync(
        int index,
        string? xml,
        string[] candidates,
        string? customPath,
        CancellationToken ct
    )
    {
        // Read the hierarchy through ATX, but inject touches through real ADB.

        if (string.IsNullOrWhiteSpace(xml))
            return false;

        try
        {
            var docs = SafeParseXmlDocuments(xml);
            foreach (var doc in docs)
            {
                foreach (var candidate in candidates)
                {
                    var match = doc.Descendants("node")
                        .FirstOrDefault(n =>
                        {
                            var text = n.Attribute("text")?.Value?.Trim();
                            var desc = n.Attribute("content-desc")?.Value?.Trim();
                            return string.Equals(
                                    text,
                                    candidate,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(
                                    desc,
                                    candidate,
                                    StringComparison.OrdinalIgnoreCase
                                );
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

            // Look for explicit edit text or autocomplete views
            var editNode = nodes.FirstOrDefault(n =>
            {
                var cls = n.Attribute("class")?.Value ?? "";
                return cls.Contains("EditText", StringComparison.OrdinalIgnoreCase)
                    || cls.Contains("AutoCompleteTextView", StringComparison.OrdinalIgnoreCase)
                    || cls.Contains("TextInput", StringComparison.OrdinalIgnoreCase);
            });
            if (editNode != null)
                return editNode;

            // Look for resource-id containing caption or description
            var resNode = nodes.FirstOrDefault(n =>
            {
                var res = n.Attribute("resource-id")?.Value ?? "";
                return res.Contains("caption", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("description", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("composer_text", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("text_input", StringComparison.OrdinalIgnoreCase);
            });
            if (resNode != null)
                return resNode;

            // Fallback: any focusable view in the upper half of screen
            return nodes.FirstOrDefault(n =>
            {
                var focusable = string.Equals(
                    n.Attribute("focusable")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase
                );
                var (x, y) = ParseNodeBoundsCenter(n);
                return focusable && y > 100 && y < 750;
            });
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
        // Port forwards disappear when Android/ADB restarts. Repair the
        // connection before interpreting a missing hierarchy as missing data.
        if (!await _atxClient.IsRunningAsync(index, cancellationToken))
            await EnsureAtxAgentRunningAsync(index, customPath, cancellationToken);
        // 1. Fast path: Query atx-agent on-device daemon via HTTP (~15ms)
        var atxDump = await _atxClient.DumpHierarchyAsync(index, cancellationToken);
        if (
            !string.IsNullOrWhiteSpace(atxDump)
            && atxDump.Contains("<node", StringComparison.OrdinalIgnoreCase)
        )
        {
            return atxDump;
        }

        // 2. Standard ADB fallback path (~2.5s)
        var dumpResult = await ExecuteAdbAsync(
            index,
            "shell uiautomator dump /sdcard/window_dump.xml",
            customPath,
            cancellationToken
        );

        // Never read an older window dump when the current dump failed.
        if (!dumpResult.Contains("dumped", StringComparison.OrdinalIgnoreCase))
            return null;

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
                @"(?:(?<value>[\d,.]+\s*[KMBkmb]?)\s*(?<label>followers|fans)\b|\b(?<labelAfter>followers|fans)\s*[:·]?\s*(?<valueAfter>[\d,.]+\s*[KMBkmb]?))",
                RegexOptions.IgnoreCase
            );
            var reachRegex = new Regex(
                @"(?:(?<value>[\d,.]+\s*[KMBkmb]?)\s*(?<label>post reach|reach)\b|\b(?<labelAfter>post reach|reach)\s*[:·]?\s*(?<valueAfter>[\d,.]+\s*[KMBkmb]?))",
                RegexOptions.IgnoreCase
            );
            var reelsRegex = new Regex(
                @"(?:([\d,.]+\s*[KMB]?)\s*reels\b|\breels\s*[:·]?\s*([\d,.]+\s*[KMB]?))",
                RegexOptions.IgnoreCase
            );
            var videosRegex = new Regex(
                @"(?:([\d,.]+\s*[KMB]?)\s*videos\b|\bvideos\s*[:·]?\s*([\d,.]+\s*[KMB]?))",
                RegexOptions.IgnoreCase
            );
            var picturesRegex = new Regex(
                @"(?:([\d,.]+\s*[KMB]?)\s*(?:photos|pictures)\b|\b(?:photos|pictures)\s*[:·]?\s*([\d,.]+\s*[KMB]?))",
                RegexOptions.IgnoreCase
            );
            var joinedVisibleText = string.Join(" ", texts);
            var metricTexts = nodes
                .Where(node => !IsIndexedNavigationNode(node))
                .SelectMany(node =>
                    new[] { node.Attribute("text")?.Value, node.Attribute("content-desc")?.Value }
                )
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToHashSet();

            // Bloks often renders a metric label and its value as adjacent
            // accessibility nodes, so parse both individual nodes and the
            // combined screen text.
            foreach (var t in texts)
            {
                var createdMatch = Regex.Match(
                    t,
                    @"(?:Page\s+)?(?:created|joined|created on)\s*[:·,]?\s*((?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4})",
                    RegexOptions.IgnoreCase
                );
                if (createdMatch.Success && string.IsNullOrWhiteSpace(info.CreationDate))
                    info.CreationDate = createdMatch.Groups[1].Value.Trim();
                if (!metricTexts.Contains(t))
                    continue;

                var fm = followerRegex.Match(t);
                if (fm.Success && !info.FollowersKnown)
                {
                    var value = fm.Groups["value"].Success
                        ? fm.Groups["value"].Value
                        : fm.Groups["valueAfter"].Value;
                    info.Followers = ParseCountString(value);
                    info.FollowersKnown = true;
                }

                var rm = reachRegex.Match(t);
                if (rm.Success && string.IsNullOrEmpty(info.Reach))
                {
                    var value = rm.Groups["value"].Success
                        ? rm.Groups["value"].Value
                        : rm.Groups["valueAfter"].Value;
                    var label = rm.Groups["label"].Success
                        ? rm.Groups["label"].Value
                        : rm.Groups["labelAfter"].Value;
                    info.Reach = $"{value} {label}";
                }

                var reelMatch = reelsRegex.Match(t);
                if (reelMatch.Success && !info.ReelsCountKnown)
                {
                    info.ReelsCount = ParseCountString(
                        reelMatch.Groups[1].Success
                            ? reelMatch.Groups[1].Value
                            : reelMatch.Groups[2].Value
                    );
                    info.ReelsCountKnown = true;
                }

                var videoMatch = videosRegex.Match(t);
                if (videoMatch.Success && !info.VideosCountKnown)
                {
                    info.VideosCount = ParseCountString(
                        videoMatch.Groups[1].Success
                            ? videoMatch.Groups[1].Value
                            : videoMatch.Groups[2].Value
                    );
                    info.VideosCountKnown = true;
                }

                var pictureMatch = picturesRegex.Match(t);
                if (pictureMatch.Success && !info.PictureCountKnown)
                {
                    info.PictureCount = ParseCountString(
                        pictureMatch.Groups[1].Success
                            ? pictureMatch.Groups[1].Value
                            : pictureMatch.Groups[2].Value
                    );
                    info.PictureCountKnown = true;
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

            // Page Transparency often exposes the label and value as separate
            // accessibility nodes ("Date created" / "Aug 12, 2020"). Join the
            // hierarchy text so that split labels and abbreviated month names parse.
            if (string.IsNullOrWhiteSpace(info.CreationDate))
            {
                const string month =
                    @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";
                var joinedCreatedMatch = Regex.Match(
                    joinedVisibleText,
                    $@"(?:page\s+)?(?:date\s+created|creation\s+date|page\s+created|created(?:\s+(?:on|in))?|joined(?:\s+(?:Facebook\s+)?(?:in\s+)?(?:on\s+)?)?)\s*[:·,]?\s*({month}\s+\d{{1,2}},?\s+\d{{4}}|\d{{1,2}}\s+{month}\s+\d{{4}}|{month}\s+\d{{4}}|(?:19|20)\d{{2}})",
                    RegexOptions.IgnoreCase
                );
                if (joinedCreatedMatch.Success)
                    info.CreationDate = joinedCreatedMatch.Groups[1].Value.Trim();
            }

            var hierarchy = AtxAgentClient.ParseBloksHierarchy(xml);
            info.Birthdate = ExtractHierarchyFieldValue(
                hierarchy,
                ["Birthday", "Date of birth", "Birth date"]
            );
            if (FacebookPageTarget.TryParseCreationDate(info.CreationDate) == null)
                info.CreationDate = ExtractJoinedDate(hierarchy);

            // Counts on dashboard cards are often separate label/value nodes.
            // Pair nodes by their row/card bounds rather than concatenating the
            // entire screen, which can attach a neighbouring card's number.
            var followers = ExtractHierarchyMetric(hierarchy, ["Followers", "Fans"]);
            if (followers != null)
            {
                info.Followers = ParseCountString(followers);
                info.FollowersKnown = true;
            }
            var reach = ExtractHierarchyMetric(hierarchy, ["Reach", "Post reach"]);
            if (reach != null)
                info.Reach = $"{reach} reach";
            var reels = ExtractHierarchyMetric(hierarchy, ["Reels"]);
            if (reels != null)
            {
                info.ReelsCount = ParseCountString(reels);
                info.ReelsCountKnown = true;
            }
            var videos = ExtractHierarchyMetric(hierarchy, ["Videos"]);
            if (videos != null)
            {
                info.VideosCount = ParseCountString(videos);
                info.VideosCountKnown = true;
            }
            var pictures = ExtractHierarchyMetric(hierarchy, ["Photos", "Pictures"]);
            if (pictures != null)
            {
                info.PictureCount = ParseCountString(pictures);
                info.PictureCountKnown = true;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RootShellUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LD-{index}] Root-shell device discovery failed: {ex.Message}");
        }

        return null;
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
        CancellationToken cancellationToken = default,
        Func<FacebookAccountDetails, Task>? rootShellCompleted = null,
        Func<string, Task>? progress = null
    )
    {
        var details = await ExtractFacebookAccountDetailsCoreAsync(
            index,
            customPath,
            cancellationToken,
            rootShellCompleted,
            progress
        );
        if (!string.IsNullOrWhiteSpace(details?.Uid))
            return details;
        var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowUiPrivacy.ContainsInput(xml ?? "")
            || PFlowAutoPostService.IsComposer(xml ?? "", "Reel")
        )
            return details;
        if (progress != null)
            await progress(
                "Account UID unavailable: clearing recent apps and reopening Facebook before one retry."
            );
        await PFlowSession.RestartForUidRecoveryAsync(this, index, customPath, cancellationToken);
        return await ExtractFacebookAccountDetailsCoreAsync(
            index,
            customPath,
            cancellationToken,
            rootShellCompleted,
            progress
        );
    }

    private async Task<FacebookAccountDetails?> ExtractFacebookAccountDetailsCoreAsync(
        int index,
        string? customPath,
        CancellationToken cancellationToken,
        Func<FacebookAccountDetails, Task>? rootShellCompleted,
        Func<string, Task>? progress
    )
    {
        await PFlowFetchTrace.WriteAsync(
            index,
            "root-shell.begin",
            "Reading Facebook profile and Page records.",
            cancellationToken
        );
        if (progress != null)
            await progress("Root shell: reading the personal account and managed Page records.");

        List<FacebookAccountInfo> rootAccounts;
        try
        {
            rootAccounts = await ExtractFacebookAccountsFromRootShellAsync(
                index,
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RootShellUnavailableException)
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "root-shell.unavailable",
                "Root access or Facebook storage could not be read.",
                cancellationToken
            );
            throw;
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "root-shell.error",
                $"{ex.GetType().Name}: {ex.Message}",
                cancellationToken
            );
            Debug.WriteLine($"[LD-{index}] Root-shell account extraction failed: {ex.Message}");
            return null;
        }

        if (rootAccounts.Count == 0)
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "root-shell.empty",
                "No account or Page records were found.",
                cancellationToken
            );
            Debug.WriteLine(
                $"[LD-{index}] Root-shell discovery returned no account or Page UIDs; UI scraping is disabled."
            );
            return null;
        }

        var profileAccount = rootAccounts.FirstOrDefault(a =>
            string.Equals(a.Type, "Profile", StringComparison.OrdinalIgnoreCase)
        );
        var userUid =
            !string.IsNullOrWhiteSpace(profileAccount?.UserUid) ? profileAccount.UserUid
            : !string.IsNullOrWhiteSpace(profileAccount?.Uid) ? profileAccount.Uid
            : rootAccounts
                .Select(a => a.UserUid)
                .FirstOrDefault(uid => !string.IsNullOrWhiteSpace(uid))
                ?? string.Empty;

        var details = new FacebookAccountDetails
        {
            DeviceName = $"LD-{index}",
            Name = profileAccount?.Name ?? string.Empty,
            Uid = userUid,
            Email = profileAccount?.Email ?? string.Empty,
            ProfilePictureUrl = profileAccount?.PicUrl ?? string.Empty,
        };

        if (string.IsNullOrWhiteSpace(userUid))
            return null;

        foreach (
            var pageAccount in rootAccounts.Where(a =>
                string.Equals(a.Type, "Page", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var personaUid = !string.IsNullOrWhiteSpace(pageAccount.Uid)
                ? pageAccount.Uid
                : pageAccount.PageId;
            if (
                string.IsNullOrWhiteSpace(personaUid)
                || string.Equals(personaUid, userUid, StringComparison.Ordinal)
                || details.Pages.Any(p =>
                    string.Equals(p.PersonaUid, personaUid, StringComparison.Ordinal)
                )
            )
                continue;

            details.Pages.Add(
                new FacebookPageInfo
                {
                    PageId = pageAccount.PageId,
                    PersonaUid = personaUid,
                    UserUid = userUid,
                    PageName = pageAccount.Name,
                    Type = "Page",
                    Email = pageAccount.Email,
                    ManagedBy = details.Name,
                }
            );
        }

        if (progress != null)
        {
            await progress(
                $"Root shell: found the personal account and {details.Pages.Count} managed Page(s)."
            );
        }

        await PFlowFetchTrace.WriteAsync(
            index,
            "root-shell.complete",
            $"profileFound={!string.IsNullOrWhiteSpace(details.Uid)}; pageCount={details.Pages.Count}",
            cancellationToken
        );

        // Publish root-shell rows as soon as they are parsed, before any slower enrichment.
        if (rootShellCompleted != null)
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "root-shell.callback.begin",
                cancellationToken: cancellationToken
            );
            await rootShellCompleted(details);
            await PFlowFetchTrace.WriteAsync(
                index,
                "root-shell.callback.complete",
                cancellationToken: cancellationToken
            );
        }

        return details;
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
            || FacebookNavigationLabels.IsSectionExpansion(t)
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
                            && !FacebookNavigationLabels.IsSectionExpansion(val)
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

                bool isSwitchPhrase =
                    label.Contains("switch into", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("switch to", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("switch profile", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Switch to ", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Switch into ", StringComparison.OrdinalIgnoreCase);

                if (isSwitchPhrase)
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

    public static bool IsAccountSwitcherOpen(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        return xml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Go to Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || (
                xml.Contains("Dismiss", StringComparison.OrdinalIgnoreCase)
                && xml.Contains("Page", StringComparison.OrdinalIgnoreCase)
            );
    }

    public static bool IsAlreadyOnTargetPersona(string? xml, string personaName)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(personaName))
            return false;

        // If in switcher sheet, check if target persona is marked selected
        if (IsAccountSwitcherOpen(xml))
        {
            var match = FindPersonaInSwitcher(xml, personaName);
            return match.HasValue && match.Value.IsSelected;
        }

        // Facebook can show a managed Page to its owner while the personal profile
        // remains active. That visitor view is not a successful persona switch.
        if (
            xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Switch into your page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Switch into", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        // A visitor can see the Page name in its public header while an
        // "Admin quick access / switch to [Page]" action is still present.
        // That screen is not proof that Facebook has switched personas.
        if (FindDirectSwitchButton(xml, personaName) != null)
            return false;

        // Check if top header card contains personaName
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
                if (cy > 0 && cy < 350)
                {
                    if (
                        string.Equals(label, personaName, StringComparison.OrdinalIgnoreCase)
                        || label.Equals(
                            $"{personaName}, see your profile",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || label.StartsWith($"{personaName},", StringComparison.OrdinalIgnoreCase)
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

    public static (int X, int Y, bool IsSelected)? FindPersonaInSwitcher(
        string? switcherXml,
        string personaName
    )
    {
        if (string.IsNullOrWhiteSpace(switcherXml) || string.IsNullOrWhiteSpace(personaName))
            return null;

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

                bool isExact = string.Equals(
                    label,
                    personaName,
                    StringComparison.OrdinalIgnoreCase
                );
                bool isMatch =
                    isExact
                    || IsPageTitleMatch(
                        NormalizeHierarchyText(label),
                        NormalizeHierarchyText(personaName)
                    )
                    || (
                        label.Length >= 5
                        && (label.EndsWith('…') || label.EndsWith("...", StringComparison.Ordinal))
                        && personaName.StartsWith(
                            label.TrimEnd('…', '.'),
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                if (!isMatch)
                    continue;

                var (cx, cy) = ParseNodeBoundsCenter(node);
                if (cx <= 0 || cy <= 0)
                    continue;

                // Inspect one bounded row. A common parent can contain all
                // personas and its selected checkmark belongs to only one row.
                var nameBounds = ParseAccessibilityBounds(node);
                var row = node.Ancestors("node")
                    .FirstOrDefault(a =>
                    {
                        var bounds = ParseAccessibilityBounds(a);
                        return bounds.Height > 0
                            && bounds.Height <= 200
                            && bounds.Top <= nameBounds.Top
                            && bounds.Bottom >= nameBounds.Bottom;
                    });
                var selectedNodes =
                    row != null
                        ? row.DescendantsAndSelf("node")
                        : allNodes.Where(other =>
                        {
                            var bounds = ParseAccessibilityBounds(other);
                            return bounds.Height > 0
                                && bounds.Height <= 120
                                && Math.Abs((bounds.Top + bounds.Bottom) / 2 - cy) <= 45;
                        });
                bool isSelected =
                    IsAccessibilityNodeSelected(node)
                    || selectedNodes.Any(IsAccessibilityNodeSelected);

                candidates.Add((node, label, isExact, isSelected, cx, cy));
            }

            if (candidates.Count > 0)
            {
                // On a bottom sheet the dimmed profile's header can carry the
                // same name. Prefer the row in the sheet below that header.
                var best = candidates
                    .Where(c => c.IsExact)
                    .OrderByDescending(c => c.Y)
                    .FirstOrDefault();
                if (best.Node == null)
                {
                    best = candidates.FirstOrDefault(c =>
                        c.Label.StartsWith(personaName, StringComparison.OrdinalIgnoreCase)
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
            var maxRight = 0;
            var maxBottom = 0;
            foreach (var doc in docs)
            {
                foreach (var node in doc.Descendants("node"))
                {
                    var nodeBounds = node.Attribute("bounds")?.Value;
                    if (string.IsNullOrWhiteSpace(nodeBounds))
                        continue;
                    var nodeMatch = Regex.Match(nodeBounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if (!nodeMatch.Success)
                        continue;
                    maxRight = Math.Max(maxRight, int.Parse(nodeMatch.Groups[3].Value));
                    maxBottom = Math.Max(maxBottom, int.Parse(nodeMatch.Groups[4].Value));
                }
            }
            // Use the extent of the entire hierarchy. The first root child is
            // often only a card/feed viewport (e.g. y=0..350), not the full
            // device; treating it as screen height caused content buttons to
            // be mistaken for the bottom Profile tab.
            if (maxRight > 0 && maxBottom > 0)
                return (maxRight, maxBottom);
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
        // Read the hierarchy through ATX, but inject touches through real ADB.

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
        // Read the hierarchy through ATX, but inject touches through real ADB.

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
        // Read the hierarchy through ATX, but inject touches through real ADB.

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
            var atxSent = await _atxClient.InputTextAsync(index, text, cancellationToken);
            if (atxSent)
                return true;
        }

        // Check & enable ADB Keyboard for full Unicode/Emoji/linebreaks support
        bool isInstalled = await IsAdbKeyboardInstalledAsync(index, customPath, cancellationToken);
        if (!isInstalled)
        {
            await InstallAdbKeyboardAsync(index, null, customPath, cancellationToken);
            isInstalled = await IsAdbKeyboardInstalledAsync(index, customPath, cancellationToken);
        }

        if (isInstalled)
        {
            await EnableAdbKeyboardAsync(index, customPath, cancellationToken);
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

        await ExecuteAdbAsync(
            index,
            $"shell input text \"{escaped}\"",
            customPath,
            cancellationToken
        );
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

        // Ensure ADB Keyboard is active IME before broadcasting
        await EnableAdbKeyboardAsync(index, customPath, cancellationToken);

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
        return !string.IsNullOrWhiteSpace(output)
            && !output.Contains("Error", StringComparison.OrdinalIgnoreCase);
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
            $"shell ime enable {AdbKeyboardConstants.ImeComponent} && ime set {AdbKeyboardConstants.ImeComponent} && settings put secure default_input_method {AdbKeyboardConstants.ImeComponent}",
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
        return await SendKeyAsync(index, 3, customPath, cancellationToken);
    }

    public async Task<bool> PressBackAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return await SendKeyAsync(index, 4, customPath, cancellationToken);
    }

    public async Task<bool> PressRecentAppsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return await SendKeyAsync(index, 187, customPath, cancellationToken);
    }

    public async Task<bool> ClearRecentAppsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<string, Task>? progress = null
    )
    {
        if (progress != null)
            await progress("Clearing recent apps and stopping Facebook.");

        var cleared = false;
        var stopped = false;
        try
        {
            await PressHomeAsync(index, customPath, cancellationToken);
            if (await PressRecentAppsAsync(index, customPath, cancellationToken))
            {
                await Task.Delay(650, cancellationToken);
                var button = await PFlowRecentAppsNavigation.FindClearAsync(
                    this,
                    index,
                    customPath,
                    cancellationToken
                );
                var point = RecentAppsClearPoint(button);
                if (
                    point.HasValue
                    && await TapAsync(
                        index,
                        point.Value.X,
                        point.Value.Y,
                        customPath,
                        cancellationToken
                    )
                )
                {
                    await Task.Delay(650, cancellationToken);
                    var after = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
                    cleared =
                        !string.IsNullOrWhiteSpace(after)
                        && FindRecentAppsClearTarget(after) == null;
                }
            }
        }
        finally
        {
            // Always stop Facebook, including when Recents has no clear button.
            // This does not clear app data or the saved account session.
            await PressHomeAsync(index, customPath, cancellationToken);
            stopped = await StopFacebookAppAsync(index, customPath, cancellationToken);
        }
        var message =
            !stopped ? "Cleanup incomplete: Facebook could not be stopped."
            : cleared ? "Recent apps cleared; Facebook stopped."
            : "Facebook stopped; Recents clear-all was unavailable or could not be confirmed.";
        await PFlowFetchTrace.WriteAsync(index, "cleanup.complete", message, cancellationToken);
        if (progress != null)
            await progress(message);
        return cleared;
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
            var activity = await _atxClient.GetCurrentFocusedActivityAsync(
                index,
                cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(activity))
                return activity;
        }

        var output = await ExecuteAdbAsync(
            index,
            "shell dumpsys window windows",
            customPath,
            cancellationToken
        );

        return AtxAgentClient.ParseFocusedActivity(output);
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
        if (string.IsNullOrWhiteSpace(bloksAppId))
            return false;

        var previousScreen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        var uri = BloksConstants.CreateBloksScreenQueryUri(bloksAppId, parameters);
        await PFlowFetchTrace.WriteAsync(
            index,
            "bloks.query.begin",
            $"appId={bloksAppId}; parameterKeys=[{string.Join(",", parameters?.Keys ?? Enumerable.Empty<string>())}]",
            cancellationToken
        );
        if (!await OpenDeepLinkAsync(index, uri, packageName, customPath, cancellationToken))
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "bloks.query.intent-failed",
                $"appId={bloksAppId}",
                cancellationToken
            );
            return false;
        }

        BloksScreenInfo? screen = null;
        var state = BloksQueryScreenState.AwaitingContent;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
            state = BloksQueryScreenValidator.Inspect(screen, previousScreen);
            if (state == BloksQueryScreenState.Ready)
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "bloks.query.opened",
                    PFlowFetchTrace.SummarizeScreen(screen),
                    cancellationToken
                );
                return true;
            }
            if (state is BloksQueryScreenState.DemoShell or BloksQueryScreenState.Error)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        await PFlowFetchTrace.WriteAsync(
            index,
            "bloks.query.rejected",
            $"appId={bloksAppId}; reason={state}; {PFlowFetchTrace.SummarizeScreen(screen)}",
            cancellationToken
        );
        return false;
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

    private async Task<bool> OpenAccountSwitcherFromAuthenticatedMenuAsync(
        int index,
        string profileName,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress = null
    )
    {
        await PFlowFetchTrace.WriteAsync(
            index,
            "switcher.open.begin",
            "Opening Feed then authenticated Menu.",
            cancellationToken
        );
        // Do not deep-link to the personal UID before opening the switcher:
        // when a Page persona is active, that URI opens the personal profile
        // as a visitor and the profile-header chevron is absent. Open the
        // authenticated Feed/Menu first, where Facebook exposes the account
        // switcher for the currently active persona.
        await OpenDeepLinkAsync(
            index,
            "fb://feed",
            BloksConstants.KatanaPackage,
            customPath,
            cancellationToken
        );
        var readiness = await WaitForAuthenticatedFacebookScreenAsync(
            index,
            customPath,
            cancellationToken,
            progress
        );
        if (!readiness.Ready)
            return false;
        var xml = readiness.Xml;
        var activity = readiness.Activity;
        await PFlowFetchTrace.WriteAsync(
            index,
            "switcher.feed.loaded",
            $"activity={activity}; accountSwitcherAlreadyOpen={IsAccountSwitcherOpen(xml)}",
            cancellationToken
        );
        if (!IsAccountSwitcherOpen(xml))
        {
            var initialScreen = AtxAgentClient.ParseBloksHierarchy(xml, activity);
            if (IsFacebookLoginGate(activity, initialScreen))
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "switcher.login-required",
                    PFlowFetchTrace.SummarizeScreen(initialScreen),
                    cancellationToken
                );
                return false;
            }

            var menuOpened = await OpenFacebookMenuFromFeedAsync(
                index,
                customPath,
                cancellationToken
            );
            await Task.Delay(900, cancellationToken);
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            activity = await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken);
            await PFlowFetchTrace.WriteAsync(
                index,
                "switcher.menu.result",
                $"menuActionReported={menuOpened}; activity={activity}; switcherOpen={IsAccountSwitcherOpen(xml)}; {PFlowFetchTrace.SummarizeScreen(AtxAgentClient.ParseBloksHierarchy(xml, activity))}",
                cancellationToken
            );
        }

        for (var attempt = 0; attempt < 3 && !IsAccountSwitcherOpen(xml); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            activity = await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken);
            var screen = AtxAgentClient.ParseBloksHierarchy(xml, activity);
            if (IsFacebookLoginGate(activity, screen))
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "switcher.login-required",
                    PFlowFetchTrace.SummarizeScreen(screen),
                    cancellationToken
                );
                return false;
            }

            var dropdownTapped = await ClickProfileSwitcherDropdownAsync(
                index,
                profileName,
                customPath,
                cancellationToken
            );
            if (!dropdownTapped)
            {
                var menuToolbar = FindAuthenticatedMenuToolbarTarget(screen);
                if (menuToolbar != null && !AtxAgentClient.IsMenuPageInBloks(screen))
                {
                    if (progress != null)
                        await progress(
                            "Opening the current profile's Menu to access its account switcher."
                        );
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "switcher.current-profile.menu.tap",
                        $"x={menuToolbar.CenterX}; y={menuToolbar.CenterY}",
                        cancellationToken
                    );
                    await TapAsync(
                        index,
                        menuToolbar.CenterX,
                        menuToolbar.CenterY,
                        customPath,
                        cancellationToken
                    );
                    await Task.Delay(900, cancellationToken);
                    dropdownTapped = await ClickProfileSwitcherDropdownAsync(
                        index,
                        null,
                        customPath,
                        cancellationToken
                    );
                }
            }
            if (!dropdownTapped)
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "switcher.dropdown.not-actionable",
                    PFlowFetchTrace.SummarizeScreen(screen),
                    cancellationToken
                );
                return false;
            }
            // Facebook first draws a mostly empty bottom sheet. Do not tap
            // the profile dropdown again while the sheet is loading.
            var switcherWait = Stopwatch.StartNew();
            var switcherReady = false;
            var nextProgressSeconds = 0;
            await PFlowFetchTrace.WriteAsync(
                index,
                "switcher.dropdown.wait",
                "Dropdown tapped; waiting up to 45 seconds for the populated account switcher.",
                cancellationToken
            );
            while (switcherWait.Elapsed < TimeSpan.FromSeconds(45))
            {
                await Task.Delay(350, cancellationToken);
                xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
                if (IsProfileSwitcherPopulated(xml, profileName))
                {
                    switcherReady = true;
                    break;
                }
                var seconds = (int)switcherWait.Elapsed.TotalSeconds;
                if (seconds >= nextProgressSeconds)
                {
                    var message =
                        $"Waiting for Facebook account switcher {Math.Min(seconds, 45)}s/45s.";
                    if (progress != null)
                        await progress(message);
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "switcher.dropdown.loading",
                        message,
                        cancellationToken
                    );
                    nextProgressSeconds = seconds + 5;
                }
            }
            await PFlowFetchTrace.WriteAsync(
                index,
                switcherReady ? "switcher.dropdown.ready" : "switcher.dropdown.timeout",
                $"waitMs={switcherWait.ElapsedMilliseconds}; requestedRowVisible={FindPersonaInSwitcher(xml, profileName).HasValue}",
                cancellationToken
            );
            if (!switcherReady)
                return false;
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(350, cancellationToken);
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var persona = FindPersonaInSwitcher(xml, profileName);
            if (IsAccountSwitcherOpen(xml) && !persona.HasValue)
                persona = await FindPersonaInSwitcherWithScrollingAsync(
                    index,
                    profileName,
                    customPath,
                    cancellationToken
                );
            if (IsAccountSwitcherOpen(xml) && persona.HasValue)
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "switcher.open.success",
                    $"requestedPersonaFound=true; selected={persona.Value.IsSelected}",
                    cancellationToken
                );
                return true;
            }
        }

        activity = await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken);
        var failedScreen = AtxAgentClient.ParseBloksHierarchy(xml, activity);
        await PFlowFetchTrace.WriteAsync(
            index,
            "switcher.open.failed",
            $"activity={activity}; accountSwitcherOpen={IsAccountSwitcherOpen(xml)}; nameFoundInHierarchy={FindPersonaInSwitcher(xml, profileName).HasValue}; {PFlowFetchTrace.SummarizeScreen(failedScreen)}",
            cancellationToken
        );
        return false;
    }

    private async Task<(int X, int Y, bool IsSelected)?> FindPersonaInSwitcherWithScrollingAsync(
        int index,
        string personaName,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (!IsAccountSwitcherOpen(xml))
                return null;
            var match = FindPersonaInSwitcher(xml, personaName);
            if (match.HasValue)
                return match;
            var (width, height) = ParseScreenDimensions(xml);
            await PFlowFetchTrace.WriteAsync(
                index,
                "switcher.find.scroll",
                $"requested={personaName}; attempt={attempt + 1}/6",
                cancellationToken
            );
            await SwipeAsync(
                index,
                width / 2,
                (int)(height * 0.80),
                width / 2,
                (int)(height * 0.55),
                durationMs: 350,
                customPath,
                cancellationToken
            );
            await Task.Delay(400, cancellationToken);
            var afterSwipe = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var afterMatch = FindPersonaInSwitcher(afterSwipe, personaName);
            if (afterMatch.HasValue)
                return afterMatch;
            if (string.Equals(xml, afterSwipe, StringComparison.Ordinal))
                return null;
        }
        return null;
    }

    public static bool IsProfileSwitcherPopulated(string? xml, string personaName) =>
        IsAccountSwitcherOpen(xml) && FindPersonaInSwitcher(xml, personaName).HasValue;

    public static BloksElement? FindAuthenticatedMenuToolbarTarget(BloksScreenInfo screen)
    {
        var width = screen.AllElements.Select(e => e.Bounds.Right).DefaultIfEmpty(720).Max();
        var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(1280).Max();
        return screen.AllElements.FirstOrDefault(element =>
            element.Enabled
            && element.Clickable
            && element.Matches("Menu", partialMatch: false)
            && element.CenterX < width * 0.22
            && element.CenterY < height * 0.22
        );
    }

    private async Task<(
        bool Ready,
        string? Xml,
        string? Activity
    )> WaitForAuthenticatedFacebookScreenAsync(
        int index,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress = null
    )
    {
        var elapsed = Stopwatch.StartNew();
        string? xml = null;
        string? activity = null;
        BloksScreenInfo? screen = null;
        await PFlowFetchTrace.WriteAsync(
            index,
            "switcher.feed.wait",
            "Waiting up to 45 seconds for the authenticated Feed/Menu hierarchy after launch.",
            cancellationToken
        );
        var nextUpdate = TimeSpan.Zero;
        var feedRevealAttempts = 0;
        while (elapsed.Elapsed < TimeSpan.FromSeconds(45))
        {
            cancellationToken.ThrowIfCancellationRequested();
            xml = await DumpUiHierarchyAsync(index, customPath, cancellationToken);
            activity = await GetCurrentFocusedActivityAsync(index, customPath, cancellationToken);
            screen = AtxAgentClient.ParseBloksHierarchy(xml, activity);
            if (IsAuthenticatedFacebookSurface(screen))
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "switcher.feed.ready",
                    $"waitMs={elapsed.ElapsedMilliseconds}; activity={activity}; {PFlowFetchTrace.SummarizeScreen(screen)}",
                    cancellationToken
                );
                return (true, xml, activity);
            }
            if (IsFacebookLoginGate(activity, screen))
                break;
            // A restored, scrolled Feed can hide both header and tab bar.
            // Reveal them from observed Feed content before declaring timeout.
            if (
                feedRevealAttempts < 3
                && screen.AllElements.Any(e => e.Matches("Story tray", partialMatch: false))
            )
            {
                var (width, height) = ParseScreenDimensions(xml);
                if (width > 0 && height > 0)
                {
                    feedRevealAttempts++;
                    if (progress != null)
                        await progress("Revealing Facebook's hidden navigation bar.");
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "switcher.feed.reveal-navigation",
                        $"attempt={feedRevealAttempts}/3",
                        cancellationToken
                    );
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
                    await Task.Delay(500, cancellationToken);
                    continue;
                }
            }
            if (elapsed.Elapsed >= nextUpdate)
            {
                if (progress != null)
                    await progress(
                        $"Waiting for Facebook's authenticated screen {(int)elapsed.Elapsed.TotalSeconds}s/45s."
                    );
                nextUpdate = elapsed.Elapsed + TimeSpan.FromSeconds(5);
            }
            await Task.Delay(400, cancellationToken);
        }

        var loginRequired = IsFacebookLoginGate(activity, screen);
        await PFlowFetchTrace.WriteAsync(
            index,
            loginRequired ? "switcher.login-required" : "switcher.feed.timeout",
            $"waitMs={elapsed.ElapsedMilliseconds}; activity={activity}; confirmedLoginForm={loginRequired}; {PFlowFetchTrace.SummarizeScreen(screen)}",
            cancellationToken
        );
        return (false, xml, activity);
    }

    private static bool IsAuthenticatedFacebookSurface(BloksScreenInfo? screen)
    {
        if (screen == null)
            return false;
        var labels = screen.VisibleTexts.Concat(screen.ContentDescriptions).ToList();
        return IsAccountSwitcherOpen(screen.RawXml)
            || labels.Any(label =>
                label.Contains("What's on your mind", StringComparison.OrdinalIgnoreCase)
                || label.Contains("What’s on your mind", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Go to profile", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Your shortcuts", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Edit profile", StringComparison.OrdinalIgnoreCase)
                || label.Equals("Professional dashboard", StringComparison.OrdinalIgnoreCase)
            );
    }

    public static bool IsFacebookLoginGate(string? activity, BloksScreenInfo? screen)
    {
        // LoginActivity is also used while a saved session is being restored.
        // Only visible login controls establish that sign-in is required.
        if (screen == null || IsAuthenticatedFacebookSurface(screen))
            return false;

        var visibleText = string.Join(" ", screen.VisibleTexts.Concat(screen.ContentDescriptions));
        return visibleText.Contains("Log into Facebook", StringComparison.OrdinalIgnoreCase)
            || visibleText.Contains("Log in to Facebook", StringComparison.OrdinalIgnoreCase)
            || (
                Regex.IsMatch(visibleText, @"\blog\s*in\b", RegexOptions.IgnoreCase)
                && (
                    visibleText.Contains("Password", StringComparison.OrdinalIgnoreCase)
                    || visibleText.Contains("Forgot password", StringComparison.OrdinalIgnoreCase)
                    || visibleText.Contains(
                        "Log into another account",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || visibleText.Contains(
                        "Create new Facebook account",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
    }

    public static XElement? FindProfileHeaderSwitcherButton(string? xml, string? profileName = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
        if (screenWidth <= 0)
            screenWidth = 720;

        foreach (var doc in SafeParseXmlDocuments(xml))
        {
            var nodes = doc.Descendants("node").ToList();
            var isMenuScreen = AtxAgentClient.IsMenuPageInBloks(
                AtxAgentClient.ParseBloksHierarchy(xml)
            );
            var nameNodes = nodes
                .Where(node =>
                {
                    var text = node.Attribute("text")?.Value?.Trim();
                    var desc = node.Attribute("content-desc")?.Value?.Trim();
                    var label = !string.IsNullOrWhiteSpace(text) ? text : desc;
                    if (string.IsNullOrWhiteSpace(label))
                        return false;
                    if (profileName == null)
                    {
                        var (_, cy) = ParseNodeBoundsCenter(node);
                        return cy > 80
                            && cy < screenHeight * 0.70
                            && (
                                isMenuScreen
                                || nodes.Any(other =>
                                {
                                    var (_, counterY) = ParseNodeBoundsCenter(other);
                                    var counter =
                                        $"{other.Attribute("text")?.Value} {other.Attribute("content-desc")?.Value}";
                                    return counterY >= cy
                                        && counterY - cy <= 140
                                        && Regex.IsMatch(
                                            counter,
                                            @"\b(?:friends|followers|following|posts)\b",
                                            RegexOptions.IgnoreCase
                                        );
                                })
                            )
                            && label.Length is > 2 and < 80
                            && !IsIgnoredSwitcherHeaderOrAction(label)
                            && !IsIgnoredProfileText(label)
                            && !label.Contains("profile", StringComparison.OrdinalIgnoreCase)
                            && !label.Contains("story", StringComparison.OrdinalIgnoreCase)
                            && !label.Contains("friends", StringComparison.OrdinalIgnoreCase);
                    }
                    return string.Equals(label, profileName, StringComparison.OrdinalIgnoreCase)
                        || label.StartsWith(profileName, StringComparison.OrdinalIgnoreCase)
                        || (
                            label.Length >= 4
                            && profileName.StartsWith(
                                label.TrimEnd('…', '.'),
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                })
                .ToList();

            foreach (var nameNode in nameNodes)
            {
                var (_, nameY) = ParseNodeBoundsCenter(nameNode);
                if (nameY <= 0)
                    continue;

                var explicitlyNamed = nodes.FirstOrDefault(node =>
                {
                    var label =
                        $"{node.Attribute("text")?.Value} {node.Attribute("content-desc")?.Value}";
                    return label.Contains("profile switcher", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("switch accounts", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("switch profile", StringComparison.OrdinalIgnoreCase)
                        || label.Contains(
                            "your profiles and pages",
                            StringComparison.OrdinalIgnoreCase
                        );
                });
                if (explicitlyNamed != null)
                    return explicitlyNamed;

                // The dropdown is the small clickable control to the right of the
                // verified profile name. Bind its vertical position to that title
                // instead of tapping a fixed screen coordinate or the cover photo.
                var candidate = nodes
                    .Where(node =>
                    {
                        if (
                            !string.Equals(
                                node.Attribute("clickable")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            return false;

                        var label =
                            $"{node.Attribute("text")?.Value} {node.Attribute("content-desc")?.Value}".Trim();
                        if (
                            !string.IsNullOrWhiteSpace(label)
                            && !Regex.IsMatch(
                                label,
                                @"\b(?:switch(?:er)?|chevron|dropdown)\b",
                                RegexOptions.IgnoreCase
                            )
                        )
                            return false;

                        var bounds = node.Attribute("bounds")?.Value;
                        if (string.IsNullOrWhiteSpace(bounds))
                            return false;
                        var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                        if (!match.Success)
                            return false;

                        var left = int.Parse(match.Groups[1].Value);
                        var top = int.Parse(match.Groups[2].Value);
                        var right = int.Parse(match.Groups[3].Value);
                        var bottom = int.Parse(match.Groups[4].Value);
                        var centerX = (left + right) / 2;
                        var centerY = (top + bottom) / 2;
                        var width = right - left;
                        var height = bottom - top;
                        return centerX >= screenWidth * 0.82
                            && Math.Abs(centerY - nameY) <= 100
                            && width is > 0 and <= 180
                            && height is > 0 and <= 180;
                    })
                    .OrderBy(node =>
                    {
                        var (x, y) = ParseNodeBoundsCenter(node);
                        var bounds = node.Attribute("bounds")?.Value ?? string.Empty;
                        var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                        var area = match.Success
                            ? (long)(
                                int.Parse(match.Groups[3].Value) - int.Parse(match.Groups[1].Value)
                            )
                                * (
                                    int.Parse(match.Groups[4].Value)
                                    - int.Parse(match.Groups[2].Value)
                                )
                            : long.MaxValue;
                        return Math.Abs(y - nameY) * 100000L + area;
                    })
                    .FirstOrDefault();

                if (candidate != null)
                    return candidate;
            }
        }

        return null;
    }

    private static bool IsAccessibilityNodeSelected(XElement node)
    {
        static bool IsTrue(XElement element, string name) =>
            string.Equals(
                element.Attribute(name)?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase
            );

        if (IsTrue(node, "selected") || IsTrue(node, "checked"))
            return true;

        var label = string.Join(
            " ",
            node.Attribute("text")?.Value,
            node.Attribute("content-desc")?.Value,
            node.Attribute("resource-id")?.Value
        );
        if (
            Regex.IsMatch(
                label,
                @"\b(?:unselected|unchecked|not\s+selected|not\s+checked)\b",
                RegexOptions.IgnoreCase
            )
        )
            return false;
        return Regex.IsMatch(
            label,
            @"\b(?:selected|checked|current\s+profile)\b",
            RegexOptions.IgnoreCase
        );
    }

    private static (int Left, int Top, int Right, int Bottom, int Height) ParseAccessibilityBounds(
        XElement node
    )
    {
        var match = Regex.Match(
            node.Attribute("bounds")?.Value ?? string.Empty,
            @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
        );
        if (!match.Success)
            return (0, 0, 0, 0, 0);
        var left = int.Parse(match.Groups[1].Value);
        var top = int.Parse(match.Groups[2].Value);
        var right = int.Parse(match.Groups[3].Value);
        var bottom = int.Parse(match.Groups[4].Value);
        return (left, top, right, bottom, bottom - top);
    }

    private async Task<bool> OpenAndVerifyPrimaryProfileAsync(
        int index,
        string accountUid,
        string accountName,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress
    )
    {
        if (string.IsNullOrWhiteSpace(accountUid) || string.IsNullOrWhiteSpace(accountName))
            return false;
        if (progress != null)
            await progress($"Selecting and confirming personal profile {accountName}.");
        // The native switcher owns the single navigation recovery attempt.
        if (
            !await EnsureSwitchedToPersonalProfileAsync(
                index,
                accountName,
                customPath,
                cancellationToken
            )
        )
            return false;
        var current = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        var confirmed =
            current != null
            && !PFlowUiPrivacy.ContainsInput(current.RawXml)
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(current.RawXml)
            && IsExpectedOwnProfileVisible(current, accountUid, accountName);
        await PFlowFetchTrace.WriteAsync(
            index,
            confirmed ? "personal.profile.verified" : "personal.profile.not-confirmed",
            "Shared identity selection completed; checking the requested own-profile controls.",
            cancellationToken
        );
        return confirmed;
    }

    private async Task<bool> SwitchToPageFromProfileSwitcherAsync(
        int index,
        string accountUid,
        string accountName,
        string pageName,
        string pageId,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress,
        bool ownerAlreadyVerified,
        FacebookPageFetchStateMachine flow
    )
    {
        if (
            !ownerAlreadyVerified
            && !await OpenAndVerifyPrimaryProfileAsync(
                index,
                accountUid,
                accountName,
                customPath,
                cancellationToken,
                progress
            )
        )
            return false;
        // OpenAndVerifyPrimaryProfileAsync or the caller's current-session verifier
        // has already compared the live owner UID. Layout recognition supplies no UID.
        if (!flow.VerifyOwnerUid(accountUid).Allowed)
            return false;
        if (progress != null)
            await progress($"Selecting and confirming managed Page {pageName}.");
        var switched = await SwitchToPersonaAsync(
            index,
            new FacebookPageTarget
            {
                Type = "Page",
                PageName = pageName,
                PageId = pageId,
                UserUid = accountUid,
                ManagedBy = accountName,
            },
            customPath,
            cancellationToken
        );
        return flow.ConfirmPageSelection(switched).Allowed;
    }

    public static BloksElement? FindAuthenticatedProfileNavigationTarget(BloksScreenInfo screen) =>
        screen.AllElements.FirstOrDefault(element =>
            element.Enabled
            && element.Clickable
            && element.Width > 0
            && element.Height > 0
            && (
                element.ContentDesc.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                || element.Text.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
            )
        )
        ?? screen.AllElements.FirstOrDefault(element =>
            element.Enabled
            && element.Clickable
            && element.Width > 0
            && element.Height > 0
            && element.Matches("Go to profile", partialMatch: false)
        );

    private async Task<bool> OpenAuthenticatedProfileFromNavigationAsync(
        int index,
        BloksScreenInfo currentScreen,
        string expectedName,
        string? customPath,
        CancellationToken cancellationToken,
        Func<string, Task>? progress
    )
    {
        var verified = await PFlowOwnProfileNavigation.OpenAsync(
            this,
            index,
            expectedName,
            customPath,
            screen => IsExpectedOwnProfileVisible(screen, string.Empty, expectedName),
            cancellationToken,
            progress,
            delay: PFlowFetchPacing.DelayAsync
        );
        await PFlowFetchTrace.WriteAsync(
            index,
            verified ? "page.switch.profile-tab.verified" : "page.switch.profile-tab.timeout",
            verified
                ? "Authenticated own-profile navigation confirmed."
                : "Profile navigation did not expose the expected owner controls.",
            cancellationToken
        );
        return verified;
    }

    internal static FacebookLayoutControl? FindPageDashboardControl(
        BloksScreenInfo screen,
        string? targetName,
        string? targetUid
    ) =>
        FacebookLayoutRegistry
            .MatchScreen(screen, FacebookScreenKind.OwnProfile, new(targetName, targetUid))
            .Control(FacebookControlRole.Dashboard);

    private async Task<FacebookPageInfo?> ExtractPageInfoCoreAsync(
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
        await PFlowFetchTrace.WriteAsync(
            index,
            "page.lookup.begin",
            "Starting Page persona, join-date, and analytics lookup.",
            cancellationToken
        );
        if (
            (string.IsNullOrWhiteSpace(pageId) && string.IsNullOrWhiteSpace(pageUid))
            || (string.IsNullOrWhiteSpace(pageId) && string.IsNullOrWhiteSpace(expectedPageName))
            || string.IsNullOrWhiteSpace(expectedProfileUid)
            || string.IsNullOrWhiteSpace(expectedProfileName)
        )
            return null;
        pageId ??= string.Empty;

        var flow = new FacebookPageFetchStateMachine(
            expectedProfileUid,
            expectedPageName ?? pageId
        );
        string? lastLayoutDiagnostic = null;
        FacebookLayoutMatch? guardedLayout = null;
        var observations = new System.Runtime.CompilerServices.ConditionalWeakTable<
            BloksScreenInfo,
            FacebookLayoutObservation
        >();
        async Task<bool> Guard(
            FacebookPageFetchAction action,
            BloksScreenInfo captured,
            FacebookScreenKind kind
        )
        {
            var observation = observations.GetValue(
                captured,
                s =>
                    FacebookLayoutObservation.FromScreen(
                        s,
                        new(expectedPageName ?? pageId, pageUid)
                    )
            );
            var match = FacebookLayoutRegistry.Match(observation, kind);
            guardedLayout = match;
            var decision = flow.Advance(action, match);
            var diagnostic = $"action={action}; {decision.Diagnostic}; {match.Diagnostic}";
            if (diagnostic != lastLayoutDiagnostic)
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "page.layout",
                    diagnostic,
                    cancellationToken
                );
                lastLayoutDiagnostic = diagnostic;
            }
            return decision.Allowed;
        }

        if (progress != null)
            await progress($"Page UI: switching to {expectedPageName ?? pageId}.");

        if (
            !await SwitchToPageFromProfileSwitcherAsync(
                index,
                expectedProfileUid,
                expectedProfileName,
                expectedPageName ?? pageId,
                pageId,
                customPath,
                cancellationToken,
                progress,
                ownerAlreadyVerified,
                flow
            )
        )
        {
            var activity = await GetCurrentFocusedActivityAsync(
                index,
                customPath,
                cancellationToken
            );
            await PFlowFetchTrace.WriteAsync(
                index,
                "page.switch.failed",
                $"activity={activity}",
                cancellationToken
            );
            if (progress != null)
                await progress(
                    $"Page UI: skipped {expectedPageName ?? pageId}; Facebook did not confirm the Page switch."
                );
            return null;
        }

        if (progress != null)
            await progress(
                $"Page UI: opening {expectedPageName ?? pageId} after confirming the Page switch."
            );

        // Keep the selected persona running and navigate through its Profile tab.
        if (
            !await OpenCurrentOwnProfileAsync(
                index,
                expectedPageName ?? pageId,
                customPath,
                cancellationToken,
                progress
            )
        )
            return null;

        BloksScreenInfo? screen = null;
        var pageContextConfirmed = false;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
            if (screen != null && !string.IsNullOrWhiteSpace(screen.RawXml))
            {
                pageContextConfirmed = IsExpectedOwnProfileVisible(
                    screen,
                    pageUid,
                    expectedPageName
                );
                if (pageContextConfirmed)
                    break;
            }
            await PFlowFetchPacing.DelayAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        if (!pageContextConfirmed || screen == null)
        {
            var activity = await GetCurrentFocusedActivityAsync(
                index,
                customPath,
                cancellationToken
            );
            await PFlowFetchTrace.WriteAsync(
                index,
                "page.home.not-confirmed",
                $"activity={activity}; {PFlowFetchTrace.SummarizeScreen(screen)}",
                cancellationToken
            );
            Debug.WriteLine(
                $"[LD-{index}] Page hierarchy did not identify the requested page after opening its Page URI."
            );
            return null;
        }

        screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (
            screen == null
            || string.IsNullOrWhiteSpace(screen.RawXml)
            || !IsExpectedOwnProfileVisible(screen, pageUid, expectedPageName)
        )
        {
            Debug.WriteLine(
                $"[LD-{index}] Page {pageId} was not visible after the account switcher confirmed it as active."
            );
            return null;
        }

        if (
            !await Guard(
                FacebookPageFetchAction.ConfirmOwnProfile,
                screen,
                FacebookScreenKind.OwnProfile
            )
        )
            return null;
        if (progress != null)
            await progress(
                $"Page UI: {expectedPageName ?? pageId} is active; reading its details."
            );
        await PFlowFetchTrace.WriteAsync(
            index,
            "page.home.confirmed",
            PFlowFetchTrace.SummarizeScreen(screen),
            cancellationToken
        );

        var currentScreen = screen;
        var info = ReadPageInfoFromHierarchy(currentScreen, pageId, pageUid, expectedPageName);
        var graphPage = await new PFlowGraphIdentityReader().TryReadAsync(
            this,
            index,
            customPath,
            cancellationToken
        );
        if (graphPage != null && (graphPage.Id == pageId || graphPage.Id == pageUid))
        {
            info.PageName =
                PFlowPageDetailsNavigation.FindFullHeaderName(
                    screen,
                    expectedPageName ?? graphPage.Name
                ) ?? graphPage.Name;
            info.ProfilePictureUrl = graphPage.PictureUrl ?? info.ProfilePictureUrl;
        }

        // Graph has no verified Page creation-date equivalent; fall back to Transparency.
        // Creation dates use only the verified header-name -> transparency route.
        // Do not search posts for an About link when the name sheet is unavailable.
        if (!string.IsNullOrWhiteSpace(expectedPageName))
        {
            if (progress != null)
                await progress(
                    $"Page UI: reading {expectedPageName} creation date through its header and transparency."
                );
            var dates = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
                this,
                index,
                expectedPageName,
                false,
                customPath,
                cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(dates.CreationDate))
                info.CreationDate = dates.CreationDate;
            await PFlowFetchTrace.WriteAsync(
                index,
                "page.join.result",
                $"creationDateFound={!string.IsNullOrWhiteSpace(dates.CreationDate)}; route=header-transparency",
                cancellationToken
            );
            if (string.IsNullOrWhiteSpace(dates.CreationDate) && progress != null)
                await progress(
                    "Page UI: creation date unavailable through the confirmed header/transparency route."
                );
        }
        // Professional dashboard analytics is where Facebook exposes Page
        // audience and performance counts. Re-open the Page home because the
        // About/Transparency routes above may have changed the current screen.
        await OpenCurrentOwnProfileAsync(
            index,
            expectedPageName ?? pageId,
            customPath,
            cancellationToken,
            progress
        );
        await PFlowFetchPacing.DelayAsync(900, cancellationToken);
        var pageHome = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (
            pageHome != null
            && !string.IsNullOrWhiteSpace(pageHome.RawXml)
            && IsExpectedOwnProfileVisible(pageHome, pageUid, expectedPageName)
        )
        {
            currentScreen = pageHome;
            var pageHomeInfo = ReadPageInfoFromHierarchy(
                currentScreen,
                pageId,
                pageUid,
                expectedPageName
            );
            MergePageUiInfo(info, pageHomeInfo);

            var dashboard = FindPageDashboardControl(
                currentScreen,
                expectedPageName ?? pageId,
                pageUid
            );
            if (dashboard == null)
            {
                var refreshed = await PFlowFetchRefresh.TryAsync(
                    this,
                    index,
                    "Page home",
                    s => FindPageDashboardControl(s, expectedPageName ?? pageId, pageUid) != null,
                    s => IsExpectedOwnProfileVisible(s, pageUid, expectedPageName),
                    customPath,
                    cancellationToken,
                    progress,
                    trace: message =>
                        PFlowFetchTrace.WriteAsync(
                            index,
                            "page.home.refresh",
                            message,
                            cancellationToken
                        )
                );
                if (
                    refreshed.Ready
                    && refreshed.Screen != null
                    && await Guard(
                        FacebookPageFetchAction.ConfirmOwnProfile,
                        refreshed.Screen,
                        FacebookScreenKind.OwnProfile
                    )
                )
                {
                    currentScreen = refreshed.Screen;
                    MergePageUiInfo(
                        info,
                        ReadPageInfoFromHierarchy(currentScreen, pageId, pageUid, expectedPageName)
                    );
                    dashboard = FindPageDashboardControl(
                        currentScreen,
                        expectedPageName ?? pageId,
                        pageUid
                    );
                }
            }
            if (
                dashboard != null
                && await Guard(
                    FacebookPageFetchAction.OpenDashboard,
                    currentScreen,
                    FacebookScreenKind.OwnProfile
                )
            )
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "page.dashboard.tap",
                    "Professional dashboard control found; tapping.",
                    cancellationToken
                );
                if (progress != null)
                    await progress(
                        $"Page UI: opening Professional dashboard for {expectedPageName ?? pageId}."
                    );
                await TapAsync(
                    index,
                    guardedLayout!.Control(FacebookControlRole.Dashboard)!.X,
                    guardedLayout.Control(FacebookControlRole.Dashboard)!.Y,
                    customPath,
                    cancellationToken
                );
                BloksScreenInfo? dashboardScreen = null;
                var dashboardWait = Stopwatch.StartNew();
                var nextDashboardProgress = 5;
                while (dashboardWait.Elapsed < TimeSpan.FromSeconds(30))
                {
                    await PFlowFetchPacing.DelayAsync(400, cancellationToken);
                    dashboardScreen = await DumpBloksScreenAsync(
                        index,
                        customPath,
                        cancellationToken
                    );
                    if (
                        dashboardScreen != null
                        && !string.IsNullOrWhiteSpace(dashboardScreen.RawXml)
                        && await Guard(
                            FacebookPageFetchAction.ReadDashboard,
                            dashboardScreen,
                            FacebookScreenKind.Dashboard
                        )
                        && guardedLayout!.Control(FacebookControlRole.Analytics) != null
                    )
                        break;
                    if (flow.State == FacebookPageFetchState.Stopped)
                        return null;
                    var seconds = (int)dashboardWait.Elapsed.TotalSeconds;
                    if (seconds >= nextDashboardProgress)
                    {
                        var message =
                            $"Waiting for Facebook Professional dashboard {seconds}s/30s.";
                        if (progress != null)
                            await progress(message);
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "page.dashboard.loading",
                            message,
                            cancellationToken
                        );
                        nextDashboardProgress = seconds + 5;
                    }
                }

                if (
                    dashboardScreen == null
                    || FacebookLayoutRegistry
                        .MatchScreen(dashboardScreen, FacebookScreenKind.Dashboard)
                        .Control(FacebookControlRole.Analytics) == null
                )
                {
                    var refreshed = await PFlowFetchRefresh.TryAsync(
                        this,
                        index,
                        "Professional dashboard",
                        s =>
                            FacebookLayoutRegistry
                                .MatchScreen(s, FacebookScreenKind.Dashboard)
                                .Control(FacebookControlRole.Analytics) != null,
                        FacebookLayoutRegistry.HasDashboardHeader,
                        customPath,
                        cancellationToken,
                        progress,
                        trace: message =>
                            PFlowFetchTrace.WriteAsync(
                                index,
                                "page.dashboard.refresh",
                                message,
                                cancellationToken
                            )
                    );
                    if (refreshed.Ready)
                        dashboardScreen = refreshed.Screen;
                }
                if (
                    dashboardScreen != null
                    && !string.IsNullOrWhiteSpace(dashboardScreen.RawXml)
                    && await Guard(
                        FacebookPageFetchAction.ReadDashboard,
                        dashboardScreen,
                        FacebookScreenKind.Dashboard
                    )
                )
                {
                    currentScreen = dashboardScreen;
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "page.dashboard.screen",
                        PFlowFetchTrace.SummarizeScreen(currentScreen),
                        cancellationToken
                    );
                    MergePageUiInfo(
                        info,
                        ReadPageInfoFromHierarchy(currentScreen, pageId, pageUid, expectedPageName)
                    );
                    if (
                        await Guard(
                            FacebookPageFetchAction.OpenAnalytics,
                            currentScreen,
                            FacebookScreenKind.Dashboard
                        )
                    )
                    {
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "page.analytics.tap",
                            "Registry Analytics entry found (tab or section heading); tapping.",
                            cancellationToken
                        );
                        if (progress != null)
                            await progress(
                                "Page UI: opening Analytics and collecting visible Page metrics."
                            );
                        await TapAsync(
                            index,
                            guardedLayout!.Control(FacebookControlRole.Analytics)!.X,
                            guardedLayout.Control(FacebookControlRole.Analytics)!.Y,
                            customPath,
                            cancellationToken
                        );

                        // The selected tab can remain unchanged while its
                        // network content loads. Wait for analytics content
                        // before comparing snapshots or scrolling the screen.
                        var analyticsWait = Stopwatch.StartNew();
                        var nextAnalyticsProgress = 5;
                        var analyticsReady = false;
                        while (analyticsWait.Elapsed < TimeSpan.FromSeconds(30))
                        {
                            await PFlowFetchPacing.DelayAsync(400, cancellationToken);
                            var loadedAnalytics = await DumpBloksScreenAsync(
                                index,
                                customPath,
                                cancellationToken
                            );
                            if (
                                loadedAnalytics != null
                                && await Guard(
                                    FacebookPageFetchAction.ReadAnalytics,
                                    loadedAnalytics,
                                    FacebookScreenKind.Analytics
                                )
                            )
                            {
                                currentScreen = loadedAnalytics;
                                info.Views = ReadDashboardViews(loadedAnalytics) ?? info.Views;
                                MergePageUiInfo(
                                    info,
                                    ReadPageInfoFromHierarchy(
                                        currentScreen,
                                        pageId,
                                        pageUid,
                                        expectedPageName
                                    )
                                );
                                analyticsReady = true;
                                break;
                            }
                            if (flow.State == FacebookPageFetchState.Stopped)
                                return null;
                            var seconds = (int)analyticsWait.Elapsed.TotalSeconds;
                            if (seconds >= nextAnalyticsProgress)
                            {
                                var message = $"Waiting for Facebook Analytics {seconds}s/30s.";
                                if (progress != null)
                                    await progress(message);
                                await PFlowFetchTrace.WriteAsync(
                                    index,
                                    "page.analytics.loading",
                                    message,
                                    cancellationToken
                                );
                                nextAnalyticsProgress = seconds + 5;
                            }
                        }
                        if (!analyticsReady)
                        {
                            var refreshed = await PFlowFetchRefresh.TryAsync(
                                this,
                                index,
                                "Analytics",
                                IsAnalyticsContentReady,
                                s =>
                                    PFlowFetchRefresh.HasHeader(
                                        s,
                                        "Analytics",
                                        "Professional dashboard"
                                    ),
                                customPath,
                                cancellationToken,
                                progress,
                                trace: message =>
                                    PFlowFetchTrace.WriteAsync(
                                        index,
                                        "page.analytics.refresh",
                                        message,
                                        cancellationToken
                                    )
                            );
                            if (
                                refreshed.Ready
                                && refreshed.Screen != null
                                && await Guard(
                                    FacebookPageFetchAction.ReadAnalytics,
                                    refreshed.Screen,
                                    FacebookScreenKind.Analytics
                                )
                            )
                            {
                                currentScreen = refreshed.Screen;
                                info.Views = ReadDashboardViews(currentScreen) ?? info.Views;
                                MergePageUiInfo(
                                    info,
                                    ReadPageInfoFromHierarchy(
                                        currentScreen,
                                        pageId,
                                        pageUid,
                                        expectedPageName
                                    )
                                );
                                analyticsReady = true;
                            }
                        }
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            analyticsReady ? "page.analytics.ready" : "page.analytics.timeout",
                            $"waitMs={analyticsWait.ElapsedMilliseconds}; contentReady={analyticsReady}",
                            cancellationToken
                        );
                        for (
                            var scrollAttempt = 0;
                            analyticsReady && scrollAttempt < 5;
                            scrollAttempt++
                        )
                        {
                            await PFlowFetchPacing.DelayAsync(650, cancellationToken);
                            var analyticsScreen = await DumpBloksScreenAsync(
                                index,
                                customPath,
                                cancellationToken
                            );
                            if (
                                analyticsScreen == null
                                || string.IsNullOrWhiteSpace(analyticsScreen.RawXml)
                            )
                                break;
                            if (
                                scrollAttempt > 0
                                && string.Equals(
                                    currentScreen.RawXml,
                                    analyticsScreen.RawXml,
                                    StringComparison.Ordinal
                                )
                            )
                                break;

                            currentScreen = analyticsScreen;
                            if (
                                !await Guard(
                                    FacebookPageFetchAction.ReadAnalytics,
                                    currentScreen,
                                    FacebookScreenKind.Analytics
                                )
                            )
                                break;
                            info.Views = ReadDashboardViews(analyticsScreen) ?? info.Views;
                            await PFlowFetchTrace.WriteAsync(
                                index,
                                $"page.analytics.screen.{scrollAttempt + 1}",
                                PFlowFetchTrace.SummarizeScreen(currentScreen),
                                cancellationToken
                            );
                            MergePageUiInfo(
                                info,
                                ReadPageInfoFromHierarchy(
                                    currentScreen,
                                    pageId,
                                    pageUid,
                                    expectedPageName
                                )
                            );

                            var (width, height) = ParseScreenDimensions(currentScreen.RawXml);
                            if (width <= 0)
                                width = 720;
                            if (height <= 0)
                                height = 1280;
                            await SwipeAsync(
                                index,
                                width / 2,
                                (int)(height * 0.80),
                                width / 2,
                                (int)(height * 0.35),
                                durationMs: 400,
                                customPath,
                                cancellationToken
                            );
                        }
                        await PFlowFetchTrace.WriteAsync(
                            index,
                            "page.analytics.metrics",
                            $"followersKnown={info.FollowersKnown}; followers={info.Followers}; views={info.Views}; reach={!string.IsNullOrWhiteSpace(info.Reach)}; reelsKnown={info.ReelsCountKnown}; reels={info.ReelsCount}; videosKnown={info.VideosCountKnown}; videos={info.VideosCount}",
                            cancellationToken
                        );
                    }
                    else if (progress != null)
                    {
                        await progress(
                            "Page UI: Professional dashboard opened, but Analytics was not found in its hierarchy."
                        );
                    }
                }
            }
            else if (progress != null)
            {
                await progress(
                    "Page UI: Professional dashboard control was not visible on the Page home."
                );
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "page.dashboard.not-found",
                    PFlowFetchTrace.SummarizeScreen(currentScreen),
                    cancellationToken
                );
            }
        }
        else
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "page.home.dump-failed",
                "Page URI returned no readable UI hierarchy.",
                cancellationToken
            );
        }

        // Only merge screens at the guarded read sites above. The last observation
        // may be a loading/unknown overlay or a blocked authentication screen.
        Debug.WriteLine(
            string.IsNullOrWhiteSpace(info.CreationDate)
                ? $"[LD-{index}] Page UI hierarchy was read, but no creation date was exposed for Page {pageId}."
                : $"[LD-{index}] Read Page creation date from the UI hierarchy for Page {pageId}."
        );
        if (progress != null && string.IsNullOrWhiteSpace(info.CreationDate))
        {
            var visible = string.Join(
                " | ",
                currentScreen
                    .VisibleTexts.Concat(currentScreen.ContentDescriptions)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(24)
            );
            await progress(
                $"Page UI: finished reading {expectedPageName ?? pageId}; Created date was not exposed. Last screen labels: {visible}"
            );
        }
        if (flow.State == FacebookPageFetchState.Stopped)
            return null;
        var hasEnrichment = HasPageEnrichment(info);
        if (flow.State != FacebookPageFetchState.Stopped)
            flow.Complete();
        await PFlowFetchTrace.WriteAsync(
            index,
            "page.flow.complete",
            $"state={flow.State}; hasData={hasEnrichment}",
            cancellationToken
        );
        await PFlowFetchTrace.WriteAsync(
            index,
            "page.lookup.complete",
            $"hasData={hasEnrichment}; creationDate={(!string.IsNullOrWhiteSpace(info.CreationDate))}; followersKnown={info.FollowersKnown}; followers={info.Followers}; views={info.Views}; reach={(!string.IsNullOrWhiteSpace(info.Reach))}; reelsKnown={info.ReelsCountKnown}; reels={info.ReelsCount}; videosKnown={info.VideosCountKnown}; videos={info.VideosCount}",
            cancellationToken
        );
        return info;
    }

    public async Task<FacebookAccountDetails?> ExtractPersonalAccountDetailsFromUiAsync(
        int index,
        string accountUid,
        string accountName,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<string, Task>? progress = null
    )
    {
        await PFlowFetchTrace.WriteAsync(
            index,
            "personal.lookup.begin",
            "Starting profile Birthday and Joined-date lookup.",
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(accountName))
            accountName =
                await ResolvePersonalProfileNameAsync(
                    index,
                    accountUid,
                    customPath,
                    cancellationToken,
                    progress
                ) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountName))
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "personal.lookup.invalid-root-data",
                "Profile name is empty.",
                cancellationToken
            );
            return null;
        }

        if (progress != null)
            await progress($"Personal account: switching to {accountName}.");

        if (
            !await OpenAndVerifyPrimaryProfileAsync(
                index,
                accountUid,
                accountName,
                customPath,
                cancellationToken,
                progress
            )
        )
        {
            var activity = await GetCurrentFocusedActivityAsync(
                index,
                customPath,
                cancellationToken
            );
            await PFlowFetchTrace.WriteAsync(
                index,
                "personal.switch.failed",
                $"activity={activity}",
                cancellationToken
            );
            Debug.WriteLine(
                $"[LD-{index}] Could not confirm the personal profile before its UI lookup."
            );
            return null;
        }

        if (progress != null)
            await progress($"Personal account: opening {accountName}'s profile.");

        BloksScreenInfo? screen = null;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
            if (
                screen != null
                && !string.IsNullOrWhiteSpace(screen.RawXml)
                && IsExpectedOwnProfileVisible(screen, accountUid, accountName)
            )
                break;
            await PFlowFetchPacing.DelayAsync(500, cancellationToken);
        }

        if (
            screen == null
            || string.IsNullOrWhiteSpace(screen.RawXml)
            || !IsExpectedOwnProfileVisible(screen, accountUid, accountName)
        )
        {
            var activity = await GetCurrentFocusedActivityAsync(
                index,
                customPath,
                cancellationToken
            );
            await PFlowFetchTrace.WriteAsync(
                index,
                "personal.profile.not-confirmed",
                $"activity={activity}; {PFlowFetchTrace.SummarizeScreen(screen)}",
                cancellationToken
            );
            Debug.WriteLine($"[LD-{index}] Personal profile UI did not match '{accountName}'.");
            return null;
        }

        var result = new FacebookAccountDetails
        {
            DeviceName = $"LD-{index}",
            Name = accountName,
            Uid = accountUid,
            SwitchedToProfile = true,
        };
        if (progress != null)
            await progress("Personal account: reading profile details and birthday.");
        await PFlowFetchTrace.WriteAsync(
            index,
            "personal.profile.screen",
            PFlowFetchTrace.SummarizeScreen(screen),
            cancellationToken
        );
        ReadPersonalDatesFromHierarchy(screen, result);
        var graphPersonal = await new PFlowGraphIdentityReader().TryReadAsync(
            this,
            index,
            customPath,
            cancellationToken
        );
        if (
            graphPersonal != null
            && graphPersonal.Id == accountUid
            && graphPersonal.UnderlyingUid == accountUid
        )
        {
            result.Name = graphPersonal.Name;
            result.ProfilePictureUrl = graphPersonal.PictureUrl ?? result.ProfilePictureUrl;
        }
        await PFlowFetchTrace.WriteAsync(
            index,
            "personal.profile.fields",
            $"birthdayFound={!string.IsNullOrWhiteSpace(result.Birthdate)}; creationDateFound={!string.IsNullOrWhiteSpace(result.CreationDate)}",
            cancellationToken
        );

        // Tapping the signed-in user's name opens Facebook's profile details
        // sheet, which contains the "Joined Facebook ..." line. This must be
        // done from the authenticated profile screen, not from a UID deep link
        // while a Page persona is active.
        if (!PFlowProfileDates.HasFullDate(result.CreationDate))
        {
            var nameTarget = FindHierarchyTapTarget(screen, [accountName]);
            if (nameTarget != null)
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "personal.join.tap-name",
                    "Profile name target found; tapping.",
                    cancellationToken
                );
                if (progress != null)
                    await progress(
                        "Personal account: tapping the profile name to read the Facebook join date."
                    );
                await TapAsync(
                    index,
                    nameTarget.CenterX,
                    nameTarget.CenterY,
                    customPath,
                    cancellationToken
                );
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    await PFlowFetchPacing.DelayAsync(350, cancellationToken);
                    var detailsSheet = await DumpBloksScreenAsync(
                        index,
                        customPath,
                        cancellationToken
                    );
                    if (detailsSheet == null || string.IsNullOrWhiteSpace(detailsSheet.RawXml))
                        continue;
                    screen = detailsSheet;
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.join.details-screen",
                        PFlowFetchTrace.SummarizeScreen(screen),
                        cancellationToken
                    );
                    ReadPersonalDatesFromHierarchy(screen, result);
                    if (!string.IsNullOrWhiteSpace(result.CreationDate))
                        break;
                }

                if (!PFlowProfileDates.HasFullDate(result.CreationDate))
                {
                    if (progress != null)
                        await progress(
                            "Personal account: checking Profile transparency for the full creation date."
                        );
                    var exactCreated = await PFlowProfileDetailsReader.ReadTransparencyAsync(
                        this,
                        index,
                        screen,
                        accountName,
                        true,
                        customPath,
                        cancellationToken
                    );
                    if (!string.IsNullOrWhiteSpace(exactCreated))
                        result.CreationDate = exactCreated;
                }
                if (string.IsNullOrWhiteSpace(result.CreationDate) && progress != null)
                    await progress(
                        "Personal account: the name details sheet opened, but no Joined Facebook date was exposed."
                    );
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "personal.join.result",
                    $"creationDateFound={!string.IsNullOrWhiteSpace(result.CreationDate)}",
                    cancellationToken
                );

                await PressBackAsync(index, customPath, cancellationToken);
                await PFlowFetchPacing.DelayAsync(400, cancellationToken);
                var profileScreen = await DumpBloksScreenAsync(
                    index,
                    customPath,
                    cancellationToken
                );
                if (profileScreen != null && !string.IsNullOrWhiteSpace(profileScreen.RawXml))
                {
                    screen = profileScreen;
                    await PFlowFetchTrace.WriteAsync(
                        index,
                        "personal.profile.returned",
                        PFlowFetchTrace.SummarizeScreen(screen),
                        cancellationToken
                    );
                    ReadPersonalDatesFromHierarchy(screen, result);
                }
            }
            else
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "personal.join.name-not-found",
                    PFlowFetchTrace.SummarizeScreen(screen),
                    cancellationToken
                );
            }
        }

        // On the profile home, Birthday can be rendered as a date-only row
        // under Personal details (with no accessible "Birthday" label).
        if (string.IsNullOrWhiteSpace(result.Birthdate))
            result.Birthdate = ExtractBirthdayFromPersonalDetails(screen);
        for (
            var scrollAttempt = 0;
            scrollAttempt < 16 && string.IsNullOrWhiteSpace(result.Birthdate);
            scrollAttempt++
        )
        {
            if (PFlowProfileDates.ReachedProfileFeed(screen))
                break;
            if (progress != null)
                await progress(
                    $"Personal account: reading profile details, screen {scrollAttempt + 1}/16."
                );
            var (width, height) = ParseScreenDimensions(screen.RawXml);
            await SwipeAsync(
                index,
                width / 2,
                (int)(height * 0.78),
                width / 2,
                (int)(height * 0.37),
                durationMs: 400,
                customPath,
                cancellationToken
            );
            await PFlowFetchPacing.DelayAsync(450, cancellationToken);
            var scrolledProfile = await DumpBloksScreenAsync(index, customPath, cancellationToken);
            if (scrolledProfile == null || string.IsNullOrWhiteSpace(scrolledProfile.RawXml))
                break;
            var unchanged = string.Equals(
                screen.RawXml,
                scrolledProfile.RawXml,
                StringComparison.Ordinal
            );
            screen = scrolledProfile;
            await PFlowFetchTrace.WriteAsync(
                index,
                $"personal.details.screen.{scrollAttempt + 1}",
                PFlowFetchTrace.SummarizeScreen(screen),
                cancellationToken
            );
            ReadPersonalDatesFromHierarchy(screen, result);
            if (unchanged)
                break;
            // Stop once the details section has passed, to avoid reading dates
            // from feed posts as a birthday.
            if (
                FindHierarchyTapTarget(screen, ["Posts", "All posts"]) != null
                && !screen.AllElements.Any(e =>
                    e.Matches("Personal details", partialMatch: false)
                    || e.Matches("At a glance", partialMatch: false)
                )
            )
                break;
        }
        await PFlowFetchTrace.WriteAsync(
            index,
            "personal.birthday.result",
            $"birthdayFound={!string.IsNullOrWhiteSpace(result.Birthdate)}; creationDateFound={!string.IsNullOrWhiteSpace(result.CreationDate)}; {PFlowFetchTrace.SummarizeScreen(screen)}",
            cancellationToken
        );

        // Birthday is usually private profile data. Try the signed-in user's
        // Accounts Center through Facebook's public route and hierarchy labels.
        if (string.IsNullOrWhiteSpace(result.Birthdate))
        {
            if (progress != null)
                await progress("Personal account: opening Accounts Center to look for birthday.");
            result.Birthdate = await PFlowPersonalBirthdayReader.ReadAsync(
                this,
                index,
                accountName,
                customPath,
                cancellationToken
            );
        }

        if (progress != null)
        {
            var found = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Birthdate))
                found.Add("birthday");
            if (!string.IsNullOrWhiteSpace(result.CreationDate))
                found.Add("join date");
            await progress(
                found.Count > 0
                    ? $"Personal account: finished; found {string.Join(" and ", found)}."
                    : "Personal account: finished; birthday and join date were not exposed by Facebook UI."
            );
        }

        await PFlowFetchTrace.WriteAsync(
            index,
            "personal.lookup.complete",
            $"birthdayFound={!string.IsNullOrWhiteSpace(result.Birthdate)}; creationDateFound={!string.IsNullOrWhiteSpace(result.CreationDate)}",
            cancellationToken
        );

        // A verified owner name is needed by every subsequent Page lookup,
        // even when Facebook hides the birthday or join date.
        return result;
    }

    private static void ReadPersonalDatesFromHierarchy(
        BloksScreenInfo screen,
        FacebookAccountDetails result
    )
    {
        if (string.IsNullOrWhiteSpace(ExtractValidBirthdate(result.Birthdate)))
        {
            result.Birthdate = ExtractHierarchyFieldValue(
                screen,
                ["Birthday", "Date of birth", "Birth date"]
            );
            if (string.IsNullOrWhiteSpace(result.Birthdate))
                result.Birthdate = ExtractBirthdayFromPersonalDetails(screen);
        }

        if (!string.IsNullOrWhiteSpace(result.CreationDate))
            return;

        result.CreationDate = ExtractJoinedDate(screen);
    }

    /// <summary>Reads the dates actually exposed by a profile hierarchy.</summary>
    public static FacebookAccountDetails ParsePersonalDatesFromHierarchy(BloksScreenInfo screen)
    {
        var result = new FacebookAccountDetails();
        ReadPersonalDatesFromHierarchy(screen, result);
        return result;
    }

    private const string FacebookMonthPattern =
        @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";

    private static string ExtractJoinedDate(BloksScreenInfo screen)
    {
        var pattern =
            $@"\b(?:Joined\s+(?:Facebook\s+)?(?:in\s+|on\s+)?|Member\s+since\s+|(?:Page\s+)?(?:Date\s+created|Creation\s+date|Created(?:\s+on|\s+in)?)\s*[:·,]?\s*)({FacebookMonthPattern}\s+\d{{1,2}},?\s+\d{{4}}|\d{{1,2}}\s+{FacebookMonthPattern}\s+\d{{4}}|{FacebookMonthPattern}\s+\d{{4}}|(?:19|20)\d{{2}})\b";
        var texts = screen
            .VisibleTexts.Concat(screen.ContentDescriptions)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();
        foreach (var text in texts.Append(string.Join(" ", texts)))
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (
                match.Success
                && FacebookPageTarget.TryParseCreationDate(match.Groups[1].Value) != null
            )
                return match.Value.Trim();
        }
        return string.Empty;
    }

    private static string ExtractValidBirthdate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var pattern =
            $@"(?<!\w)({FacebookMonthPattern}\s+\d{{1,2}},?\s+\d{{4}}|\d{{1,2}}\s+{FacebookMonthPattern}\s+\d{{4}}|\d{{4}}-\d{{1,2}}-\d{{1,2}}|\d{{1,2}}/\d{{1,2}}/\d{{4}})(?!\w)";
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return string.Empty;
        var candidate = match.Groups[1].Value.Trim();
        return
            DateTime.TryParse(
                candidate,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsed
            )
            && parsed.Year >= 1900
            && parsed.Date <= DateTime.Today
            ? candidate
            : string.Empty;
    }

    private static string ExtractBirthdayFromPersonalDetails(BloksScreenInfo screen) =>
        PFlowProfileDates.Birthdate(screen, confirmedProfileContext: true);

    private static string ExtractHierarchyFieldValue(
        BloksScreenInfo screen,
        IEnumerable<string> labels
    )
    {
        foreach (var label in labels)
        {
            var labelElement = screen.AllElements.FirstOrDefault(e =>
                e.Matches(label, partialMatch: true)
            );
            if (labelElement == null)
                continue;

            var inlineDate = ExtractValidBirthdate(labelElement.Text);
            if (string.IsNullOrWhiteSpace(inlineDate))
                inlineDate = ExtractValidBirthdate(labelElement.ContentDesc);
            if (!string.IsNullOrWhiteSpace(inlineDate))
                return inlineDate;

            var value = screen
                .AllElements.Where(e =>
                    e != labelElement
                    && (
                        !string.IsNullOrWhiteSpace(e.Text)
                        || !string.IsNullOrWhiteSpace(e.ContentDesc)
                    )
                    && !labels.Any(other => e.Matches(other, partialMatch: false))
                    && Math.Abs(e.CenterY - labelElement.CenterY) <= 120
                )
                .OrderBy(e =>
                {
                    var dy = e.CenterY - labelElement.CenterY;
                    var dx = Math.Abs(e.CenterX - labelElement.CenterX);
                    return (dy >= 0 && dy < 180 ? dy : Math.Abs(dy) * 4) + dx;
                })
                .SelectMany(e => new[] { e.Text, e.ContentDesc })
                .Select(ExtractValidBirthdate)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static string? ExtractHierarchyMetric(
        BloksScreenInfo screen,
        IEnumerable<string> labels
    )
    {
        var navigationElements = new HashSet<BloksElement>();
        void CollectNavigation(BloksElement element, bool indexedAncestor)
        {
            var indexed =
                indexedAncestor
                || IsIndexedNavigationLabel($"{element.Text} {element.ContentDesc}");
            if (indexed)
                navigationElements.Add(element);
            foreach (var child in element.Children)
                CollectNavigation(child, indexed);
        }
        foreach (var root in screen.RootElements)
            CollectNavigation(root, false);
        foreach (var label in labels)
        {
            // Analytics cards can expose the count and its percentage change as one
            // accessibility label rather than separate numeric nodes.
            foreach (var element in screen.AllElements.Where(e => !navigationElements.Contains(e)))
            {
                foreach (var text in new[] { element.Text, element.ContentDesc })
                {
                    var inline = Regex.Match(
                        text.Trim(),
                        @"^"
                            + Regex.Escape(label)
                            + @"[\s,:]+(?<count>\d(?:[\d,.]*\d)?\s*[KMB]?)(?:\s*[,;]?\s*[↑↓↗↘+−-]?\s*\d[\d,.]*\s*%)?\s*$",
                        RegexOptions.IgnoreCase
                    );
                    if (inline.Success)
                        return inline.Groups["count"].Value.Trim();
                }
            }
            foreach (
                var labelElement in screen.AllElements.Where(e =>
                    e.Matches(label, partialMatch: false) && !navigationElements.Contains(e)
                )
            )
            {
                var candidates = screen
                    .AllElements.Where(e =>
                        e != labelElement
                        && !navigationElements.Contains(e)
                        && Math.Abs(e.CenterY - labelElement.CenterY) <= 160
                        && Math.Abs(e.CenterX - labelElement.CenterX) <= 110
                    )
                    .SelectMany(e =>
                        new[] { (Element: e, Text: e.Text), (Element: e, Text: e.ContentDesc) }
                    )
                    .Select(item =>
                        (
                            item.Element,
                            Match: Regex.Match(
                                item.Text.Trim(),
                                @"^(?<count>\d(?:[\d,.]*\d)?\s*[KMB]?)(?:\s*[,;]?\s*[↑↓↗↘+−-]\s*\d[\d,.]*\s*%)?\s*$",
                                RegexOptions.IgnoreCase
                            )
                        )
                    )
                    .Where(item => item.Match.Success)
                    .OrderBy(item =>
                        Math.Abs(item.Element.CenterX - labelElement.CenterX) * 2
                        + Math.Abs(item.Element.CenterY - labelElement.CenterY)
                    )
                    .ToList();
                if (candidates.Count > 0)
                    return candidates[0].Match.Groups["count"].Value.Trim();
            }
        }
        return null;
    }

    private static bool IsIndexedNavigationLabel(string label) =>
        Regex.IsMatch(
            label,
            @"(?:,\s*(?:tab\s*)?\d+\s+of\s+\d+|\btab\s+\d+\s+of\s+\d+)",
            RegexOptions.IgnoreCase
        );

    private static bool IsIndexedNavigationNode(XElement node) =>
        node.AncestorsAndSelf("node")
            .Any(element =>
                IsIndexedNavigationLabel(
                    $"{element.Attribute("text")?.Value} {element.Attribute("content-desc")?.Value}"
                )
            );

    public static bool IsAnalyticsContentReady(BloksScreenInfo screen) =>
        FacebookLayoutRegistry.MatchScreen(screen, FacebookScreenKind.Analytics).IsMatch;

    internal static bool HasAnalyticsContentEvidence(BloksScreenInfo screen)
    {
        if (
            screen.AllElements.Any(element =>
                element.ClassName.Contains("ProgressBar", StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;
        var labels = screen.VisibleTexts.Concat(screen.ContentDescriptions).ToList();
        var height = screen
            .AllElements.Select(element => element.Bounds.Bottom)
            .DefaultIfEmpty(1280)
            .Max();
        var titleLimit = Math.Max(96, height * 0.12);
        var titleElements = screen
            .AllElements.Where(element =>
                element.Height > 0 && element.CenterY > 0 && element.CenterY <= titleLimit
            )
            .ToList();
        if (
            titleElements.Any(element =>
                element.Matches("Professional dashboard", partialMatch: false)
            )
        )
            return false;
        var analytics = titleElements.Any(element =>
            element.Matches("Analytics", partialMatch: false)
        );
        return analytics
            && labels.Any(label =>
                new[]
                {
                    "Views",
                    "Followers",
                    "Reach",
                    "Earnings",
                    "Engagement",
                    "Interactions",
                }.Any(metric => label.Equals(metric, StringComparison.OrdinalIgnoreCase))
                || label.Contains("No data", StringComparison.OrdinalIgnoreCase)
                || label.Contains("published any content", StringComparison.OrdinalIgnoreCase)
                || label.Contains("Create your first post", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static void MergePageUiInfo(FacebookPageInfo target, FacebookPageInfo source)
    {
        if (!string.IsNullOrWhiteSpace(source.PageId))
            target.PageId = source.PageId;
        if (
            string.IsNullOrWhiteSpace(target.PageName)
            && !string.IsNullOrWhiteSpace(source.PageName)
        )
            target.PageName = source.PageName;
        if (!target.FollowersKnown && (source.FollowersKnown || source.Followers > 0))
        {
            target.Followers = source.Followers;
            target.FollowersKnown = true;
        }
        if (string.IsNullOrWhiteSpace(target.Reach) && !string.IsNullOrWhiteSpace(source.Reach))
            target.Reach = source.Reach;
        if (string.IsNullOrWhiteSpace(target.Views) && !string.IsNullOrWhiteSpace(source.Views))
            target.Views = source.Views;
        if (!target.ReelsCountKnown && (source.ReelsCountKnown || source.ReelsCount > 0))
        {
            target.ReelsCount = source.ReelsCount;
            target.ReelsCountKnown = true;
        }
        if (!target.VideosCountKnown && (source.VideosCountKnown || source.VideosCount > 0))
        {
            target.VideosCount = source.VideosCount;
            target.VideosCountKnown = true;
        }
        if (!target.PictureCountKnown && (source.PictureCountKnown || source.PictureCount > 0))
        {
            target.PictureCount = source.PictureCount;
            target.PictureCountKnown = true;
        }
        if (
            string.IsNullOrWhiteSpace(target.Birthdate)
            && !string.IsNullOrWhiteSpace(source.Birthdate)
        )
            target.Birthdate = source.Birthdate;
        if (
            (
                string.IsNullOrWhiteSpace(target.CreationDate)
                || (
                    !PFlowProfileDates.HasFullDate(target.CreationDate)
                    && PFlowProfileDates.HasFullDate(source.CreationDate)
                )
            ) && !string.IsNullOrWhiteSpace(source.CreationDate)
        )
            target.CreationDate = source.CreationDate;
    }

    private static bool HasPageEnrichment(FacebookPageInfo info) =>
        !string.IsNullOrWhiteSpace(info.CreationDate)
        || !string.IsNullOrWhiteSpace(info.Birthdate)
        || info.FollowersKnown
        || info.Followers > 0
        || !string.IsNullOrWhiteSpace(info.Reach)
        || !string.IsNullOrWhiteSpace(info.Views)
        || info.ReelsCountKnown
        || info.ReelsCount > 0
        || info.VideosCountKnown
        || info.VideosCount > 0
        || info.PictureCountKnown
        || info.PictureCount > 0;

    public static string? ReadDashboardViews(BloksScreenInfo screen) =>
        ExtractHierarchyMetric(screen, ["Views"]);

    private static FacebookPageInfo ReadPageInfoFromHierarchy(
        BloksScreenInfo screen,
        string pageId,
        string pageUid,
        string? expectedPageName
    )
    {
        var info = ParsePageInfoFromXml(screen.RawXml ?? string.Empty, pageId);
        if (!string.IsNullOrWhiteSpace(expectedPageName))
            info.PageName = expectedPageName.Trim();
        if (!string.IsNullOrWhiteSpace(expectedPageName))
        {
            var exactCreated = PFlowProfileDates.CreationDate(screen, false, expectedPageName);
            if (!string.IsNullOrWhiteSpace(exactCreated))
                info.CreationDate = exactCreated;
        }
        info.PersonaUid = pageUid;
        info.Type = "Page";
        return info;
    }

    private static bool IsExpectedPageVisible(
        BloksScreenInfo screen,
        string pageId,
        string? expectedPageName
    )
    {
        if (!string.IsNullOrWhiteSpace(expectedPageName))
        {
            var expected = NormalizeHierarchyText(expectedPageName);
            return screen
                .VisibleTexts.Concat(screen.ContentDescriptions)
                .Any(text => IsPageTitleMatch(NormalizeHierarchyText(text), expected));
        }

        return screen
            .VisibleTexts.Concat(screen.ContentDescriptions)
            .Any(text => text.Contains(pageId, StringComparison.Ordinal));
    }

    public static bool IsExpectedOwnProfileVisible(
        BloksScreenInfo screen,
        string personaUid,
        string? expectedName
    ) =>
        FacebookLayoutRegistry
            .MatchScreen(screen, FacebookScreenKind.OwnProfile, new(expectedName, personaUid))
            .IsMatch;

    internal static bool HasExpectedOwnProfileEvidence(
        BloksScreenInfo screen,
        string personaUid,
        string? expectedName
    )
    {
        if (!IsExpectedPageVisible(screen, personaUid, expectedName))
            return false;
        var labels = screen.VisibleTexts.Concat(screen.ContentDescriptions).ToList();
        if (
            labels.Any(text =>
                text.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Switch into", StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;
        return labels.Any(text =>
            text.Equals("Edit profile", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Edit profile picture", StringComparison.OrdinalIgnoreCase)
            || (
                text.Contains("Professional dashboard", StringComparison.OrdinalIgnoreCase)
                && !text.Contains(", tab", StringComparison.OrdinalIgnoreCase)
            )
            || text.Equals("Edit Page", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Manage Page", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static string NormalizeHierarchyText(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static string DescribeHierarchyElement(BloksElement element) =>
        !string.IsNullOrWhiteSpace(element.Text) ? element.Text.Trim()
        : !string.IsNullOrWhiteSpace(element.ContentDesc) ? element.ContentDesc.Trim()
        : "matched navigation";

    private static bool IsPageTitleMatch(string visibleText, string expectedPageName)
    {
        if (visibleText.Equals(expectedPageName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!visibleText.StartsWith(expectedPageName, StringComparison.OrdinalIgnoreCase))
            return false;

        return visibleText.Length == expectedPageName.Length
            || !char.IsLetterOrDigit(visibleText[expectedPageName.Length]);
    }

    private static BloksElement? FindHierarchyTapTarget(
        BloksScreenInfo screen,
        IEnumerable<string> labels
    )
    {
        foreach (var label in labels)
        {
            foreach (var exact in new[] { true, false })
            {
                foreach (var root in screen.RootElements)
                {
                    var match = FindHierarchyTapTarget(root, label, null, exact);
                    if (match != null)
                        return match;
                }
            }
        }

        return null;
    }

    private static BloksElement? FindHierarchyTapTarget(
        BloksElement element,
        string label,
        BloksElement? nearestClickableAncestor,
        bool exactMatch
    )
    {
        var clickableAncestor =
            element.Clickable
            && element.Enabled
            && element.Width > 0
            && element.Height > 0
            && element.Height <= 240
                ? element
                : nearestClickableAncestor;
        if (
            element.Enabled
            && element.Matches(label, partialMatch: !exactMatch)
            && element.Width > 0
            && element.Height > 0
            && element.Height <= 240
        )
        {
            return clickableAncestor ?? element;
        }

        foreach (var child in element.Children)
        {
            var match = FindHierarchyTapTarget(child, label, clickableAncestor, exactMatch);
            if (match != null)
                return match;
        }

        return null;
    }

    #endregion

    #region Root Shell Facebook Extraction

    public async Task<List<FacebookAccountInfo>> ExtractFacebookAccountsFromRootShellAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(packageName))
            packageName = "com.facebook.katana";

        // Read each logged_in_* file with a separate root ADB command. Compound
        // shell commands get re-quoted by ldconsole.exe and can silently lose data.
        return await ExtractFacebookAccountsDirectFromRootAdbAsync(
            index,
            packageName,
            customPath,
            cancellationToken
        );
    }

    public async Task<List<FacebookAccountInfo>> ExtractFacebookAccountsDirectFromRootAdbAsync(
        int index,
        string packageName,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        var result = new List<FacebookAccountInfo>();
        var lightPrefs = $"/data/data/{packageName}/app_light_prefs/{packageName}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = timeout.Token;

            var rootProbe = await ExecuteAdbAsync(index, "shell su 0 id", customPath, ct);
            if (!Regex.IsMatch(rootProbe, @"\buid=0\b", RegexOptions.IgnoreCase))
            {
                var detail = string.IsNullOrWhiteSpace(rootProbe)
                    ? "the root probe returned no output"
                    : rootProbe.Trim();
                throw new RootShellUnavailableException(
                    $"Root shell is unavailable on LD-{index} ({detail}). Enable root access in LDPlayer settings, then restart the instance."
                );
            }

            var fileList = await ExecuteAdbAsync(
                index,
                $"shell su 0 ls -1 {lightPrefs}",
                customPath,
                ct
            );
            var accountFiles = fileList
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(name => Regex.IsMatch(name, @"^logged_in_\d{10,20}$"))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var authDump = await ReadRootTextFileAsync(
                index,
                $"{lightPrefs}/underlying_account",
                customPath,
                ct
            );
            authDump +=
                "\n"
                + await ReadRootTextFileAsync(
                    index,
                    $"{lightPrefs}/authentication",
                    customPath,
                    ct
                );
            authDump = SanitizeRootDump(authDump);
            var underlyingUid = Regex
                .Match(
                    authDump,
                    @"underlying_account_uid[\r\n\s]+(\d{10,20})",
                    RegexOptions.IgnoreCase
                )
                .Groups[1]
                .Value;
            var activeUid = Regex
                .Match(
                    authDump,
                    @"(?:underlying_account_uid|user_id)[\r\n\s]+(\d{10,20})",
                    RegexOptions.IgnoreCase
                )
                .Groups[1]
                .Value;
            if (!string.IsNullOrWhiteSpace(underlyingUid))
                activeUid = underlyingUid;
            if (string.IsNullOrWhiteSpace(activeUid))
                activeUid = Regex.Match(authDump, @"\b(615\d{11,13}|\d{14,16})\b").Groups[1].Value;
            var activeName = Regex
                .Match(
                    authDump,
                    @"(?:username|name)[\r\n\s]+([^\r\n]{2,60})",
                    RegexOptions.IgnoreCase
                )
                .Groups[1]
                .Value.Trim();

            foreach (var fileName in accountFiles)
            {
                ct.ThrowIfCancellationRequested();
                var uid = Regex.Match(fileName, @"^logged_in_(\d{10,20})$").Groups[1].Value;
                var dump = SanitizeRootDump(
                    await ReadRootTextFileAsync(index, $"{lightPrefs}/{fileName}", customPath, ct)
                );
                if (string.IsNullOrWhiteSpace(dump))
                    continue;

                var name = Regex
                    .Match(
                        dump,
                        @"(?:^|[\r\n])name[\r\n\s]+([^\r\n]{2,60})",
                        RegexOptions.IgnoreCase
                    )
                    .Groups[1]
                    .Value.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    var first = Regex
                        .Match(
                            dump,
                            @"(?:^|[\r\n])first_name[\r\n\s]+([^\r\n]{1,30})",
                            RegexOptions.IgnoreCase
                        )
                        .Groups[1]
                        .Value.Trim();
                    var last = Regex
                        .Match(
                            dump,
                            @"(?:^|[\r\n])last_name[\r\n\s]+([^\r\n]{1,30})",
                            RegexOptions.IgnoreCase
                        )
                        .Groups[1]
                        .Value.Trim();
                    name = $"{first} {last}".Trim();
                }
                var pageId = Regex
                    .Match(
                        dump,
                        @"(?:delegate_page_id|page_id)[\r\n\s]+(\d{10,20})",
                        RegexOptions.IgnoreCase
                    )
                    .Groups[1]
                    .Value;
                // Cached owner records can retain Page delegation. This classification
                // does not prove the live actor; native identity checks remain required.
                var isUnderlyingOwner =
                    !string.IsNullOrWhiteSpace(underlyingUid) && uid == underlyingUid;
                var isPage =
                    !isUnderlyingOwner
                    && (
                        Regex.IsMatch(
                            dump,
                            @"(?:^|[\r\n])is_page[\r\n\s]+(?:1|true)(?=\s|$)",
                            RegexOptions.IgnoreCase
                        )
                        || !string.IsNullOrWhiteSpace(pageId)
                        || (!string.IsNullOrWhiteSpace(activeUid) && uid != activeUid)
                    );
                var userUid = Regex
                    .Match(
                        dump,
                        @"(?:^|[\r\n])user_id[\r\n\s]+(\d{10,20})",
                        RegexOptions.IgnoreCase
                    )
                    .Groups[1]
                    .Value;
                if (string.IsNullOrWhiteSpace(userUid))
                    userUid = activeUid;
                if (isUnderlyingOwner)
                    userUid = uid;
                if (!isPage && (isUnderlyingOwner || string.IsNullOrWhiteSpace(pageId)))
                    pageId = uid;

                MergeRootAccount(
                    result,
                    new FacebookAccountInfo
                    {
                        Type = isPage ? "Page" : "Profile",
                        Uid = uid,
                        PageId = pageId,
                        Name = name,
                        UserUid = userUid,
                        Email = Regex
                            .Match(dump, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")
                            .Value,
                        PicUrl = ReadRootProfilePicture(dump),
                        IsActive = !string.IsNullOrWhiteSpace(activeUid) && uid == activeUid,
                    }
                );
            }

            if (!string.IsNullOrWhiteSpace(activeUid) && result.All(a => a.Uid != activeUid))
            {
                MergeRootAccount(
                    result,
                    new FacebookAccountInfo
                    {
                        Type = "Profile",
                        Uid = activeUid,
                        PageId = activeUid,
                        Name = activeName,
                        UserUid = activeUid,
                        IsActive = true,
                    }
                );
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "root-shell.timeout",
                "Direct ADB discovery exceeded 30 seconds; incomplete results were discarded."
            );
            throw new RootShellUnavailableException(
                $"Root-shell discovery timed out on LD-{index} after 30 seconds; saved data was preserved."
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RootShellUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LD-{index}] Direct Facebook root read failed: {ex.Message}");
        }

        if (packageName == "com.facebook.katana" && result.Count > 0)
        {
            var graph = await new PFlowGraphIdentityReader().TryReadAsync(
                this,
                index,
                customPath,
                cancellationToken
            );
            var actor = graph == null ? null : result.SingleOrDefault(a => a.Uid == graph.Id);
            if (actor != null && graph != null)
            {
                actor.Name = graph.Name;
                actor.PicUrl = graph.PictureUrl ?? actor.PicUrl;
            }
        }
        return result;
    }

    private async Task<string> ReadRootTextFileAsync(
        int index,
        string remotePath,
        string? customPath,
        CancellationToken cancellationToken
    ) =>
        await ExecuteAdbAsync(index, $"shell su 0 cat {remotePath}", customPath, cancellationToken);

    private static string SanitizeRootDump(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;
        return new string(
            raw.Select(ch => ch < 32 && ch is not '\r' and not '\n' and not '\t' ? '\n' : ch)
                .ToArray()
        );
    }

    internal static string ReadRootProfilePicture(string? dump)
    {
        var value = Regex
            .Match(
                SanitizeRootDump(dump),
                @"(?:^|[\r\n])(?:profile_pic_url|profile_pic|pic_square|pic_uri)[\r\n\s]+(https://[^\s\p{C}]+)",
                RegexOptions.IgnoreCase
            )
            .Groups[1]
            .Value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            ? uri.AbsoluteUri
            : string.Empty;
    }

    private static void MergeRootAccount(
        List<FacebookAccountInfo> accounts,
        FacebookAccountInfo account
    )
    {
        var existing = accounts.FirstOrDefault(a =>
            string.Equals(a.Type, account.Type, StringComparison.OrdinalIgnoreCase)
            && (
                a.Uid == account.Uid
                || (!string.IsNullOrWhiteSpace(a.PageId) && a.PageId == account.PageId)
            )
        );
        if (existing is null)
        {
            accounts.Add(account);
            return;
        }
        if (string.IsNullOrWhiteSpace(existing.Name))
            existing.Name = account.Name;
        if (string.IsNullOrWhiteSpace(existing.PageId))
            existing.PageId = account.PageId;
        if (string.IsNullOrWhiteSpace(existing.UserUid))
            existing.UserUid = account.UserUid;
        if (string.IsNullOrWhiteSpace(existing.Email))
            existing.Email = account.Email;
        if (string.IsNullOrWhiteSpace(existing.PicUrl))
            existing.PicUrl = account.PicUrl;
        existing.IsActive |= account.IsActive;
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

        var existingPages = await PFlowPageStorage.LoadPagesAsync(storageFilePath);

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

        await PFlowPageStorage.SavePagesAsync(existingPages, storageFilePath);
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
            else if (line.StartsWith("PERSONA|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('|');
                if (parts.Length < 3)
                    continue;

                var userUid = parts[1].Trim();
                var personaUid = parts[2].Trim();
                if (
                    !Regex.IsMatch(userUid, @"^\d{10,20}$")
                    || !Regex.IsMatch(personaUid, @"^\d{10,20}$")
                )
                    continue;

                var isProfile = string.Equals(userUid, personaUid, StringComparison.Ordinal);
                var account = new FacebookAccountInfo
                {
                    Type = isProfile ? "Profile" : "Page",
                    Uid = personaUid,
                    // Scoped preferences reveal a persona UID, not delegate_page_id.
                    PageId = isProfile ? personaUid : string.Empty,
                    Name = isProfile ? activeName : string.Empty,
                    UserUid = userUid,
                    IsActive = isProfile,
                    AccessToken = isProfile ? activeAccessToken : string.Empty,
                    Cookies = isProfile ? activeCookies : string.Empty,
                };

                var existing = accounts.FirstOrDefault(a =>
                    string.Equals(a.Type, account.Type, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Uid, account.Uid, StringComparison.Ordinal)
                );
                if (existing == null)
                {
                    accounts.Add(account);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(existing.PageId))
                        existing.PageId = account.PageId;
                    if (string.IsNullOrWhiteSpace(existing.UserUid))
                        existing.UserUid = account.UserUid;
                    if (string.IsNullOrWhiteSpace(existing.Name))
                        existing.Name = account.Name;
                    if (!existing.IsActive && account.IsActive)
                        existing.IsActive = true;
                }
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

    public LDPlayerService(AtxAgentClient? atxClient, IPFlowPersonaSwitcherService? personaSwitcher)
        : this(atxClient, personaSwitcher, null, null, null, null, null, null) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IPFlowPersonaSwitcherService? personaSwitcher,
        IPFlowAutoPostService? autoPostService
    )
        : this(atxClient, personaSwitcher, autoPostService, null, null, null, null, null) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IPFlowPersonaSwitcherService? personaSwitcher,
        IPFlowAutoPostService? autoPostService,
        IPFlowFeedEngagementService? feedEngagementService
    )
        : this(
            atxClient,
            personaSwitcher,
            autoPostService,
            feedEngagementService,
            null,
            null,
            null,
            null
        ) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IPFlowPersonaSwitcherService? personaSwitcher,
        IPFlowAutoPostService? autoPostService,
        IPFlowFeedEngagementService? feedEngagementService,
        IPFlowBloksService? bloksService
    )
        : this(
            atxClient,
            personaSwitcher,
            autoPostService,
            feedEngagementService,
            bloksService,
            null,
            null,
            null
        ) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IPFlowPersonaSwitcherService? personaSwitcher,
        IPFlowAutoPostService? autoPostService,
        IPFlowFeedEngagementService? feedEngagementService,
        IPFlowBloksService? bloksService,
        IPFlowAccountDiscoveryService? accountDiscoveryService
    )
        : this(
            atxClient,
            personaSwitcher,
            autoPostService,
            feedEngagementService,
            bloksService,
            accountDiscoveryService,
            null,
            null
        ) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IPFlowPersonaSwitcherService? personaSwitcher,
        IPFlowAutoPostService? autoPostService,
        IPFlowFeedEngagementService? feedEngagementService,
        IPFlowBloksService? bloksService,
        IPFlowAccountDiscoveryService? accountDiscoveryService,
        IAdbKeyboardService? adbKeyboardService
    )
        : this(
            atxClient,
            personaSwitcher,
            autoPostService,
            feedEngagementService,
            bloksService,
            accountDiscoveryService,
            adbKeyboardService,
            null
        ) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IPFlowPersonaSwitcherService? personaSwitcher,
        IPFlowAutoPostService? autoPostService,
        IPFlowFeedEngagementService? feedEngagementService,
        IPFlowBloksService? bloksService,
        IPFlowAccountDiscoveryService? accountDiscoveryService,
        IAdbKeyboardService? adbKeyboardService,
        ILDPlayerUiAutomationService? uiAutomationService
    )
    {
        _atxClient = atxClient ?? new AtxAgentClient();
        _personaSwitcher = personaSwitcher ?? new PFlowPersonaSwitcherService(this, _atxClient);
        _autoPostService = autoPostService ?? new PFlowAutoPostService(this, _atxClient);
        _feedEngagementService = feedEngagementService ?? new PFlowFeedEngagementService(this);
        _bloksService = bloksService ?? new PFlowBloksService(this, _atxClient);
        _accountDiscoveryService =
            accountDiscoveryService
            ?? new PFlowAccountDiscoveryService(
                this,
                _atxClient,
                _bloksService,
                personaSwitcher: _personaSwitcher
            );
        _adbKeyboardService = adbKeyboardService ?? new AdbKeyboardService(this, _atxClient);
        _uiAutomationService =
            uiAutomationService
            ?? new LDPlayerUiAutomationService(this, _atxClient, _adbKeyboardService);
    }

    private readonly IPFlowPersonaSwitcherService _personaSwitcher;
    private readonly IPFlowAutoPostService _autoPostService;
    private readonly IPFlowFeedEngagementService _feedEngagementService;
    private readonly IPFlowBloksService _bloksService;
    private readonly IPFlowAccountDiscoveryService _accountDiscoveryService;
    private readonly IAdbKeyboardService _adbKeyboardService;
    private readonly ILDPlayerUiAutomationService _uiAutomationService;

    public Task<bool> SwitchToPageByIdAsync(
        int index,
        string pageName,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _personaSwitcher.SwitchToPageByIdAsync(
            index,
            pageName,
            pageId,
            customPath,
            cancellationToken
        );

    public Task<bool> ClickProfileSwitcherDropdownAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _personaSwitcher.ClickProfileSwitcherDropdownAsync(index, customPath, cancellationToken);

    public Task<int> ScrollNewsFeedLikeHumanAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.ScrollNewsFeedLikeHumanAsync(index, customPath, cancellationToken);

    public Task<bool> ScrollToNextVideoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.ScrollToNextVideoAsync(index, customPath, cancellationToken);

    public Task<(
        bool IsActive,
        string? AccountName,
        string? StatusReason
    )> VerifyFacebookAccountActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _accountDiscoveryService.VerifyFacebookAccountActiveAsync(
            index,
            customPath,
            cancellationToken
        );

    public void ApplyDiscoveryDelayConfig(PFlowDiscoveryDelayConfig config) =>
        _accountDiscoveryService.ApplyDelayConfig(config);

    public void ApplyDiscoveryDelayConfig(AutoPostConfig config) =>
        _accountDiscoveryService.ApplyDelayConfig(config);

    public void ApplyDiscoveryDelayConfig(LDSettings settings) =>
        _accountDiscoveryService.ApplyDelayConfig(settings);

    public static (int X, int Y)? FindQuickSwitchAvatarInMenu(
        string? menuXml,
        string targetPersonaName
    ) => PFlowPersonaSwitcherService.FindQuickSwitchAvatarInMenu(menuXml, targetPersonaName);

    public static (int X, int Y)? FindPageInMenuShortcuts(
        string? menuXml,
        string targetPersonaName
    ) => PFlowPersonaSwitcherService.FindPageInMenuShortcuts(menuXml, targetPersonaName);

    #endregion
}
