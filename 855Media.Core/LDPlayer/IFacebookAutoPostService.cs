using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Domain service interface for publishing multimedia content (Reels, Pictures, Videos)
/// to Facebook on an LDPlayer emulator instance.
/// </summary>
public interface IFacebookAutoPostService
{
    /// <summary>
    /// Posts a short-form video (Reel) to Facebook via the Katana Reel creation wizard.
    /// </summary>
    Task<bool> PostReelAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Posts a photo or image to Facebook feed via the Katana composer.
    /// </summary>
    Task<bool> PostPictureAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Posts a standard video to Facebook feed.
    /// </summary>
    Task<bool> PostVideoAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Waits until Facebook Katana finishes encoding and uploading media.
    /// </summary>
    Task<bool> WaitForUploadCompletedAsync(
        int index,
        string? remotePath = null,
        TimeSpan? timeout = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );
}
