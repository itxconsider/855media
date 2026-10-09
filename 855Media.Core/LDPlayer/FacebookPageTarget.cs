using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public enum PageMaturityLevel
{
    New,
    Growing,
    Mature,
}

public class FacebookPageTarget
{
    public bool IsSelected { get; set; } = true;
    public string DeviceName { get; set; } = string.Empty;
    public string AppClone { get; set; } = "FB-01";
    public string PageId { get; set; } = string.Empty;
    public string PersonaUid { get; set; } = string.Empty;
    public string UserUid { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string Type { get; set; } = "Page"; // "Profile" or "Page"

    public int ReelsCount { get; set; }
    public int VideosCount { get; set; }
    public int PictureCount { get; set; }
    public string FileInGroup { get; set; } = string.Empty;
    public int Followers { get; set; }
    public string Reach { get; set; } = string.Empty;
    public string PageInvite { get; set; } = string.Empty;

    // Account Details
    public string Email { get; set; } = string.Empty;
    public string Birthdate { get; set; } = string.Empty;
    public string CreationDate { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Hometown { get; set; } = string.Empty;
    public string Work { get; set; } = string.Empty;
    public string Education { get; set; } = string.Empty;
    public string RelationshipStatus { get; set; } = string.Empty;
    public string Notifications { get; set; } = string.Empty;
    public string ManagedBy { get; set; } = string.Empty;
    public bool IsPrimaryProfile =>
        string.Equals(Type, "Profile", StringComparison.OrdinalIgnoreCase);

    // Per-page Media Config
    public bool UseTitleFromTextFile { get; set; }
    public string TitleVideoReel { get; set; } = string.Empty;
    public string TitleVideoReelMode { get; set; } = "Replace All";
    public string VideoReelPath { get; set; } = string.Empty;

    public string TitleImage { get; set; } = string.Empty;
    public string TitleImageMode { get; set; } = "Replace All";
    public string ImagePath { get; set; } = string.Empty;

    public string TitleVideoLong { get; set; } = string.Empty;
    public string TitleVideoLongMode { get; set; } = "Replace All";
    public string VideoLongPath { get; set; } = string.Empty;

    public string PageNote { get; set; } = string.Empty;

    // Post in Group
    public string GroupPostTitle { get; set; } = string.Empty;
    public string GroupPicturePath { get; set; } = string.Empty;
    public string GroupReelPath { get; set; } = string.Empty;
    public string GroupVideoPath { get; set; } = string.Empty;

    // Page Maturity & Daily Limit Tracking
    public int PostsTodayCount { get; set; } = 0;
    public string LastPostDate { get; set; } = string.Empty;
    public int CustomMaxDailyPosts { get; set; } = 0; // 0 = automatic by maturity

    public int GetPostsToday()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!string.Equals(LastPostDate, today, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        return PostsTodayCount;
    }

    public void RecordPostToday()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!string.Equals(LastPostDate, today, StringComparison.OrdinalIgnoreCase))
        {
            LastPostDate = today;
            PostsTodayCount = 1;
        }
        else
        {
            PostsTodayCount++;
        }
    }

    public static DateTime? TryParseCreationDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        var clean = dateStr.Trim();
        if (
            clean.Equals("Not Set", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("Not Discovered", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        // 1. Direct standard parse (e.g. "January 15, 2024", "2024-05-10", "10/15/2023")
        if (DateTime.TryParse(clean, System.Globalization.CultureInfo.InvariantCulture, out var dt))
        {
            return dt;
        }

        // Full Month Day, Year with or without prefix (e.g. "Account created: January 15, 2024", "Joined on March 5, 2021")
        var fullDateMatch = System.Text.RegularExpressions.Regex.Match(
            clean,
            @"(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (
            fullDateMatch.Success
            && DateTime.TryParse(
                fullDateMatch.Value,
                System.Globalization.CultureInfo.InvariantCulture,
                out var fdt
            )
        )
        {
            return fdt;
        }

        // 2. Month + Year with or without prefix (e.g. "Joined Facebook in October 2021", "Member since July 2023", "Joined October 2021")
        var monthYearMatch = System.Text.RegularExpressions.Regex.Match(
            clean,
            @"(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (monthYearMatch.Success)
        {
            if (
                DateTime.TryParseExact(
                    monthYearMatch.Value,
                    ["MMMM yyyy", "MMM yyyy"],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var myDt
                )
            )
            {
                return myDt;
            }
        }

        // Short month format (e.g. "Oct 2021", "Jan 2024")
        var shortMonthYearMatch = System.Text.RegularExpressions.Regex.Match(
            clean,
            @"(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (shortMonthYearMatch.Success)
        {
            if (
                DateTime.TryParseExact(
                    shortMonthYearMatch.Value,
                    "MMM yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var smyDt
                )
            )
            {
                return smyDt;
            }
        }

        // 3. Just Year (e.g. "Joined Facebook in 2020", "Joined 2022", "Account created: 2021")
        var yearMatch = System.Text.RegularExpressions.Regex.Match(clean, @"\b(19\d{2}|20\d{2})\b");
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
        {
            return new DateTime(year, 1, 1);
        }

        return null;
    }

    public PageMaturityLevel GetPageMaturity()
    {
        var created = TryParseCreationDate(CreationDate);
        if (created.HasValue)
        {
            var ageDays = (DateTime.Now - created.Value).TotalDays;
            if (ageDays >= 90)
                return PageMaturityLevel.Mature;
            if (ageDays >= 30)
                return PageMaturityLevel.Growing;
            return PageMaturityLevel.New;
        }

        // Fallback based on content counts and followers
        int totalContent = ReelsCount + VideosCount + PictureCount;
        if (Followers >= 500 || totalContent >= 30)
        {
            return PageMaturityLevel.Mature;
        }
        if (Followers >= 50 || totalContent >= 10)
        {
            return PageMaturityLevel.Growing;
        }

        return PageMaturityLevel.New;
    }

    public static int GetMaturityDailyLimit(PageMaturityLevel maturity) =>
        maturity switch
        {
            PageMaturityLevel.New => 3,
            PageMaturityLevel.Growing => 5,
            PageMaturityLevel.Mature => 10,
            _ => 3,
        };

    public int GetMaxDailyPostsLimit()
    {
        if (CustomMaxDailyPosts > 0)
        {
            return CustomMaxDailyPosts;
        }
        return GetMaturityDailyLimit(GetPageMaturity());
    }

    public bool HasReachedDailyLimit(int additionalPendingPosts = 0)
    {
        return (GetPostsToday() + additionalPendingPosts) >= GetMaxDailyPostsLimit();
    }

    public void RefreshMediaCounts()
    {
        ReelsCount = CountMediaFiles(VideoReelPath, [".mp4", ".mov", ".webm"]);
        VideosCount = CountMediaFiles(VideoLongPath, [".mp4", ".mkv", ".mov"]);
        PictureCount = CountMediaFiles(ImagePath, [".jpg", ".jpeg", ".png", ".webp"]);

        int groupCount =
            CountMediaFiles(GroupReelPath, [".mp4", ".mov", ".webm"])
            + CountMediaFiles(GroupPicturePath, [".jpg", ".jpeg", ".png", ".webp"])
            + CountMediaFiles(GroupVideoPath, [".mp4", ".mkv", ".mov"]);

        FileInGroup = groupCount.ToString();
    }

    public static int CountMediaFiles(string? path, string[] validExtensions)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        var clean = path.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(clean))
            return 0;

        try
        {
            if (File.Exists(clean))
            {
                var ext = Path.GetExtension(clean);
                return validExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    ? 1
                    : 0;
            }

            if (Directory.Exists(clean))
            {
                return Directory
                    .EnumerateFiles(clean, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(f =>
                    {
                        var ext = Path.GetExtension(f);
                        return validExtensions.Any(e =>
                            e.Equals(ext, StringComparison.OrdinalIgnoreCase)
                        );
                    });
            }
        }
        catch
        {
            // Ignore path errors (e.g. drive unmounted or permissions)
        }

        return 0;
    }
}

public class AutoPostTaskItem : INotifyPropertyChanged
{
    private string _status = "Queued";
    private string _title = string.Empty;
    private string _filePath = string.Empty;

    public int Index { get; set; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged(nameof(FilePath));
            }
        }
    }

    public string DeviceName { get; set; } = string.Empty;
    public string App { get; set; } = "FB-01";
    public string Collaborator { get; set; } = string.Empty;
    public string Type { get; set; } = "Reel";
    public string ShareToGroup { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;
    public string PageId { get; set; } = string.Empty;
    public bool IsProfile { get; set; }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class FacebookPageInfo
{
    public string PageId { get; set; } = string.Empty;
    public string PersonaUid { get; set; } = string.Empty;
    public string UserUid { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string Type { get; set; } = "Page";
    public int Followers { get; set; }
    public string Reach { get; set; } = string.Empty;
    public int ReelsCount { get; set; }
    public int VideosCount { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Birthdate { get; set; } = string.Empty;
    public string CreationDate { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Hometown { get; set; } = string.Empty;
    public string Work { get; set; } = string.Empty;
    public string Education { get; set; } = string.Empty;
    public string RelationshipStatus { get; set; } = string.Empty;
    public string Notifications { get; set; } = string.Empty;
    public string ManagedBy { get; set; } = string.Empty;
    public bool IsActiveProfile { get; set; }
}

public class FacebookDeviceAccountInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public string UserUid { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public FacebookPageInfo UserProfile { get; set; } = new();
    public List<FacebookPageInfo> Pages { get; set; } = [];
}

public class FacebookAccountDetails
{
    public string DeviceName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CreationDate { get; set; } = string.Empty;
    public string Birthdate { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Hometown { get; set; } = string.Empty;
    public string Work { get; set; } = string.Empty;
    public string Education { get; set; } = string.Empty;
    public string RelationshipStatus { get; set; } = string.Empty;
    public bool SwitchedToProfile { get; set; }
    public List<FacebookPageInfo> Pages { get; set; } = [];
}

/// <summary>
/// Account or Page information extracted directly from rooted device storage
/// (/data/data/com.facebook.katana/) via root shell (LightSharedPreferences, prefs_db, cache).
/// </summary>
public class FacebookAccountInfo
{
    public string Uid { get; set; } = string.Empty;
    public string PageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Page"; // "Profile" or "Page"
    public string UserUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PicUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string Cookies { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public FacebookPageTarget ToFacebookPageTarget(string deviceName, string appClone = "FB-01")
    {
        return new FacebookPageTarget
        {
            DeviceName = deviceName,
            AppClone = appClone,
            PageId = this.PageId,
            PersonaUid = !string.IsNullOrWhiteSpace(Uid) ? Uid : PageId,
            UserUid = !string.IsNullOrWhiteSpace(UserUid) ? UserUid : Uid,
            PageName = Name,
            Type = Type,
            Email = Email,
            ManagedBy = string.Equals(Type, "Page", StringComparison.OrdinalIgnoreCase)
                ? UserUid
                : string.Empty,
        };
    }

    public FacebookPageInfo ToFacebookPageInfo()
    {
        return new FacebookPageInfo
        {
            PageId = this.PageId,
            PersonaUid = !string.IsNullOrWhiteSpace(Uid) ? Uid : PageId,
            UserUid = !string.IsNullOrWhiteSpace(UserUid) ? UserUid : Uid,
            PageName = Name,
            Type = Type,
            Email = Email,
            IsActiveProfile = IsActive,
        };
    }
}
