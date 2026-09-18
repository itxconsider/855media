using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace _855Media.Core.Dubbing;

public enum DubbingJobStatus
{
    Queued,
    ExtractingAudio,
    SeparatingStems,
    Transcribing,
    Translating,
    SynthesizingSpeech,
    ApplyingVoiceClone,
    Remuxing,
    Completed,
    Failed,
    Canceled,
}

public class MovieCharacter : INotifyPropertyChanged
{
    private string _name = "Character";
    private string _baseVoice = "km-KH-PisethNeural";
    private string _speechRate = "+15%";
    private bool _enableRvc;
    private string? _rvcModelPath;
    private string? _rvcIndexPath;
    private int _pitchShift;
    private string _colorTag = "#3B82F6";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public string BaseVoice
    {
        get => _baseVoice;
        set
        {
            if (_baseVoice != value)
            {
                _baseVoice = value;
                OnPropertyChanged(nameof(BaseVoice));
            }
        }
    }

    public string SpeechRate
    {
        get => _speechRate;
        set
        {
            if (_speechRate != value)
            {
                _speechRate = value;
                OnPropertyChanged(nameof(SpeechRate));
            }
        }
    }

    public bool EnableRvc
    {
        get => _enableRvc;
        set
        {
            if (_enableRvc != value)
            {
                _enableRvc = value;
                OnPropertyChanged(nameof(EnableRvc));
            }
        }
    }

    public string? RvcModelPath
    {
        get => _rvcModelPath;
        set
        {
            if (_rvcModelPath != value)
            {
                _rvcModelPath = value;
                OnPropertyChanged(nameof(RvcModelPath));
            }
        }
    }

    public string? RvcIndexPath
    {
        get => _rvcIndexPath;
        set
        {
            if (_rvcIndexPath != value)
            {
                _rvcIndexPath = value;
                OnPropertyChanged(nameof(RvcIndexPath));
            }
        }
    }

    public int PitchShift
    {
        get => _pitchShift;
        set
        {
            if (_pitchShift != value)
            {
                _pitchShift = value;
                OnPropertyChanged(nameof(PitchShift));
            }
        }
    }

    public string ColorTag
    {
        get => _colorTag;
        set
        {
            if (_colorTag != value)
            {
                _colorTag = value;
                OnPropertyChanged(nameof(ColorTag));
            }
        }
    }

    private string _gender = "Male";
    public string Gender
    {
        get => _gender;
        set
        {
            if (_gender != value)
            {
                _gender = value;
                OnPropertyChanged(nameof(Gender));
            }
        }
    }

    private string _baseSpeechRate = "+15%";
    public string BaseSpeechRate
    {
        get => _baseSpeechRate;
        set
        {
            if (_baseSpeechRate != value)
            {
                _baseSpeechRate = value;
                OnPropertyChanged(nameof(BaseSpeechRate));
                ApplyEmotionPreset(EmotionPreset);
            }
        }
    }

    private string _toneArchetype = "Hero";
    public string ToneArchetype
    {
        get => _toneArchetype;
        set
        {
            if (_toneArchetype != value)
            {
                _toneArchetype = value;
                OnPropertyChanged(nameof(ToneArchetype));
                ApplyToneArchetype(value);
            }
        }
    }

    private double _toneWarmth = 0.25;
    public double ToneWarmth
    {
        get => _toneWarmth;
        set
        {
            if (Math.Abs(_toneWarmth - value) > 0.01)
            {
                _toneWarmth = value;
                OnPropertyChanged(nameof(ToneWarmth));
            }
        }
    }

    private double _toneClarity = 0.35;
    public double ToneClarity
    {
        get => _toneClarity;
        set
        {
            if (Math.Abs(_toneClarity - value) > 0.01)
            {
                _toneClarity = value;
                OnPropertyChanged(nameof(ToneClarity));
            }
        }
    }

    private string _emotionPreset = "Normal";
    public string EmotionPreset
    {
        get => _emotionPreset;
        set
        {
            if (_emotionPreset != value)
            {
                _emotionPreset = value;
                OnPropertyChanged(nameof(EmotionPreset));
                ApplyEmotionPreset(value);
            }
        }
    }

    public void ApplyToneArchetype(string archetype)
    {
        var arch = ActorEmotionEngine.GetArchetype(archetype);
        PitchShift = arch.DefaultPitchShift;
        BaseSpeechRate = arch.DefaultRate;
        ToneWarmth = arch.DefaultWarmth;
        ToneClarity = arch.DefaultClarity;
        _emotionPreset = arch.DefaultEmotion;
        OnPropertyChanged(nameof(EmotionPreset));
        ApplyEmotionPreset(arch.DefaultEmotion);
    }

    public void ApplyEmotionPreset(string preset)
    {
        var cfg = ActorEmotionEngine.GetConfig(preset);
        SpeechRate = ActorEmotionEngine.ComputeEffectiveRate(BaseSpeechRate, cfg.TtsRateOffset);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class SubtitleSegment : INotifyPropertyChanged
{
    private string _originalText = string.Empty;
    private string _khmerText = string.Empty;
    private TimeSpan _startTime;
    private TimeSpan _endTime;
    private Guid? _characterId;
    private string _speakerName = "Hero / Male";
    private string _speakerColor = "#3B82F6";

    public int Index { get; set; }

    public TimeSpan StartTime
    {
        get => _startTime;
        set
        {
            if (_startTime != value)
            {
                _startTime = value;
                OnPropertyChanged(nameof(StartTime));
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(SyncStatus));
                OnPropertyChanged(nameof(SyncStatusColor));
            }
        }
    }

    public TimeSpan EndTime
    {
        get => _endTime;
        set
        {
            if (_endTime != value)
            {
                _endTime = value;
                OnPropertyChanged(nameof(EndTime));
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(SyncStatus));
                OnPropertyChanged(nameof(SyncStatusColor));
            }
        }
    }

    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.FromSeconds(1);
    public double DurationSeconds => Math.Max(0.1, (EndTime - StartTime).TotalSeconds);

    private double _audioDurationSeconds;
    public double AudioDurationSeconds
    {
        get => _audioDurationSeconds;
        set
        {
            if (Math.Abs(_audioDurationSeconds - value) > 0.01)
            {
                _audioDurationSeconds = value;
                OnPropertyChanged(nameof(AudioDurationSeconds));
                OnPropertyChanged(nameof(SyncStatus));
                OnPropertyChanged(nameof(SyncStatusColor));
            }
        }
    }

    public string SyncStatus
    {
        get
        {
            if (AudioDurationSeconds <= 0.05)
                return "Pending";
            var diff = AudioDurationSeconds - DurationSeconds;
            if (diff <= 0.15)
                return $"In-Sync ({AudioDurationSeconds:F1}s)";
            return $"Fit ({AudioDurationSeconds:F1}s)";
        }
    }

    public string SyncStatusColor
    {
        get
        {
            if (AudioDurationSeconds <= 0.05)
                return "#64748B"; // Slate Gray
            var diff = AudioDurationSeconds - DurationSeconds;
            if (diff <= 0.15)
                return "#10B981"; // Emerald Green
            return "#F59E0B"; // Amber
        }
    }

    public string OriginalText
    {
        get => _originalText;
        set
        {
            if (_originalText != value)
            {
                _originalText = value;
                OnPropertyChanged(nameof(OriginalText));
            }
        }
    }

    public string KhmerText
    {
        get => _khmerText;
        set
        {
            if (_khmerText != value)
            {
                _khmerText = value;
                OnPropertyChanged(nameof(KhmerText));
            }
        }
    }

    public Guid? CharacterId
    {
        get => _characterId;
        set
        {
            if (_characterId != value)
            {
                _characterId = value;
                OnPropertyChanged(nameof(CharacterId));
            }
        }
    }

    public string SpeakerName
    {
        get => _speakerName;
        set
        {
            if (_speakerName != value)
            {
                _speakerName = value;
                OnPropertyChanged(nameof(SpeakerName));
            }
        }
    }

    public string SpeakerColor
    {
        get => _speakerColor;
        set
        {
            if (_speakerColor != value)
            {
                _speakerColor = value;
                OnPropertyChanged(nameof(SpeakerColor));
            }
        }
    }

    private string? _audioClipPath;
    public string? AudioClipPath
    {
        get => _audioClipPath;
        set
        {
            if (_audioClipPath != value)
            {
                _audioClipPath = value;
                OnPropertyChanged(nameof(AudioClipPath));
            }
        }
    }

    private string? _thumbnailPath;
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath != value)
            {
                _thumbnailPath = value;
                OnPropertyChanged(nameof(ThumbnailPath));
            }
        }
    }

    private string _emotion = ActorEmotionEngine.EmotionNormal;
    public string Emotion
    {
        get => _emotion;
        set
        {
            if (_emotion != value)
            {
                _emotion = value;
                OnPropertyChanged(nameof(Emotion));
                OnPropertyChanged(nameof(EmotionColor));
                OnPropertyChanged(nameof(EmotionIcon));
                OnPropertyChanged(nameof(EmotionDisplayName));
            }
        }
    }

    public string EmotionColor => ActorEmotionEngine.GetConfig(Emotion).Color;
    public string EmotionIcon => ActorEmotionEngine.GetConfig(Emotion).Icon;
    public string EmotionDisplayName => ActorEmotionEngine.GetConfig(Emotion).DisplayName;

    private string _detectedGender = "Unknown";
    public string DetectedGender
    {
        get => _detectedGender;
        set
        {
            if (_detectedGender != value)
            {
                _detectedGender = value;
                OnPropertyChanged(nameof(DetectedGender));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class DubbingJob : INotifyPropertyChanged
{
    private DubbingJobStatus _status = DubbingJobStatus.Queued;
    private double _progress;
    private string _statusMessage = "Ready";
    private string _detailedLog = string.Empty;

    public Guid Id { get; } = Guid.NewGuid();

    public string VideoFilePath { get; set; } = string.Empty;

    public string FileName => Path.GetFileName(VideoFilePath);

    public string OutputFilePath { get; set; } = string.Empty;

    public string SourceLanguage { get; set; } = "Auto";

    public string SelectedVoice { get; set; } = "km-KH-PisethNeural";

    public bool EnableVoiceCloning { get; set; }

    public string? RvcModelPath { get; set; }

    public string? RvcIndexPath { get; set; }

    public int PitchShift { get; set; } // Semitones: -12 to +12

    public int RvcConcurrency { get; set; } = 4; // Number of parallel RVC GPU workers (e.g. 2, 4, 6)

    public double BgmVolume { get; set; } = 0.25; // 25% background music ducking

    public double VoiceVolume { get; set; } = 1.0; // 100% dubbed speech volume

    public bool EnableDynamicDucking { get; set; } = true; // Sidechain ducking during speech

    public bool EnableAiStemSeparation { get; set; } // GPU Demucs / MDX-Net stem isolation

    public bool EnableLoudnessNormalization { get; set; } = true; // EBU R128 (-16 LUFS) broadcast loudness mastering

    public bool EnableSmartTimeStretch { get; set; } = true; // Auto-fit translated speech to visual scene duration

    public ObservableCollection<MovieCharacter> Characters { get; } = [];

    public ObservableCollection<SubtitleSegment> Segments { get; } = [];

    public DubbingJobStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public bool IsActive =>
        Status != DubbingJobStatus.Queued
        && Status != DubbingJobStatus.Completed
        && Status != DubbingJobStatus.Failed
        && Status != DubbingJobStatus.Canceled;

    public string StatusColor =>
        Status switch
        {
            DubbingJobStatus.Completed => "#10B981",
            DubbingJobStatus.Failed => "#EF4444",
            DubbingJobStatus.Canceled => "#94A3B8",
            DubbingJobStatus.Queued => "#64748B",
            _ => "#3B82F6",
        };

    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.01)
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    public string DetailedLog
    {
        get => _detailedLog;
        set
        {
            if (_detailedLog != value)
            {
                _detailedLog = value;
                OnPropertyChanged(nameof(DetailedLog));
            }
        }
    }

    public string? ExtractedDialoguePath { get; set; }

    public string? ExtractedBgmPath { get; set; }

    public string? DubbedAudioPath { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
