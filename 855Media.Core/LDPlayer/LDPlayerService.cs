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
    private readonly IFacebookPersonaSwitcherService _personaSwitcher;
    private readonly IFacebookAutoPostService _autoPostService;
    private readonly IFacebookFeedEngagementService _feedEngagementService;
    private readonly IFacebookBloksService _bloksService;
    private readonly IFacebookAccountDiscoveryService _accountDiscoveryService;
    private readonly IAdbKeyboardService _adbKeyboardService;
    private readonly ILDPlayerUiAutomationService _uiAutomationService;

    public LDPlayerService()
        : this(null, null, null, null, null, null, null, null) { }

    public LDPlayerService(AtxAgentClient? atxClient)
        : this(atxClient, null, null, null, null, null, null, null) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IFacebookPersonaSwitcherService? personaSwitcher
    )
        : this(atxClient, personaSwitcher, null, null, null, null, null, null) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IFacebookPersonaSwitcherService? personaSwitcher,
        IFacebookAutoPostService? autoPostService
    )
        : this(atxClient, personaSwitcher, autoPostService, null, null, null, null, null) { }

    public LDPlayerService(
        AtxAgentClient? atxClient,
        IFacebookPersonaSwitcherService? personaSwitcher,
        IFacebookAutoPostService? autoPostService,
        IFacebookFeedEngagementService? feedEngagementService
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
        IFacebookPersonaSwitcherService? personaSwitcher,
        IFacebookAutoPostService? autoPostService,
        IFacebookFeedEngagementService? feedEngagementService,
        IFacebookBloksService? bloksService
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
        IFacebookPersonaSwitcherService? personaSwitcher,
        IFacebookAutoPostService? autoPostService,
        IFacebookFeedEngagementService? feedEngagementService,
        IFacebookBloksService? bloksService,
        IFacebookAccountDiscoveryService? accountDiscoveryService
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
        IFacebookPersonaSwitcherService? personaSwitcher,
        IFacebookAutoPostService? autoPostService,
        IFacebookFeedEngagementService? feedEngagementService,
        IFacebookBloksService? bloksService,
        IFacebookAccountDiscoveryService? accountDiscoveryService,
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
        IFacebookPersonaSwitcherService? personaSwitcher,
        IFacebookAutoPostService? autoPostService,
        IFacebookFeedEngagementService? feedEngagementService,
        IFacebookBloksService? bloksService,
        IFacebookAccountDiscoveryService? accountDiscoveryService,
        IAdbKeyboardService? adbKeyboardService,
        ILDPlayerUiAutomationService? uiAutomationService
    )
    {
        _atxClient = atxClient ?? new AtxAgentClient();
        _personaSwitcher = personaSwitcher ?? new FacebookPersonaSwitcherService(this, _atxClient);
        _autoPostService = autoPostService ?? new FacebookAutoPostService(this, _atxClient);
        _feedEngagementService = feedEngagementService ?? new FacebookFeedEngagementService(this);
        _bloksService = bloksService ?? new FacebookBloksService(this, _atxClient);
        _accountDiscoveryService =
            accountDiscoveryService
            ?? new FacebookAccountDiscoveryService(
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
        if (!SurfsharkVpnMonitor.IsConnected())
        {
            Debug.WriteLine(
                $"[LD-{index}] Waiting for the Surfshark VPN tunnel before launching LDPlayer."
            );
            await SurfsharkVpnMonitor.WaitUntilConnectedAsync(cancellationToken);
        }

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

    public virtual async Task<string> ExecuteAdbAsync(
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

    public Task<bool> SwipeAsync(
        int index,
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMs = 350,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _uiAutomationService.SwipeAsync(
            index,
            startX,
            startY,
            endX,
            endY,
            durationMs,
            customPath,
            cancellationToken
        );

    public Task<bool> TapAsync(
        int index,
        int x,
        int y,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.TapAsync(index, x, y, customPath, cancellationToken);

    public Task<bool> SendKeyAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.SendKeyAsync(index, keyCode, customPath, cancellationToken);

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

    public Task<bool> WaitForFacebookUploadCompletedAsync(
        int index,
        string? remotePath = null,
        TimeSpan? timeout = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return _autoPostService.WaitForUploadCompletedAsync(
            index,
            remotePath,
            timeout,
            customPath,
            cancellationToken
        );
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

    public Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _personaSwitcher.SwitchToPersonaAsync(index, personaName, customPath, cancellationToken);

    public Task<bool> SwitchToPersonaAsync(
        int index,
        FacebookPageTarget target,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _personaSwitcher.SwitchToPersonaAsync(index, target, customPath, cancellationToken);

    public Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _personaSwitcher.SwitchToPersonaAsync(
            index,
            personaName,
            pageId,
            customPath,
            cancellationToken
        );

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

    public Task<bool> OpenFacebookMenuFromFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _personaSwitcher.OpenFacebookMenuFromFeedAsync(index, customPath, cancellationToken);

    public Task<bool> ClickProfileSwitcherDropdownAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _personaSwitcher.ClickProfileSwitcherDropdownAsync(index, customPath, cancellationToken);

    public Task<(int X, int Y)?> QueryPersonalProfileInSwitcherAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _personaSwitcher.QueryPersonalProfileInSwitcherAsync(
            index,
            targetProfileName,
            customPath,
            cancellationToken
        );

    public Task<bool> LikeCurrentFeedPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.LikeCurrentFeedPostAsync(index, customPath, cancellationToken);

    public Task<int> ScrollNewsFeedLikeHumanAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.ScrollNewsFeedLikeHumanAsync(index, customPath, cancellationToken);

    public Task<bool> LikeCurrentVideoPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.LikeCurrentVideoPostAsync(index, customPath, cancellationToken);

    public Task<bool> ScrollToNextVideoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.ScrollToNextVideoAsync(index, customPath, cancellationToken);

    public Task<bool> EnsureOnFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _feedEngagementService.EnsureOnFeedAsync(index, customPath, cancellationToken);

    public Task<bool> PostReelToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return _autoPostService.PostReelAsync(
            index,
            localVideoPath,
            caption,
            customPath,
            cancellationToken
        );
    }

    public Task<bool> PostPictureToFacebookAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return _autoPostService.PostPictureAsync(
            index,
            localImagePath,
            caption,
            customPath,
            cancellationToken
        );
    }

    public Task<bool> PostVideoToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return _autoPostService.PostVideoAsync(
            index,
            localVideoPath,
            caption,
            customPath,
            cancellationToken
        );
    }

    public Task<string?> DumpUiHierarchyAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.DumpUiHierarchyAsync(index, customPath, cancellationToken);

    public Task<FacebookPageInfo?> FetchPageInfoFromDeviceAsync(
        int index,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _accountDiscoveryService.FetchPageInfoFromDeviceAsync(
            index,
            pageId,
            customPath,
            cancellationToken
        );

    public static FacebookPageInfo ParsePageInfoFromXml(string xml, string fallbackPageId) =>
        FacebookAccountDiscoveryService.ParsePageInfoFromXml(xml, fallbackPageId);

    public Task<FacebookDeviceAccountInfo?> FetchDeviceFacebookAccountAndPagesAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _accountDiscoveryService.FetchDeviceFacebookAccountAndPagesAsync(
            index,
            customPath,
            cancellationToken
        );

    public static FacebookDeviceAccountInfo ParseDeviceAccountAndPagesFromXml(
        string? profileXml,
        string? pagesXml,
        string? prefsOutput,
        int deviceIndex
    ) =>
        FacebookAccountDiscoveryService.ParseDeviceAccountAndPagesFromXml(
            profileXml,
            pagesXml,
            prefsOutput,
            deviceIndex
        );

    public Task<FacebookAccountDetails?> ExtractFacebookAccountDetailsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<FacebookAccountDetails, Task>? rootShellCompleted = null
    ) =>
        _accountDiscoveryService.ExtractFacebookAccountDetailsAsync(
            index,
            customPath,
            cancellationToken,
            rootShellCompleted
        );

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

    public void ApplyDiscoveryDelayConfig(FacebookDiscoveryDelayConfig config) =>
        _accountDiscoveryService.ApplyDelayConfig(config);

    public void ApplyDiscoveryDelayConfig(AutoPostConfig config) =>
        _accountDiscoveryService.ApplyDelayConfig(config);

    public void ApplyDiscoveryDelayConfig(LDSettings settings) =>
        _accountDiscoveryService.ApplyDelayConfig(settings);

    public static bool IsIgnoredSwitcherHeaderOrAction(string text) =>
        FacebookAccountDiscoveryService.IsIgnoredSwitcherHeaderOrAction(text);

    public static bool IsNotificationSubtitle(string text) =>
        FacebookAccountDiscoveryService.IsNotificationSubtitle(text);

    public static FacebookAccountDetails ParseFacebookAccountDetails(
        string? switcherXml,
        string? personalInfoXml,
        string? profileXml,
        string? prefsDump,
        string? dbDump,
        int deviceIndex
    ) =>
        FacebookAccountDiscoveryService.ParseFacebookAccountDetails(
            switcherXml,
            personalInfoXml,
            profileXml,
            prefsDump,
            dbDump,
            deviceIndex
        );

    public static (int X, int Y)? FindProfileSwitcherCoordinates(string? menuXml) =>
        FacebookPersonaSwitcherService.FindProfileSwitcherCoordinates(menuXml);

    public static XElement? FindDirectSwitchButton(string? xml, string? targetProfileName = null) =>
        FacebookPersonaSwitcherService.FindDirectSwitchButton(xml, targetProfileName);

    public static bool IsAccountSwitcherOpen(string? xml) =>
        FacebookPersonaSwitcherService.IsAccountSwitcherOpen(xml);

    public static (int X, int Y)? FindQuickSwitchAvatarInMenu(
        string? menuXml,
        string targetPersonaName
    ) => FacebookPersonaSwitcherService.FindQuickSwitchAvatarInMenu(menuXml, targetPersonaName);

    public static (int X, int Y)? FindPageInMenuShortcuts(
        string? menuXml,
        string targetPersonaName
    ) => FacebookPersonaSwitcherService.FindPageInMenuShortcuts(menuXml, targetPersonaName);

    public static bool IsAlreadyOnTargetPersona(string? xml, string personaName) =>
        FacebookPersonaSwitcherService.IsAlreadyOnTargetPersona(xml, personaName);

    public static (int X, int Y, bool IsSelected)? FindPersonaInSwitcher(
        string? switcherXml,
        string personaName
    ) => FacebookPersonaSwitcherService.FindPersonaInSwitcher(switcherXml, personaName);

    public static XElement? FindPersonalProfileNodeInSwitcher(
        string? switcherXml,
        string? targetProfileName = null
    ) =>
        FacebookPersonaSwitcherService.FindPersonalProfileNodeInSwitcher(
            switcherXml,
            targetProfileName
        );

    public static (int X, int Y)? FindLikeButtonCoordinates(string? xml) =>
        FacebookFeedEngagementService.FindLikeButtonCoordinates(xml);

    public static (int X, int Y)? FindVideoLikeButtonCoordinates(string? xml) =>
        FacebookFeedEngagementService.FindVideoLikeButtonCoordinates(xml);

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

    public Task<bool> ClickElementByTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.ClickElementByTextAsync(index, text, customPath, cancellationToken);

    public Task<bool> ClickElementByResourceIdAsync(
        int index,
        string resourceId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _uiAutomationService.ClickElementByResourceIdAsync(
            index,
            resourceId,
            customPath,
            cancellationToken
        );

    public Task<bool> ClickElementByDescAsync(
        int index,
        string contentDesc,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _uiAutomationService.ClickElementByDescAsync(
            index,
            contentDesc,
            customPath,
            cancellationToken
        );

    public Task<bool> InputTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.InputTextAsync(index, text, customPath, cancellationToken);

    public Task<bool> InputTextAdbKeyboardAsync(
        int index,
        string text,
        bool clearFirst = false,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _adbKeyboardService.InputTextAdbKeyboardAsync(
            index,
            text,
            clearFirst,
            customPath,
            cancellationToken
        );

    public Task<bool> ClearTextAdbKeyboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _adbKeyboardService.ClearTextAdbKeyboardAsync(index, customPath, cancellationToken);

    public Task<bool> SendAdbKeyboardEditorActionAsync(
        int index,
        AdbEditorAction action,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _adbKeyboardService.SendAdbKeyboardEditorActionAsync(
            index,
            action,
            customPath,
            cancellationToken
        );

    public Task<bool> SendAdbKeyboardCodeAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _adbKeyboardService.SendAdbKeyboardCodeAsync(index, keyCode, customPath, cancellationToken);

    public Task<bool> IsAdbKeyboardInstalledAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _adbKeyboardService.IsAdbKeyboardInstalledAsync(index, customPath, cancellationToken);

    public Task<bool> IsAdbKeyboardActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _adbKeyboardService.IsAdbKeyboardActiveAsync(index, customPath, cancellationToken);

    public Task<bool> EnableAdbKeyboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _adbKeyboardService.EnableAdbKeyboardAsync(index, customPath, cancellationToken);

    public Task<bool> InstallAdbKeyboardAsync(
        int index,
        string? apkPath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _adbKeyboardService.InstallAdbKeyboardAsync(index, apkPath, customPath, cancellationToken);

    public Task<bool> PressHomeAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.PressHomeAsync(index, customPath, cancellationToken);

    public Task<bool> PressBackAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.PressBackAsync(index, customPath, cancellationToken);

    public Task<bool> PressRecentAppsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _uiAutomationService.PressRecentAppsAsync(index, customPath, cancellationToken);

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

    public Task<bool> IsBloksNativeShellActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _bloksService.IsBloksNativeShellActiveAsync(index, customPath, cancellationToken);

    public Task<bool> OpenBloksNativeShellAsync(
        int index,
        string bloksAppId,
        Dictionary<string, string>? parameters = null,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _bloksService.OpenBloksNativeShellAsync(
            index,
            bloksAppId,
            parameters,
            packageName,
            customPath,
            cancellationToken
        );

    public Task<bool> OpenBloksAccountsCenterAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _bloksService.OpenBloksAccountsCenterAsync(
            index,
            packageName,
            customPath,
            cancellationToken
        );

    public Task<bool> OpenBloksProfileSwitcherAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _bloksService.OpenBloksProfileSwitcherAsync(
            index,
            packageName,
            customPath,
            cancellationToken
        );

    public Task<BloksScreenInfo?> DumpBloksScreenAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _bloksService.DumpBloksScreenAsync(index, customPath, cancellationToken);

    public Task<bool> ClickBloksElementAsync(
        int index,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _bloksService.ClickBloksElementAsync(
            index,
            textOrDesc,
            partialMatch,
            caseInsensitive,
            customPath,
            cancellationToken
        );

    public Task<string?> ExtractBloksFieldValueAsync(
        int index,
        string fieldLabel,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _bloksService.ExtractBloksFieldValueAsync(index, fieldLabel, customPath, cancellationToken);

    public Task<BloksFacebookAccountInfo?> ExtractBloksAccountInfoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _bloksService.ExtractBloksAccountInfoAsync(index, customPath, cancellationToken);

    #endregion

    #region Root Shell Facebook Extraction

    public Task<List<FacebookAccountInfo>> ExtractFacebookAccountsFromRootShellAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _accountDiscoveryService.ExtractFacebookAccountsFromRootShellAsync(
            index,
            packageName,
            customPath,
            cancellationToken
        );

    public Task<List<FacebookAccountInfo>> ExtractFacebookAccountsDirectFromRootAdbAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _accountDiscoveryService.ExtractFacebookAccountsDirectFromRootAdbAsync(
            index,
            packageName,
            customPath,
            cancellationToken
        );

    public Task<List<FacebookPageTarget>> SyncDiscoveredAccountsToStorageAsync(
        int index,
        string? deviceName = null,
        string appClone = "FB-01",
        string packageName = "com.facebook.katana",
        string? storageFilePath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) =>
        _accountDiscoveryService.SyncDiscoveredAccountsToStorageAsync(
            index,
            deviceName,
            appClone,
            packageName,
            storageFilePath,
            customPath,
            cancellationToken
        );

    public static List<FacebookAccountInfo> ParseRootShellAccountOutput(string? output) =>
        FacebookAccountDiscoveryService.ParseRootShellAccountOutput(output);

    #endregion
}
