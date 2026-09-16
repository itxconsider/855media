using System;
using _855Media.Core.Licensing;
using _855Media.Core.Upscaling;
using _855Media.Core.Utils;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.ViewModels;
using _855Media.ViewModels.Components;
using _855Media.ViewModels.Dialogs;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using AvaloniaWebView;
using Material.Styles.Themes;
using Microsoft.Extensions.DependencyInjection;
using PowerKit.Extensions;

namespace _855Media;

public class App : Application, IDisposable
{
    private readonly ServiceProvider _services;
    private readonly SettingsService _settingsService;

    private readonly IDisposable _eventSubscription;

    private bool _isDisposed;

    public App()
    {
        var services = new ServiceCollection();

        // Framework
        services.AddSingleton<DialogManager>();
        services.AddSingleton<SnackbarManager>();
        services.AddSingleton<ViewManager>();
        services.AddSingleton<ViewModelManager>();

        // Localization
        services.AddSingleton<LocalizationManager>();

        // Services
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<ExtensionInstallerService>();
        services.AddSingleton<LocalBridgeServer>();
        services.AddSingleton<FacebookBrowserLauncher>();
        services.AddSingleton<VideoUpscaleService>();
        services.AddSingleton<VideoQueueManager>();
        services.AddSingleton<HardwareMonitorService>();

        // View models
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<YouTubeDownloaderViewModel>();
        services.AddTransient<TikTokDownloaderViewModel>();
        services.AddTransient<FacebookDownloaderViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<VideoUpscalerViewModel>();
        services.AddTransient<DubbingViewModel>();
        services.AddTransient<DownloadViewModel>();
        services.AddTransient<AuthSetupViewModel>();
        services.AddTransient<DownloadMultipleSetupViewModel>();
        services.AddTransient<DownloadSingleSetupViewModel>();
        services.AddTransient<MessageBoxViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<BatchInputViewModel>();
        services.AddTransient<LicenseActivationViewModel>();

        _services = services.BuildServiceProvider(true);
        _settingsService = _services.GetRequiredService<SettingsService>();

        // Re-initialize the theme when the user changes it
        _eventSubscription = _settingsService.WatchProperty(
            o => o.Theme,
            v =>
            {
                RequestedThemeVariant = v switch
                {
                    ThemeVariant.Light => Avalonia.Styling.ThemeVariant.Light,
                    ThemeVariant.Dark => Avalonia.Styling.ThemeVariant.Dark,
                    _ => Avalonia.Styling.ThemeVariant.Default,
                };

                InitializeTheme();
            }
        );
    }

    private void InitializeTheme()
    {
        var actualTheme = RequestedThemeVariant?.Key switch
        {
            "Light" => PlatformThemeVariant.Light,
            "Dark" => PlatformThemeVariant.Dark,
            _ => PlatformSettings?.GetColorValues().ThemeVariant ?? PlatformThemeVariant.Light,
        };

        this.LocateMaterialTheme<MaterialThemeBase>().CurrentTheme =
            actualTheme == PlatformThemeVariant.Light
                ? Theme.Create(Theme.Light, Color.Parse("#1A1A1A"), Color.Parse("#000000"))
                : Theme.Create(Theme.Dark, Color.Parse("#E0E0E0"), Color.Parse("#FFFFFF"));
    }

    public override void Initialize()
    {
        base.Initialize();

        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();

        AvaloniaWebViewBuilder.Initialize(config => config.IsInPrivateModeEnabled = false);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load settings
        _settingsService.Load();

        // Initialize licensing
        _settingsService.FirstRunDate ??= DateTime.UtcNow;
        var licenseService = _services.GetRequiredService<ILicenseService>();
        licenseService.Initialize(
            _settingsService.LicenseToken,
            _settingsService.FirstRunDate,
            _settingsService.LastExecutionDate
        );
        _settingsService.LastExecutionDate = DateTime.UtcNow;
        _settingsService.Save();

        // Initialize and configure the main window
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewManager = _services.GetRequiredService<ViewManager>();
            var viewModelManager = _services.GetRequiredService<ViewModelManager>();

            desktop.MainWindow = viewManager.TryBindWindow(viewModelManager.GetMainViewModel());

            if (desktop.MainWindow is { } mainWindow)
            {
                mainWindow.Closing += (_, _) =>
                {
                    try
                    {
                        _settingsService.Save();
                    }
                    catch { }

                    try
                    {
                        var queueManager = _services.GetService<VideoQueueManager>();
                        queueManager?.StopAllJobs();
                    }
                    catch { }
                    ChildProcessTracker.KillAll();
                };
            }

            desktop.Exit += (_, _) => Dispose();
        }

        // Initialize the theme for the first time; must be done after the main window is created
        InitializeTheme();

        base.OnFrameworkInitializationCompleted();
    }

    private void Application_OnActualThemeVariantChanged(object? sender, EventArgs args) =>
        // Re-initialize the theme when the system theme changes
        InitializeTheme();

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        ChildProcessTracker.KillAll();

        _eventSubscription.Dispose();
        _services.Dispose();
    }
}
