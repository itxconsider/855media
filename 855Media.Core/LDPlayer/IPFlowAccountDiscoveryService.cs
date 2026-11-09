using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Domain service interface for discovering, verifying, and synchronizing Facebook accounts,
/// profiles, and managed pages through native emulator UI.
/// </summary>
public interface IPFlowAccountDiscoveryService
{
    /// <summary>
    /// Fetches page metrics (followers, reach, type) by opening the target page on the device.
    /// </summary>
    Task<FacebookPageInfo?> FetchPageInfoFromDeviceAsync(
        int index,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Discovers the active Facebook account, its profiles, and managed pages on the emulator instance.
    /// </summary>
    Task<FacebookDeviceAccountInfo?> FetchDeviceFacebookAccountAndPagesAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Extracts comprehensive account details (email, birthday, UID, name) using UI hierarchy,
    /// Settings/Accounts Center and native UI; private-storage fallback was removed.
    /// </summary>
    Task<FacebookAccountDetails?> ExtractFacebookAccountDetailsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<FacebookAccountDetails, Task>? rootShellCompleted = null
    );

    /// <summary>
    /// Verifies whether Facebook has an active, logged-in session on the emulator instance.
    /// Inspects the native selected actor and UI hierarchy to detect
    /// login prompts, expired sessions, or checkpoints.
    /// </summary>
    Task<(
        bool IsActive,
        string? AccountName,
        string? StatusReason
    )> VerifyFacebookAccountActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retired compatibility entry point. Always rejects private-storage extraction without device access.
    /// </summary>
    Task<List<FacebookAccountInfo>> ExtractFacebookAccountsFromRootShellAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retired compatibility entry point. Always rejects private-storage extraction without device access.
    /// </summary>
    Task<List<FacebookAccountInfo>> ExtractFacebookAccountsDirectFromRootAdbAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Synchronizes native UI discovery results into facebook_pages.json.
    /// </summary>
    Task<List<FacebookPageTarget>> SyncDiscoveredAccountsToStorageAsync(
        int index,
        string? deviceName = null,
        string appClone = "FB-01",
        string packageName = "com.facebook.katana",
        string? storageFilePath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Current timing and delay configuration for account discovery workflows.
    /// </summary>
    PFlowDiscoveryDelayConfig DelayConfig { get; set; }

    /// <summary>
    /// Applies custom delay configuration for discovery workflows.
    /// </summary>
    void ApplyDelayConfig(PFlowDiscoveryDelayConfig config);

    /// <summary>
    /// Applies delay configuration derived from AutoPostConfig.
    /// </summary>
    void ApplyDelayConfig(AutoPostConfig config);

    /// <summary>
    /// Applies delay configuration derived from LDSettings.
    /// </summary>
    void ApplyDelayConfig(LDSettings settings);
}
