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
    public MainViewModel GetMainViewModel() => services.GetRequiredService<MainViewModel>();

    public DashboardViewModel GetDashboardViewModel() =>
        services.GetRequiredService<DashboardViewModel>();

    public YouTubeDownloaderViewModel GetYouTubeDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = services.GetRequiredService<YouTubeDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public TikTokDownloaderViewModel GetTikTokDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = services.GetRequiredService<TikTokDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public FacebookDownloaderViewModel GetFacebookDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = services.GetRequiredService<FacebookDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public DramaBoxDownloaderViewModel GetDramaBoxDownloaderViewModel(
        DashboardViewModel dashboardViewModel
    )
    {
        var viewModel = services.GetRequiredService<DramaBoxDownloaderViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public HistoryViewModel GetHistoryViewModel(DashboardViewModel dashboardViewModel)
    {
        var viewModel = services.GetRequiredService<HistoryViewModel>();
        viewModel.Initialize(dashboardViewModel);
        return viewModel;
    }

    public VideoUpscalerViewModel GetVideoUpscalerViewModel() =>
        services.GetRequiredService<VideoUpscalerViewModel>();

    public DubbingViewModel GetDubbingViewModel() =>
        services.GetRequiredService<DubbingViewModel>();

    public AuthSetupViewModel GetAuthSetupViewModel() =>
        services.GetRequiredService<AuthSetupViewModel>();

    public DownloadViewModel GetDownloadViewModel(
        VideoInfo video,
        VideoDownloadOption downloadOption,
        string filePath
    )
    {
        var viewModel = services.GetRequiredService<DownloadViewModel>();

        viewModel.Video = video;
        viewModel.DownloadOption = downloadOption;
        viewModel.FilePath = filePath;

        return viewModel;
    }

    public DownloadViewModel GetDownloadViewModel(
        VideoInfo video,
        VideoDownloadPreference downloadPreference,
        string filePath
    )
    {
        var viewModel = services.GetRequiredService<DownloadViewModel>();

        viewModel.Video = video;
        viewModel.DownloadPreference = downloadPreference;
        viewModel.FilePath = filePath;

        return viewModel;
    }

    public DownloadMultipleSetupViewModel GetDownloadMultipleSetupViewModel(
        string title,
        IReadOnlyList<VideoInfo> availableVideos,
        bool preselectVideos = true
    )
    {
        var viewModel = services.GetRequiredService<DownloadMultipleSetupViewModel>();

        viewModel.Title = title;
        viewModel.AvailableVideos = availableVideos;

        if (preselectVideos)
            viewModel.SelectedVideos.AddRange(availableVideos);

        return viewModel;
    }

    public DownloadSingleSetupViewModel GetDownloadSingleSetupViewModel(
        VideoInfo video,
        IReadOnlyList<VideoDownloadOption> availableDownloadOptions
    )
    {
        var viewModel = services.GetRequiredService<DownloadSingleSetupViewModel>();

        viewModel.Video = video;
        viewModel.AvailableDownloadOptions = availableDownloadOptions;

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
