using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Persisted configuration state for AutoPost workflow, ScreenTime sessions, delays, and options.
/// </summary>
public class AutoPostConfig
{
    public ScreenTimeOptions ScreenTimeOptions { get; set; } = new();

    // --- ScreenTime (legacy JSON property names retained for compatibility) ---
    public bool IsWatchVideo { get; set; } = true;
    public int WatchVideoMin { get; set; } = 60;
    public int WatchVideoMax { get; set; } = 120;

    public bool IsMarketPlace { get; set; } = false;
    public int MarketPlaceMin { get; set; } = 20;
    public int MarketPlaceMax { get; set; } = 50;

    public bool IsLikePost { get; set; } = false;
    public int LikePostMin { get; set; } = 1;
    public int LikePostMax { get; set; } = 3;

    public bool IsNewsFeed { get; set; } = false;
    public int NewsFeedMin { get; set; } = 60;
    public int NewsFeedMax { get; set; } = 120;

    public bool IsCheckIn { get; set; } = false;
    public string CheckInLocation { get; set; } = string.Empty;

    public bool IsReadNotification { get; set; } = false;
    public bool IsClearRecentApp { get; set; } = true;

    // --- Social Actions & Friends ---
    public bool AddFriendSuggest { get; set; } = false;
    public int AddFriendSuggestMin { get; set; } = 1;
    public int AddFriendSuggestMax { get; set; } = 1;

    public bool AcceptFriends { get; set; } = false;
    public int AcceptFriendsMin { get; set; } = 1;
    public int AcceptFriendsMax { get; set; } = 2;

    public bool DeleteFriendRequest { get; set; } = false;
    public int DeleteFriendRequestMin { get; set; } = 2;
    public int DeleteFriendRequestMax { get; set; } = 2;

    // --- Posting Options ---
    public bool IsPostReel { get; set; } = false;
    public bool IsDeleteReelFile { get; set; } = true;
    public bool IsBackupReelFile { get; set; } = false;

    public bool IsPostPicture { get; set; } = false;
    public bool IsDeletePictureFile { get; set; } = false;
    public bool IsBackupPictureFile { get; set; } = false;

    public bool IsPostVideo { get; set; } = false;
    public bool IsDeleteVideoFile { get; set; } = false;
    public bool IsBackupVideoFile { get; set; } = false;

    // --- Delays & Timing (in seconds) ---
    public int DelayUpload { get; set; } = 60;
    public int DelayOpenApp { get; set; } = 10;
    public int DelayAfterOpenLD { get; set; } = 20;
    public int DelayOpenEachLD { get; set; } = 15;
    public int DelayBeforeCloseLD { get; set; } = 15;
    public int DelayBeforeSwitchPage { get; set; } = 15;

    // --- Behavior Flags ---
    public bool CloseLDAfterAction { get; set; } = true;
    public bool ClearCacheFBBeforeOpen { get; set; } = false;
    public bool AfterPostCheckCopyRight { get; set; } = true;
    public bool SwitchToProfileAfterPost { get; set; } = false;
    public bool SleepPcAfterActionDone { get; set; } = false;

    // --- LDPlayer Environment Configuration ---
    public string LdFolder { get; set; } = @"C:\LDPlayer\LDPlayer9";
    public int RunThread { get; set; } = 8;
    public bool AutoSortLDPlayer { get; set; } = true;
    public bool ShowOnlyOpen { get; set; } = false;
}

/// <summary>
/// Persistent storage manager for AutoPost configuration state in %AppData%/855Media/autopost_config.json
/// </summary>
public static class AutoPostConfigStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string GetDefaultFilePath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media"
        );
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "autopost_config.json");
    }

    public static async Task SaveConfigAsync(AutoPostConfig config, string? customFilePath = null)
    {
        var filePath = customFilePath ?? GetDefaultFilePath();
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static async Task<AutoPostConfig> LoadConfigAsync(string? customFilePath = null)
    {
        var filePath = customFilePath ?? GetDefaultFilePath();
        if (!File.Exists(filePath))
        {
            return new AutoPostConfig();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var result = JsonSerializer.Deserialize<AutoPostConfig>(json, JsonOptions);
            return result ?? new AutoPostConfig();
        }
        catch
        {
            return new AutoPostConfig();
        }
    }
}
