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

    Task<bool> LikeCurrentVideoPostAsync(
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
        CancellationToken cancellationToken = default
    );

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
