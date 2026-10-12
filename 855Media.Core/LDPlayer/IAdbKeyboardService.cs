using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Service abstraction for managing and interacting with ADB Keyboard on Android emulators/devices.
/// Supports text entry, clearing text, sending editor actions/keycodes, and IME lifecycle management.
/// </summary>
public interface IAdbKeyboardService
{
    Task<bool> InputTextAdbKeyboardAsync(
        int index,
        string text,
        bool clearFirst = false,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClearTextAdbKeyboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> SendAdbKeyboardEditorActionAsync(
        int index,
        AdbEditorAction action,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> SendAdbKeyboardCodeAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsAdbKeyboardInstalledAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsAdbKeyboardActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> EnableAdbKeyboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> InstallAdbKeyboardAsync(
        int index,
        string? apkPath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );
}
