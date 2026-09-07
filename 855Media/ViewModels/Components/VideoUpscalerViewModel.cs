using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Upscaling;
using _855Media.Framework;
using _855Media.Localization;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public enum PostBatchAction
{
    DoNothing,
    OpenDestinationFolder,
    PlaySoundNotification,
    ShutdownSystem,
}

public partial class VideoUpscalerViewModel : ViewModelBase
{
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".mp4",
        ".mov",
        ".mkv",
        ".avi",
        ".webm",
        ".flv",
        ".wmv",
        ".m4v",
    };

    private readonly VideoQueueManager _queueManager;
    private readonly DialogManager _dialogManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly VideoPreviewService _previewService = new();
    private CancellationTokenSource? _previewCts;
    private string? _lastProbedVideoPath;

    [ObservableProperty]
    private UpscaleTargetResolution _selectedTargetResolution = UpscaleTargetResolution.Hd1080p;

    [ObservableProperty]
    private UpscaleVideoCodec _selectedCodec = UpscaleVideoCodec.H264;

    [ObservableProperty]
    private HardwareAccelerationMode _selectedHardwareAcceleration = HardwareAccelerationMode.Auto;

    [ObservableProperty]
    private string _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    [ObservableProperty]
    private int _maxConcurrency = 2;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private UpscaleJob? _selectedJob;

    [ObservableProperty]
    private bool _isLogViewerVisible;

    [ObservableProperty]
    private string? _logViewerContent;

    [ObservableProperty]
    private bool _isColorGradingPanelOpen;

    [ObservableProperty]
    private ColorGradingSettings _activeColorGrading;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private double _previewTimestampSeconds = 1.0;

    [ObservableProperty]
    private double _videoDurationSeconds = 10.0;

    [ObservableProperty]
    private string? _previewError;

    [ObservableProperty]
    private bool _hasPreviewVideo;

    [ObservableProperty]
    private double _splitDividerRatio = 0.50;

    [ObservableProperty]
    private double _zoomScale = 1.0;

    [ObservableProperty]
    private string _selectedPresetName = "Default / Neutral";

    [ObservableProperty]
    private bool _enableDenoise;

    [ObservableProperty]
    private bool _enableDeinterlace;

    [ObservableProperty]
    private PostBatchAction _selectedPostBatchAction = PostBatchAction.DoNothing;

    [ObservableProperty]
    private string _selectedColorPresetName = "Neutral / Custom";

    [ObservableProperty]
    private bool _isBasicExposureExpanded = true;

    [ObservableProperty]
    private bool _isColorWheelsExpanded = true;

    [ObservableProperty]
    private bool _isFilmEmulationExpanded = true;

    [ObservableProperty]
    private bool _isSavePresetPopupOpen;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private string _newPresetDescription = string.Empty;

    public ObservableCollection<string> AvailableColorPresetNames { get; } = [];

    public IReadOnlyList<string> AvailableToneCurves =>
        AdvancedColorGradeService.AvailableToneCurves;

    public string FormattedPreviewTimestamp =>
        $"{TimeSpan.FromSeconds(PreviewTimestampSeconds):mm\\:ss} / {TimeSpan.FromSeconds(VideoDurationSeconds):mm\\:ss}";

    public ColorGradingSettings GlobalColorGrading { get; } = new();

    public bool IsEditingJobGrading => SelectedJob != null;

    public ObservableCollection<UpscaleJob> Jobs => _queueManager.Jobs;

    public LocalizationManager LocalizationManager { get; }

    public IReadOnlyList<UpscaleTargetResolution> AvailableResolutions { get; } =
        Enum.GetValues<UpscaleTargetResolution>();

    public IReadOnlyList<UpscaleVideoCodec> AvailableCodecs { get; } =
        Enum.GetValues<UpscaleVideoCodec>();

    public IReadOnlyList<HardwareAccelerationMode> AvailableHardwareAccelerations { get; } =
        Enum.GetValues<HardwareAccelerationMode>();

    public IReadOnlyList<int> AvailableConcurrencies { get; } = [1, 2, 3, 4];

    public IReadOnlyList<PostBatchAction> AvailablePostBatchActions { get; } =
        Enum.GetValues<PostBatchAction>();

    public IReadOnlyList<string> AvailablePresetNames => PresetManager.PresetNames;

    public string GpuStatusDescription
    {
        get
        {
            var info = HardwareDetector.GetGpuInfo();
            return $"{info.Name} ({info.DedicatedVramGb:F1} GB VRAM) - Tile: {(info.SafeTileSize == 0 ? "Full (0)" : info.SafeTileSize.ToString())}";
        }
    }

    public void RefreshColorPresetsList()
    {
        AvailableColorPresetNames.Clear();
        foreach (var name in ColorPresetManager.GetPresetNames())
        {
            AvailableColorPresetNames.Add(name);
        }
    }

    public VideoUpscalerViewModel(
        VideoQueueManager queueManager,
        DialogManager dialogManager,
        SnackbarManager snackbarManager,
        LocalizationManager localizationManager
    )
    {
        _queueManager = queueManager;
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        LocalizationManager = localizationManager;
        _activeColorGrading = GlobalColorGrading;
        GlobalColorGrading.PropertyChanged += OnColorGradingSettingsChanged;
        _queueManager.BatchCompleted += OnBatchCompleted;

        RefreshColorPresetsList();

        if (string.IsNullOrWhiteSpace(OutputDirectory) || !Directory.Exists(OutputDirectory))
        {
            OutputDirectory = AppContext.BaseDirectory;
        }
    }

    private void OnBatchCompleted(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (SelectedPostBatchAction)
            {
                case PostBatchAction.OpenDestinationFolder:
                    if (Directory.Exists(OutputDirectory))
                    {
                        try
                        {
                            Process.Start(
                                new ProcessStartInfo
                                {
                                    FileName = OutputDirectory,
                                    UseShellExecute = true,
                                }
                            );
                        }
                        catch { }
                    }
                    break;
                case PostBatchAction.PlaySoundNotification:
                    try
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            MessageBeep(0x00000040);
                        }
                        else
                        {
                            Console.Beep();
                        }
                    }
                    catch { }
                    _snackbarManager.Notify("Batch queue processing completed!");
                    break;
                case PostBatchAction.ShutdownSystem:
                    try
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            Process.Start(
                                new ProcessStartInfo
                                {
                                    FileName = "shutdown",
                                    Arguments =
                                        "/s /t 60 /c \"855Media Batch Upscaling Complete. Shutting down system in 60 seconds...\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                }
                            );
                            _snackbarManager.Notify("System shutdown scheduled in 60 seconds.");
                        }
                    }
                    catch { }
                    break;
            }
        });
    }

    partial void OnSplitDividerRatioChanged(double value)
    {
        RequestPreviewUpdate(150);
    }

    partial void OnSelectedColorPresetNameChanged(string value)
    {
        var preset = ColorPresetManager.GetPreset(value);
        if (preset != null)
        {
            ActiveColorGrading.ApplyPreset(preset);
            _snackbarManager.Notify($"Applied preset: {preset.Name}");
            RequestPreviewUpdate(0);
        }
    }

    [RelayCommand]
    public void OpenSavePresetPopup()
    {
        NewPresetName = string.Empty;
        NewPresetDescription = string.Empty;
        IsSavePresetPopupOpen = true;
    }

    [RelayCommand]
    public void CancelSavePreset()
    {
        IsSavePresetPopupOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmSavePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName))
        {
            _snackbarManager.Notify("Please enter a name for the custom preset.");
            return;
        }

        var preset = ActiveColorGrading.ToPreset(NewPresetName.Trim(), NewPresetDescription.Trim());
        await ColorPresetManager.SaveCustomPresetAsync(preset);
        RefreshColorPresetsList();
        SelectedColorPresetName = preset.Name;
        IsSavePresetPopupOpen = false;
        _snackbarManager.Notify($"Saved custom preset '{preset.Name}'.");
    }

    [RelayCommand]
    public async Task DeleteCurrentPresetAsync()
    {
        var preset = ColorPresetManager.GetPreset(SelectedColorPresetName);
        if (preset == null || preset.IsBuiltIn)
        {
            _snackbarManager.Notify("Built-in presets cannot be deleted.");
            return;
        }

        await ColorPresetManager.DeleteCustomPresetAsync(preset.Name);
        RefreshColorPresetsList();
        SelectedColorPresetName = "Neutral / Custom";
        _snackbarManager.Notify($"Deleted custom preset '{preset.Name}'.");
    }

    [RelayCommand]
    public async Task ExportPresetsAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("JSON Files (*.json)") { Patterns = ["*.json"] },
        };
        var path = await _dialogManager.PromptSaveFilePathAsync(fileTypes, "855Media_Presets.json");
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ColorPresetManager.ExportPresetsAsync(path);
            _snackbarManager.Notify("Color presets exported successfully.");
        }
    }

    [RelayCommand]
    public async Task ImportPresetsAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("JSON Files (*.json)") { Patterns = ["*.json"] },
        };
        var path = await _dialogManager.PromptOpenFilePathAsync(fileTypes);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var count = await ColorPresetManager.ImportPresetsAsync(path);
            RefreshColorPresetsList();
            _snackbarManager.Notify($"Imported {count} custom preset(s).");
        }
    }

    [RelayCommand]
    public void ToggleBasicExposure() => IsBasicExposureExpanded = !IsBasicExposureExpanded;

    [RelayCommand]
    public void ToggleColorWheels() => IsColorWheelsExpanded = !IsColorWheelsExpanded;

    [RelayCommand]
    public void ToggleFilmEmulation() => IsFilmEmulationExpanded = !IsFilmEmulationExpanded;

    partial void OnSelectedPresetNameChanged(string value)
    {
        var preset = PresetManager.GetPreset(value);
        if (preset != null)
        {
            EnableDenoise = preset.EnableDenoise;
            EnableDeinterlace = preset.EnableDeinterlace;
            ActiveColorGrading.Brightness = preset.Brightness;
            ActiveColorGrading.Contrast = preset.Contrast;
            ActiveColorGrading.Saturation = preset.Saturation;
            ActiveColorGrading.AutoNormalize = preset.AutoNormalize;
            if (SelectedJob != null)
            {
                SelectedJob.EnableDenoise = preset.EnableDenoise;
                SelectedJob.EnableDeinterlace = preset.EnableDeinterlace;
                SelectedJob.ActivePresetName = preset.Name;
            }
            _snackbarManager.Notify($"Applied preset: {preset.Name}");
            RequestPreviewUpdate(0);
        }
    }

    partial void OnEnableDenoiseChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableDenoise = value;
        }
    }

    partial void OnEnableDeinterlaceChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableDeinterlace = value;
        }
    }

    [RelayCommand]
    public void ZoomIn() => ZoomScale = Math.Min(4.0, Math.Round(ZoomScale + 0.5, 1));

    [RelayCommand]
    public void ZoomOut() => ZoomScale = Math.Max(1.0, Math.Round(ZoomScale - 0.5, 1));

    [RelayCommand]
    public void ResetZoom() => ZoomScale = 1.0;

    [RelayCommand]
    public void SetZoom(double scale) => ZoomScale = Math.Clamp(scale, 1.0, 4.0);

    partial void OnIsColorGradingPanelOpenChanged(bool value)
    {
        if (value)
        {
            RequestPreviewUpdate(0);
        }
    }

    partial void OnActiveColorGradingChanged(
        ColorGradingSettings? oldValue,
        ColorGradingSettings newValue
    )
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnColorGradingSettingsChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnColorGradingSettingsChanged;
        }
        RequestPreviewUpdate(0);
    }

    private void OnColorGradingSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        RequestPreviewUpdate(250);
    }

    partial void OnPreviewTimestampSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedPreviewTimestamp));
        RequestPreviewUpdate(200);
    }

    partial void OnVideoDurationSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedPreviewTimestamp));
    }

    partial void OnSelectedJobChanged(UpscaleJob? value)
    {
        ActiveColorGrading = value?.ColorGrading ?? GlobalColorGrading;
        if (value != null)
        {
            EnableDenoise = value.EnableDenoise;
            EnableDeinterlace = value.EnableDeinterlace;
            if (!string.IsNullOrWhiteSpace(value.ActivePresetName))
            {
                _selectedPresetName = value.ActivePresetName;
                OnPropertyChanged(nameof(SelectedPresetName));
            }
        }
        OnPropertyChanged(nameof(IsEditingJobGrading));
        _lastProbedVideoPath = null;
        RequestPreviewUpdate(0);
    }

    partial void OnMaxConcurrencyChanged(int value)
    {
        _queueManager.MaxConcurrency = value;
        _snackbarManager.Notify($"Concurrency set to {value} parallel worker(s).");
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("Video Files")
            {
                Patterns =
                [
                    "*.mp4",
                    "*.mov",
                    "*.mkv",
                    "*.avi",
                    "*.webm",
                    "*.flv",
                    "*.wmv",
                    "*.m4v",
                ],
            },
            new FilePickerFileType("All Files") { Patterns = ["*.*"] },
        };

        var selectedPaths = await _dialogManager.PromptOpenFilePathsAsync(fileTypes);
        if (selectedPaths.Count == 0)
            return;

        await IngestFilePathsAsync(selectedPaths);
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        var dirPath = await _dialogManager.PromptDirectoryPathAsync(OutputDirectory);
        if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
            return;

        var videoFiles = Directory
            .EnumerateFiles(dirPath, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .ToArray();

        if (videoFiles.Length == 0)
        {
            _snackbarManager.Notify(
                "No supported video files were found in the selected directory."
            );
            return;
        }

        await IngestFilePathsAsync(videoFiles);
    }

    public async Task HandleDroppedFilesAsync(IEnumerable<string> paths)
    {
        var validFiles = new List<string>();

        foreach (var path in paths)
        {
            if (File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                validFiles.Add(path);
            }
            else if (Directory.Exists(path))
            {
                var filesInDir = Directory
                    .EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)));
                validFiles.AddRange(filesInDir);
            }
        }

        if (validFiles.Count > 0)
        {
            await IngestFilePathsAsync(validFiles);
        }
        else
        {
            _snackbarManager.Notify("Dropped items did not contain supported video files.");
        }
    }

    private async Task IngestFilePathsAsync(IEnumerable<string> filePaths)
    {
        int count = 0;
        foreach (var path in filePaths)
        {
            if (Jobs.Any(j => string.Equals(j.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var job = new UpscaleJob
            {
                FilePath = path,
                OutputDirectory = OutputDirectory,
                TargetResolution = SelectedTargetResolution,
                Codec = SelectedCodec,
                HardwareAcceleration = SelectedHardwareAcceleration,
                ColorGrading = GlobalColorGrading.Clone(),
                EnableDenoise = EnableDenoise,
                EnableDeinterlace = EnableDeinterlace,
                ActivePresetName = SelectedPresetName,
                Status = UpscaleJobStatus.Queued,
            };

            await _queueManager.EnqueueJobAsync(job);
            count++;
        }

        if (count > 0)
        {
            _snackbarManager.Notify($"Added {count} video(s) to the upscale queue.");
        }
    }

    [RelayCommand]
    public async Task SelectOutputDirectoryAsync()
    {
        var dir = await _dialogManager.PromptDirectoryPathAsync(OutputDirectory);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            OutputDirectory = dir;
            _snackbarManager.Notify($"Output folder updated to: {dir}");
        }
    }

    [RelayCommand]
    public async Task TogglePauseResumeAsync()
    {
        if (IsPaused)
        {
            _queueManager.Resume();
            IsPaused = false;
            _snackbarManager.Notify("Batch queue resumed.");
        }
        else
        {
            await _queueManager.PauseAsync();
            IsPaused = true;
            _snackbarManager.Notify("Batch queue paused.");
        }
    }

    [RelayCommand]
    public void CancelJob(UpscaleJob? job)
    {
        if (job is null)
            return;
        _queueManager.CancelJob(job);
        _snackbarManager.Notify($"Canceled job: {job.FileName}");
    }

    [RelayCommand]
    public async Task RetryJobAsync(UpscaleJob? job)
    {
        if (job is null)
            return;
        await _queueManager.RetryJobAsync(job);
        _snackbarManager.Notify($"Re-queued job: {job.FileName}");
    }

    [RelayCommand]
    public void MoveJobUp(UpscaleJob? job)
    {
        if (job is null)
            return;
        _queueManager.MoveUp(job);
    }

    [RelayCommand]
    public void MoveJobDown(UpscaleJob? job)
    {
        if (job is null)
            return;
        _queueManager.MoveDown(job);
    }

    [RelayCommand]
    public void CancelAll()
    {
        _queueManager.CancelAll();
        _snackbarManager.Notify("All running and queued upscale jobs canceled.");
    }

    [RelayCommand]
    public void ClearCompleted()
    {
        _queueManager.ClearCompleted();
        _snackbarManager.Notify("Cleared finished jobs from the queue.");
    }

    [RelayCommand]
    public void ViewDetailedLog(UpscaleJob? job)
    {
        if (job is null)
            return;
        SelectedJob = job;
        LogViewerContent = !string.IsNullOrWhiteSpace(job.DetailedLog)
            ? job.DetailedLog
            : (job.ErrorMessage ?? "No log entries available.");
        IsLogViewerVisible = true;
    }

    [RelayCommand]
    public void CloseLogViewer()
    {
        IsLogViewerVisible = false;
        LogViewerContent = null;
    }

    [RelayCommand]
    public void ToggleColorGradingPanel()
    {
        IsColorGradingPanelOpen = !IsColorGradingPanelOpen;
    }

    [RelayCommand]
    public async Task SelectLutFileAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("3D LUT Files (*.cube)") { Patterns = ["*.cube"] },
            new FilePickerFileType("All Files") { Patterns = ["*.*"] },
        };

        var selectedPaths = await _dialogManager.PromptOpenFilePathsAsync(fileTypes);
        if (selectedPaths.Count > 0 && File.Exists(selectedPaths[0]))
        {
            ActiveColorGrading.LutPath = selectedPaths[0];
            _snackbarManager.Notify($"Loaded 3D LUT: {Path.GetFileName(selectedPaths[0])}");
        }
    }

    [RelayCommand]
    public void ClearLutFile()
    {
        ActiveColorGrading.LutPath = null;
        _snackbarManager.Notify("Cleared 3D LUT.");
    }

    [RelayCommand]
    public void ResetColorGrading()
    {
        ActiveColorGrading.Reset();
        _snackbarManager.Notify("Color grading settings reset to defaults.");
    }

    [RelayCommand]
    public void ApplyColorGradingToAll()
    {
        int count = 0;
        foreach (var job in Jobs)
        {
            if (job.Status == UpscaleJobStatus.Queued || job.Status == UpscaleJobStatus.Paused)
            {
                job.ColorGrading.CopyFrom(ActiveColorGrading);
                count++;
            }
        }
        _snackbarManager.Notify($"Applied color grading settings to {count} queued video(s).");
    }

    [RelayCommand]
    public void SelectJobForGrading(UpscaleJob? job)
    {
        if (job is null)
            return;
        SelectedJob = job;
        IsColorGradingPanelOpen = true;
    }

    [RelayCommand]
    public void SwitchToGlobalGrading()
    {
        SelectedJob = null;
        ActiveColorGrading = GlobalColorGrading;
    }

    [RelayCommand]
    public void RefreshPreview()
    {
        RequestPreviewUpdate(0);
    }

    public void RequestPreviewUpdate(int delayMs = 250)
    {
        if (!IsColorGradingPanelOpen)
            return;

        var targetVideo =
            SelectedJob?.FilePath ?? Jobs.FirstOrDefault(j => File.Exists(j.FilePath))?.FilePath;
        if (string.IsNullOrWhiteSpace(targetVideo) || !File.Exists(targetVideo))
        {
            HasPreviewVideo = false;
            PreviewImage = null;
            return;
        }

        HasPreviewVideo = true;

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _previewCts = cts;

        _ = GeneratePreviewInternalAsync(targetVideo, delayMs, cts.Token);
    }

    private async Task GeneratePreviewInternalAsync(
        string videoPath,
        int delayMs,
        CancellationToken token
    )
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }

            IsPreviewLoading = true;
            PreviewError = null;

            // Probe duration if not yet probed for this video
            if (
                VideoDurationSeconds <= 0
                || !string.Equals(
                    _lastProbedVideoPath,
                    videoPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                var duration = await _previewService.GetVideoDurationAsync(videoPath, token);
                if (duration.TotalSeconds > 0)
                {
                    VideoDurationSeconds = duration.TotalSeconds;
                    _lastProbedVideoPath = videoPath;
                    if (PreviewTimestampSeconds > VideoDurationSeconds)
                    {
                        PreviewTimestampSeconds = Math.Min(1.0, VideoDurationSeconds);
                    }
                }
            }

            var filterChain = ActiveColorGrading.BuildFilterString();
            var timestamp = TimeSpan.FromSeconds(Math.Max(0.0, PreviewTimestampSeconds));

            var bytes = await _previewService.GenerateSplitScreenPreviewAsync(
                videoPath,
                filterChain,
                timestamp,
                splitRatio: SplitDividerRatio,
                maxWidth: 960,
                cancellationToken: token
            );

            if (token.IsCancellationRequested)
                return;

            if (bytes != null && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);
                PreviewImage = bitmap;
                PreviewError = null;
            }
            else
            {
                PreviewError = "Unable to render preview frame at this timestamp.";
            }
        }
        catch (OperationCanceledException)
        {
            // Debounced cancellation is expected
        }
        catch (Exception ex)
        {
            PreviewError = ex.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsPreviewLoading = false;
            }
        }
    }
}
