using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

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

public class AutoPostTaskItem
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string App { get; set; } = "FB-01";
    public string Collaborator { get; set; } = string.Empty;
    public string Type { get; set; } = "Reel";
    public string ShareToGroup { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
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
            PageId = !string.IsNullOrWhiteSpace(PageId) ? PageId : Uid,
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
            PageId = !string.IsNullOrWhiteSpace(PageId) ? PageId : Uid,
            PersonaUid = !string.IsNullOrWhiteSpace(Uid) ? Uid : PageId,
            UserUid = !string.IsNullOrWhiteSpace(UserUid) ? UserUid : Uid,
            PageName = Name,
            Type = Type,
            Email = Email,
            IsActiveProfile = IsActive,
        };
    }
}
