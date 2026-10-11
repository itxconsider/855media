using System;
using System.IO;
using System.Linq;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Components;

public partial class FacebookPageTargetViewModel : ObservableObject
{
    private bool _initializing = true;
    public FacebookPageTarget Model { get; }
    public string AccessStatus =>
        IsProfile
            ? ""
            : Model.LivePageAccessVerified switch
            {
                true => "Access verified",
                false => "Access not verified · posting blocked",
                _ => "Cached access · confirm before posting",
            };

    public void RefreshAccessStatus() => OnPropertyChanged(nameof(AccessStatus));

    [ObservableProperty]
    private bool _isSelected = true;

    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        if (Model != null)
        {
            Model.IsSelected = value;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _appClone = "FB-01";

    [ObservableProperty]
    private string _pageId = string.Empty;

    [ObservableProperty]
    private string _personaUid = string.Empty;

    [ObservableProperty]
    private string _userUid = string.Empty;

    [ObservableProperty]
    private string _pageName = string.Empty;

    [ObservableProperty]
    private string _type = "Page";

    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsProfile));
        OnPropertyChanged(nameof(IsPage));
        if (Model != null)
        {
            Model.Type = value;
        }
    }

    [ObservableProperty]
    private int _reelsCount;

    partial void OnReelsCountChanged(int value)
    {
        if (Model != null)
            Model.ReelsCount = value;
        if (value > 0)
            ReelsCountKnown = true;
        OnPropertyChanged(nameof(ReelsCountDisplay));
    }

    [ObservableProperty]
    private int _videosCount;

    partial void OnVideosCountChanged(int value)
    {
        if (Model != null)
            Model.VideosCount = value;
        if (value > 0)
            VideosCountKnown = true;
        OnPropertyChanged(nameof(VideosCountDisplay));
    }

    [ObservableProperty]
    private int _pictureCount;

    partial void OnPictureCountChanged(int value)
    {
        if (Model != null)
            Model.PictureCount = value;
        if (value > 0)
            PictureCountKnown = true;
        OnPropertyChanged(nameof(PictureCountDisplay));
    }

    [ObservableProperty]
    private string _fileInGroup = string.Empty;

    [ObservableProperty]
    private int _followers;

    partial void OnFollowersChanged(int value)
    {
        if (Model != null)
            Model.Followers = value;
        if (value > 0)
            FollowersKnown = true;
        OnPropertyChanged(nameof(FollowersDisplay));
    }

    [ObservableProperty]
    private bool _followersKnown;

    partial void OnFollowersKnownChanged(bool value)
    {
        if (Model != null)
            Model.FollowersKnown = value;
        OnPropertyChanged(nameof(FollowersDisplay));
    }

    [ObservableProperty]
    private bool _reelsCountKnown;

    partial void OnReelsCountKnownChanged(bool value)
    {
        if (Model != null)
            Model.ReelsCountKnown = value;
        OnPropertyChanged(nameof(ReelsCountDisplay));
    }

    [ObservableProperty]
    private bool _videosCountKnown;

    partial void OnVideosCountKnownChanged(bool value)
    {
        if (Model != null)
            Model.VideosCountKnown = value;
        OnPropertyChanged(nameof(VideosCountDisplay));
    }

    [ObservableProperty]
    private bool _pictureCountKnown;

    partial void OnPictureCountKnownChanged(bool value)
    {
        if (Model != null)
            Model.PictureCountKnown = value;
        OnPropertyChanged(nameof(PictureCountDisplay));
    }

    public string FollowersDisplay => FollowersKnown ? Followers.ToString() : "—";
    public string ReelsCountDisplay => ReelsCountKnown ? ReelsCount.ToString() : "—";
    public string VideosCountDisplay => VideosCountKnown ? VideosCount.ToString() : "—";
    public string PictureCountDisplay => PictureCountKnown ? PictureCount.ToString() : "—";

    [ObservableProperty]
    private string _reach = string.Empty;

    [ObservableProperty]
    private string _views = string.Empty;

    public string ViewsDisplay => string.IsNullOrWhiteSpace(Views) ? "—" : Views;

    partial void OnViewsChanged(string value)
    {
        if (Model != null)
            Model.Views = value;
        OnPropertyChanged(nameof(ViewsDisplay));
    }

    [ObservableProperty]
    private string _pageInvite = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _birthdate = string.Empty;

    public string BirthdateDisplay => FacebookDateDisplay.Format(Birthdate);
    public string CreationDateDisplay => FacebookDateDisplay.Format(CreationDate);

    partial void OnBirthdateChanged(string value) => OnPropertyChanged(nameof(BirthdateDisplay));

    [ObservableProperty]
    private string _creationDate = string.Empty;

    partial void OnCreationDateChanged(string value)
    {
        OnPropertyChanged(nameof(CreationDateDisplay));
        if (Model != null)
            Model.CreationDate = value;
        RefreshMaturityStatus();
    }

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _hometown = string.Empty;

    [ObservableProperty]
    private string _work = string.Empty;

    [ObservableProperty]
    private string _education = string.Empty;

    [ObservableProperty]
    private string _relationshipStatus = string.Empty;

    [ObservableProperty]
    private string _notifications = string.Empty;

    [ObservableProperty]
    private string _managedBy = string.Empty;

    public bool HasNotifications => !string.IsNullOrWhiteSpace(Notifications);
    public bool IsProfile => string.Equals(Type, "Profile", StringComparison.OrdinalIgnoreCase);
    public bool IsPage => !IsProfile;

    // Page Maturity & Daily Limit Tracking
    public PageMaturityLevel Maturity => Model?.GetPageMaturity() ?? PageMaturityLevel.Unknown;
    public string MaturityName => Maturity.ToString();
    public int MaxDailyPosts => Model?.GetMaxDailyPostsLimit() ?? 3;
    public int PostsToday => Model?.GetPostsToday() ?? 0;
    public string DailyPostStatus => $"{PostsToday}/{MaxDailyPosts}";
    public int UploadReelsCount => Model?.UploadReelsCount ?? 0;
    public int UploadVideosCount => Model?.UploadVideosCount ?? 0;
    public int UploadPictureCount => Model?.UploadPictureCount ?? 0;

    [ObservableProperty]
    private int _customMaxDailyPosts;

    partial void OnCustomMaxDailyPostsChanged(int value)
    {
        if (Model != null)
        {
            Model.CustomMaxDailyPosts = value;
        }
        OnPropertyChanged(nameof(MaxDailyPosts));
        OnPropertyChanged(nameof(DailyPostStatus));
    }

    public string ReelPathStatus =>
        PageMediaPathEdit.Describe(VideoReelPath, [".mp4", ".mov", ".webm"]);
    public string ImagePathStatus =>
        PageMediaPathEdit.Describe(ImagePath, [".jpg", ".jpeg", ".png", ".webp"]);
    public string VideoPathStatus =>
        PageMediaPathEdit.Describe(VideoLongPath, [".mp4", ".mkv", ".mov"]);

    public string GroupPicturePathStatus =>
        PageMediaPathEdit.Describe(GroupPicturePath, [".jpg", ".jpeg", ".png", ".webp"], true);
    public string GroupReelPathStatus =>
        PageMediaPathEdit.Describe(GroupReelPath, [".mp4", ".mov", ".webm"], true);
    public string GroupVideoPathStatus =>
        PageMediaPathEdit.Describe(GroupVideoPath, [".mp4", ".mkv", ".mov"], true);

    // Per-page Config
    [ObservableProperty]
    private bool _useTitleFromTextFile;

    [ObservableProperty]
    private bool _useCaptionFromFilename;

    [ObservableProperty]
    private string _titleVideoReel = string.Empty;

    [ObservableProperty]
    private string _titleVideoReelMode = "Replace All";

    [ObservableProperty]
    private string _videoReelPath = string.Empty;

    partial void OnVideoReelPathChanged(string value)
    {
        RefreshMediaCounts();
        OnPropertyChanged(nameof(ReelPathStatus));
    }

    [ObservableProperty]
    private string _titleImage = string.Empty;

    [ObservableProperty]
    private string _titleImageMode = "Replace All";

    [ObservableProperty]
    private string _imagePath = string.Empty;

    partial void OnImagePathChanged(string value)
    {
        RefreshMediaCounts();
        OnPropertyChanged(nameof(ImagePathStatus));
    }

    [ObservableProperty]
    private string _titleVideoLong = string.Empty;

    [ObservableProperty]
    private string _titleVideoLongMode = "Replace All";

    [ObservableProperty]
    private string _videoLongPath = string.Empty;

    partial void OnVideoLongPathChanged(string value)
    {
        RefreshMediaCounts();
        OnPropertyChanged(nameof(VideoPathStatus));
    }

    [ObservableProperty]
    private string _pageNote = string.Empty;

    [ObservableProperty]
    private string _groupPostTitle = string.Empty;

    [ObservableProperty]
    private string _groupPicturePath = string.Empty;

    partial void OnGroupPicturePathChanged(string value)
    {
        RefreshMediaCounts();
        OnPropertyChanged(nameof(GroupPicturePathStatus));
    }

    [ObservableProperty]
    private string _groupReelPath = string.Empty;

    partial void OnGroupReelPathChanged(string value)
    {
        RefreshMediaCounts();
        OnPropertyChanged(nameof(GroupReelPathStatus));
    }

    [ObservableProperty]
    private string _groupVideoPath = string.Empty;

    partial void OnGroupVideoPathChanged(string value)
    {
        RefreshMediaCounts();
        OnPropertyChanged(nameof(GroupVideoPathStatus));
    }

    public FacebookPageTargetViewModel(FacebookPageTarget model, int number = 1)
    {
        Model = model;
        Number = number;

        IsSelected = model.IsSelected;
        DeviceName = model.DeviceName;
        AppClone = model.AppClone;
        PageId = model.PageId;
        PersonaUid = !string.IsNullOrWhiteSpace(model.PersonaUid)
            ? model.PersonaUid
            : (
                string.Equals(model.Type, "Profile", StringComparison.OrdinalIgnoreCase)
                    ? model.UserUid
                    : model.PageId
            );
        UserUid = model.UserUid;
        PageName = model.PageName;
        Type = model.Type;
        ReelsCount = model.ReelsCount;
        VideosCount = model.VideosCount;
        PictureCount = model.PictureCount;
        FileInGroup = model.FileInGroup;
        Followers = model.Followers;
        FollowersKnown = model.FollowersKnown || model.Followers > 0;
        ReelsCountKnown = model.ReelsCountKnown || model.ReelsCount > 0;
        VideosCountKnown = model.VideosCountKnown || model.VideosCount > 0;
        PictureCountKnown = model.PictureCountKnown || model.PictureCount > 0;
        Reach = model.Reach;
        Views = model.Views;
        PageInvite = model.PageInvite;
        Email = model.Email;
        Birthdate = model.Birthdate;
        CreationDate = model.CreationDate;
        Location = model.Location;
        Hometown = model.Hometown;
        Work = model.Work;
        Education = model.Education;
        RelationshipStatus = model.RelationshipStatus;
        Notifications = model.Notifications;
        ManagedBy = model.ManagedBy;

        UseTitleFromTextFile = model.UseTitleFromTextFile;
        UseCaptionFromFilename = model.UseCaptionFromFilename;
        TitleVideoReel = model.TitleVideoReel;
        TitleVideoReelMode = model.TitleVideoReelMode;
        VideoReelPath = model.VideoReelPath;

        TitleImage = model.TitleImage;
        TitleImageMode = model.TitleImageMode;
        ImagePath = model.ImagePath;

        TitleVideoLong = model.TitleVideoLong;
        TitleVideoLongMode = model.TitleVideoLongMode;
        VideoLongPath = model.VideoLongPath;

        PageNote = model.PageNote;

        GroupPostTitle = model.GroupPostTitle;
        GroupPicturePath = model.GroupPicturePath;
        GroupReelPath = model.GroupReelPath;
        GroupVideoPath = model.GroupVideoPath;
        CustomMaxDailyPosts = model.CustomMaxDailyPosts;

        _initializing = false;
        RefreshMediaCounts();
    }

    public void RefreshMediaCounts()
    {
        if (_initializing)
            return;
        if (Model != null)
        {
            Model.VideoReelPath = VideoReelPath;
            Model.VideoLongPath = VideoLongPath;
            Model.ImagePath = ImagePath;
            Model.GroupReelPath = GroupReelPath;
            Model.GroupPicturePath = GroupPicturePath;
            Model.GroupVideoPath = GroupVideoPath;

            Model.RefreshMediaCounts();

            FileInGroup = Model.FileInGroup;
        }
        else
        {
            int groupCount =
                FacebookPageTarget.CountMediaFiles(GroupReelPath, [".mp4", ".mov", ".webm"])
                + FacebookPageTarget.CountMediaFiles(
                    GroupPicturePath,
                    [".jpg", ".jpeg", ".png", ".webp"]
                )
                + FacebookPageTarget.CountMediaFiles(GroupVideoPath, [".mp4", ".mkv", ".mov"]);

            FileInGroup = groupCount.ToString();
        }

        OnPropertyChanged(nameof(UploadReelsCount));
        OnPropertyChanged(nameof(UploadVideosCount));
        OnPropertyChanged(nameof(UploadPictureCount));
        RefreshMaturityStatus();
    }

    public int GetPostsToday() => Model?.GetPostsToday() ?? 0;

    public void RecordPostToday()
    {
        Model?.RecordPostToday();
        RefreshMaturityStatus();
    }

    public PageMaturityLevel GetPageMaturity() =>
        Model?.GetPageMaturity() ?? PageMaturityLevel.Unknown;

    public int GetMaxDailyPostsLimit() => Model?.GetMaxDailyPostsLimit() ?? 3;

    public bool HasReachedDailyLimit(int pending = 0) =>
        Model?.HasReachedDailyLimit(pending) ?? false;

    public void RefreshMaturityStatus()
    {
        OnPropertyChanged(nameof(Maturity));
        OnPropertyChanged(nameof(MaturityName));
        OnPropertyChanged(nameof(MaxDailyPosts));
        OnPropertyChanged(nameof(PostsToday));
        OnPropertyChanged(nameof(DailyPostStatus));
    }

    public void SyncToModel()
    {
        Model.IsSelected = IsSelected;
        Model.DeviceName = DeviceName;
        Model.AppClone = AppClone;
        Model.PageId = PageId;
        Model.PersonaUid = PersonaUid;
        Model.UserUid = UserUid;
        Model.PageName = PageName;
        Model.Type = Type;
        Model.ReelsCount = ReelsCount;
        Model.VideosCount = VideosCount;
        Model.PictureCount = PictureCount;
        Model.FileInGroup = FileInGroup;
        Model.Followers = Followers;
        Model.FollowersKnown = FollowersKnown;
        Model.ReelsCountKnown = ReelsCountKnown;
        Model.VideosCountKnown = VideosCountKnown;
        Model.PictureCountKnown = PictureCountKnown;
        Model.Reach = Reach;
        Model.Views = Views;
        Model.PageInvite = PageInvite;
        Model.Email = Email;
        Model.Birthdate = Birthdate;
        Model.CreationDate = CreationDate;
        Model.Location = Location;
        Model.Hometown = Hometown;
        Model.Work = Work;
        Model.Education = Education;
        Model.RelationshipStatus = RelationshipStatus;
        Model.Notifications = Notifications;
        Model.ManagedBy = ManagedBy;

        Model.UseTitleFromTextFile = UseTitleFromTextFile;
        Model.UseCaptionFromFilename = UseCaptionFromFilename;
        Model.TitleVideoReel = TitleVideoReel;
        Model.TitleVideoReelMode = TitleVideoReelMode;
        Model.VideoReelPath = VideoReelPath;

        Model.TitleImage = TitleImage;
        Model.TitleImageMode = TitleImageMode;
        Model.ImagePath = ImagePath;

        Model.TitleVideoLong = TitleVideoLong;
        Model.TitleVideoLongMode = TitleVideoLongMode;
        Model.VideoLongPath = VideoLongPath;

        Model.PageNote = PageNote;

        Model.GroupPostTitle = GroupPostTitle;
        Model.GroupPicturePath = GroupPicturePath;
        Model.GroupReelPath = GroupReelPath;
        Model.GroupVideoPath = GroupVideoPath;
        Model.CustomMaxDailyPosts = CustomMaxDailyPosts;
    }
}
