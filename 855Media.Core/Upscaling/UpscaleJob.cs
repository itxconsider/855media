using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace _855Media.Core.Upscaling;

public enum UpscaleJobStatus
{
    Queued,
    Processing,
    Paused,
    Complete,
    Failed,
    Canceled,
}

public enum UpscaleTargetResolution
{
    [System.ComponentModel.DataAnnotations.Display(Name = "1080p (Full HD)")]
    Hd1080p,

    [System.ComponentModel.DataAnnotations.Display(Name = "4K (UHD)")]
    Uhd4k,

    [System.ComponentModel.DataAnnotations.Display(Name = "Scale 2x")]
    Scale2x,

    [System.ComponentModel.DataAnnotations.Display(Name = "Scale 4x")]
    Scale4x,

    [System.ComponentModel.DataAnnotations.Display(Name = "Original 1x (Fast Re-Frame & Filter)")]
    Original1x,
}

public enum UpscaleVideoCodec
{
    H264,
    H265,
    Av1,
}

public enum HardwareAccelerationMode
{
    Auto,
    NvidiaNvenc,
    IntelQsv,
    AmdAmf,
    CpuSoftware,
}

public enum UpscaleModelType
{
    [System.ComponentModel.DataAnnotations.Display(
        Name = "Real-World / People (RealESRGAN_x4plus)"
    )]
    RealWorld,

    [System.ComponentModel.DataAnnotations.Display(
        Name = "Animation / Cartoons (realesr-animevideov3)"
    )]
    Animation,

    [System.ComponentModel.DataAnnotations.Display(
        Name = "Fast Native / Pass-Through (No AI - GPU Filters & Re-Frame)"
    )]
    FastNative,
}

public class UpscaleJob : INotifyPropertyChanged
{
    private UpscaleJobStatus _status = UpscaleJobStatus.Queued;
    private double _progress;
    private long _currentFrame;
    private long _totalFrames;
    private double _fps;
    private TimeSpan? _elapsedTime;
    private TimeSpan? _estimatedRemaining;
    private string? _errorMessage;
    private string? _detailedLog;
    private int _priority;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string FilePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    public string OutputDirectory { get; set; } = string.Empty;

    public string? ScratchDirectory { get; set; }

    private string? _customOutputFilePath;
    public string? CustomOutputFilePath
    {
        get => _customOutputFilePath;
        set => SetField(ref _customOutputFilePath, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string OutputFilePath =>
        !string.IsNullOrWhiteSpace(_customOutputFilePath)
            ? _customOutputFilePath
            : Path.Combine(
                string.IsNullOrWhiteSpace(OutputDirectory)
                    ? Path.GetDirectoryName(FilePath) ?? "."
                    : OutputDirectory,
                $"{Path.GetFileNameWithoutExtension(FilePath)}_{TargetResolution.ToString().ToLowerInvariant()}{DetermineSafeOutputExtension(FilePath, Codec)}"
            );

    public static string DetermineSafeOutputExtension(string inputPath, UpscaleVideoCodec codec)
    {
        var inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
        // Incompatible or legacy containers that cannot mux modern H264/H265/AV1 streams or causes ffmpeg failures
        if (inputExt is ".flv" or ".avi" or ".wmv" or ".webm" or ".vob" or ".ts" or ".3gp")
        {
            return ".mp4";
        }

        if (string.IsNullOrWhiteSpace(inputExt))
        {
            return ".mp4";
        }

        return inputExt;
    }

    public CustomSplitOptions? SplitOptions { get; set; }

    private bool _enableSplitAndUpscale;
    public bool EnableSplitAndUpscale
    {
        get => _enableSplitAndUpscale;
        set
        {
            if (SetField(ref _enableSplitAndUpscale, value))
            {
                OnPropertyChanged(nameof(SplitSummary));
            }
        }
    }

    private bool _mergeAfterUpscale = true;
    public bool MergeAfterUpscale
    {
        get => _mergeAfterUpscale;
        set
        {
            if (SetField(ref _mergeAfterUpscale, value))
            {
                OnPropertyChanged(nameof(SplitSummary));
            }
        }
    }

    private string? _caption;
    public string? Caption
    {
        get => _caption;
        set => SetField(ref _caption, value);
    }

    private int? _partNumber;
    public int? PartNumber
    {
        get => _partNumber;
        set
        {
            if (SetField(ref _partNumber, value))
            {
                OnPropertyChanged(nameof(SplitSummary));
                OnPropertyChanged(nameof(PartBadge));
                OnPropertyChanged(nameof(HasPartBadge));
            }
        }
    }

    public string? OriginalBaseName { get; set; }

    public bool DeleteSourceAfterUpscale { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSplitPart => PartNumber.HasValue;

    [System.Text.Json.Serialization.JsonIgnore]
    public string PartBadge => PartNumber.HasValue ? $"Part {PartNumber.Value}" : string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPartBadge => PartNumber.HasValue;

    [System.Text.Json.Serialization.JsonIgnore]
    public string SplitSummary =>
        PartNumber.HasValue
            ? $"Part {PartNumber.Value}"
            : (
                EnableSplitAndUpscale
                    ? (
                        MergeAfterUpscale
                            ? "Split & Merge"
                            : (
                                SplitOptions != null && SplitOptions.Mode == SplitMode.ByPartCount
                                    ? $"Split ({SplitOptions.PartCount} parts)"
                                    : (
                                        SplitOptions != null
                                        && SplitOptions.Mode == SplitMode.ByDuration
                                            ? $"Split ({SplitOptions.SegmentDurationSeconds:0}s)"
                                            : "Split (2 parts)"
                                    )
                            )
                    )
                    : "Direct"
            );

    public string? InputResolution { get; set; } = "Probing...";

    public UpscaleTargetResolution TargetResolution { get; set; } = UpscaleTargetResolution.Hd1080p;

    public AspectRatioMode TargetAspectRatio { get; set; } = AspectRatioMode.Original;

    public SmartTrackingMode TrackingMode { get; set; } = SmartTrackingMode.StaticCenter;

    public UpscaleVideoCodec Codec { get; set; } = UpscaleVideoCodec.H264;

    public HardwareAccelerationMode HardwareAcceleration { get; set; } =
        HardwareAccelerationMode.Auto;

    public ColorGradingSettings ColorGrading { get; set; } = new();

    public CameraMetadataSettings CameraMetadata { get; set; } = new();

    public bool EnableDenoise { get; set; }

    public bool EnableDeinterlace { get; set; }

    public bool EnableMicroZoom { get; set; }

    public double MicroZoomPercent { get; set; } = 3.0;

    public SmartZoomMode ZoomMode { get; set; } = SmartZoomMode.ActionAnchored;

    public double ActionCentroidX { get; set; } = 0.5;

    public double ActionCentroidY { get; set; } = 0.5;

    public string? ActivePresetName { get; set; }

    private UpscaleModelType _modelType = UpscaleModelType.RealWorld;
    public UpscaleModelType ModelType
    {
        get => _modelType;
        set => SetField(ref _modelType, value);
    }

    private bool _enableFacialClarity = true;
    public bool EnableFacialClarity
    {
        get => _enableFacialClarity;
        set => SetField(ref _enableFacialClarity, value);
    }

    private bool _enableFaceRestoration;
    public bool EnableFaceRestoration
    {
        get => _enableFaceRestoration;
        set => SetField(ref _enableFaceRestoration, value);
    }

    private double _faceRestorationFidelity = 0.7;
    public double FaceRestorationFidelity
    {
        get => _faceRestorationFidelity;
        set => SetField(ref _faceRestorationFidelity, value);
    }

    public UpscaleJobStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public long CurrentFrame
    {
        get => _currentFrame;
        set => SetField(ref _currentFrame, value);
    }

    public long TotalFrames
    {
        get => _totalFrames;
        set => SetField(ref _totalFrames, value);
    }

    public int InputWidth { get; set; }
    public int InputHeight { get; set; }

    private double _videoFps = 30.0;
    public double VideoFps
    {
        get => _videoFps;
        set => SetField(ref _videoFps, value);
    }

    public double Fps
    {
        get => _fps;
        set => SetField(ref _fps, value);
    }

    public TimeSpan ElapsedTime
    {
        get => _elapsedTime ?? TimeSpan.Zero;
        set => SetField(ref _elapsedTime, value);
    }

    public TimeSpan? EstimatedRemaining
    {
        get => _estimatedRemaining;
        set => SetField(ref _estimatedRemaining, value);
    }

    public DateTimeOffset? StartTime { get; set; }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public string? DetailedLog
    {
        get => _detailedLog;
        set => SetField(ref _detailedLog, value);
    }

    public int Priority
    {
        get => _priority;
        set => SetField(ref _priority, value);
    }

    public int RetryCount { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationTokenSource? Cts { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Diagnostics.Process? ActiveProcess { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
