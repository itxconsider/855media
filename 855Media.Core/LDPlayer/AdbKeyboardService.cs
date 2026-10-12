using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Clean Architecture implementation of ADB Keyboard service for typing text (including UTF-8/Unicode),
/// sending editor actions, and installing/configuring ADB Keyboard IME on LDPlayer emulators.
/// </summary>
public class AdbKeyboardService : IAdbKeyboardService
{
    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;

    public AdbKeyboardService(ILDPlayerService ldPlayer, AtxAgentClient? atxClient = null)
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
    }

    public async Task<bool> InputTextAdbKeyboardAsync(
        int index,
        string text,
        bool clearFirst = false,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
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
        var output = await _ldPlayer.ExecuteAdbAsync(
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
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ClearTextAdbKeyboardAsync(index, cancellationToken);
        }

        var output = await _ldPlayer.ExecuteAdbAsync(
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
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.SendAdbKeyboardEditorActionAsync(
                index,
                action,
                cancellationToken
            );
        }

        var output = await _ldPlayer.ExecuteAdbAsync(
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
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.SendAdbKeyboardCodeAsync(index, keyCode, cancellationToken);
        }

        var output = await _ldPlayer.ExecuteAdbAsync(
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
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsAdbKeyboardInstalledAsync(index, cancellationToken);
        }

        var output = await _ldPlayer.ExecuteAdbAsync(
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
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsAdbKeyboardActiveAsync(index, cancellationToken);
        }

        var output = await _ldPlayer.ExecuteAdbAsync(
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
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.EnableAdbKeyboardAsync(index, cancellationToken);
        }

        var output = await _ldPlayer.ExecuteAdbAsync(
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
                using var http = new HttpClient();
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
        var installOut = await _ldPlayer.ExecuteAdbAsync(
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
}
