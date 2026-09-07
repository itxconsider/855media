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
    Hd1080p,
    Uhd4k,
    Scale2x,
    Scale4x,
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
                $"{Path.GetFileNameWithoutExtension(FilePath)}_upscaled_{TargetResolution.ToString().ToLowerInvariant()}{Path.GetExtension(FilePath)}"
            );

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

    [System.Text.Json.Serialization.JsonIgnore]
    public string SplitSummary =>
        EnableSplitAndUpscale
            ? (MergeAfterUpscale ? "Split & Merge" : "Split (2 parts)")
            : "Direct";

    public string? InputResolution { get; set; } = "Probing...";

    public UpscaleTargetResolution TargetResolution { get; set; } = UpscaleTargetResolution.Hd1080p;

    public UpscaleVideoCodec Codec { get; set; } = UpscaleVideoCodec.H264;

    public HardwareAccelerationMode HardwareAcceleration { get; set; } =
        HardwareAccelerationMode.Auto;

    public ColorGradingSettings ColorGrading { get; set; } = new();

    public bool EnableDenoise { get; set; }

    public bool EnableDeinterlace { get; set; }

    public string? ActivePresetName { get; set; }

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
