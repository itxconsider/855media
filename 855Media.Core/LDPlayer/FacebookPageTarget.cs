using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public sealed class RootShellUnavailableException : InvalidOperationException
{
    public RootShellUnavailableException(string message)
        : base(message) { }
}

public enum PageMaturityLevel
{
    New,
    Growing,
    Mature,
    Unknown,
}

public partial class FacebookPageTarget
{
    public string ProfilePictureUrl { get; set; } = string.Empty;
    public PFlowAccountBinding? AccountBinding { get; set; }
    public bool IsSelected { get; set; } = true;

    // Root cache is a candidate only. False blocks posting until a live Page switch succeeds.
    public bool? LivePageAccessVerified { get; set; }
    public string PageAccessIssue { get; set; } = "";

    public string DeviceName { get; set; } = string.Empty;
    public string AppClone { get; set; } = "FB-01";
    public string PageId { get; set; } = string.Empty;
    public string PersonaUid { get; set; } = string.Empty;
    public string UserUid { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;

    // A UID-verified display label survives root-cache refreshes; this is not access authorization.
    public string VerifiedDisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = "Page"; // "Profile" or "Page"

    public int ReelsCount { get; set; }
    public int VideosCount { get; set; }
    public int PictureCount { get; set; }
    public string FileInGroup { get; set; } = string.Empty;
    public int Followers { get; set; }
    public bool FollowersKnown { get; set; }
    public bool ReelsCountKnown { get; set; }
    public bool VideosCountKnown { get; set; }
    public bool PictureCountKnown { get; set; }
    public string Reach { get; set; } = string.Empty;
    public string Views { get; set; } = string.Empty;
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
    public bool UseCaptionFromFilename { get; set; }

    // Nullable values migrate older configurations using the former shared option.
    public bool KeepReelPartsTogether { get; set; }
    public System.Collections.Generic.List<ReelPartGroup> PendingReelPartGroups { get; set; } = [];
    public bool? ReelTitleFromTextFile { get; set; }
    public bool? PhotoTitleFromTextFile { get; set; }
    public bool? VideoTitleFromTextFile { get; set; }
    public string TitleVideoReel { get; set; } = string.Empty;
    public string TitleVideoReelMode { get; set; } = "Replace All";
    public string VideoReelPath { get; set; } = string.Empty;

    public string TitleImage { get; set; } = string.Empty;
    public string TitleImageMode { get; set; } = "Replace All";
    public string ImagePath { get; set; } = string.Empty;
    public string StoryPath { get; set; } = string.Empty;

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

    // Facebook totals and files waiting to be uploaded are separate values.
    // Local folder contents must not change the discovered account statistics.
    [System.Text.Json.Serialization.JsonIgnore]
    public int UploadReelsCount { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int UploadVideosCount { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int UploadPictureCount { get; private set; }

    public FacebookPageTarget CreateSnapshot() => (FacebookPageTarget)MemberwiseClone();

    public bool MatchesIdentity(
        string deviceName,
        string appClone,
        string ownerUid,
        string personaUid,
        string pageId,
        bool isProfile,
        string? deviceAlias = null
    ) =>
        (
            DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase)
            || (
                !string.IsNullOrWhiteSpace(deviceAlias)
                && DeviceName.Equals(deviceAlias, StringComparison.OrdinalIgnoreCase)
            )
        )
        && AppClone.Equals(appClone, StringComparison.OrdinalIgnoreCase)
        && IsPrimaryProfile == isProfile
        && !string.IsNullOrWhiteSpace(ownerUid)
        && UserUid.Equals(ownerUid, StringComparison.Ordinal)
        && (
            (
                !string.IsNullOrWhiteSpace(personaUid)
                && PersonaUid.Equals(personaUid, StringComparison.Ordinal)
            )
            || (
                !string.IsNullOrWhiteSpace(pageId)
                && PageId.Equals(pageId, StringComparison.Ordinal)
            )
        );

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

    public PageMaturityLevel GetPageMaturity() => GetPageMaturity(DateTime.Today);

    internal PageMaturityLevel GetPageMaturity(DateTime today)
    {
        today = today.Date;
        var created = TryParseCreationDate(CreationDate);
        if (created.HasValue && created.Value.Date <= today)
        {
            var latestPossibleCreation = GetLatestPossibleCreationDate(CreationDate, created.Value);
            if (latestPossibleCreation > today)
                latestPossibleCreation = today;
            var ageDays = (today - latestPossibleCreation).TotalDays;
            if (ageDays >= 90)
                return PageMaturityLevel.Mature;
            if (ageDays >= 30)
                return PageMaturityLevel.Growing;
            return PageMaturityLevel.New;
        }

        // Neither follower totals nor files waiting to be uploaded establish
        // when Facebook created the account. Keep the age explicitly unknown.
        return PageMaturityLevel.Unknown;
    }

    private static DateTime GetLatestPossibleCreationDate(string text, DateTime parsed)
    {
        const string month =
            @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";
        var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        var hasExplicitDay = System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"\b"
                + month
                + @"\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}\b"
                + @"|\b\d{1,2}(?:st|nd|rd|th)?\s+"
                + month
                + @"\s+\d{4}\b"
                + @"|\b(?:\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{4})\b",
            options
        );
        if (hasExplicitDay)
            return parsed.Date;

        var hasMonthAndYear = System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"\b" + month + @"\s+\d{4}\b|\b(?:\d{1,2}[-/]\d{4}|\d{4}[-/]\d{1,2})\b",
            options
        );
        if (hasMonthAndYear)
            return new DateTime(
                parsed.Year,
                parsed.Month,
                DateTime.DaysInMonth(parsed.Year, parsed.Month)
            );

        // A displayed year establishes no month or day. Use the last possible
        // date so the daily limit never increases from an assumed January 1.
        return new DateTime(parsed.Year, 12, 31);
    }

    public static int GetMaturityDailyLimit(PageMaturityLevel maturity) =>
        maturity switch
        {
            PageMaturityLevel.New => 3,
            PageMaturityLevel.Growing => 5,
            PageMaturityLevel.Mature => 10,
            PageMaturityLevel.Unknown => 3,
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
        RefreshMediaSnapshot();
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
    public string QueueId { get; set; } = Guid.NewGuid().ToString("N");
    private string _pFlowJobId = "";
    public string PFlowJobId
    {
        get => _pFlowJobId;
        set
        {
            if (_pFlowJobId != value)
            {
                _pFlowJobId = value;
                OnPropertyChanged(nameof(PFlowJobId));
            }
        }
    }
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
    public string PageId { get; set; } = string.Empty;
    public bool IsProfile { get; set; }
    public string OwnerUid { get; set; } = string.Empty;
    public PFlowAccountBinding? AccountBinding { get; set; }
    public string PartGroupKey { get; set; } = "";
    public int PartNumber { get; set; }
    public string PartGroupDisplay =>
        PartGroupKey.Length == 0
            ? ""
            : $"{System.IO.Path.GetFileName(PartGroupKey)} · Part {PartNumber}";
    public string Page { get; set; } = string.Empty;

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
    public string ProfilePictureUrl { get; set; } = string.Empty;
    public bool? LivePageAccessVerified { get; set; }
    public string PageAccessIssue { get; set; } = "";
    public string PageId { get; set; } = string.Empty;
    public string PersonaUid { get; set; } = string.Empty;
    public string UserUid { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string Type { get; set; } = "Page";
    public int Followers { get; set; }
    public string Reach { get; set; } = string.Empty;
    public string Views { get; set; } = string.Empty;
    public int ReelsCount { get; set; }
    public int VideosCount { get; set; }
    public int PictureCount { get; set; }
    public bool FollowersKnown { get; set; }
    public bool ReelsCountKnown { get; set; }
    public bool VideosCountKnown { get; set; }
    public bool PictureCountKnown { get; set; }
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
    public string ProfilePictureUrl { get; set; } = string.Empty;
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

    // Root data can be saved even when UI identity selection fails.
    public bool IdentitySwitchFailed =>
        !SwitchedToProfile || Pages.Any(p => p.LivePageAccessVerified == false);
    public string FetchStatus =>
        IdentitySwitchFailed
            ? "Data fetched; identity switch failed. Review the selected LD."
            : "Account and Pages fetched; identity switches confirmed.";

    /// <summary>
    /// Retain stable Page identities for this same account when a fresh cache
    /// snapshot omits them. Each identity still requires the normal UI switch
    /// verification before any new Page details are accepted.
    /// </summary>
    public int RetainSavedPageIdentities(
        IEnumerable<FacebookPageTarget> savedRows,
        string deviceName,
        string appClone,
        string? deviceAlias = null
    )
    {
        if (!IsStableId(Uid))
            return 0;

        var retained = 0;
        foreach (var saved in savedRows)
        {
            var sameDevice =
                saved.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase)
                || (
                    !string.IsNullOrWhiteSpace(deviceAlias)
                    && saved.DeviceName.Equals(deviceAlias, StringComparison.OrdinalIgnoreCase)
                );
            if (
                !sameDevice
                || !saved.AppClone.Equals(appClone, StringComparison.OrdinalIgnoreCase)
                || saved.IsPrimaryProfile
                || !saved.UserUid.Equals(Uid, StringComparison.Ordinal)
                || !IsStableId(saved.PageId)
                || !IsStableId(saved.PersonaUid)
                || saved.PersonaUid.Equals(Uid, StringComparison.Ordinal)
                || Pages.Any(page =>
                    page.PageId.Equals(saved.PageId, StringComparison.Ordinal)
                    || page.PersonaUid.Equals(saved.PersonaUid, StringComparison.Ordinal)
                )
            )
                continue;

            // Only restore the identity. Old values remain on the existing
            // table model; they must not be counted as fresh UI discoveries.
            Pages.Add(
                new FacebookPageInfo
                {
                    PageId = saved.PageId,
                    PersonaUid = saved.PersonaUid,
                    UserUid = Uid,
                    PageName = saved.PageName,
                    ManagedBy = saved.ManagedBy,
                    Type = "Page",
                }
            );
            retained++;
        }
        return retained;
    }

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 5 and <= 32
        && value.All(character => character is >= '0' and <= '9')
        && value.Any(character => character != '0');
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
            PageId = !string.IsNullOrWhiteSpace(PageId) ? PageId : Uid,
            PersonaUid = !string.IsNullOrWhiteSpace(Uid) ? Uid : PageId,
            UserUid = !string.IsNullOrWhiteSpace(UserUid) ? UserUid : Uid,
            PageName = Name,
            Type = Type,
            Email = Email,
            ProfilePictureUrl = PicUrl,
            ManagedBy = string.Equals(Type, "Page", StringComparison.OrdinalIgnoreCase)
                ? UserUid
                : string.Empty,
        };
    }

    public FacebookPageInfo ToFacebookPageInfo()
    {
        return new FacebookPageInfo
        {
            PageId = !string.IsNullOrWhiteSpace(PageId) ? PageId : Uid,
            PersonaUid = !string.IsNullOrWhiteSpace(Uid) ? Uid : PageId,
            UserUid = !string.IsNullOrWhiteSpace(UserUid) ? UserUid : Uid,
            PageName = Name,
            Type = Type,
            Email = Email,
            ProfilePictureUrl = PicUrl,
            IsActiveProfile = IsActive,
        };
    }
}
