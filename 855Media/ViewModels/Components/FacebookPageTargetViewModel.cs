using System;
using System.IO;
using System.Linq;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Components;

public partial class FacebookPageTargetViewModel : ObservableObject
{
    public FacebookPageTarget Model { get; }

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

    [ObservableProperty]
    private int _videosCount;

    [ObservableProperty]
    private int _pictureCount;

    [ObservableProperty]
    private string _fileInGroup = string.Empty;

    [ObservableProperty]
    private int _followers;

    [ObservableProperty]
    private string _reach = string.Empty;

    [ObservableProperty]
    private string _pageInvite = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _birthdate = string.Empty;

    [ObservableProperty]
    private string _creationDate = string.Empty;

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

    // Per-page Config
    [ObservableProperty]
    private bool _useTitleFromTextFile;

    [ObservableProperty]
    private string _titleVideoReel = string.Empty;

    [ObservableProperty]
    private string _titleVideoReelMode = "Replace All";

    [ObservableProperty]
    private string _videoReelPath = string.Empty;

    partial void OnVideoReelPathChanged(string value) => RefreshMediaCounts();

    [ObservableProperty]
    private string _titleImage = string.Empty;

    [ObservableProperty]
    private string _titleImageMode = "Replace All";

    [ObservableProperty]
    private string _imagePath = string.Empty;

    partial void OnImagePathChanged(string value) => RefreshMediaCounts();

    [ObservableProperty]
    private string _titleVideoLong = string.Empty;

    [ObservableProperty]
    private string _titleVideoLongMode = "Replace All";

    [ObservableProperty]
    private string _videoLongPath = string.Empty;

    partial void OnVideoLongPathChanged(string value) => RefreshMediaCounts();

    [ObservableProperty]
    private string _pageNote = string.Empty;

    [ObservableProperty]
    private string _groupPostTitle = string.Empty;

    [ObservableProperty]
    private string _groupPicturePath = string.Empty;

    partial void OnGroupPicturePathChanged(string value) => RefreshMediaCounts();

    [ObservableProperty]
    private string _groupReelPath = string.Empty;

    partial void OnGroupReelPathChanged(string value) => RefreshMediaCounts();

    [ObservableProperty]
    private string _groupVideoPath = string.Empty;

    partial void OnGroupVideoPathChanged(string value) => RefreshMediaCounts();

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
        Reach = model.Reach;
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

        RefreshMediaCounts();
    }

    public void RefreshMediaCounts()
    {
        if (Model != null)
        {
            Model.VideoReelPath = VideoReelPath;
            Model.VideoLongPath = VideoLongPath;
            Model.ImagePath = ImagePath;
            Model.GroupReelPath = GroupReelPath;
            Model.GroupPicturePath = GroupPicturePath;
            Model.GroupVideoPath = GroupVideoPath;

            Model.RefreshMediaCounts();

            ReelsCount = Model.ReelsCount;
            VideosCount = Model.VideosCount;
            PictureCount = Model.PictureCount;
            FileInGroup = Model.FileInGroup;
        }
        else
        {
            ReelsCount = FacebookPageTarget.CountMediaFiles(
                VideoReelPath,
                [".mp4", ".mov", ".webm"]
            );
            VideosCount = FacebookPageTarget.CountMediaFiles(
                VideoLongPath,
                [".mp4", ".mkv", ".mov"]
            );
            PictureCount = FacebookPageTarget.CountMediaFiles(
                ImagePath,
                [".jpg", ".jpeg", ".png", ".webp"]
            );

            int groupCount =
                FacebookPageTarget.CountMediaFiles(GroupReelPath, [".mp4", ".mov", ".webm"])
                + FacebookPageTarget.CountMediaFiles(
                    GroupPicturePath,
                    [".jpg", ".jpeg", ".png", ".webp"]
                )
                + FacebookPageTarget.CountMediaFiles(GroupVideoPath, [".mp4", ".mkv", ".mov"]);

            FileInGroup = groupCount.ToString();
        }
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
        Model.Reach = Reach;
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
    }
}
