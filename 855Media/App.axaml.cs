using System;
using _855Media.Core.LDPlayer;
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
    private Avalonia.Threading.DispatcherTimer? _subscriptionTimer;

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
        services.AddSingleton<PFlowBrowserLauncher>();
        services.AddSingleton<VideoUpscaleService>();
        services.AddSingleton<VideoQueueManager>(sp =>
        {
            var license = sp.GetRequiredService<ILicenseService>();
            var queue = new VideoQueueManager(sp.GetRequiredService<VideoUpscaleService>())
            {
                CanStartJob = () => license.IsFeatureAllowed(LicenseFeature.All),
            };
            license.LicenseChanged += queue.RefreshSubscriptionAccess;
            return queue;
        });
        services.AddSingleton<HardwareMonitorService>();
        services.AddSingleton<AtxAgentClient>();
        services.AddSingleton<ILDPlayerService>(sp => new LDPlayerService(
            sp.GetService<AtxAgentClient>()
        ));
        services.AddSingleton<IPFlowPersonaSwitcherService, PFlowPersonaSwitcherService>();
        services.AddSingleton<IPFlowAutoPostService, PFlowAutoPostService>();
        services.AddSingleton<IPFlowFeedEngagementService, PFlowFeedEngagementService>();
        services.AddSingleton<IPFlowBloksService, PFlowBloksService>();
        services.AddSingleton<IPFlowAccountDiscoveryService, PFlowAccountDiscoveryService>();
        services.AddSingleton<IAdbKeyboardService, AdbKeyboardService>();
        services.AddSingleton<ILDPlayerUiAutomationService, LDPlayerUiAutomationService>();

        services.AddSingleton<IScreenTimeService, ScreenTimeService>();

        // View models
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<UnifiedDownloaderViewModel>();
        services.AddTransient<YouTubeDownloaderViewModel>();
        services.AddTransient<TikTokDownloaderViewModel>();
        services.AddTransient<FacebookDownloaderViewModel>();
        services.AddTransient<DramaBoxDownloaderViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<VideoUpscalerViewModel>();
        services.AddTransient<DubbingViewModel>();
        services.AddTransient<AutoPostViewModel>();
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

        var theme =
            actualTheme == PlatformThemeVariant.Light
                ? Theme.Create(Theme.Light, Color.Parse("#111111"), Color.Parse("#333333"))
                : Theme.Create(Theme.Dark, Color.Parse("#737373"), Color.Parse("#737373"));
        if (actualTheme == PlatformThemeVariant.Dark)
        {
            // Material controls and application panels share the reference palette.
            theme.Background = Color.Parse("#111111");
            theme.Paper = Color.Parse("#1B1B1B");
            theme.CardBackground = Color.Parse("#242424");
            theme.ToolBarBackground = Color.Parse("#151515");
            theme.Body = Color.Parse("#FCFCFC");
            theme.BodyLight = Color.Parse("#ABABAB");
            theme.ColumnHeader = Color.Parse("#FCFCFC");
            theme.Divider = Color.Parse("#2D2D2D");
            theme.Selection = Color.Parse("#383838");
            theme.ToolForeground = Color.Parse("#FCFCFC");
            theme.ToolBackground = Color.Parse("#202020");
            theme.ToolTipBackground = Color.Parse("#242424");
            theme.ChipBackground = Color.Parse("#242424");
            theme.SnackbarBackground = Color.Parse("#242424");
            theme.SnackbarMouseOver = Color.Parse("#383838");
            theme.TextBoxBorder = Color.Parse("#454545");
            theme.TextFieldBoxBackground = Color.Parse("#1B1B1B");
            theme.TextFieldBoxHoverBackground = Color.Parse("#202020");
            theme.CheckBoxOff = Color.Parse("#ABABAB");
            theme.CheckBoxDisabled = Color.Parse("#454545");
        }
        this.LocateMaterialTheme<MaterialThemeBase>().CurrentTheme = theme;
    }

    public override void Initialize()
    {
        base.Initialize();

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load settings safely
        try
        {
            _settingsService.Load();
        }
        catch
        {
            // SettingsService handles corruption fallback internally
        }

        _855Media.Core.Downloading.MediaCompatibility.MaxConcurrentConversions =
            _settingsService.ConversionParallelLimit;

        var layoutRules = FacebookLayoutRegistry.InitializeConfiguration();
        System.Diagnostics.Debug.WriteLine(layoutRules.Message);

        // Apply saved theme on startup
        RequestedThemeVariant = _settingsService.Theme switch
        {
            ThemeVariant.Light => Avalonia.Styling.ThemeVariant.Light,
            ThemeVariant.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
        InitializeTheme();

        // Initialize licensing
        _settingsService.FirstRunDate ??= DateTime.UtcNow;
        var licenseService = _services.GetRequiredService<ILicenseService>();
        licenseService.Initialize(
            _settingsService.LicenseToken,
            _settingsService.FirstRunDate,
            _settingsService.LastExecutionDate
        );
        if (
            !_settingsService.LastExecutionDate.HasValue
            || DateTime.UtcNow > _settingsService.LastExecutionDate.Value
        )
            _settingsService.LastExecutionDate = DateTime.UtcNow;
        _settingsService.Save();
        _subscriptionTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _subscriptionTimer.Tick += (_, _) =>
        {
            licenseService.Refresh();
            if (
                !_settingsService.LastExecutionDate.HasValue
                || DateTime.UtcNow > _settingsService.LastExecutionDate.Value.AddMinutes(1)
            )
            {
                _settingsService.LastExecutionDate = DateTime.UtcNow;
                _settingsService.Save();
            }
        };
        _subscriptionTimer.Start();

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
        _subscriptionTimer?.Stop();

        ChildProcessTracker.KillAll();

        _eventSubscription.Dispose();
        _services.Dispose();
    }
}
