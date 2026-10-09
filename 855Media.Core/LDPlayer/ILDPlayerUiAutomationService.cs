using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Service abstraction for UI hierarchy dumping, element discovery, clicks, kinetic swipes,
/// text input injection, and hardware button navigation on LDPlayer instances.
/// </summary>
public interface ILDPlayerUiAutomationService
{
    Task<string?> DumpUiHierarchyAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClickElementByTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClickElementByResourceIdAsync(
        int index,
        string resourceId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClickElementByDescAsync(
        int index,
        string contentDesc,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> InputTextAsync(
        int index,
        string text,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> SwipeAsync(
        int index,
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMs = 350,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> TapAsync(
        int index,
        int x,
        int y,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> SendKeyAsync(
        int index,
        int keyCode,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PressHomeAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PressBackAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PressRecentAppsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );
}
