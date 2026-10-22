using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Core.Resolving;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.Utils;
using _855Media.ViewModels.Components;
using _855Media.ViewModels.Dialogs;
using _855Media.Views.Components;
using _855Media.Views.Dialogs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using Microsoft.Extensions.DependencyInjection;

Environment.SetEnvironmentVariable(
    "MediaTag_SETTINGS_PATH",
    Path.Combine(Path.GetTempPath(), "855-responsive-preview.json")
);
System.Reflection.Assembly.Load("Material.Avalonia");
System.Reflection.Assembly.Load("Material.Avalonia.DataGrid");
AppBuilder
    .Configure<_855Media.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseDesktopWebViewSafe()
    .SetupWithoutStarting();
Application.Current!.Styles.Add(
    new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Material.Avalonia/"))
    {
        Source = new Uri("avares://Material.Avalonia/Material.Avalonia.Templates.xaml"),
    }
);

// Exercise the actual source-generated settings contract after the Editor rename.
var settingsContextType = typeof(SettingsService).GetNestedType(
    "SerializerContext",
    System.Reflection.BindingFlags.NonPublic
)!;
var settingsContext = (System.Text.Json.Serialization.JsonSerializerContext)
    settingsContextType
        .GetProperty(
            "Default",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        )!
        .GetValue(null)!;
var settingsInfo = settingsContext.GetTypeInfo(typeof(SettingsService))!;
var legacySettings = (SettingsService)
    System.Text.Json.JsonSerializer.Deserialize(
        "{\"UpscalerMaxConcurrency\":3,\"UpscalerEnableSplitAndUpscale\":true,\"UpscalerColorBrightness\":0.15,\"UpscalerOutputDirectory\":\"D:/legacy-output\",\"LastSelectedDashboardTab\":1}",
        settingsInfo
    )!;
if (
    legacySettings.EditorMaxConcurrency != 3
    || !legacySettings.EditorEnableSplitAndEdit
    || legacySettings.EditorColorBrightness != 0.15
    || legacySettings.EditorOutputDirectory != "D:/legacy-output"
)
    throw new Exception("Legacy editor settings did not deserialize");
var legacyRoundTrip = System.Text.Json.JsonSerializer.Serialize(legacySettings, settingsInfo);
if (
    !legacyRoundTrip.Contains("UpscalerOutputDirectory")
    || legacyRoundTrip.Contains("\"EditorOutputDirectory\"")
)
    throw new Exception("Editor settings changed the persisted contract");
var tabOptions = new System.Text.Json.JsonSerializerOptions
{
    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
};
if (
    System.Text.Json.JsonSerializer.Deserialize<DashboardTab>("\"Upscaler\"", tabOptions)
    != DashboardTab.Editor
)
    throw new Exception("Saved dashboard tab no longer loads");
if (
    typeof(_855Media.Views.Components.VideoEditorView).Assembly.GetType(
        "_855Media.Views.Components.VideoUpscalerView"
    )
    is not null
)
    throw new Exception("Old editor view type remains registered");
Console.WriteLine(
    "PASS Editor legacy settings source-generated round-trip, saved tab and renamed view"
);

Directory.CreateDirectory("artifacts/responsive-ui");
var view = new AutoPostView();
foreach (var child in ((Grid)view.Content!).Children)
    child.IsVisible = child.Name == "DashboardLayout";
view.FindControl<DataGrid>("WorkerDataGrid")!.ItemsSource = Enumerable
    .Range(1, 20)
    .Select(i => new LDInstanceItemViewModel(
        new LDInstanceInfo { Index = i, Name = $"LD-{i:00}" },
        null!
    )
    {
        Status = "Waiting for publication confirmation",
    })
    .ToArray();
view.FindControl<Button>("AutomationToggleButton")!.Content = "Start automation";
var window = new Window { Content = view };
window.Show();
foreach (
    var size in new[] { (800, 600), (960, 560), (1180, 760), (1440, 900), (1920, 1080), (960, 560) }
)
{
    window.Width = size.Item1;
    window.Height = size.Item2;
    for (var n = 0; n < 4; n++)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    var tables = view.FindControl<Grid>("DashboardTables")!;
    var device = view.FindControl<Border>("DevicePanel")!;
    var queue = view.FindControl<Border>("QueuePanel")!;
    var actions = view.FindControl<StackPanel>("RunActionButtons")!;
    if (
        device.Bounds.Width < 300
        || queue.Bounds.Width < 300
        || device.Bounds.Height < 80
        || queue.Bounds.Height < 80
    )
        throw new Exception($"Unusable table viewport {size}: {device.Bounds} / {queue.Bounds}");
    if (size.Item1 < 1100 && Grid.GetRow(queue) != 2)
        throw new Exception("Narrow tables not stacked");
    if (size.Item1 >= 1100 && Grid.GetColumn(queue) != 2)
        throw new Exception("Wide tables not restored");
    if (actions.Bounds.Right > view.Bounds.Width || actions.Bounds.Width < 300)
        throw new Exception($"Actions clipped: {actions.Bounds}, view={view.Bounds}");
    using var bitmap = new RenderTargetBitmap(new PixelSize(size.Item1, size.Item2));
    bitmap.Render(view);
    bitmap.Save($"artifacts/responsive-ui/posting-{size.Item1}x{size.Item2}.png");
    Console.WriteLine(
        $"PASS {size}: device={device.Bounds}, queue={queue.Bounds}; table font=13, action font=12"
    );
}
window.Content = new RunSettingsView();
window.Width = 800;
window.Height = 600;
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();
using (var bitmap = new RenderTargetBitmap(new PixelSize(800, 600)))
{
    bitmap.Render((Control)window.Content);
    bitmap.Save("artifacts/responsive-ui/run-settings-800x600.png");
}
using var localization = new LocalizationManager(new SettingsService());
using var dialogs = new DialogManager();
using var downloaderVm = new UnifiedDownloaderViewModel(
    localization,
    dialogs,
    new SnackbarManager()
);
var downloaderView = new UnifiedDownloaderView { DataContext = downloaderVm };
window.Content = downloaderView;
var instagramCases = new[]
{
    ("https://www.instagram.com/nample/reels/", true, true, "@nample", 2),
    ("https://www.instagram.com/nample/", true, true, "@nample", 1),
    ("https://www.instagram.com/p/BA/", true, false, (string?)null, 1),
    ("https://www.instagram.com/reel/BA/?igsh=shared", true, false, (string?)null, 2),
    ("https://instagram.com.evil.test/nample/", false, false, (string?)null, 2),
};
foreach (var (url, instagram, profile, handle, mode) in instagramCases)
{
    downloaderVm.Query = url;
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    if (
        downloaderVm.IsInstagramDetected != instagram
        || downloaderVm.IsProfileQuery != profile
        || downloaderVm.ProfileHandleDisplay != handle
        || downloaderVm.SelectedInstagramMediaIndex != mode
        || downloaderView.FindControl<Control>("InstagramInputIcon")!.IsVisible != instagram
        || downloaderView.FindControl<Border>("InstagramGuidance")!.IsVisible != instagram
    )
        throw new Exception(
            "Instagram URL detection, profile or visible guidance mismatch: " + url
        );
}
downloaderVm.Query = "https://www.instagram.com/nample/";
var selector = downloaderView.FindControl<ComboBox>("InstagramMediaSelector")!;
selector.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
if (downloaderVm.SelectedInstagramMediaIndex != 0)
    throw new Exception("Instagram media selector did not update the view model");
downloaderVm.IsBatchMode = true;
Dispatcher.UIThread.RunJobs();
if (!downloaderVm.ShowInstagramOptions)
    throw new Exception("Instagram media choices are missing in batch mode");
downloaderVm.IsBatchMode = false;
downloaderVm.Query = "https://www.instagram.com/nample/reels/";
Console.WriteLine(
    "PASS Instagram photo/profile/Reel detection, mode defaults, bindings and batch choices"
);
downloaderVm.IsBrowserMode = true;
downloaderVm.IsInstagramBrowserMode = true;
if (
    downloaderVm.IsBrowserMode
    || !downloaderVm.IsAnyBrowserMode
    || !downloaderVm.HasOpenedInstagramBrowser
)
    throw new Exception("Instagram and Facebook native browsers can overlap");
downloaderVm.SwitchToDirectModeCommand.Execute(null);
if (downloaderVm.IsAnyBrowserMode)
    throw new Exception("Direct mode did not hide both browsers");
Console.WriteLine(
    "PASS mutually exclusive Facebook/Instagram browser modes and direct-mode return"
);
foreach (var width in new[] { 700, 960, 1440 })
{
    window.Width = width;
    window.Height = 700;
    for (var n = 0; n < 4; n++)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    var download = (Control)window.Content;
    var inputs = download
        .GetVisualDescendants()
        .OfType<Border>()
        .Where(b => b.MaxWidth == 740)
        .ToArray();
    if (inputs.Length == 0 || inputs.Any(b => b.Bounds.Width > width - 48))
        throw new Exception("Download input overflow");
    using var bitmap = new RenderTargetBitmap(new PixelSize(width, 700));
    bitmap.Render(download);
    bitmap.Save($"artifacts/responsive-ui/download-{width}.png");
    Console.WriteLine($"PASS downloader inputs at {width}px");
}

// Use a real inactive license and isolated settings; no entitlement bypass or user data writes.
var inactiveLicense = new LicenseService();
using var provider = new ServiceCollection()
    .AddSingleton<ILicenseService>(inactiveLicense)
    .AddSingleton(new SettingsService())
    .AddSingleton(localization)
    .AddSingleton(dialogs)
    .AddSingleton(new SnackbarManager())
    .AddSingleton<ViewModelManager>()
    .AddTransient<DownloadSingleSetupViewModel>()
    .AddTransient<DownloadMultipleSetupViewModel>()
    .AddTransient<InstagramDownloaderViewModel>()
    .BuildServiceProvider();
var manager = provider.GetRequiredService<ViewModelManager>();
var photo = new VideoInfo(
    VideoSource.InstagramPhoto,
    "ABC_1",
    "https://www.instagram.com/p/ABC/",
    "Caption",
    "nample",
    null,
    null,
    null,
    []
);
using var multiple = manager.GetDownloadMultipleSetupViewModel("Instagram", [photo]);
using var single = manager.GetDownloadSingleSetupViewModel(
    photo,
    [new _855Media.Core.Downloading.VideoDownloadOption(new("jpg"), true, [])]
);
var licenseProperty = typeof(ViewModelBase).GetProperty(
    "ModuleLicense",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
)!;
if (
    !ReferenceEquals(licenseProperty.GetValue(single), inactiveLicense)
    || !ReferenceEquals(licenseProperty.GetValue(multiple), inactiveLicense)
)
    throw new Exception(
        "Download setup view models are missing the execution-time license service"
    );
single.ConfirmCommand.ExecuteAsync(null).GetAwaiter().GetResult();
multiple.ConfirmCommand.ExecuteAsync(null).GetAwaiter().GetResult();
using var instagramVm = manager.GetInstagramDownloaderViewModel(null!);
var commands = new List<string>();
instagramVm.BrowserCommand += commands.Add;
instagramVm.ToggleCollectionCommand.Execute(null);
instagramVm.ImportCommand.Execute(null);
instagramVm.NavigateCommand.Execute(null);
if (commands.Count != 0)
    throw new Exception("Inactive subscription can collect/import/navigate Instagram");
instagramVm.IsImporting = true;
instagramVm.ToggleCollectionCommand.Execute(null);
instagramVm.ClearCommand.Execute(null);
if (!commands.SequenceEqual(new[] { "stop", "clear" }))
    throw new Exception("Stop/Clear are unavailable after access expires");
instagramVm.IsImporting = false;
instagramVm.SelectedMediaIndex = 2;
if (instagramVm.MediaKind != InstagramMediaKind.Videos)
    throw new Exception("Instagram browser media filter mismatch");
Console.WriteLine(
    "PASS actual factory license wiring, blocked inactive actions and accessible Stop/Clear"
);
var instagramView = new InstagramDownloaderView { DataContext = instagramVm };

// Native WebView rendering is covered separately by BrowserCheck; headless has no native child host.
var nativeBrowser = instagramView.FindControl<Control>("InstagramWebBrowser")!;
((Panel)nativeBrowser.Parent!).Children.Remove(nativeBrowser);
window.Content = instagramView;
foreach (var width in new[] { 700, 1440 })
{
    window.Width = width;
    window.Height = 700;
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    var autoScroll = instagramView.FindControl<Button>("InstagramAutoScroll")!;
    var import = instagramView.FindControl<Button>("InstagramImport")!;
    var mediaSelector = instagramView.FindControl<ComboBox>("InstagramBrowserMediaSelector")!;
    if (
        autoScroll.Bounds.Width <= 0
        || import.Bounds.Width <= 0
        || mediaSelector.Bounds.Width < 200
    )
        throw new Exception("Instagram browser toolbar controls are clipped");
    using var bitmap = new RenderTargetBitmap(new PixelSize(width, 700));
    bitmap.Render(instagramView);
    bitmap.Save($"artifacts/responsive-ui/instagram-browser-{width}.png");
    Console.WriteLine($"PASS Instagram browser Auto-Scroll/Import/media toolbar at {width}px");
}

// Reproduce browser import's seven-item selection inside the real modal host.
multiple.AvailableVideos = Enumerable
    .Range(1, 7)
    .Select(index => new VideoInfo(
        VideoSource.Instagram,
        "reel-" + index,
        "https://www.instagram.com/reel/TEST" + index + "/",
        "Instagram Reel " + index,
        "Creator",
        null,
        null,
        TimeSpan.FromSeconds(12),
        [],
        DirectMediaUrl: "https://video.fixture.fbcdn.net/clip" + index + ".mp4"
    ))
    .ToArray();
var dialogView = new DownloadMultipleSetupView { DataContext = multiple };
var browserSurface = new Border { Child = new TextBlock { Text = "Native browser presentation" } };
var browserOwner = new Border { Child = browserSurface };
var modalHost = new DialogHost
{
    Content = browserOwner,
    DialogContent = dialogView,
    DisableOpeningAnimation = true,
};
window.Content = modalHost;
window.Width = 1180;
window.Height = 760;
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();
using (var guard = new NativeBrowserDialogGuard(visible => browserSurface.IsVisible = visible))
{
    guard.Attach(browserOwner);
    guard.SetActive(true);
    if (!browserSurface.IsVisible)
        throw new Exception("Active browser is hidden without a dialog");
    modalHost.IsOpen = true;
    for (var n = 0; n < 4; n++)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    if (
        browserSurface.IsVisible
        || !dialogView.IsVisible
        || dialogView.Bounds.Width < 500
        || multiple.SelectedVideos.Count != 7
    )
        throw new Exception("Seven-media selection is obscured by the native browser presentation");
    using (var bitmap = new RenderTargetBitmap(new PixelSize(1180, 760)))
    {
        bitmap.Render(modalHost);
        bitmap.Save("artifacts/responsive-ui/instagram-media-selection-dialog.png");
    }
    Console.WriteLine(
        "PASS seven-media selection dialog hides the browser surface and renders its destination controls"
    );
    modalHost.IsOpen = false;
    if (!browserSurface.IsVisible)
        throw new Exception("Browser was not restored after closing the dialog");
    modalHost.IsOpen = true;
    guard.SetActive(false);
    modalHost.IsOpen = false;
    if (browserSurface.IsVisible)
        throw new Exception("Closing a dialog revived an inactive browser");
    guard.SetActive(true);
    modalHost.IsOpen = true;
    guard.SetActive(true); // A delayed WebView-created callback must respect an already open dialog.
    if (browserSurface.IsVisible)
        throw new Exception("A late native browser callback covered the open modal");
    guard.Dispose();
    modalHost.IsOpen = false;
    if (browserSurface.IsVisible)
        throw new Exception("Detached browser subscribed to stale dialog events");
    guard.Attach(browserOwner);
    guard.SetActive(true);
    if (!browserSurface.IsVisible)
        throw new Exception("Browser could not reattach after modal cleanup");
    Console.WriteLine(
        "PASS modal close/Cancel restoration, tab switches, delayed creation, detach and reattach"
    );
}
using (var guard = new NativeBrowserDialogGuard(visible => browserSurface.IsVisible = visible))
{
    guard.Attach(new Border());
    guard.SetActive(true);
    using (NativeBrowserDialogGuard.SuspendForDialog())
    {
        if (browserSurface.IsVisible)
            throw new Exception("Host-independent modal suppression failed");
        using (NativeBrowserDialogGuard.SuspendForDialog())
            guard.SetActive(true);
        guard.Refresh();
        if (browserSurface.IsVisible)
            throw new Exception("Nested modal restored browser early");
    }
    if (!browserSurface.IsVisible)
        throw new Exception("Modal scope failed to restore browser");
    try
    {
        using var scope = NativeBrowserDialogGuard.SuspendForDialog();
        throw new InvalidOperationException("simulated dialog failure");
    }
    catch (InvalidOperationException) { }
    if (!browserSurface.IsVisible)
        throw new Exception("Failed dialog left browser hidden");
    Console.WriteLine("PASS ancestor-independent dialog scopes, nesting and exception cleanup");
}
var overflowGrid = new DataGrid
{
    AutoGenerateColumns = false,
    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    RowHeight = 48,
};
overflowGrid.Columns.Add(
    new DataGridTextColumn
    {
        Header = "Name",
        Binding = new Avalonia.Data.Binding("Name"),
        Width = new DataGridLength(280),
    }
);
window.Content = overflowGrid;
window.Width = 500;
window.Height = 350;
foreach (var count in new[] { 3, 100, 3, 100 })
{
    overflowGrid.ItemsSource = Enumerable
        .Range(1, count)
        .Select(i => new LDInstanceItemViewModel(
            new LDInstanceInfo { Index = i, Name = $"LD-{i}" },
            null!
        ))
        .ToArray();
    for (var n = 0; n < 6; n++)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    var bar = overflowGrid
        .GetVisualDescendants()
        .OfType<Avalonia.Controls.Primitives.ScrollBar>()
        .Single(b => b.Name == "PART_VerticalScrollbar");
    if (bar.IsVisible != (count > 3))
        throw new Exception(
            $"Scrollbar visibility wrong for {count}: {bar.IsVisible}, max={bar.Maximum}, bounds={bar.Bounds}"
        );
    if (count > 3 && (bar.Bounds.Width < 14 || bar.Maximum <= 0 || bar.AllowAutoHide))
        throw new Exception(
            $"Overflow scrollbar not usable: bounds={bar.Bounds}, width={bar.Width}, max={bar.Maximum}, autohide={bar.AllowAutoHide}"
        );
    if (count > 3)
    {
        overflowGrid.ScrollIntoView(
            overflowGrid.ItemsSource.Cast<object>().Last(),
            overflowGrid.Columns[0]
        );
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    using var bmp = new RenderTargetBitmap(new PixelSize(500, 350));
    bmp.Render(overflowGrid);
    bmp.Save($"artifacts/responsive-ui/scrollbar-{count}.png");
    Console.WriteLine(
        $"PASS scrollbar count={count}: visible={bar.IsVisible}, width={bar.Bounds.Width}, max={bar.Maximum}"
    );
}

// Real grading workspace with an isolated VM and a landscape image fixture.
_855Media.Core.Editing.QueuePersistenceService.CustomQueueFilePath = Path.GetFullPath(
    "artifacts/responsive-ui/grading-empty-queue.json"
);
using (
    var gradingQueue = new _855Media.Core.Editing.VideoQueueManager(
        new _855Media.Core.Editing.VideoEditorService()
    )
)
{
    var gradingVm = new VideoEditorViewModel(
        gradingQueue,
        dialogs,
        new SnackbarManager(),
        localization,
        new SettingsService()
    );
    gradingVm.IsColorGradingPanelOpen = true;
    gradingVm.SelectedStudioTab = 0;
    gradingVm.ZoomScale = 1;
    var fixture = new Grid
    {
        Width = 1920,
        Height = 1080,
        Background = Avalonia.Media.Brushes.SteelBlue,
    };
    fixture.Children.Add(
        new Border
        {
            Background = Avalonia.Media.Brushes.DarkSeaGreen,
            Height = 440,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        }
    );
    fixture.Children.Add(
        new TextBlock
        {
            Text = "Original  |  Graded\nFull frame preview",
            FontSize = 80,
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        }
    );
    fixture.Measure(new Size(1920, 1080));
    fixture.Arrange(new Rect(0, 0, 1920, 1080));
    using var image = new RenderTargetBitmap(new PixelSize(1920, 1080));
    image.Render(fixture);
    gradingVm.PreviewImage = image;
    var grading = new VideoEditorView { DataContext = gradingVm };
    window.Content = new Material.Styles.Controls.SnackbarHost
    {
        HostName = "GradingCheck",
        Content = grading,
    };
    foreach (var size in new[] { (960, 560), (1180, 760), (1920, 1080), (1180, 760) })
    {
        window.Width = size.Item1;
        window.Height = size.Item2;
        for (var step = 0; step < 5; step++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        var frame = grading.FindControl<Border>("PreviewFrame")!;
        var inspector = grading.FindControl<ScrollViewer>("GradingControlsScroll")!;
        var drawer = grading.FindControl<Border>("StudioDrawer")!;
        var preview = grading.FindControl<Image>("GradingPreviewImage")!;
        if (
            frame.Bounds.Height < 60
            || drawer.Bounds.Height > grading.Bounds.Height - 140
            || inspector.Viewport.Height <= 0
            || inspector.Extent.Height <= inspector.Viewport.Height
        )
            throw new Exception(
                $"Grading workspace not usable at {size}: frame={frame.Bounds}, drawer={drawer.Bounds}, inspector={inspector.Viewport}/{inspector.Extent}"
            );
        if (
            preview.Bounds.Width > frame.Bounds.Width + 2
            || preview.Bounds.Height > frame.Bounds.Height + 2
        )
            throw new Exception("Normal preview exceeds its viewport");
        using var capture = new RenderTargetBitmap(new PixelSize(size.Item1, size.Item2));
        capture.Render(grading);
        capture.Save($"artifacts/responsive-ui/grading-{size.Item1}x{size.Item2}.png");
        Console.WriteLine(
            $"PASS grading {size}: frame={frame.Bounds.Size}, scrolling inspector={inspector.Viewport}"
        );
    }
    var expand = grading.FindControl<Button>("ExpandStudioButton")!;
    var before = grading.FindControl<Border>("StudioDrawer")!.Height;
    expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    if (grading.FindControl<Border>("StudioDrawer")!.Height <= before)
        throw new Exception("Expand preview did not enlarge workspace");
    expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    grading.FindControl<NumericUpDown>("GradeBrightness")!.Value = 0.2m;
    if (gradingVm.ActiveColorGrading.Brightness != 0.2)
        throw new Exception("Grading control binding failed");
    gradingVm.ZoomIn();
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    gradingVm.ResetZoom();
    gradingVm.ResetColorGrading();
    if (gradingVm.ActiveColorGrading.Brightness != 0)
        throw new Exception("Grading reset failed");
    Console.WriteLine("PASS expanded workspace, numeric binding, zoom/reset and grading reset");
}
window.Close();
((IDisposable)Application.Current!).Dispose();
