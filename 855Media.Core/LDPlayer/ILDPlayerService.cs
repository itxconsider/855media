using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public interface ILDPlayerService
{
    bool IsInstalled(string? customPath = null);
    string ResolveLdConsolePath(string? customPath = null);

    Task<IReadOnlyList<LDInstanceInfo>> GetInstancesAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> LaunchInstanceAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> QuitInstanceAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> QuitAllInstancesAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> RebootInstanceAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> SortWindowsAsync(
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<string> ExecuteAdbAsync(
        int index,
        string adbCommand,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClearFacebookCacheAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> StopFacebookAppAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PushFileToEmulatorAsync(
        int index,
        string sourceLocalPath,
        string targetRemotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> WaitForBootCompletedAsync(
        int index,
        TimeSpan timeout,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> LaunchAppAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenDeepLinkAsync(
        int index,
        string deepLink,
        string packageName = "com.facebook.katana",
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

    Task<bool> ScanMediaFileAsync(
        int index,
        string remotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ShareVideoToFacebookAsync(
        int index,
        string remotePath,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> DeleteFileFromEmulatorAsync(
        int index,
        string remotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> WaitForFacebookUploadCompletedAsync(
        int index,
        string? remotePath = null,
        TimeSpan? timeout = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ShareImageToFacebookAsync(
        int index,
        string remotePath,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Switches to the target persona represented by a FacebookPageTarget.
    /// If target is a Page, utilizes Tier 1 deep-link switching with its PageId/PersonaUid.
    /// If target is a Profile, routes to EnsureSwitchedToPersonalProfileAsync.
    /// </summary>
    Task<bool> SwitchToPersonaAsync(
        int index,
        FacebookPageTarget target,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Switches to a persona by name, optionally specifying pageId to enable Tier 1 deep-link switching.
    /// </summary>
    Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Direct deep-link switch pipeline to manage a Facebook Page by its numeric PageId,
    /// bypassing navigation bar, menu, and switcher sheet variations.
    /// </summary>
    Task<bool> SwitchToPageByIdAsync(
        int index,
        string pageName,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> EnsureSwitchedToPersonalProfileAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenFacebookMenuFromFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClickProfileSwitcherDropdownAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<(int X, int Y)?> QueryPersonalProfileInSwitcherAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> LikeCurrentFeedPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Performs an authentic, human-like finger scroll transition on the Facebook newsfeed.
    /// Emulates natural thumb flick kinetics (smooth momentum glide / fling transition), reading drags,
    /// micro-adjustments, thumb arc curvature/drift, and returns the recommended dwell time in ms.
    /// </summary>
    Task<int> ScrollNewsFeedLikeHumanAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> LikeCurrentVideoPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Performs an upward swipe gesture specifically tuned for video/reels feeds to advance to the next video,
    /// ensuring any blocking comment overlays or modal sheets are dismissed and swiping with sufficient travel
    /// and duration to reliably trigger ViewPager page switching without snapping back.
    /// </summary>
    Task<bool> ScrollToNextVideoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> EnsureOnFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PostReelToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PostPictureToFacebookAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> PostVideoToFacebookAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<string?> DumpUiHierarchyAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<FacebookPageInfo?> FetchPageInfoFromDeviceAsync(
        int index,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<FacebookDeviceAccountInfo?> FetchDeviceFacebookAccountAndPagesAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<FacebookAccountDetails?> ExtractFacebookAccountDetailsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<FacebookAccountDetails, Task>? rootShellCompleted = null
    );

    /// <summary>
    /// Verifies whether Facebook has an active, logged-in session on the emulator instance.
    /// Checks root session database / shared_prefs and inspects UI hierarchy to detect
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
    /// Applies custom delay configuration to the Facebook account discovery service.
    /// </summary>
    void ApplyDiscoveryDelayConfig(FacebookDiscoveryDelayConfig config);

    /// <summary>
    /// Applies discovery delay configuration derived from AutoPostConfig.
    /// </summary>
    void ApplyDiscoveryDelayConfig(AutoPostConfig config);

    /// <summary>
    /// Applies discovery delay configuration derived from LDSettings.
    /// </summary>
    void ApplyDiscoveryDelayConfig(LDSettings settings);

    Task<bool> EnsureAtxAgentRunningAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsAtxAgentRunningAsync(int index, CancellationToken cancellationToken = default);

    Task<byte[]?> TakeScreenshotAsync(
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

    Task<AtxDeviceInfo?> GetAtxDeviceInfoAsync(
        int index,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsAppRunningAsync(
        int index,
        string packageName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<byte[]?> DownloadFileFromEmulatorAsync(
        int index,
        string targetRemotePath,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<string?> GetCurrentFocusedActivityAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsBloksNativeShellActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenBloksNativeShellAsync(
        int index,
        string bloksAppId,
        Dictionary<string, string>? parameters = null,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenBloksAccountsCenterAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenBloksProfileSwitcherAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<BloksScreenInfo?> DumpBloksScreenAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClickBloksElementAsync(
        int index,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<string?> ExtractBloksFieldValueAsync(
        int index,
        string fieldLabel,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<BloksFacebookAccountInfo?> ExtractBloksAccountInfoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Extracts Facebook accounts, profiles, and managed pages directly from rooted device storage
    /// (/data/data/com.facebook.katana/) via root shell (LightSharedPreferences, prefs_db, cache).
    /// </summary>
    Task<List<FacebookAccountInfo>> ExtractFacebookAccountsFromRootShellAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fallback direct in-memory extraction of Facebook accounts via ADB root shell commands without pushing scripts.
    /// </summary>
    Task<List<FacebookAccountInfo>> ExtractFacebookAccountsDirectFromRootAdbAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Synchronizes discovered Facebook accounts and pages from rooted device storage into facebook_pages.json.
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
}
