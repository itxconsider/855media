using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Upscaling;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    private readonly VideoUpscaleService _upscaleService;
    private readonly DialogManager _dialogManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly SettingsService _settingsService;
    private readonly HardwareMonitorService? _hardwareMonitor;
    private readonly VideoPreviewService _previewService = new();
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _debouncedSaveCts;
    private string? _lastProbedVideoPath;
    private bool _isRestoringSettings;

    [ObservableProperty]
    private UpscaleTargetResolution _selectedTargetResolution = UpscaleTargetResolution.Hd1080p;

    [ObservableProperty]
    private AspectRatioMode _selectedTargetAspectRatio = AspectRatioMode.Original;

    public AspectRatioMode[] AvailableAspectRatios { get; } = Enum.GetValues<AspectRatioMode>();

    [ObservableProperty]
    private SmartTrackingMode _selectedTrackingMode = SmartTrackingMode.StaticCenter;

    public SmartTrackingMode[] AvailableTrackingModes { get; } =
        Enum.GetValues<SmartTrackingMode>();

    [ObservableProperty]
    private UpscaleVideoCodec _selectedCodec = UpscaleVideoCodec.H264;

    [ObservableProperty]
    private HardwareAccelerationMode _selectedHardwareAcceleration = HardwareAccelerationMode.Auto;

    [ObservableProperty]
    private TargetFramerate _selectedTargetFramerate = TargetFramerate.Original;

    public IReadOnlyList<TargetFramerate> AvailableTargetFramerates { get; } =
        Enum.GetValues<TargetFramerate>();

    [ObservableProperty]
    private string _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    [ObservableProperty]
    private string? _scratchDirectory;

    [ObservableProperty]
    private bool _isFaceRestorationAvailable = VideoUpscaleService.IsFaceRestorationAvailable;

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
    private int _selectedStudioTab;

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
    private bool _enableMicroZoom;

    [ObservableProperty]
    private double _microZoomPercent = 3.0;

    [ObservableProperty]
    private SmartZoomMode _selectedZoomMode = SmartZoomMode.ActionAnchored;

    public SmartZoomMode[] AvailableZoomModes { get; } = Enum.GetValues<SmartZoomMode>();

    [ObservableProperty]
    private RenderSpeedMode _selectedSpeedMode = RenderSpeedMode.Balanced;

    public IReadOnlyList<RenderSpeedMode> AvailableSpeedModes { get; } =
        Enum.GetValues<RenderSpeedMode>();

    [ObservableProperty]
    private double _selectedPlaybackSpeed = 1.0;

    public IReadOnlyList<double> AvailablePlaybackSpeeds { get; } =
    [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0];

    [ObservableProperty]
    private UpscaleAudioMode _selectedAudioMode = UpscaleAudioMode.CopyOriginal;

    public IReadOnlyList<UpscaleAudioMode> AvailableAudioModes { get; } =
        Enum.GetValues<UpscaleAudioMode>();

    [ObservableProperty]
    private UpscaleModelType _selectedModelType = UpscaleModelType.RealWorld;

    [ObservableProperty]
    private bool _enableFacialClarity = true;

    [ObservableProperty]
    private bool _enableFaceRestoration;

    [ObservableProperty]
    private double _faceRestorationFidelity = 0.7;

    public IReadOnlyList<UpscaleModelType> AvailableModelTypes { get; } =
        Enum.GetValues<UpscaleModelType>();

    [ObservableProperty]
    private PostBatchAction _selectedPostBatchAction = PostBatchAction.DoNothing;

    [ObservableProperty]
    private string _realtimeHardwareStatus = "GPU: 0% | VRAM: 0GB | CPU: 0%";

    [ObservableProperty]
    private double _hardwareCpuPercent;

    [ObservableProperty]
    private double _hardwareGpuPercent;

    [ObservableProperty]
    private double _hardwareVramPercent;

    [ObservableProperty]
    private string _hardwareVramText = string.Empty;

    [ObservableProperty]
    private int _hardwareGpuTemp;

    [ObservableProperty]
    private bool _hasHardwareGpuTemp;

    [ObservableProperty]
    private string _selectedColorPresetName = "Neutral / Custom";

    [ObservableProperty]
    private bool _isBasicExposureExpanded = true;

    [ObservableProperty]
    private bool _isColorWheelsExpanded = true;

    [ObservableProperty]
    private bool _isFilmEmulationExpanded = true;

    [ObservableProperty]
    private bool _showHistogram = true;

    [ObservableProperty]
    private string _redHistogramPath = string.Empty;

    [ObservableProperty]
    private string _greenHistogramPath = string.Empty;

    [ObservableProperty]
    private string _blueHistogramPath = string.Empty;

    [ObservableProperty]
    private string _lumaHistogramPath = string.Empty;

    [ObservableProperty]
    private bool _hasHistogramData;

    [ObservableProperty]
    private bool _enableSplitAndUpscale;

    [ObservableProperty]
    private bool _mergeAfterUpscale = true;

    [ObservableProperty]
    private SplitMode _selectedSplitMode = SplitMode.InHalf;

    [ObservableProperty]
    private int _customSplitPartCount = 2;

    [ObservableProperty]
    private double _customSplitSegmentDurationSeconds = 60.0;

    [ObservableProperty]
    private bool _isCustomSplitFlyoutOpen;

    public IReadOnlyList<SplitMode> AvailableSplitModes { get; } = Enum.GetValues<SplitMode>();

    public CustomSplitOptions GetCurrentSplitOptions() =>
        new()
        {
            Mode = SelectedSplitMode,
            PartCount = CustomSplitPartCount,
            SegmentDurationSeconds = CustomSplitSegmentDurationSeconds,
        };

    // Camera & Metadata Normalization Settings
    [ObservableProperty]
    private CameraProfileType _selectedCameraProfileType = CameraProfileType.CleanNormalized;

    [ObservableProperty]
    private string? _cameraMake;

    [ObservableProperty]
    private string? _cameraModel;

    [ObservableProperty]
    private string? _cameraSoftware;

    [ObservableProperty]
    private string? _cameraLensModel;

    [ObservableProperty]
    private string? _cameraFocalLength;

    [ObservableProperty]
    private string? _cameraFNumber;

    [ObservableProperty]
    private string? _cameraArtist;

    [ObservableProperty]
    private string? _cameraCopyright;

    [ObservableProperty]
    private bool _cameraInjectTimestamp = true;

    public IReadOnlyList<CameraProfileType> AvailableCameraProfileTypes { get; } =
        Enum.GetValues<CameraProfileType>();

    public CameraMetadataSettings GetCurrentCameraMetadataSettings() =>
        new()
        {
            ProfileType = SelectedCameraProfileType,
            Make = CameraMake,
            Model = CameraModel,
            Software = CameraSoftware,
            LensModel = CameraLensModel,
            FocalLength = CameraFocalLength,
            FNumber = CameraFNumber,
            Artist = CameraArtist,
            Copyright = CameraCopyright,
            InjectCurrentTimestamp = CameraInjectTimestamp,
        };

    [RelayCommand]
    public void ToggleCustomSplitFlyout()
    {
        IsCustomSplitFlyoutOpen = !IsCustomSplitFlyoutOpen;
    }

    [RelayCommand]
    public void SetSplitPartCount(object? parameter)
    {
        if (parameter is int count)
        {
            CustomSplitPartCount = Math.Clamp(count, 2, 50);
        }
        else if (
            parameter is string str
            && int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
        )
        {
            CustomSplitPartCount = Math.Clamp(parsed, 2, 50);
        }
    }

    [RelayCommand]
    public void SetSplitDuration(object? parameter)
    {
        if (parameter is double sec)
        {
            CustomSplitSegmentDurationSeconds = Math.Clamp(sec, 5.0, 7200.0);
        }
        else if (parameter is int secInt)
        {
            CustomSplitSegmentDurationSeconds = Math.Clamp(secInt, 5.0, 7200.0);
        }
        else if (
            parameter is string str
            && double.TryParse(
                str,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed
            )
        )
        {
            CustomSplitSegmentDurationSeconds = Math.Clamp(parsed, 5.0, 7200.0);
        }
    }

    [RelayCommand]
    public void IncrementSplitPartCount()
    {
        CustomSplitPartCount = Math.Min(50, CustomSplitPartCount + 1);
    }

    [RelayCommand]
    public void DecrementSplitPartCount()
    {
        CustomSplitPartCount = Math.Max(2, CustomSplitPartCount - 1);
    }

    [RelayCommand]
    public void IncrementSplitDuration()
    {
        CustomSplitSegmentDurationSeconds = Math.Min(
            7200.0,
            CustomSplitSegmentDurationSeconds + 10.0
        );
    }

    [RelayCommand]
    public void DecrementSplitDuration()
    {
        CustomSplitSegmentDurationSeconds = Math.Max(5.0, CustomSplitSegmentDurationSeconds - 10.0);
    }

    [RelayCommand]
    public void SetPlaybackSpeed(object? parameter)
    {
        if (parameter is double d)
        {
            SelectedPlaybackSpeed = d;
        }
        else if (
            parameter is string s
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        )
        {
            SelectedPlaybackSpeed = parsed;
        }
    }

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

    public int QueuedJobsCount => Jobs.Count(j => j.Status == UpscaleJobStatus.Queued);
    public int ProcessingJobsCount => Jobs.Count(j => j.Status == UpscaleJobStatus.Processing);
    public int CompletedJobsCount => Jobs.Count(j => j.Status == UpscaleJobStatus.Complete);
    public int FailedJobsCount => Jobs.Count(j => j.Status == UpscaleJobStatus.Failed);
    public bool HasActiveJobs =>
        Jobs.Any(j =>
            j.Status == UpscaleJobStatus.Processing || j.Status == UpscaleJobStatus.Queued
        );
    public bool HasProcessingJob => Jobs.Any(j => j.Status == UpscaleJobStatus.Processing);
    public bool HasCompletedJobs => Jobs.Any(j => j.Status == UpscaleJobStatus.Complete);

    public double OverallProgressPercent
    {
        get
        {
            if (Jobs.Count == 0)
                return 0;
            return Math.Round(
                Jobs.Average(j => j.Status == UpscaleJobStatus.Complete ? 100.0 : j.Progress),
                1
            );
        }
    }

    public string QueueStatusSummary
    {
        get
        {
            if (Jobs.Count == 0)
                return "Queue is empty";
            var processing = ProcessingJobsCount;
            var queued = QueuedJobsCount;
            var complete = CompletedJobsCount;
            if (processing > 0)
            {
                return $"{processing} Processing • {queued} Queued • {complete} Completed";
            }
            if (IsPaused && queued > 0)
            {
                return $"Paused • {queued} Queued • {complete} Completed";
            }
            if (queued > 0)
            {
                return $"{queued} Queued • {complete} Completed";
            }
            return $"{complete} Completed";
        }
    }

    public bool IsShorts9x16Active
    {
        get =>
            SelectedTargetAspectRatio
                is AspectRatioMode.Vertical916Crop
                    or AspectRatioMode.Vertical916BlurredCanvas;
        set =>
            SelectedTargetAspectRatio = value
                ? AspectRatioMode.Vertical916Crop
                : AspectRatioMode.Original;
    }

    public bool IsSquare1x1Active
    {
        get => SelectedTargetAspectRatio == AspectRatioMode.Square11;
        set =>
            SelectedTargetAspectRatio = value ? AspectRatioMode.Square11 : AspectRatioMode.Original;
    }

    public bool IsSmooth60FpsActive
    {
        get => SelectedTargetFramerate == TargetFramerate.Fps60;
        set => SelectedTargetFramerate = value ? TargetFramerate.Fps60 : TargetFramerate.Original;
    }

    public void NotifyQueueStatsChanged()
    {
        OnPropertyChanged(nameof(QueuedJobsCount));
        OnPropertyChanged(nameof(ProcessingJobsCount));
        OnPropertyChanged(nameof(CompletedJobsCount));
        OnPropertyChanged(nameof(FailedJobsCount));
        OnPropertyChanged(nameof(HasActiveJobs));
        OnPropertyChanged(nameof(HasProcessingJob));
        OnPropertyChanged(nameof(HasCompletedJobs));
        OnPropertyChanged(nameof(OverallProgressPercent));
        OnPropertyChanged(nameof(QueueStatusSummary));
    }

    private void OnJobsCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e
    )
    {
        if (e.NewItems != null)
        {
            foreach (UpscaleJob job in e.NewItems)
            {
                job.PropertyChanged += OnJobPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (UpscaleJob job in e.OldItems)
            {
                job.PropertyChanged -= OnJobPropertyChanged;
            }
        }
        NotifyQueueStatsChanged();
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpscaleJob.Status) or nameof(UpscaleJob.Progress))
        {
            NotifyQueueStatsChanged();
        }
    }

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

    public ModuleReadiness HardwareReadiness =>
        ModuleHardwareCheck.CheckUpscaler(HardwareDetector.GetGpuInfo());

    public string GpuStatusDescription
    {
        get
        {
            var info = HardwareDetector.GetGpuInfo();
            var rtxBadge = info.HasRtx ? " [RTX Tensor Cores Ready]" : "";
            return $"{info.Name} ({info.DedicatedVramGb:F1} GB VRAM){rtxBadge} - Tile: {(info.SafeTileSize == 0 ? "Full (0)" : info.SafeTileSize.ToString())}";
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
        LocalizationManager localizationManager,
        SettingsService settingsService,
        VideoUpscaleService? upscaleService = null,
        HardwareMonitorService? hardwareMonitor = null
    )
    {
        _queueManager = queueManager;
        _upscaleService = upscaleService ?? queueManager.UpscaleService;
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        LocalizationManager = localizationManager;
        _settingsService = settingsService;
        _hardwareMonitor = hardwareMonitor;
        _activeColorGrading = GlobalColorGrading;
        GlobalColorGrading.PropertyChanged += OnColorGradingSettingsChanged;
        _queueManager.BatchCompleted += OnBatchCompleted;

        _queueManager.Jobs.CollectionChanged += OnJobsCollectionChanged;
        foreach (var job in _queueManager.Jobs)
        {
            job.PropertyChanged += OnJobPropertyChanged;
        }

        if (_hardwareMonitor != null)
        {
            UpdateRealtimeMetrics(_hardwareMonitor.CurrentMetrics);
            _hardwareMonitor.MetricsUpdated += metrics =>
            {
                Dispatcher.UIThread.Post(() => UpdateRealtimeMetrics(metrics));
            };
        }

        RefreshColorPresetsList();
        RestoreSettingsFromService();

        _ = InitializeQueueAsync();
    }

    private void ScheduleDebouncedSaveSettings()
    {
        if (_isRestoringSettings)
            return;

        _debouncedSaveCts?.Cancel();
        _debouncedSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debouncedSaveCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    _settingsService.Save();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        });
    }

    private void RestoreSettingsFromService()
    {
        _isRestoringSettings = true;
        try
        {
            if (
                !string.IsNullOrWhiteSpace(_settingsService.UpscalerOutputDirectory)
                && Directory.Exists(_settingsService.UpscalerOutputDirectory)
            )
            {
                OutputDirectory = _settingsService.UpscalerOutputDirectory;
            }
            else
            {
                var defaultVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                OutputDirectory = Directory.Exists(defaultVideos)
                    ? defaultVideos
                    : AppContext.BaseDirectory;
            }

            SelectedTargetResolution = _settingsService.UpscalerTargetResolution;
            SelectedTargetAspectRatio = _settingsService.UpscalerTargetAspectRatio;
            SelectedTrackingMode = _settingsService.UpscalerTrackingMode;
            SelectedCodec = _settingsService.UpscalerCodec;
            SelectedHardwareAcceleration = _settingsService.UpscalerHardwareAcceleration;
            SelectedTargetFramerate = _settingsService.UpscalerTargetFramerate;
            MaxConcurrency = Math.Clamp(_settingsService.UpscalerMaxConcurrency, 1, 4);
            _queueManager.MaxConcurrency = MaxConcurrency;
            ScratchDirectory = _settingsService.UpscalerScratchDirectory;
            _upscaleService.ScratchDirectory = ScratchDirectory;

            if (
                !_settingsService.UpscalerHasConfiguredModelType
                && HardwareDetector.GetGpuInfo().HasRtx
            )
            {
                SelectedModelType = UpscaleModelType.NvidiaRtx;
                _settingsService.UpscalerModelType = UpscaleModelType.NvidiaRtx;
            }
            else
            {
                SelectedModelType = _settingsService.UpscalerModelType;
            }

            SelectedPresetName = _settingsService.UpscalerPresetName;
            SelectedPostBatchAction = _settingsService.UpscalerPostBatchAction;

            EnableFacialClarity = _settingsService.UpscalerEnableFacialClarity;
            EnableFaceRestoration = _settingsService.UpscalerEnableFaceRestoration;
            FaceRestorationFidelity = _settingsService.UpscalerFaceRestorationFidelity;
            EnableDenoise = _settingsService.UpscalerEnableDenoise;
            EnableDeinterlace = _settingsService.UpscalerEnableDeinterlace;
            EnableMicroZoom = _settingsService.UpscalerEnableMicroZoom;
            MicroZoomPercent = _settingsService.UpscalerMicroZoomPercent;
            SelectedZoomMode = _settingsService.UpscalerZoomMode;
            SelectedSpeedMode = _settingsService.UpscalerSpeedMode;
            SelectedPlaybackSpeed =
                _settingsService.UpscalerPlaybackSpeed > 0
                    ? _settingsService.UpscalerPlaybackSpeed
                    : 1.0;
            SelectedAudioMode = _settingsService.UpscalerAudioMode;

            EnableSplitAndUpscale = _settingsService.UpscalerEnableSplitAndUpscale;
            MergeAfterUpscale = _settingsService.UpscalerMergeAfterUpscale;
            SelectedSplitMode = _settingsService.UpscalerSplitMode;
            CustomSplitPartCount = _settingsService.UpscalerCustomSplitPartCount;
            CustomSplitSegmentDurationSeconds =
                _settingsService.UpscalerCustomSplitSegmentDurationSeconds;

            SelectedCameraProfileType = _settingsService.UpscalerCameraProfileType;
            CameraMake = _settingsService.UpscalerCameraMake;
            CameraModel = _settingsService.UpscalerCameraModel;
            CameraSoftware = _settingsService.UpscalerCameraSoftware;
            CameraLensModel = _settingsService.UpscalerCameraLensModel;
            CameraFocalLength = _settingsService.UpscalerCameraFocalLength;
            CameraFNumber = _settingsService.UpscalerCameraFNumber;
            CameraArtist = _settingsService.UpscalerCameraArtist;
            CameraCopyright = _settingsService.UpscalerCameraCopyright;
            CameraInjectTimestamp = _settingsService.UpscalerCameraInjectTimestamp;

            IsColorGradingPanelOpen = _settingsService.UpscalerIsColorGradingPanelOpen;
            SplitDividerRatio = _settingsService.UpscalerSplitDividerRatio;
            ZoomScale = _settingsService.UpscalerZoomScale;
            IsBasicExposureExpanded = _settingsService.UpscalerIsBasicExposureExpanded;
            IsColorWheelsExpanded = _settingsService.UpscalerIsColorWheelsExpanded;
            IsFilmEmulationExpanded = _settingsService.UpscalerIsFilmEmulationExpanded;
            ShowHistogram = _settingsService.UpscalerShowHistogram;

            GlobalColorGrading.Brightness = _settingsService.UpscalerColorBrightness;
            GlobalColorGrading.Contrast = _settingsService.UpscalerColorContrast;
            GlobalColorGrading.Saturation = _settingsService.UpscalerColorSaturation;
            GlobalColorGrading.Gamma = _settingsService.UpscalerColorGamma;
            GlobalColorGrading.Vibrance = _settingsService.UpscalerColorVibrance;
            GlobalColorGrading.AutoNormalize = _settingsService.UpscalerColorAutoNormalize;
            GlobalColorGrading.ColorTemperature = _settingsService.UpscalerColorTemperature;
            GlobalColorGrading.ShadowRed = _settingsService.UpscalerColorShadowRed;
            GlobalColorGrading.ShadowGreen = _settingsService.UpscalerColorShadowGreen;
            GlobalColorGrading.ShadowBlue = _settingsService.UpscalerColorShadowBlue;
            GlobalColorGrading.MidtoneRed = _settingsService.UpscalerColorMidtoneRed;
            GlobalColorGrading.MidtoneGreen = _settingsService.UpscalerColorMidtoneGreen;
            GlobalColorGrading.MidtoneBlue = _settingsService.UpscalerColorMidtoneBlue;
            GlobalColorGrading.HighlightRed = _settingsService.UpscalerColorHighlightRed;
            GlobalColorGrading.HighlightGreen = _settingsService.UpscalerColorHighlightGreen;
            GlobalColorGrading.HighlightBlue = _settingsService.UpscalerColorHighlightBlue;
            GlobalColorGrading.ToneCurve = _settingsService.UpscalerColorToneCurve;
            GlobalColorGrading.FilmGrain = _settingsService.UpscalerColorFilmGrain;
            GlobalColorGrading.Vignette = _settingsService.UpscalerColorVignette;
            GlobalColorGrading.LutPath = _settingsService.UpscalerColorLutPath;
            GlobalColorGrading.LutOpacity = _settingsService.UpscalerColorLutOpacity;

            if (!string.IsNullOrWhiteSpace(_settingsService.UpscalerColorPresetName))
            {
                SelectedColorPresetName = _settingsService.UpscalerColorPresetName;
            }
        }
        finally
        {
            _isRestoringSettings = false;
        }
    }

    private async Task InitializeQueueAsync()
    {
        await _queueManager.InitializeFromDiskAsync();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsPaused = _queueManager.IsPaused;
            if (Jobs.Count > 0 && SelectedJob == null)
            {
                SelectedJob = Jobs.FirstOrDefault();
            }
            NotifyQueueStatsChanged();
        });
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
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerSplitDividerRatio = value;
            ScheduleDebouncedSaveSettings();
        }
        RequestPreviewUpdate(150);
    }

    partial void OnSelectedColorPresetNameChanged(string value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerColorPresetName = value;
            ScheduleDebouncedSaveSettings();
        }

        var preset = ColorPresetManager.GetPreset(value);
        if (preset != null && !_isRestoringSettings)
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

    [RelayCommand]
    public void ToggleHistogram() => ShowHistogram = !ShowHistogram;

    partial void OnOutputDirectoryChanged(string value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerOutputDirectory = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedTargetResolutionChanged(UpscaleTargetResolution value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerTargetResolution = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedTargetAspectRatioChanged(AspectRatioMode value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.TargetAspectRatio = value;
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.TargetAspectRatio = value;
            }
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerTargetAspectRatio = value;
            ScheduleDebouncedSaveSettings();
        }
        OnPropertyChanged(nameof(IsShorts9x16Active));
        OnPropertyChanged(nameof(IsSquare1x1Active));
        RequestPreviewUpdate(150);
    }

    partial void OnSelectedTrackingModeChanged(SmartTrackingMode value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.TrackingMode = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerTrackingMode = value;
            ScheduleDebouncedSaveSettings();
        }
        RequestPreviewUpdate(150);
    }

    partial void OnSelectedCodecChanged(UpscaleVideoCodec value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCodec = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedHardwareAccelerationChanged(HardwareAccelerationMode value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerHardwareAcceleration = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedTargetFramerateChanged(TargetFramerate value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.TargetFramerate = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerTargetFramerate = value;
            ScheduleDebouncedSaveSettings();
        }
        OnPropertyChanged(nameof(IsSmooth60FpsActive));
    }

    partial void OnSelectedAudioModeChanged(UpscaleAudioMode value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.AudioMode = value;
        }
        else
        {
            foreach (var j in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                j.AudioMode = value;
            }
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerAudioMode = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedPostBatchActionChanged(PostBatchAction value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerPostBatchAction = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedSplitModeChanged(SplitMode value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.SplitOptions ??= new CustomSplitOptions();
            SelectedJob.SplitOptions.Mode = value;
            SelectedJob.NotifySplitChanged();
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.SplitOptions ??= new CustomSplitOptions();
                job.SplitOptions.Mode = value;
                job.NotifySplitChanged();
            }
        }

        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerSplitMode = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCustomSplitPartCountChanged(int value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.SplitOptions ??= new CustomSplitOptions();
            SelectedJob.SplitOptions.PartCount = value;
            SelectedJob.NotifySplitChanged();
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.SplitOptions ??= new CustomSplitOptions();
                job.SplitOptions.PartCount = value;
                job.NotifySplitChanged();
            }
        }

        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCustomSplitPartCount = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCustomSplitSegmentDurationSecondsChanged(double value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.SplitOptions ??= new CustomSplitOptions();
            SelectedJob.SplitOptions.SegmentDurationSeconds = value;
            SelectedJob.NotifySplitChanged();
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.SplitOptions ??= new CustomSplitOptions();
                job.SplitOptions.SegmentDurationSeconds = value;
                job.NotifySplitChanged();
            }
        }

        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCustomSplitSegmentDurationSeconds = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnIsBasicExposureExpandedChanged(bool value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerIsBasicExposureExpanded = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnIsColorWheelsExpandedChanged(bool value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerIsColorWheelsExpanded = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnIsFilmEmulationExpandedChanged(bool value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerIsFilmEmulationExpanded = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnShowHistogramChanged(bool value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerShowHistogram = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedPresetNameChanged(string value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerPresetName = value;
            ScheduleDebouncedSaveSettings();
        }

        var preset = PresetManager.GetPreset(value);
        if (preset != null && !_isRestoringSettings)
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
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerEnableDenoise = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnEnableDeinterlaceChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableDeinterlace = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerEnableDeinterlace = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnEnableMicroZoomChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableMicroZoom = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerEnableMicroZoom = value;
            ScheduleDebouncedSaveSettings();
        }
        RequestPreviewUpdate(100);
    }

    partial void OnMicroZoomPercentChanged(double value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.MicroZoomPercent = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerMicroZoomPercent = value;
            ScheduleDebouncedSaveSettings();
        }
        RequestPreviewUpdate(150);
    }

    partial void OnSelectedZoomModeChanged(SmartZoomMode value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.ZoomMode = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerZoomMode = value;
            ScheduleDebouncedSaveSettings();
        }
        RequestPreviewUpdate(150);
    }

    partial void OnSelectedSpeedModeChanged(RenderSpeedMode value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.SpeedMode = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerSpeedMode = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedPlaybackSpeedChanged(double value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.PlaybackSpeed = value;
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.PlaybackSpeed = value;
            }
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerPlaybackSpeed = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnEnableSplitAndUpscaleChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableSplitAndUpscale = value;
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.EnableSplitAndUpscale = value;
            }
        }

        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerEnableSplitAndUpscale = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnMergeAfterUpscaleChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.MergeAfterUpscale = value;
        }
        else
        {
            foreach (var job in Jobs.Where(j => j.Status == UpscaleJobStatus.Queued))
            {
                job.MergeAfterUpscale = value;
            }
        }

        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerMergeAfterUpscale = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnSelectedModelTypeChanged(UpscaleModelType value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.ModelType = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerHasConfiguredModelType = true;
            _settingsService.UpscalerModelType = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnEnableFacialClarityChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableFacialClarity = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerEnableFacialClarity = value;
            ScheduleDebouncedSaveSettings();
        }
        RequestPreviewUpdate(100);
    }

    partial void OnScratchDirectoryChanged(string? value)
    {
        _upscaleService.ScratchDirectory = value;
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerScratchDirectory = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnEnableFaceRestorationChanged(bool value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.EnableFaceRestoration = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerEnableFaceRestoration = value;
            ScheduleDebouncedSaveSettings();
            if (value && !IsFaceRestorationAvailable)
            {
                _snackbarManager.Notify(
                    "CodeFormer / GFPGAN binary not detected in tools directory. Facial clarity filter will be applied as fallback."
                );
            }
        }
    }

    partial void OnSelectedCameraProfileTypeChanged(CameraProfileType value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraProfileType = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraMakeChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraMake = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraModelChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraModel = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraSoftwareChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraSoftware = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraLensModelChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraLensModel = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraFocalLengthChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraFocalLength = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraFNumberChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraFNumber = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraArtistChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraArtist = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraCopyrightChanged(string? value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraCopyright = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnCameraInjectTimestampChanged(bool value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerCameraInjectTimestamp = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnFaceRestorationFidelityChanged(double value)
    {
        if (SelectedJob != null)
        {
            SelectedJob.FaceRestorationFidelity = value;
        }
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerFaceRestorationFidelity = value;
            ScheduleDebouncedSaveSettings();
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

    partial void OnZoomScaleChanged(double value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerZoomScale = value;
            ScheduleDebouncedSaveSettings();
        }
    }

    partial void OnIsColorGradingPanelOpenChanged(bool value)
    {
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerIsColorGradingPanelOpen = value;
            ScheduleDebouncedSaveSettings();
        }
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
        if (ActiveColorGrading == GlobalColorGrading && !_isRestoringSettings)
        {
            _settingsService.UpscalerColorBrightness = GlobalColorGrading.Brightness;
            _settingsService.UpscalerColorContrast = GlobalColorGrading.Contrast;
            _settingsService.UpscalerColorSaturation = GlobalColorGrading.Saturation;
            _settingsService.UpscalerColorGamma = GlobalColorGrading.Gamma;
            _settingsService.UpscalerColorVibrance = GlobalColorGrading.Vibrance;
            _settingsService.UpscalerColorAutoNormalize = GlobalColorGrading.AutoNormalize;
            _settingsService.UpscalerColorTemperature = GlobalColorGrading.ColorTemperature;
            _settingsService.UpscalerColorShadowRed = GlobalColorGrading.ShadowRed;
            _settingsService.UpscalerColorShadowGreen = GlobalColorGrading.ShadowGreen;
            _settingsService.UpscalerColorShadowBlue = GlobalColorGrading.ShadowBlue;
            _settingsService.UpscalerColorMidtoneRed = GlobalColorGrading.MidtoneRed;
            _settingsService.UpscalerColorMidtoneGreen = GlobalColorGrading.MidtoneGreen;
            _settingsService.UpscalerColorMidtoneBlue = GlobalColorGrading.MidtoneBlue;
            _settingsService.UpscalerColorHighlightRed = GlobalColorGrading.HighlightRed;
            _settingsService.UpscalerColorHighlightGreen = GlobalColorGrading.HighlightGreen;
            _settingsService.UpscalerColorHighlightBlue = GlobalColorGrading.HighlightBlue;
            _settingsService.UpscalerColorToneCurve = GlobalColorGrading.ToneCurve;
            _settingsService.UpscalerColorFilmGrain = GlobalColorGrading.FilmGrain;
            _settingsService.UpscalerColorVignette = GlobalColorGrading.Vignette;
            _settingsService.UpscalerColorLutPath = GlobalColorGrading.LutPath;
            _settingsService.UpscalerColorLutOpacity = GlobalColorGrading.LutOpacity;
            ScheduleDebouncedSaveSettings();
        }

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
            SelectedTargetAspectRatio = value.TargetAspectRatio;
            SelectedTrackingMode = value.TrackingMode;
            EnableMicroZoom = value.EnableMicroZoom;
            MicroZoomPercent = value.MicroZoomPercent;
            SelectedZoomMode = value.ZoomMode;
            SelectedSpeedMode = value.SpeedMode;
            SelectedPlaybackSpeed = value.PlaybackSpeed > 0 ? value.PlaybackSpeed : 1.0;
            SelectedAudioMode = value.AudioMode;
            EnableDenoise = value.EnableDenoise;
            EnableDeinterlace = value.EnableDeinterlace;
            EnableSplitAndUpscale = value.EnableSplitAndUpscale;
            MergeAfterUpscale = value.MergeAfterUpscale;
            if (value.SplitOptions != null)
            {
                SelectedSplitMode = value.SplitOptions.Mode;
                CustomSplitPartCount =
                    value.SplitOptions.PartCount > 0 ? value.SplitOptions.PartCount : 2;
                CustomSplitSegmentDurationSeconds =
                    value.SplitOptions.SegmentDurationSeconds > 0
                        ? value.SplitOptions.SegmentDurationSeconds
                        : 60.0;
            }
            SelectedModelType = value.ModelType;
            EnableFacialClarity = value.EnableFacialClarity;
            EnableFaceRestoration = value.EnableFaceRestoration;
            FaceRestorationFidelity = value.FaceRestorationFidelity;
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
        if (!_isRestoringSettings)
        {
            _settingsService.UpscalerMaxConcurrency = value;
            ScheduleDebouncedSaveSettings();
            _snackbarManager.Notify($"Concurrency set to {value} parallel worker(s).");
        }
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

    private UpscaleJob CreateJobFromSettings(
        string path,
        string? caption = null,
        bool enableSplit = false
    )
    {
        return new UpscaleJob
        {
            FilePath = path,
            OutputDirectory = OutputDirectory,
            ScratchDirectory = ScratchDirectory,
            TargetResolution = SelectedTargetResolution,
            TargetAspectRatio = SelectedTargetAspectRatio,
            TrackingMode = SelectedTrackingMode,
            Codec = SelectedCodec,
            HardwareAcceleration = SelectedHardwareAcceleration,
            TargetFramerate = SelectedTargetFramerate,
            ColorGrading = GlobalColorGrading.Clone(),
            CameraMetadata = GetCurrentCameraMetadataSettings(),
            EnableDenoise = EnableDenoise,
            EnableDeinterlace = EnableDeinterlace,
            EnableMicroZoom = EnableMicroZoom,
            MicroZoomPercent = MicroZoomPercent,
            ZoomMode = SelectedZoomMode,
            SpeedMode = SelectedSpeedMode,
            PlaybackSpeed = SelectedPlaybackSpeed,
            AudioMode = SelectedAudioMode,
            ActivePresetName = SelectedPresetName,
            ModelType = SelectedModelType,
            EnableFacialClarity = EnableFacialClarity,
            EnableFaceRestoration = EnableFaceRestoration,
            FaceRestorationFidelity = FaceRestorationFidelity,
            EnableSplitAndUpscale = enableSplit,
            MergeAfterUpscale = MergeAfterUpscale,
            SplitOptions = GetCurrentSplitOptions(),
            Caption = caption,
            Status = UpscaleJobStatus.Queued,
        };
    }

    private async Task IngestFilePathsAsync(IEnumerable<string> filePaths)
    {
        int count = 0;

        foreach (var path in filePaths)
        {
            if (Jobs.Any(j => string.Equals(j.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Detect existing caption text file alongside the input video
            string? baseCaption = null;
            var txtPath = Path.ChangeExtension(path, ".txt");
            if (File.Exists(txtPath))
            {
                try
                {
                    baseCaption = (await File.ReadAllTextAsync(txtPath)).Trim();
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(baseCaption))
            {
                baseCaption = Path.GetFileNameWithoutExtension(path);
            }

            var job = CreateJobFromSettings(path, baseCaption, enableSplit: EnableSplitAndUpscale);
            await _queueManager.EnqueueJobAsync(job);
            count++;
        }

        if (count > 0)
        {
            _snackbarManager.Notify($"Added {count} video job(s) to the upscale queue.");
        }
    }

    [RelayCommand]
    public async Task SplitQueuedJobAsync(UpscaleJob? job)
    {
        if (job is null || job.Status != UpscaleJobStatus.Queued)
        {
            _snackbarManager.Notify("Only queued jobs can be split into parts.");
            return;
        }

        try
        {
            var splitOutputDir =
                !string.IsNullOrWhiteSpace(job.OutputDirectory)
                && Directory.Exists(job.OutputDirectory)
                    ? job.OutputDirectory
                    : (
                        !string.IsNullOrWhiteSpace(OutputDirectory)
                        && Directory.Exists(OutputDirectory)
                            ? OutputDirectory
                            : AppContext.BaseDirectory
                    );

            var splitOptions = job.SplitOptions ?? GetCurrentSplitOptions();
            _snackbarManager.Notify($"Slicing '{job.FileName}' into parts...");
            var splitResult = await SplitAndUpscalePipeline.SplitVideoCustomAsync(
                job.FilePath,
                splitOutputDir,
                splitOptions,
                null,
                CancellationToken.None
            );

            // Remove original job from queue
            _queueManager.CancelJob(job);
            Jobs.Remove(job);

            foreach (var part in splitResult.Parts)
            {
                var partJob = new UpscaleJob
                {
                    FilePath = part.VideoPath,
                    OutputDirectory = job.OutputDirectory,
                    ScratchDirectory = job.ScratchDirectory ?? ScratchDirectory,
                    TargetResolution = job.TargetResolution,
                    TargetAspectRatio = job.TargetAspectRatio,
                    TrackingMode = job.TrackingMode,
                    Codec = job.Codec,
                    HardwareAcceleration = job.HardwareAcceleration,
                    TargetFramerate = job.TargetFramerate,
                    ColorGrading = job.ColorGrading.Clone(),
                    CameraMetadata = job.CameraMetadata.Clone(),
                    EnableDenoise = job.EnableDenoise,
                    EnableDeinterlace = job.EnableDeinterlace,
                    EnableMicroZoom = job.EnableMicroZoom,
                    MicroZoomPercent = job.MicroZoomPercent,
                    ZoomMode = job.ZoomMode,
                    SpeedMode = job.SpeedMode,
                    PlaybackSpeed = job.PlaybackSpeed,
                    AudioMode = job.AudioMode,
                    InputWidth = job.InputWidth,
                    InputHeight = job.InputHeight,
                    VideoFps = job.VideoFps,
                    ActivePresetName = job.ActivePresetName,
                    ModelType = job.ModelType,
                    EnableFacialClarity = job.EnableFacialClarity,
                    EnableFaceRestoration = job.EnableFaceRestoration,
                    FaceRestorationFidelity = job.FaceRestorationFidelity,
                    EnableSplitAndUpscale = false,
                    MergeAfterUpscale = false,
                    PartNumber = part.PartNumber,
                    OriginalBaseName = Path.GetFileNameWithoutExtension(job.FilePath),
                    Caption = part.Caption,
                    DeleteSourceAfterUpscale = true,
                    Status = UpscaleJobStatus.Queued,
                };

                await _queueManager.EnqueueJobAsync(partJob);
            }

            _snackbarManager.Notify(
                $"Separated '{job.FileName}' into {splitResult.Parts.Count} parts (original moved to 'original/' folder)."
            );
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to split video: {ex.Message}");
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
    public async Task SelectScratchDirectoryAsync()
    {
        var currentDir =
            !string.IsNullOrWhiteSpace(ScratchDirectory) && Directory.Exists(ScratchDirectory)
                ? ScratchDirectory
                : Path.GetTempPath();

        var dir = await _dialogManager.PromptDirectoryPathAsync(currentDir);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            ScratchDirectory = dir;
            _snackbarManager.Notify($"Scratch / working directory updated to: {dir}");
        }
    }

    [RelayCommand]
    public void ResetScratchDirectory()
    {
        ScratchDirectory = null;
        _snackbarManager.Notify("Scratch directory reset to default system temp folder.");
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
    public void OpenOutputFolder()
    {
        var target =
            !string.IsNullOrWhiteSpace(OutputDirectory) && Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        if (Directory.Exists(target))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _snackbarManager.Notify($"Unable to open folder: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void OpenJobFile(UpscaleJob? job)
    {
        if (job is null)
            return;
        var path = job.OutputFilePath;
        if (!File.Exists(path))
        {
            path = job.FilePath;
        }
        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _snackbarManager.Notify($"Unable to open video: {ex.Message}");
            }
        }
        else
        {
            _snackbarManager.Notify("Output file does not exist yet.");
        }
    }

    [RelayCommand]
    public void OpenJobFolder(UpscaleJob? job)
    {
        if (job is null)
            return;
        var path = job.OutputFilePath;
        if (File.Exists(path) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true,
                    }
                );
                return;
            }
            catch { }
        }

        var dir =
            !string.IsNullOrWhiteSpace(job.OutputDirectory) && Directory.Exists(job.OutputDirectory)
                ? job.OutputDirectory
                : Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _snackbarManager.Notify($"Unable to open folder: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void SelectStudioTab(object? tab)
    {
        if (tab is int intVal)
        {
            SelectedStudioTab = intVal;
        }
        else if (tab is string strVal && int.TryParse(strVal, out var parsed))
        {
            SelectedStudioTab = parsed;
        }
        IsColorGradingPanelOpen = true;
    }

    [RelayCommand]
    public void CloseStudio()
    {
        IsColorGradingPanelOpen = false;
    }

    [RelayCommand]
    public async Task CopyJobLogAsync()
    {
        if (!string.IsNullOrWhiteSpace(LogViewerContent))
        {
            if (
                Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard != null
            )
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(LogViewerContent);
                _snackbarManager.Notify("Log copied to clipboard.");
            }
        }
    }

    [RelayCommand]
    public void SelectJobForGrading(UpscaleJob? job)
    {
        if (job is null)
            return;
        SelectedJob = job;
        SelectedStudioTab = 0;
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

            var previewFilterParts = new List<string>();
            if (EnableMicroZoom && MicroZoomPercent > 0)
            {
                var zoomFilter = AspectRatioFilterBuilder.BuildMicroZoomFilter(
                    MicroZoomPercent,
                    SelectedZoomMode,
                    SelectedJob?.ActionCentroidX ?? 0.5,
                    SelectedJob?.ActionCentroidY ?? 0.5
                );
                if (!string.IsNullOrWhiteSpace(zoomFilter))
                {
                    previewFilterParts.Add(zoomFilter);
                }
            }
            if (EnableFacialClarity)
            {
                previewFilterParts.Add("unsharp=lx=5:ly=5:la=0.75:cx=3:cy=3:ca=0.3");
                previewFilterParts.Add("noise=c1s=5:c0f=u");
            }
            var colorFilter = ActiveColorGrading.BuildFilterString();
            if (!string.IsNullOrWhiteSpace(colorFilter))
            {
                previewFilterParts.Add(colorFilter);
            }
            var filterChain =
                previewFilterParts.Count > 0 ? string.Join(",", previewFilterParts) : null;
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

                var hist = HistogramCalculator.CalculateFromBmp(
                    bytes,
                    splitRatio: SplitDividerRatio
                );
                RedHistogramPath = hist.RedPath;
                GreenHistogramPath = hist.GreenPath;
                BlueHistogramPath = hist.BluePath;
                LumaHistogramPath = hist.LumaPath;
                HasHistogramData = hist.HasData;
            }
            else
            {
                PreviewError = !string.IsNullOrWhiteSpace(_previewService.LastPreviewError)
                    ? $"Unable to render preview frame: {_previewService.LastPreviewError}"
                    : "Unable to render preview frame at this timestamp.";
                HasHistogramData = false;
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

    private void UpdateRealtimeMetrics(HardwareMetrics metrics)
    {
        HardwareCpuPercent = metrics.CpuUsagePercent;
        HardwareGpuPercent = metrics.GpuUsagePercent;
        HardwareVramPercent = metrics.VramUsagePercent;
        HardwareVramText = $"{metrics.VramUsedGb:F1}/{metrics.VramTotalGb:F0}GB";
        HardwareGpuTemp = metrics.GpuTemperatureC;
        HasHardwareGpuTemp = metrics.GpuTemperatureC > 0;

        var tempStr = metrics.GpuTemperatureC > 0 ? $" | {metrics.GpuTemperatureC}°C" : "";
        RealtimeHardwareStatus =
            $"GPU: {metrics.GpuUsagePercent:0}% | VRAM: {HardwareVramText} | CPU: {metrics.CpuUsagePercent:0}%{tempStr}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debouncedSaveCts?.Cancel();
            _debouncedSaveCts?.Dispose();
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _settingsService.Save();
        }

        base.Dispose(disposing);
    }
}
