using System;
using System.Collections.Generic;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using _855Media.ViewModels;
using _855Media.ViewModels.Components;
using _855Media.ViewModels.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using PowerKit.Extensions;

namespace _855Media.Framework;

public class ViewModelManager(IServiceProvider services)
{
    private T Licensed<T>()
        where T : ViewModelBase
    {
        var viewModel = services.GetRequiredService<T>();
        viewModel.ModuleLicense =
            services.GetRequiredService<_855Media.Core.Licensing.ILicenseService>();
        return viewModel;
    }

    public MainViewModel GetMainViewModel() => services.GetRequiredService<MainViewModel>();

    public DashboardViewModel GetDashboardViewModel() => Licensed<DashboardViewModel>();

    public UnifiedDownloaderViewModel GetUnifiedDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = Licensed<UnifiedDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public YouTubeDownloaderViewModel GetYouTubeDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = Licensed<YouTubeDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public TikTokDownloaderViewModel GetTikTokDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = Licensed<TikTokDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public FacebookDownloaderViewModel GetFacebookDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = Licensed<FacebookDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public DramaBoxDownloaderViewModel GetDramaBoxDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = Licensed<DramaBoxDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public HistoryViewModel GetHistoryViewModel(DashboardViewModel dashboardViewModel)
    {
        var viewModel = Licensed<HistoryViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public VideoUpscalerViewModel GetVideoUpscalerViewModel() => Licensed<VideoUpscalerViewModel>();

    public DubbingViewModel GetDubbingViewModel() => Licensed<DubbingViewModel>();

    public AutoPostViewModel GetAutoPostViewModel() => Licensed<AutoPostViewModel>();

    public AuthSetupViewModel GetAuthSetupViewModel() =>
        services.GetRequiredService<AuthSetupViewModel>();

    public DownloadViewModel GetDownloadViewModel(
        VideoInfo video,
        VideoDownloadOption downloadOption,
        string filePath,
        bool translateCaptionsToEnglish = false,
        bool translateTitleToEnglish = false
    )
    {
        var viewModel = Licensed<DownloadViewModel>();

        viewModel.Video = video;
        viewModel.DownloadOption = downloadOption;
        viewModel.FilePath = filePath;
        viewModel.TranslateCaptionsToEnglish = translateCaptionsToEnglish;
        viewModel.TranslateTitleToEnglish = translateTitleToEnglish;

        return viewModel;
    }

    public DownloadViewModel GetDownloadViewModel(
        VideoInfo video,
        VideoDownloadPreference downloadPreference,
        string filePath
    )
    {
        var viewModel = Licensed<DownloadViewModel>();

        viewModel.Video = video;
        viewModel.DownloadPreference = downloadPreference;
        viewModel.FilePath = filePath;
        viewModel.TranslateCaptionsToEnglish = downloadPreference.TranslateCaptionsToEnglish;
        viewModel.TranslateTitleToEnglish = downloadPreference.TranslateTitleToEnglish;

        return viewModel;
    }

    public DownloadMultipleSetupViewModel GetDownloadMultipleSetupViewModel(
        string title,
        IReadOnlyList<VideoInfo> availableVideos,
        bool preselectVideos = true,
        string? profilePictureUrl = null,
        string? authorName = null
    )
    {
        var viewModel = services.GetRequiredService<DownloadMultipleSetupViewModel>();

        viewModel.Title = title;
        viewModel.AvailableVideos = availableVideos;
        viewModel.ProfilePictureUrl = profilePictureUrl;
        viewModel.AuthorName = authorName;

        if (!preselectVideos)
            viewModel.SelectedVideos.Clear();

        return viewModel;
    }

    public DownloadSingleSetupViewModel GetDownloadSingleSetupViewModel(
        VideoInfo video,
        IReadOnlyList<VideoDownloadOption> availableDownloadOptions,
        string? profilePictureUrl = null,
        string? authorName = null
    )
    {
        var viewModel = services.GetRequiredService<DownloadSingleSetupViewModel>();

        viewModel.Video = video;
        viewModel.AvailableDownloadOptions = availableDownloadOptions;
        viewModel.ProfilePictureUrl = profilePictureUrl ?? video.AuthorAvatarUrl;
        viewModel.AuthorName = authorName ?? video.AuthorTitle;

        return viewModel;
    }

    public MessageBoxViewModel GetMessageBoxViewModel(
        string title,
        string message,
        string? okButtonText,
        string? cancelButtonText
    )
    {
        var viewModel = services.GetRequiredService<MessageBoxViewModel>();

        viewModel.Title = title;
        viewModel.Message = message;
        viewModel.DefaultButtonText = okButtonText;
        viewModel.CancelButtonText = cancelButtonText;

        return viewModel;
    }

    public MessageBoxViewModel GetMessageBoxViewModel(string title, string message)
    {
        var viewModel = services.GetRequiredService<MessageBoxViewModel>();

        viewModel.Title = title;
        viewModel.Message = message;

        return viewModel;
    }

    public SettingsViewModel GetSettingsViewModel() =>
        services.GetRequiredService<SettingsViewModel>();

    public BatchInputViewModel GetBatchInputViewModel() =>
        services.GetRequiredService<BatchInputViewModel>();

    public LicenseActivationViewModel GetLicenseActivationViewModel() =>
        services.GetRequiredService<LicenseActivationViewModel>();
}
