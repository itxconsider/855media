using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Dubbing;
using _855Media.Core.Utils;
using _855Media.Framework;
using _855Media.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class DubbingViewModel : ViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly SettingsService _settingsService;
    private readonly DubbingPipeline _pipeline = new();
    private readonly RvcInferenceService _rvcService = new();
    private readonly SubtitleTranslationService _subService = new();
    private readonly AudioTranscriptionService _transcriptionService = new();
    private readonly AudioStemSeparationService _stemService = new();
    private readonly GeminiTranslationService _geminiService = new();

    private string? GetFfmpegPath() =>
        !string.IsNullOrWhiteSpace(_settingsService.FFmpegFilePath)
        && File.Exists(_settingsService.FFmpegFilePath)
            ? _settingsService.FFmpegFilePath
            : FFmpeg.TryGetCliFilePath();

    private CancellationTokenSource? _activeCts;

    [ObservableProperty]
    private string _videoFilePath = string.Empty;

    [ObservableProperty]
    private string _outputFilePath = string.Empty;

    [ObservableProperty]
    private string _sourceLanguage = "Auto";

    [ObservableProperty]
    private KhmerTtsVoice _selectedVoice = KhmerTtsVoice.PisethMale;

    [ObservableProperty]
    private bool _enableVoiceCloning;

    [ObservableProperty]
    private RvcModelInfo? _selectedRvcModel;

    [ObservableProperty]
    private int _pitchShift;

    [ObservableProperty]
    private double _bgmVolume = 0.25;

    [ObservableProperty]
    private double _voiceVolume = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectStatusBadge))]
    private string? _currentProjectPath;

    [ObservableProperty]
    private string _projectName = "Untitled Project";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectStatusBadge))]
    private bool _isProjectDirty;

    public string ProjectStatusBadge =>
        string.IsNullOrWhiteSpace(CurrentProjectPath)
            ? "Unsaved Project"
            : (IsProjectDirty ? "Modified •" : "Saved");

    public ModuleReadiness HardwareReadiness =>
        ModuleHardwareCheck.CheckDubbing(HardwareDetector.GetGpuInfo());

    [ObservableProperty]
    private bool _isAutoPreparing;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isTranslating;

    [ObservableProperty]
    private bool _isScanningAudio;

    [ObservableProperty]
    private bool _isDownloadingWhisper;

    [ObservableProperty]
    private double _whisperDownloadProgress;

    [ObservableProperty]
    private bool _isExtractingScenes;

    public bool IsWhisperAvailable =>
        !string.IsNullOrWhiteSpace(AudioTranscriptionService.TryGetWhisperCliPath())
        && !string.IsNullOrWhiteSpace(AudioTranscriptionService.TryGetWhisperModelPath());

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _detailedLog = string.Empty;

    [ObservableProperty]
    private bool _enableAiStemSeparation = true;

    [ObservableProperty]
    private bool _enableDynamicDucking = true;

    [ObservableProperty]
    private bool _enableLoudnessNormalization = true;

    [ObservableProperty]
    private bool _enableSmartTimeStretch = true;

    [ObservableProperty]
    private MovieCharacter? _selectedCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSegment))]
    [NotifyPropertyChangedFor(nameof(SelectedSegmentCharacter))]
    [NotifyPropertyChangedFor(nameof(SelectedSegmentEmotion))]
    private SubtitleSegment? _selectedSegment;

    public bool HasSelectedSegment => SelectedSegment != null;

    public MovieCharacter? SelectedSegmentCharacter
    {
        get =>
            Characters.FirstOrDefault(c => c.Id == SelectedSegment?.CharacterId)
            ?? Characters.FirstOrDefault();
        set
        {
            if (SelectedSegment != null && value != null)
            {
                SelectedSegment.CharacterId = value.Id;
                SelectedSegment.SpeakerName = value.Name;
                SelectedSegment.SpeakerColor = value.ColorTag;
                IsProjectDirty = true;
                OnPropertyChanged();
            }
        }
    }

    public ActorEmotionConfig? SelectedSegmentEmotion
    {
        get =>
            AvailableEmotions.FirstOrDefault(e =>
                string.Equals(e.Name, SelectedSegment?.Emotion, StringComparison.OrdinalIgnoreCase)
            ) ?? AvailableEmotions.FirstOrDefault();
        set
        {
            if (SelectedSegment != null && value != null)
            {
                SelectedSegment.Emotion = value.Name;
                IsProjectDirty = true;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<MovieCharacter> Characters { get; } = [];

    public ObservableCollection<SubtitleSegment> Segments { get; } = [];

    public ObservableCollection<DubbingJob> BatchQueue { get; } = [];

    [ObservableProperty]
    private DubbingJob? _selectedBatchJob;

    [ObservableProperty]
    private int _batchMaxConcurrency = 1;

    [ObservableProperty]
    private bool _isBatchProcessing;

    public IReadOnlyList<int> AvailableBatchConcurrencies { get; } = [1, 2, 3];

    private CancellationTokenSource? _batchCts;

    [ObservableProperty]
    private int _rvcConcurrency = 4;

    public IReadOnlyList<int> AvailableRvcConcurrencies { get; } = [1, 2, 3, 4, 6, 8];

    public ObservableCollection<RvcModelInfo> AvailableRvcModels { get; } = [];

    public IReadOnlyList<KhmerTtsVoice> AvailableVoices { get; } = KhmerTtsVoice.All;

    public IReadOnlyList<string> AvailableSourceLanguages { get; } =
    ["Auto", "English", "Chinese", "Vietnamese", "Thai", "Japanese", "Korean", "French", "Spanish"];

    public IReadOnlyList<string> AvailablePacingOptions { get; } =
    ["+25%", "+20%", "+15%", "+10%", "+5%", "0%", "-5%", "-10%"];

    private readonly HashSet<SubtitleSegment> _dataGridSelectedSegments = [];
    private bool _isPropagatingBatchChange;

    public bool HasSelectedRows =>
        Segments.Any(s => s.IsSelected) || _dataGridSelectedSegments.Count > 0;

    public int SelectedSegmentsCount => GetSelectedSegments().Count;

    public bool HasSegments => Segments.Count > 0;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    public IEnumerable<SubtitleSegment> DisplayedSegments
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchFilter))
                return Segments;

            var term = SearchFilter.Trim();
            return Segments.Where(s =>
                (
                    !string.IsNullOrEmpty(s.OriginalText)
                    && s.OriginalText.Contains(term, StringComparison.OrdinalIgnoreCase)
                )
                || (
                    !string.IsNullOrEmpty(s.KhmerText)
                    && s.KhmerText.Contains(term, StringComparison.OrdinalIgnoreCase)
                )
                || (
                    !string.IsNullOrEmpty(s.SpeakerName)
                    && s.SpeakerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                )
                || s.Index.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    partial void OnSearchFilterChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedSegments));
    }

    public bool IsAllSegmentsSelected
    {
        get => Segments.Count > 0 && Segments.All(s => s.IsSelected);
        set
        {
            foreach (var seg in Segments)
            {
                seg.IsSelected = value;
            }
            OnPropertyChanged(nameof(IsAllSegmentsSelected));
            OnPropertyChanged(nameof(HasSelectedRows));
            OnPropertyChanged(nameof(SelectedSegmentsCount));
        }
    }

    private MovieCharacter? _batchSelectedCharacter;
    public MovieCharacter? BatchSelectedCharacter
    {
        get => _batchSelectedCharacter;
        set
        {
            _batchSelectedCharacter = value;
            if (value != null)
            {
                ApplyCharacterToSelectedSegments(value);
                _batchSelectedCharacter = null;
            }
            OnPropertyChanged(nameof(BatchSelectedCharacter));
        }
    }

    private ActorEmotionConfig? _batchSelectedEmotion;
    public ActorEmotionConfig? BatchSelectedEmotion
    {
        get => _batchSelectedEmotion;
        set
        {
            _batchSelectedEmotion = value;
            if (value != null)
            {
                ApplyEmotionToSelectedSegments(value.Name);
                _batchSelectedEmotion = null;
            }
            OnPropertyChanged(nameof(BatchSelectedEmotion));
        }
    }

    public List<SubtitleSegment> GetSelectedSegments()
    {
        var checkedSegments = Segments.Where(s => s.IsSelected).ToList();
        if (checkedSegments.Count > 0)
            return checkedSegments;

        if (_dataGridSelectedSegments.Count > 0)
            return _dataGridSelectedSegments.ToList();

        if (SelectedSegment != null)
            return [SelectedSegment];

        return [];
    }

    public void UpdateDataGridSelection(IEnumerable<SubtitleSegment> items)
    {
        _dataGridSelectedSegments.Clear();
        foreach (var item in items)
        {
            _dataGridSelectedSegments.Add(item);
        }
        OnPropertyChanged(nameof(HasSelectedRows));
        OnPropertyChanged(nameof(SelectedSegmentsCount));
    }

    public RvcModelInfo? SelectedCharacterRvcModel
    {
        get
        {
            if (
                SelectedCharacter == null
                || string.IsNullOrWhiteSpace(SelectedCharacter.RvcModelPath)
            )
                return AvailableRvcModels.FirstOrDefault();

            return AvailableRvcModels.FirstOrDefault(m =>
                    string.Equals(
                        m.PthPath,
                        SelectedCharacter.RvcModelPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        m.Name,
                        Path.GetFileNameWithoutExtension(SelectedCharacter.RvcModelPath),
                        StringComparison.OrdinalIgnoreCase
                    )
                ) ?? AvailableRvcModels.FirstOrDefault();
        }
        set
        {
            if (SelectedCharacter != null && value != null)
            {
                SelectedCharacter.RvcModelPath = value.PthPath;
                SelectedCharacter.RvcIndexPath = value.IndexPath;
                OnPropertyChanged();
            }
        }
    }

    public KhmerTtsVoice? SelectedCharacterBaseVoice
    {
        get =>
            AvailableVoices.FirstOrDefault(v => v.Id == SelectedCharacter?.BaseVoice)
            ?? AvailableVoices.FirstOrDefault();
        set
        {
            if (SelectedCharacter != null && value != null)
            {
                SelectedCharacter.BaseVoice = value.Id;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private int _selectedStudioTab; // 0 = Script & Translation Table, 1 = Multi-Track Timeline, 2 = Voice Cast Director

    [ObservableProperty]
    private double _timelineZoom = 1.0;

    public double TimelineColumnWidth => Math.Clamp(220.0 * TimelineZoom, 140.0, 480.0);

    partial void OnTimelineZoomChanged(double value)
    {
        OnPropertyChanged(nameof(TimelineColumnWidth));
    }

    [RelayCommand]
    public void SelectSegment(SubtitleSegment? seg)
    {
        if (seg != null)
        {
            SelectedSegment = seg;
        }
    }

    [ObservableProperty]
    private bool _isPreSynthesizing;

    [ObservableProperty]
    private double _preSynthesizeProgress;

    [ObservableProperty]
    private string _activeMediaTitle = "Studio Monitor Standby";

    [ObservableProperty]
    private bool _hasActiveMedia;

    [ObservableProperty]
    private bool _isMonitorPlaying;

    [ObservableProperty]
    private string? _currentPlayingMediaPath;

    [ObservableProperty]
    private bool _isCurrentMediaVideo = true;

    public event Action<string, bool, string>? PlayMediaRequested;
    public event Action? StopMediaRequested;

    public IReadOnlyList<ActorEmotionConfig> AvailableEmotions => ActorEmotionEngine.AllEmotions;

    public IReadOnlyList<string> AvailableEmotionPresets => ActorEmotionEngine.EmotionNames;

    public IReadOnlyList<ActorToneArchetype> AvailableToneArchetypes =>
        ActorEmotionEngine.AllArchetypes;

    public IReadOnlyList<string> AvailableArchetypeNames => ActorEmotionEngine.ArchetypeNames;

    public IReadOnlyList<string> AvailableGenders { get; } = ["Male", "Female", "Child"];

    public bool IsGeminiConfigured => !string.IsNullOrWhiteSpace(_settingsService.GeminiApiKey);

    public string ActiveTranslationEngineBadge =>
        IsGeminiConfigured
            ? $"Gemini AI ({_settingsService.GeminiModel})"
            : "Google Translate (Free)";

    public string TranslationTooltip =>
        IsGeminiConfigured
            ? $"Translate using Google Gemini AI ({_settingsService.GeminiModel}) with cinematic phrasing and character continuity"
            : "Translate using fast multi-tier Google Translate. (To use Gemini 2.0 Flash AI, set API key in Settings)";

    public string? SelectedCharacterEmotionPreset
    {
        get => SelectedCharacter?.EmotionPreset ?? AvailableEmotionPresets.FirstOrDefault();
        set
        {
            if (SelectedCharacter != null && !string.IsNullOrWhiteSpace(value))
            {
                SelectedCharacter.EmotionPreset = value;
                OnPropertyChanged();
            }
        }
    }

    public string? SelectedCharacterToneArchetype
    {
        get => SelectedCharacter?.ToneArchetype ?? AvailableArchetypeNames.FirstOrDefault();
        set
        {
            if (SelectedCharacter != null && !string.IsNullOrWhiteSpace(value))
            {
                SelectedCharacter.ToneArchetype = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCharacterEmotionPreset));
            }
        }
    }

    partial void OnSelectedCharacterChanged(MovieCharacter? value)
    {
        OnPropertyChanged(nameof(SelectedCharacterRvcModel));
        OnPropertyChanged(nameof(SelectedCharacterBaseVoice));
        OnPropertyChanged(nameof(SelectedCharacterEmotionPreset));
        OnPropertyChanged(nameof(SelectedCharacterToneArchetype));
    }

    public DubbingViewModel(
        DialogManager dialogManager,
        SnackbarManager snackbarManager,
        SettingsService settingsService
    )
    {
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        _settingsService = settingsService;

        RefreshRvcModels();
        InitDefaultCharacters();

        SubtitleSegment.CharacterResolver = (id, name) =>
        {
            if (Characters == null || Characters.Count == 0)
                return null;

            if (id != null)
            {
                var byId = Characters.FirstOrDefault(c => c.Id == id.Value);
                if (byId != null)
                    return byId;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var byName = Characters.FirstOrDefault(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                if (byName != null)
                    return byName;
            }

            return Characters.FirstOrDefault();
        };

        Segments.CollectionChanged += (s, e) =>
        {
            IsProjectDirty = true;
            if (e.NewItems != null)
            {
                foreach (SubtitleSegment item in e.NewItems)
                {
                    item.PropertyChanged += OnSegmentPropertyChanged;
                    SyncSegmentCharacter(item);
                }
            }
            if (e.OldItems != null)
            {
                foreach (SubtitleSegment item in e.OldItems)
                {
                    item.PropertyChanged -= OnSegmentPropertyChanged;
                }
            }
            OnPropertyChanged(nameof(HasSelectedRows));
            OnPropertyChanged(nameof(SelectedSegmentsCount));
            OnPropertyChanged(nameof(IsAllSegmentsSelected));
            OnPropertyChanged(nameof(HasSegments));
            OnPropertyChanged(nameof(DisplayedSegments));
        };

        Characters.CollectionChanged += (s, e) =>
        {
            IsProjectDirty = true;
            SyncAllSegmentCharacters();
        };
    }

    public void SyncSegmentCharacter(SubtitleSegment seg)
    {
        if (Characters.Count == 0)
            return;
        var matched =
            Characters.FirstOrDefault(c => c.Id == seg.CharacterId)
            ?? Characters.FirstOrDefault(c =>
                string.Equals(c.Name, seg.SpeakerName, StringComparison.OrdinalIgnoreCase)
            )
            ?? Characters.FirstOrDefault();
        if (matched != null && !ReferenceEquals(seg.AssignedCharacter, matched))
        {
            seg.AssignedCharacter = matched;
        }
    }

    public void SyncAllSegmentCharacters()
    {
        if (Characters.Count == 0)
            return;

        var prevPropagating = _isPropagatingBatchChange;
        _isPropagatingBatchChange = true;
        try
        {
            foreach (var seg in Segments)
            {
                SyncSegmentCharacter(seg);
            }
        }
        finally
        {
            _isPropagatingBatchChange = prevPropagating;
        }
    }

    private void OnSegmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SubtitleSegment changedSeg)
            return;

        if (e.PropertyName == nameof(SubtitleSegment.IsSelected))
        {
            OnPropertyChanged(nameof(HasSelectedRows));
            OnPropertyChanged(nameof(SelectedSegmentsCount));
            OnPropertyChanged(nameof(IsAllSegmentsSelected));
            return;
        }

        if (_isPropagatingBatchChange)
            return;

        if (e.PropertyName == nameof(SubtitleSegment.AssignedCharacter))
        {
            var selectedList = GetSelectedSegments();
            if (
                selectedList.Count > 1
                && selectedList.Contains(changedSeg)
                && changedSeg.AssignedCharacter != null
            )
            {
                _isPropagatingBatchChange = true;
                try
                {
                    foreach (var seg in selectedList)
                    {
                        if (seg != changedSeg)
                        {
                            seg.AssignedCharacter = changedSeg.AssignedCharacter;
                        }
                    }
                }
                finally
                {
                    _isPropagatingBatchChange = false;
                }
            }
        }
        else if (
            e.PropertyName == nameof(SubtitleSegment.Emotion)
            || e.PropertyName == nameof(SubtitleSegment.AssignedEmotionConfig)
        )
        {
            var selectedList = GetSelectedSegments();
            if (selectedList.Count > 1 && selectedList.Contains(changedSeg))
            {
                _isPropagatingBatchChange = true;
                try
                {
                    foreach (var seg in selectedList)
                    {
                        if (seg != changedSeg)
                        {
                            seg.Emotion = changedSeg.Emotion;
                        }
                    }
                }
                finally
                {
                    _isPropagatingBatchChange = false;
                }
            }
        }
    }

    partial void OnVideoFilePathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (string.IsNullOrWhiteSpace(ProjectName) || ProjectName == "Untitled Project")
            {
                ProjectName = Path.GetFileNameWithoutExtension(value);
            }
            IsProjectDirty = true;
        }
    }

    private void InitDefaultCharacters()
    {
        var sengDynaModel =
            AvailableRvcModels.FirstOrDefault(m =>
                m.Name.Contains("seng_dyna_v3", StringComparison.OrdinalIgnoreCase)
            )
            ?? AvailableRvcModels.FirstOrDefault(m =>
                m.Name.Contains("seng_dyna", StringComparison.OrdinalIgnoreCase)
            );

        Characters.Add(
            new MovieCharacter
            {
                Name = "Hero (Male)",
                Gender = "Male",
                BaseVoice = "km-KH-PisethNeural",
                ToneArchetype = "Hero",
                SpeechRate = "+15%",
                EnableRvc = sengDynaModel != null,
                RvcModelPath = sengDynaModel?.PthPath,
                RvcIndexPath = sengDynaModel?.IndexPath,
                ColorTag = "#3B82F6",
            }
        );

        Characters.Add(
            new MovieCharacter
            {
                Name = "Heroine (Female)",
                Gender = "Female",
                BaseVoice = "km-KH-SreymomNeural",
                ToneArchetype = "Hero",
                SpeechRate = "+12%",
                EnableRvc = false,
                ColorTag = "#EC4899",
            }
        );

        Characters.Add(
            new MovieCharacter
            {
                Name = "Child / Little Voice (កុមារ)",
                Gender = "Child",
                BaseVoice = "km-KH-SreymomNeural",
                ToneArchetype = "Youth",
                PitchShift = 4,
                SpeechRate = "+18%",
                EnableRvc = false,
                ColorTag = "#F59E0B",
            }
        );

        Characters.Add(
            new MovieCharacter
            {
                Name = "Villain / Deep Voice",
                Gender = "Male",
                BaseVoice = "km-KH-PisethNeural",
                ToneArchetype = "Villain",
                PitchShift = -4,
                SpeechRate = "+8%",
                EnableRvc = false,
                ColorTag = "#EF4444",
            }
        );

        Characters.Add(
            new MovieCharacter
            {
                Name = "Narrator / Extras",
                Gender = "Male",
                BaseVoice = "km-KH-PisethNeural",
                ToneArchetype = "Narrator",
                SpeechRate = "+15%",
                ColorTag = "#10B981",
            }
        );

        SelectedCharacter = Characters.FirstOrDefault();
    }

    [RelayCommand]
    public void SelectCharacter(MovieCharacter? character)
    {
        SelectedCharacter = character;
    }

    [RelayCommand]
    public void SetCharacterColor(string color)
    {
        if (SelectedCharacter != null && !string.IsNullOrWhiteSpace(color))
        {
            SelectedCharacter.ColorTag = color;
            foreach (var seg in Segments)
            {
                if (seg.CharacterId == SelectedCharacter.Id)
                    seg.SpeakerColor = color;
            }
        }
    }

    [RelayCommand]
    public void AddCharacter()
    {
        var nextNum = Characters.Count + 1;
        var colors = new[]
        {
            "#3B82F6",
            "#EC4899",
            "#EF4444",
            "#10B981",
            "#F59E0B",
            "#8B5CF6",
            "#06B6D4",
        };
        var color = colors[(nextNum - 1) % colors.Length];
        var isFemale = nextNum % 2 == 0;

        var c = new MovieCharacter
        {
            Name = $"Character {nextNum}",
            BaseVoice = isFemale ? "km-KH-SreymomNeural" : "km-KH-PisethNeural",
            ColorTag = color,
            SpeechRate = "+15%",
        };
        Characters.Add(c);
        SelectedCharacter = c;
    }

    [RelayCommand]
    public void RemoveCharacter(MovieCharacter? character)
    {
        var target = character ?? SelectedCharacter;
        if (target != null && Characters.Count > 1)
        {
            Characters.Remove(target);
            SelectedCharacter = Characters.FirstOrDefault();
        }
    }

    [RelayCommand]
    public void AssignCharacterToSelected(MovieCharacter? character)
    {
        var targetChar = character ?? SelectedCharacter;
        if (SelectedSegment != null && targetChar != null)
        {
            SelectedSegment.AssignedCharacter = targetChar;
            _snackbarManager.Notify($"Line {SelectedSegment.Index} assigned to {targetChar.Name}");
        }
    }

    [RelayCommand]
    public void ApplyCharacterToAll(MovieCharacter? character)
    {
        var targetChar = character ?? SelectedCharacter;
        if (targetChar == null || Segments.Count == 0)
            return;

        var prevPropagating = _isPropagatingBatchChange;
        _isPropagatingBatchChange = true;
        try
        {
            foreach (var seg in Segments)
            {
                seg.AssignedCharacter = targetChar;
                seg.AudioClipPath = null;
            }
        }
        finally
        {
            _isPropagatingBatchChange = prevPropagating;
        }

        _snackbarManager.Notify(
            $"Synced '{targetChar.Name}' cloned voice across all {Segments.Count} scenes!"
        );
        StatusMessage = $"Assigned '{targetChar.Name}' voice clone to all {Segments.Count} scenes.";
    }

    [RelayCommand]
    public void CycleNextCharacter(SubtitleSegment? seg)
    {
        var target = seg ?? SelectedSegment;
        if (target == null || Characters.Count == 0)
            return;

        var currentIndex = Characters
            .ToList()
            .FindIndex(c => c.Id == target.CharacterId || c.Name == target.SpeakerName);
        var nextIndex = (currentIndex + 1) % Characters.Count;
        var nextChar = Characters[nextIndex];

        target.AssignedCharacter = nextChar;
        target.CharacterId = nextChar.Id;
        target.SpeakerName = nextChar.Name;
        target.SpeakerColor = nextChar.ColorTag;
    }

    [RelayCommand]
    public void SetSegmentCharacter(MovieCharacter? character)
    {
        if (character == null)
            return;
        ApplyCharacterToSelectedSegments(character);
    }

    [RelayCommand]
    public void ApplyCharacterToSelectedSegments(MovieCharacter? targetChar)
    {
        if (targetChar == null)
            return;
        var selected = GetSelectedSegments();
        if (selected.Count == 0 && SelectedSegment != null)
            selected = [SelectedSegment];

        if (selected.Count == 0)
            return;

        _isPropagatingBatchChange = true;
        try
        {
            foreach (var seg in selected)
            {
                seg.AssignedCharacter = targetChar;
                seg.CharacterId = targetChar.Id;
                seg.SpeakerName = targetChar.Name;
                seg.SpeakerColor = targetChar.ColorTag;
            }
        }
        finally
        {
            _isPropagatingBatchChange = false;
        }

        IsProjectDirty = true;
        StatusMessage =
            $"Assigned '{targetChar.Name}' to {selected.Count} selected dialogue lines.";
        _snackbarManager.Notify(
            $"Assigned '{targetChar.Name}' to {selected.Count} selected dialogue lines."
        );
    }

    [RelayCommand]
    public void CycleSegmentEmotion(SubtitleSegment? seg)
    {
        var target = seg ?? SelectedSegment;
        if (target == null)
            return;

        var emotions = ActorEmotionEngine.EmotionNames;
        var currentIndex = emotions
            .ToList()
            .FindIndex(e => string.Equals(e, target.Emotion, StringComparison.OrdinalIgnoreCase));
        var nextIndex = (currentIndex + 1) % emotions.Count;
        target.Emotion = emotions[nextIndex];
    }

    [RelayCommand]
    public void SetSegmentEmotion(string emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion))
            return;

        ApplyEmotionToSelectedSegments(emotion);
    }

    [RelayCommand]
    public void ApplyEmotionToSelectedSegments(string? emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion))
            return;
        var selected = GetSelectedSegments();
        if (selected.Count == 0 && SelectedSegment != null)
            selected = [SelectedSegment];

        if (selected.Count == 0)
            return;

        _isPropagatingBatchChange = true;
        try
        {
            foreach (var seg in selected)
            {
                seg.Emotion = emotion;
            }
        }
        finally
        {
            _isPropagatingBatchChange = false;
        }

        IsProjectDirty = true;
        StatusMessage = $"Applied '{emotion}' tone to {selected.Count} selected dialogue lines.";
        _snackbarManager.Notify(
            $"Applied '{emotion}' tone to {selected.Count} selected dialogue lines."
        );
    }

    [RelayCommand]
    public void ToggleSelectAll()
    {
        bool newState = !IsAllSegmentsSelected;
        foreach (var seg in Segments)
        {
            seg.IsSelected = newState;
        }
        OnPropertyChanged(nameof(IsAllSegmentsSelected));
        OnPropertyChanged(nameof(HasSelectedRows));
        OnPropertyChanged(nameof(SelectedSegmentsCount));
    }

    [RelayCommand]
    public void SelectAllSegments()
    {
        foreach (var seg in Segments)
        {
            seg.IsSelected = true;
        }
        OnPropertyChanged(nameof(IsAllSegmentsSelected));
        OnPropertyChanged(nameof(HasSelectedRows));
        OnPropertyChanged(nameof(SelectedSegmentsCount));
    }

    [RelayCommand]
    public void DeselectAllSegments()
    {
        foreach (var seg in Segments)
        {
            seg.IsSelected = false;
        }
        _dataGridSelectedSegments.Clear();
        OnPropertyChanged(nameof(IsAllSegmentsSelected));
        OnPropertyChanged(nameof(HasSelectedRows));
        OnPropertyChanged(nameof(SelectedSegmentsCount));
    }

    [RelayCommand]
    public async Task AutoFitSelectedAudioAsync()
    {
        var targets = GetSelectedSegments()
            .Where(s => !string.IsNullOrWhiteSpace(s.AudioClipPath) && File.Exists(s.AudioClipPath))
            .ToList();

        if (targets.Count == 0)
        {
            _snackbarManager.Notify("No synthesized audio clips found in selected rows to fit.");
            return;
        }

        var ffmpeg = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            _snackbarManager.Notify("FFmpeg not found.");
            return;
        }

        int fitted = 0;
        var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing");
        Directory.CreateDirectory(tempDir);

        foreach (var target in targets)
        {
            StatusMessage = $"Time-fitting line #{target.Index} ({fitted + 1}/{targets.Count})...";
            var fittedPath = Path.Combine(
                tempDir,
                $"clip_fitted_{target.Index:D4}_{Guid.NewGuid():N}.wav"
            );
            var ok = await DubbingPipeline.ScaleAudioClipDurationAsync(
                ffmpeg,
                target.AudioClipPath!,
                fittedPath,
                target.DurationSeconds
            );
            if (ok && File.Exists(fittedPath))
            {
                target.AudioClipPath = fittedPath;
                var newDuration = await DubbingPipeline.GetAudioDurationAsync(ffmpeg, fittedPath);
                target.AudioDurationSeconds = newDuration.TotalSeconds;
                fitted++;
            }
        }

        IsProjectDirty = true;
        StatusMessage = $"Auto-fitted {fitted} selected dialogue lines.";
        _snackbarManager.Notify($"Auto-fitted {fitted} selected lines to their scene durations.");
    }

    [RelayCommand]
    public async Task AutoDetectEmotionsAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue lines loaded to detect emotions.");
            return;
        }

        int detectedCount = 0;

        // 1. If Gemini AI is configured, perform AI cinematic acting tone analysis
        if (IsGeminiConfigured && !string.IsNullOrWhiteSpace(_settingsService.GeminiApiKey))
        {
            try
            {
                StatusMessage =
                    $"Analyzing {Segments.Count} dialogue lines for cinematic acting tones with Gemini AI...";
                var apiKey = _settingsService.GeminiApiKey.Trim();
                var model = _settingsService.GeminiModel;
                var aiEmotions = await _geminiService.DetectEmotionsBatchAsync(
                    apiKey,
                    Segments.ToList(),
                    model
                );

                if (aiEmotions.Count > 0)
                {
                    foreach (var seg in Segments)
                    {
                        if (
                            aiEmotions.TryGetValue(seg.Index, out var em)
                            && !string.IsNullOrWhiteSpace(em)
                        )
                        {
                            seg.Emotion = em;
                            if (em != ActorEmotionEngine.EmotionNormal)
                                detectedCount++;
                        }
                        else
                        {
                            var offlineDetected = ActorEmotionEngine.DetectEmotion(
                                seg.OriginalText,
                                seg.KhmerText
                            );
                            seg.Emotion = offlineDetected;
                            if (offlineDetected != ActorEmotionEngine.EmotionNormal)
                                detectedCount++;
                        }
                    }

                    IsProjectDirty = true;
                    StatusMessage =
                        $"Gemini AI classified {detectedCount} acting tones across dialogue scenes.";
                    _snackbarManager.Notify(
                        $"Gemini AI classified {detectedCount} dramatic acting tones across dialogue scenes."
                    );
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusMessage =
                    $"Gemini emotion note ({ex.Message}), using upgraded offline rule engine...";
            }
        }

        // 2. High-precision offline rule engine (stage directions, typography, 19 emotion categories, and Khmer cues)
        foreach (var seg in Segments)
        {
            var detected = ActorEmotionEngine.DetectEmotion(seg.OriginalText, seg.KhmerText);
            seg.Emotion = detected;
            if (detected != ActorEmotionEngine.EmotionNormal)
            {
                detectedCount++;
            }
        }

        IsProjectDirty = true;

        if (detectedCount > 0)
        {
            StatusMessage = $"Auto-detected {detectedCount} emotional dialogue tones.";
            _snackbarManager.Notify(
                $"Auto-detected {detectedCount} emotional dialogue tones (Action, Scream, Crying, Anger, Sarcasm, etc.)."
            );
        }
        else
        {
            StatusMessage = "Dialogue segments evaluated; set to standard Normal tone.";
            _snackbarManager.Notify("Evaluated dialogue; neutral lines kept at Normal tone.");
        }
    }

    [RelayCommand]
    public void PolishKhmerDialogue()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue segments available to polish.");
            return;
        }

        int count = 0;
        foreach (var seg in Segments)
        {
            if (!string.IsNullOrWhiteSpace(seg.KhmerText))
            {
                var polished = SubtitleTranslationService.PolishKhmerDialogue(seg.KhmerText);
                if (polished != seg.KhmerText)
                {
                    seg.KhmerText = polished;
                    count++;
                }
            }
        }

        IsProjectDirty = true;
        _snackbarManager.Notify($"Polished {count} dialogue lines into natural movie Khmer.");
        StatusMessage = $"Polished {count} dialogue lines with cinematic phrasing.";
    }

    [RelayCommand]
    public async Task AutoFitSegmentAudioAsync(SubtitleSegment? seg)
    {
        var target = seg ?? SelectedSegment;
        if (
            target == null
            || string.IsNullOrWhiteSpace(target.AudioClipPath)
            || !File.Exists(target.AudioClipPath)
        )
        {
            _snackbarManager.Notify(
                "Please synthesize Khmer voice for this line first before time-fitting."
            );
            return;
        }

        var ffmpeg = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            _snackbarManager.Notify("FFmpeg not found.");
            return;
        }

        try
        {
            StatusMessage = $"Time-fitting speech for line #{target.Index}...";
            var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing");
            Directory.CreateDirectory(tempDir);

            var fittedPath = Path.Combine(
                tempDir,
                $"clip_fitted_{target.Index:D4}_{Guid.NewGuid():N}.wav"
            );
            var ok = await DubbingPipeline.ScaleAudioClipDurationAsync(
                ffmpeg,
                target.AudioClipPath,
                fittedPath,
                target.DurationSeconds
            );

            if (ok && File.Exists(fittedPath))
            {
                target.AudioClipPath = fittedPath;
                var newDuration = await DubbingPipeline.GetAudioDurationAsync(ffmpeg, fittedPath);
                target.AudioDurationSeconds = newDuration.TotalSeconds;
                IsProjectDirty = true;

                _snackbarManager.Notify(
                    $"Line #{target.Index} fitted to {target.DurationSeconds:F2}s scene duration."
                );
                StatusMessage = $"Fitted line #{target.Index} ({target.AudioDurationSeconds:F2}s).";

                CurrentPlayingMediaPath = fittedPath;
                IsCurrentMediaVideo = false;
                HasActiveMedia = true;
                IsMonitorPlaying = true;
                ActiveMediaTitle = $"Fitted Speech #{target.Index} ({target.SpeakerName})";
                PlayMediaRequested?.Invoke(fittedPath, false, ActiveMediaTitle);
            }
            else
            {
                _snackbarManager.Notify("Unable to scale audio clip duration.");
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Time-fit failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task AutoFitAllSegmentsAudioAsync()
    {
        var eligible = Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.AudioClipPath) && File.Exists(s.AudioClipPath))
            .ToList();
        if (eligible.Count == 0)
        {
            _snackbarManager.Notify(
                "No synthesized audio clips found to fit. Please generate voices first."
            );
            return;
        }

        var ffmpeg = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            _snackbarManager.Notify("FFmpeg not found.");
            return;
        }

        IsProcessing = true;
        StatusMessage = $"Auto-fitting {eligible.Count} dialogue clips to scene durations...";
        int fittedCount = 0;

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing");
            Directory.CreateDirectory(tempDir);

            foreach (var seg in eligible)
            {
                var fittedPath = Path.Combine(
                    tempDir,
                    $"clip_fitted_{seg.Index:D4}_{Guid.NewGuid():N}.wav"
                );
                var ok = await DubbingPipeline.ScaleAudioClipDurationAsync(
                    ffmpeg,
                    seg.AudioClipPath!,
                    fittedPath,
                    seg.DurationSeconds
                );

                if (ok && File.Exists(fittedPath))
                {
                    seg.AudioClipPath = fittedPath;
                    var newDuration = await DubbingPipeline.GetAudioDurationAsync(
                        ffmpeg,
                        fittedPath
                    );
                    seg.AudioDurationSeconds = newDuration.TotalSeconds;
                    fittedCount++;
                }
            }

            IsProjectDirty = true;
            _snackbarManager.Notify(
                $"Successfully fitted {fittedCount} audio lines to scene cuts."
            );
            StatusMessage = $"Auto-fit completed: {fittedCount} lines synchronized.";
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Auto-fit error: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    public void MergeWithNextSegment(SubtitleSegment? seg = null)
    {
        var target = seg ?? SelectedSegment;
        if (target == null)
            return;

        int idx = Segments.IndexOf(target);
        if (idx < 0 || idx >= Segments.Count - 1)
        {
            _snackbarManager.Notify("Cannot merge: this is the last dialogue line.");
            return;
        }

        var next = Segments[idx + 1];

        // Semantic sense-to-sense merge with language-aware punctuation, spacing, and Khmer polishing
        DialogueSenseEngine.MergeSegmentsSenseToSense(target, next);

        Segments.RemoveAt(idx + 1);

        for (int i = 0; i < Segments.Count; i++)
        {
            Segments[i].Index = i + 1;
        }

        IsProjectDirty = true;
        SelectedSegment = target;
        _snackbarManager.Notify(
            $"Merged line #{target.Index} with next line (sense-to-sense matched)."
        );
    }

    [RelayCommand]
    public async Task SplitSegmentInHalfAsync(SubtitleSegment? seg)
    {
        var target = seg ?? SelectedSegment;
        if (target == null)
            return;

        if (target.DurationSeconds < 0.6)
        {
            _snackbarManager.Notify("Line duration is too short to split (< 0.6s).");
            return;
        }

        int idx = Segments.IndexOf(target);
        if (idx < 0)
            return;

        // Perform semantic sense-to-sense splitting with proportional timestamps
        var (first, second) = DialogueSenseEngine.SplitSegmentSenseToSense(target, SourceLanguage);

        Segments.Insert(idx + 1, second);

        for (int i = 0; i < Segments.Count; i++)
        {
            Segments[i].Index = i + 1;
        }

        IsProjectDirty = true;
        SelectedSegment = first;

        // If Khmer translation was present, re-translate both halves to ensure 100% clause-by-clause semantic match
        if (
            !string.IsNullOrWhiteSpace(first.OriginalText)
            && !string.IsNullOrWhiteSpace(second.OriginalText)
            && (
                !string.IsNullOrWhiteSpace(first.KhmerText)
                || !string.IsNullOrWhiteSpace(second.KhmerText)
            )
        )
        {
            try
            {
                var t1Task = _subService.TranslateToKhmerAsync(first.OriginalText, SourceLanguage);
                var t2Task = _subService.TranslateToKhmerAsync(second.OriginalText, SourceLanguage);
                await Task.WhenAll(t1Task, t2Task);

                if (!string.IsNullOrWhiteSpace(t1Task.Result))
                    first.KhmerText = SubtitleTranslationService.PolishKhmerDialogue(t1Task.Result);

                if (!string.IsNullOrWhiteSpace(t2Task.Result))
                    second.KhmerText = SubtitleTranslationService.PolishKhmerDialogue(
                        t2Task.Result
                    );
            }
            catch
            {
                // Non-fatal, offline fallback already provided by SplitKhmerDialogueSenseToSense
            }
        }

        _snackbarManager.Notify(
            $"Split line #{first.Index} into matching sense-to-sense lines #{first.Index} & #{second.Index}."
        );
    }

    [RelayCommand]
    public void BulkAssignCharacter(MovieCharacter? character)
    {
        var targetChar = character ?? SelectedCharacter;
        if (targetChar == null)
        {
            _snackbarManager.Notify("Please select a character to assign.");
            return;
        }

        if (SelectedSegment != null)
        {
            SelectedSegment.CharacterId = targetChar.Id;
            SelectedSegment.SpeakerName = targetChar.Name;
            SelectedSegment.SpeakerColor = targetChar.ColorTag;
            IsProjectDirty = true;
            _snackbarManager.Notify(
                $"Assigned line #{SelectedSegment.Index} to {targetChar.Name}."
            );
        }
    }

    [RelayCommand]
    public async Task ExportKhmerSubtitlesAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No subtitle segments to export.");
            return;
        }

        var defaultName = !string.IsNullOrWhiteSpace(ProjectName)
            ? $"{ProjectName}_Khmer.srt"
            : "Subtitles_Khmer.srt";
        var filePath = await _dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType("SubRip Subtitle (*.srt)") { Patterns = ["*.srt"] },
                new FilePickerFileType("WebVTT Subtitle (*.vtt)") { Patterns = ["*.vtt"] },
            ],
            defaultName
        );

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var isVtt = filePath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase);
        var content = isVtt
            ? SubtitleTranslationService.GenerateVtt(Segments, includeOriginal: false)
            : SubtitleTranslationService.GenerateSrt(Segments, includeOriginal: false);

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        _snackbarManager.Notify($"Exported Khmer subtitles to {Path.GetFileName(filePath)}");
    }

    [RelayCommand]
    public async Task ExportBilingualSubtitlesAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No subtitle segments to export.");
            return;
        }

        var defaultName = !string.IsNullOrWhiteSpace(ProjectName)
            ? $"{ProjectName}_Dual.srt"
            : "Subtitles_Dual.srt";
        var filePath = await _dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType("SubRip Subtitle (*.srt)") { Patterns = ["*.srt"] },
                new FilePickerFileType("WebVTT Subtitle (*.vtt)") { Patterns = ["*.vtt"] },
            ],
            defaultName
        );

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var isVtt = filePath.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase);
        var content = isVtt
            ? SubtitleTranslationService.GenerateVtt(Segments, includeOriginal: true)
            : SubtitleTranslationService.GenerateSrt(Segments, includeOriginal: true);

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        _snackbarManager.Notify($"Exported Bilingual subtitles to {Path.GetFileName(filePath)}");
    }

    [RelayCommand]
    public async Task ExportDubbedVocalTrackAsync()
    {
        var clipsWithAudio = Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.AudioClipPath) && File.Exists(s.AudioClipPath))
            .ToList();
        if (clipsWithAudio.Count == 0)
        {
            _snackbarManager.Notify(
                "No synthesized audio clips found. Please generate speech first."
            );
            return;
        }

        var defaultName = !string.IsNullOrWhiteSpace(ProjectName)
            ? $"{ProjectName}_Khmer_Vocals.wav"
            : "Khmer_Vocals.wav";
        var filePath = await _dialogManager.PromptSaveFilePathAsync(
            [new FilePickerFileType("Broadcast WAV Audio (*.wav)") { Patterns = ["*.wav"] }],
            defaultName
        );

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var ffmpeg = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            _snackbarManager.Notify("FFmpeg not found.");
            return;
        }

        try
        {
            StatusMessage = "Assembling clean Khmer vocal track for export...";
            var tempJob = new DubbingJob
            {
                VideoFilePath = VideoFilePath,
                OutputFilePath = filePath,
                EnableSmartTimeStretch = EnableSmartTimeStretch,
            };
            foreach (var s in Segments)
                tempJob.Segments.Add(s);

            await DubbingPipeline.AssembleVocalTrackAsync(ffmpeg, tempJob, filePath);
            _snackbarManager.Notify($"Vocal track exported to {Path.GetFileName(filePath)}");
            StatusMessage = $"Vocal track exported successfully.";
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Export failed: {ex.Message}");
        }
    }

    public void EnsureStandardCastCharacters()
    {
        if (
            !Characters.Any(c =>
                string.Equals(
                    c.Gender,
                    VoiceGenderDetector.GenderMale,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            Characters.Add(
                new MovieCharacter
                {
                    Name = "Hero (Male)",
                    Gender = "Male",
                    BaseVoice = "km-KH-PisethNeural",
                    ToneArchetype = "Hero",
                    SpeechRate = "+15%",
                    ColorTag = "#3B82F6",
                }
            );
        }

        if (
            !Characters.Any(c =>
                string.Equals(
                    c.Gender,
                    VoiceGenderDetector.GenderFemale,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            Characters.Add(
                new MovieCharacter
                {
                    Name = "Heroine (Female)",
                    Gender = "Female",
                    BaseVoice = "km-KH-SreymomNeural",
                    ToneArchetype = "Hero",
                    SpeechRate = "+12%",
                    ColorTag = "#EC4899",
                }
            );
        }

        if (
            !Characters.Any(c =>
                string.Equals(
                    c.Gender,
                    VoiceGenderDetector.GenderChild,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            var child = new MovieCharacter
            {
                Name = "Child / Little Voice (កុមារ)",
                Gender = "Child",
                BaseVoice = "km-KH-SreymomNeural",
                ToneArchetype = "Youth",
                PitchShift = 4,
                SpeechRate = "+18%",
                ColorTag = "#F59E0B",
            };
            child.ApplyToneArchetype("Youth");
            child.BaseVoice = "km-KH-SreymomNeural";
            Characters.Add(child);
        }

        if (
            !Characters.Any(c =>
                string.Equals(c.ToneArchetype, "Villain", StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("Villain", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var villain = new MovieCharacter
            {
                Name = "Villain / Deep Voice",
                Gender = "Male",
                BaseVoice = "km-KH-PisethNeural",
                ToneArchetype = "Villain",
                PitchShift = -3,
                SpeechRate = "+8%",
                ColorTag = "#EF4444",
            };
            villain.ApplyToneArchetype("Villain");
            Characters.Add(villain);
        }

        if (
            !Characters.Any(c =>
                string.Equals(c.ToneArchetype, "Narrator", StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("Narrator", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            var narrator = new MovieCharacter
            {
                Name = "Narrator / Extras",
                Gender = "Male",
                BaseVoice = "km-KH-PisethNeural",
                ToneArchetype = "Narrator",
                PitchShift = -2,
                SpeechRate = "+15%",
                ColorTag = "#10B981",
            };
            narrator.ApplyToneArchetype("Narrator");
            Characters.Add(narrator);
        }
    }

    [RelayCommand]
    public async Task AutoDetectGenderAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue lines loaded to detect voice gender.");
            return;
        }

        EnsureStandardCastCharacters();

        bool hasVideo = !string.IsNullOrWhiteSpace(VideoFilePath) && File.Exists(VideoFilePath);
        IsProcessing = true;
        Progress = 5;
        StatusMessage = hasVideo
            ? "Extracting audio for vocal pitch analysis..."
            : "Analyzing dialogue text & conversational turns...";

        try
        {
            var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
            var detectionResults = await VoiceGenderDetector.DetectGendersForSegmentsAsync(
                ffmpeg,
                hasVideo ? VideoFilePath : string.Empty,
                Segments.ToList(),
                progressCallback: (curr, tot, msg) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Progress = tot > 0 ? (10.0 + ((double)curr / tot * 85.0)) : 50.0;
                        StatusMessage = msg;
                    });
                },
                cancellationToken: CancellationToken.None
            );

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var maleChar =
                    Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.Gender,
                            VoiceGenderDetector.GenderMale,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && (
                            string.Equals(
                                c.ToneArchetype,
                                "Hero",
                                StringComparison.OrdinalIgnoreCase
                            ) || c.Name.Contains("Hero")
                        )
                    )
                    ?? Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.Gender,
                            VoiceGenderDetector.GenderMale,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    ?? Characters.FirstOrDefault();

                var femaleChar =
                    Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.Gender,
                            VoiceGenderDetector.GenderFemale,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    ?? Characters.FirstOrDefault(c => c != maleChar)
                    ?? maleChar;

                var childChar =
                    Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.Gender,
                            VoiceGenderDetector.GenderChild,
                            StringComparison.OrdinalIgnoreCase
                        )
                    ) ?? femaleChar;

                var villainChar =
                    Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.ToneArchetype,
                            "Villain",
                            StringComparison.OrdinalIgnoreCase
                        ) || c.Name.Contains("Villain", StringComparison.OrdinalIgnoreCase)
                    ) ?? maleChar;

                var narratorChar =
                    Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.ToneArchetype,
                            "Narrator",
                            StringComparison.OrdinalIgnoreCase
                        ) || c.Name.Contains("Narrator", StringComparison.OrdinalIgnoreCase)
                    ) ?? maleChar;

                int maleCount = 0;
                int femaleCount = 0;
                int childCount = 0;

                var prevPropagating = _isPropagatingBatchChange;
                _isPropagatingBatchChange = true;
                try
                {
                    for (int i = 0; i < Segments.Count; i++)
                    {
                        var seg = Segments[i];
                        if (
                            detectionResults.TryGetValue(seg.Index, out var res)
                            || detectionResults.TryGetValue(i + 1, out res)
                        )
                        {
                            seg.DetectedGender = res.Gender;
                            if (res.Gender == VoiceGenderDetector.GenderChild && childChar != null)
                            {
                                seg.AssignedCharacter = childChar;
                                childCount++;
                            }
                            else if (
                                res.Gender == VoiceGenderDetector.GenderFemale
                                && femaleChar != null
                            )
                            {
                                seg.AssignedCharacter = femaleChar;
                                femaleCount++;
                            }
                            else
                            {
                                var targetMale = maleChar;
                                if (
                                    seg.Emotion == ActorEmotionEngine.EmotionVillain
                                    && villainChar != null
                                )
                                    targetMale = villainChar;
                                else if (
                                    seg.Emotion == ActorEmotionEngine.EmotionNarrator
                                    && narratorChar != null
                                )
                                    targetMale = narratorChar;

                                if (targetMale != null)
                                {
                                    seg.AssignedCharacter = targetMale;
                                    maleCount++;
                                }
                            }
                            seg.AudioClipPath = null;
                        }
                    }
                }
                finally
                {
                    _isPropagatingBatchChange = prevPropagating;
                }

                SyncAllSegmentCharacters();

                StatusMessage =
                    childCount > 0
                        ? $"Voice Actor Detection Complete: {maleCount} Male, {femaleCount} Female, {childCount} Child scenes assigned."
                        : $"Voice Gender Detection Complete: {maleCount} Male, {femaleCount} Female scenes assigned.";
                _snackbarManager.Notify(
                    childCount > 0
                        ? $"Auto-assigned {maleCount} Male, {femaleCount} Female & {childCount} Child dialogue scenes!"
                        : $"Auto-assigned {maleCount} Male & {femaleCount} Female dialogue scenes!"
                );
            });
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Voice detection note: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            Progress = 100;
        }
    }

    [RelayCommand]
    public async Task AutoDetectSpeakersAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue lines loaded to detect speakers.");
            return;
        }

        IsProcessing = true;
        Progress = 10;
        StatusMessage = "Analyzing cast, characters & speaker dialogue turns...";

        try
        {
            // Step 1: Check if Gemini is configured. If so, perform studio-grade AI Scene Diarization!
            if (IsGeminiConfigured && !string.IsNullOrWhiteSpace(_settingsService.GeminiApiKey))
            {
                StatusMessage = "Running Gemini AI Scene Diarization & Cast Assignment...";
                var diarizedItems = await _geminiService.DiarizeAndAssignSpeakersBatchAsync(
                    _settingsService.GeminiApiKey,
                    Segments.ToList(),
                    _settingsService.GeminiModel,
                    CancellationToken.None
                );

                if (diarizedItems.Count > 0)
                {
                    ApplyGeminiDiarization(diarizedItems);
                    return;
                }
            }

            // Step 2: Offline High-Precision Speaker Prefix & Cast Extraction
            int matchedCount = 0;
            var colors = new[]
            {
                "#3B82F6",
                "#EC4899",
                "#10B981",
                "#F59E0B",
                "#8B5CF6",
                "#EF4444",
                "#06B6D4",
                "#E11D48",
                "#14B8A6",
            };

            var prevPropagating = _isPropagatingBatchChange;
            _isPropagatingBatchChange = true;
            try
            {
                foreach (var seg in Segments)
                {
                    string? extractedSpeaker = null;

                    // Check original text first
                    if (
                        VoiceGenderDetector.TryExtractSpeakerPrefix(
                            seg.OriginalText,
                            out var spName,
                            out var dialText
                        )
                    )
                    {
                        extractedSpeaker = spName;
                        seg.OriginalText = dialText;
                    }

                    // Check Khmer text as well
                    if (
                        VoiceGenderDetector.TryExtractSpeakerPrefix(
                            seg.KhmerText,
                            out var kmSpName,
                            out var kmDialText
                        )
                    )
                    {
                        extractedSpeaker ??= kmSpName;
                        seg.KhmerText = kmDialText;
                    }

                    if (!string.IsNullOrWhiteSpace(extractedSpeaker))
                    {
                        var character = Characters.FirstOrDefault(c =>
                            string.Equals(
                                c.Name,
                                extractedSpeaker,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                        if (character == null)
                        {
                            var detectedGender =
                                VoiceGenderDetector.DetectGenderFromName(extractedSpeaker)
                                ?? VoiceGenderDetector.GenderMale;
                            var toneArchetype = VoiceGenderDetector.DetectToneArchetypeFromName(
                                extractedSpeaker,
                                detectedGender
                            );
                            var color = colors[Characters.Count % colors.Length];

                            bool isFemaleOrChild =
                                detectedGender == VoiceGenderDetector.GenderFemale
                                || detectedGender == VoiceGenderDetector.GenderChild;
                            var baseVoice = isFemaleOrChild
                                ? "km-KH-SreymomNeural"
                                : "km-KH-PisethNeural";

                            character = new MovieCharacter
                            {
                                Name = extractedSpeaker,
                                Gender = detectedGender,
                                BaseVoice = baseVoice,
                                ToneArchetype = toneArchetype,
                                ColorTag = color,
                                EnableRvc = false,
                            };
                            character.ApplyToneArchetype(toneArchetype);
                            if (isFemaleOrChild)
                                character.BaseVoice = "km-KH-SreymomNeural";

                            Characters.Add(character);
                        }

                        seg.AssignedCharacter = character;
                        seg.DetectedGender = character.Gender;
                        seg.AudioClipPath = null;
                        matchedCount++;
                    }
                }
            }
            finally
            {
                _isPropagatingBatchChange = prevPropagating;
            }

            SyncAllSegmentCharacters();

            if (matchedCount > 0)
            {
                _snackbarManager.Notify(
                    $"Auto-assigned {matchedCount} scenes to {Characters.Count} distinct voice actors!"
                );
                StatusMessage =
                    $"Auto-detected {matchedCount} dialogue lines across {Characters.Count} cast members.";
                return;
            }

            // Step 3: No explicit speaker prefixes found in text -> Run intelligent conversational acoustic & turn diarization
            StatusMessage =
                "No explicit prefixes found. Analyzing conversational turns & vocal pitch...";
            await AutoDetectGenderAsync();
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Auto-assign note: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            Progress = 100;
        }
    }

    private void ApplyGeminiDiarization(List<GeminiDiarizationItem> items)
    {
        var colors = new[]
        {
            "#3B82F6",
            "#EC4899",
            "#10B981",
            "#F59E0B",
            "#8B5CF6",
            "#EF4444",
            "#06B6D4",
            "#E11D48",
            "#14B8A6",
        };

        var dict = items.ToDictionary(i => i.Index);
        int assignedCount = 0;

        var prevPropagating = _isPropagatingBatchChange;
        _isPropagatingBatchChange = true;
        try
        {
            foreach (var seg in Segments)
            {
                if (dict.TryGetValue(seg.Index, out var item))
                {
                    var character = Characters.FirstOrDefault(c =>
                        string.Equals(c.Name, item.SpeakerName, StringComparison.OrdinalIgnoreCase)
                    );

                    if (character == null)
                    {
                        var color = colors[Characters.Count % colors.Length];
                        bool isFemaleOrChild =
                            item.Gender == VoiceGenderDetector.GenderFemale
                            || item.Gender == VoiceGenderDetector.GenderChild;
                        var baseVoice = isFemaleOrChild
                            ? "km-KH-SreymomNeural"
                            : "km-KH-PisethNeural";

                        character = new MovieCharacter
                        {
                            Name = item.SpeakerName,
                            Gender = item.Gender,
                            BaseVoice = baseVoice,
                            ToneArchetype = item.ToneArchetype,
                            ColorTag = color,
                            EnableRvc = false,
                        };
                        character.ApplyToneArchetype(item.ToneArchetype);
                        if (isFemaleOrChild)
                            character.BaseVoice = "km-KH-SreymomNeural";

                        Characters.Add(character);
                    }

                    seg.AssignedCharacter = character;
                    seg.DetectedGender = item.Gender;
                    if (
                        !string.IsNullOrWhiteSpace(item.Emotion)
                        && item.Emotion != ActorEmotionEngine.EmotionNormal
                    )
                    {
                        seg.Emotion = item.Emotion;
                    }
                    seg.AudioClipPath = null;

                    // Also clean any prefix from text if present
                    if (
                        VoiceGenderDetector.TryExtractSpeakerPrefix(
                            seg.OriginalText,
                            out _,
                            out var cleanOrig
                        )
                    )
                        seg.OriginalText = cleanOrig;
                    if (
                        VoiceGenderDetector.TryExtractSpeakerPrefix(
                            seg.KhmerText,
                            out _,
                            out var cleanKm
                        )
                    )
                        seg.KhmerText = cleanKm;

                    assignedCount++;
                }
            }
        }
        finally
        {
            _isPropagatingBatchChange = prevPropagating;
        }

        SyncAllSegmentCharacters();

        StatusMessage =
            $"Gemini AI Cast Assignment Complete: {assignedCount} lines assigned across {Characters.Count} characters.";
        _snackbarManager.Notify(
            $"AI Cast Diarization: Assigned {assignedCount} dialogue lines across {Characters.Count} characters!"
        );
    }

    [RelayCommand]
    public void AlternateSpeakers()
    {
        if (Segments.Count == 0 || Characters.Count < 2)
        {
            _snackbarManager.Notify(
                "Need at least 2 characters in the cast to alternate dialogue turns."
            );
            return;
        }

        var charA = Characters[0];
        var charB = Characters[1];

        var prevPropagating = _isPropagatingBatchChange;
        _isPropagatingBatchChange = true;
        try
        {
            for (int i = 0; i < Segments.Count; i++)
            {
                var charToAssign = (i % 2 == 0) ? charA : charB;
                Segments[i].AssignedCharacter = charToAssign;
                Segments[i].AudioClipPath = null;
            }
        }
        finally
        {
            _isPropagatingBatchChange = prevPropagating;
        }

        SyncAllSegmentCharacters();

        _snackbarManager.Notify(
            $"Alternated dialogue turns: {charA.Name} (A) ⇄ {charB.Name} (B) across {Segments.Count} scenes."
        );
        StatusMessage = $"Applied A/B alternating dialogue between {charA.Name} and {charB.Name}.";
    }

    [RelayCommand]
    public void LoadExampleDialogue()
    {
        Segments.Clear();

        var hero =
            Characters.FirstOrDefault(c =>
                c.Name.Contains("Hero", StringComparison.OrdinalIgnoreCase)
            ) ?? Characters.FirstOrDefault();
        var heroine =
            Characters.FirstOrDefault(c =>
                c.Name.Contains("Heroine", StringComparison.OrdinalIgnoreCase)
            ) ?? (Characters.Count > 1 ? Characters[1] : hero);

        var demoLines = new (
            string Original,
            string Khmer,
            TimeSpan Start,
            TimeSpan End,
            MovieCharacter? Speaker
        )[]
        {
            (
                "Why are you all wet, baby?",
                "ហេតុអ្វីបានជាអូនទទឹកជោកបែបនេះ អូនសម្លាញ់?",
                TimeSpan.FromSeconds(0.0),
                TimeSpan.FromSeconds(3.2),
                hero
            ),
            (
                "It's raining.",
                "ភ្លៀងធ្លាក់។",
                TimeSpan.FromSeconds(3.5),
                TimeSpan.FromSeconds(5.2),
                heroine
            ),
            (
                "Where are the kids?",
                "ចុះកូនៗនៅឯណា?",
                TimeSpan.FromSeconds(5.5),
                TimeSpan.FromSeconds(7.8),
                hero
            ),
            (
                "They're in school.",
                "ពួកគេនៅសាលារៀន។",
                TimeSpan.FromSeconds(8.0),
                TimeSpan.FromSeconds(10.2),
                heroine
            ),
            (
                "It's Saturday, honey.",
                "ថ្ងៃនេះជាថ្ងៃសៅរ៍តើ អូនសម្លាញ់។",
                TimeSpan.FromSeconds(10.5),
                TimeSpan.FromSeconds(12.8),
                hero
            ),
            (
                "My school is in session on Saturday.",
                "សាលារៀនរបស់ខ្ញុំបើកបង្រៀននៅថ្ងៃសៅរ៍។",
                TimeSpan.FromSeconds(13.0),
                TimeSpan.FromSeconds(16.5),
                heroine
            ),
            (
                "Oh, my God. Wake up! Wake up! Wake up!",
                "ឱព្រះអើយ! ភ្ញាក់ឡើង! ភ្ញាក់ឡើង! ភ្ញាក់ឡើង!",
                TimeSpan.FromSeconds(17.0),
                TimeSpan.FromSeconds(23.5),
                hero
            ),
            (
                "Please, God!",
                "សូមព្រះមេត្តាជួយផង!",
                TimeSpan.FromSeconds(24.0),
                TimeSpan.FromSeconds(29.5),
                hero
            ),
            (
                "Let's put them at the table, Andrew. We'll dry them off.",
                "តោះយកកូនៗទៅតុ Andrew។ យើងនឹងជូតខ្លួនពួកគេឲ្យស្ងួត។",
                TimeSpan.FromSeconds(37.5),
                TimeSpan.FromSeconds(43.5),
                heroine
            ),
            (
                "We'll dress them up. They'll be our living dolls.",
                "យើងនឹងស្លៀកពាក់ឲ្យពួកគេ។ ពួកគេនឹងក្លាយជាតុក្កតារស់របស់យើង។",
                TimeSpan.FromSeconds(44.0),
                TimeSpan.FromSeconds(50.0),
                heroine
            ),
            (
                "Oh, my God! Please, God, no!",
                "ឱព្រះអើយ! សូមព្រះកុំធ្វើបែបនេះអី!",
                TimeSpan.FromSeconds(50.5),
                TimeSpan.FromSeconds(58.0),
                hero
            ),
        };

        var prevPropagating = _isPropagatingBatchChange;
        _isPropagatingBatchChange = true;
        try
        {
            for (int i = 0; i < demoLines.Length; i++)
            {
                var item = demoLines[i];
                Segments.Add(
                    new SubtitleSegment
                    {
                        Index = i + 1,
                        StartTime = item.Start,
                        EndTime = item.End,
                        OriginalText = item.Original,
                        KhmerText = item.Khmer,
                        CharacterId = item.Speaker?.Id,
                        SpeakerName = item.Speaker?.Name ?? "Hero (Male)",
                        SpeakerColor = item.Speaker?.ColorTag ?? "#3B82F6",
                        AssignedCharacter = item.Speaker,
                    }
                );
            }
        }
        finally
        {
            _isPropagatingBatchChange = prevPropagating;
        }

        SyncAllSegmentCharacters();

        SelectedSegment = Segments.FirstOrDefault();
        _snackbarManager.Notify(
            $"Loaded verified dual-character movie dialogue ({Segments.Count} scenes)."
        );
        StatusMessage = "Loaded verified scene dialogue with Teddy (Male) & Dolores (Female) cast.";

        if (!string.IsNullOrWhiteSpace(VideoFilePath) && File.Exists(VideoFilePath))
        {
            _ = ExtractSceneThumbnailsAsync();
        }
    }

    [RelayCommand]
    public void RefreshRvcModels()
    {
        AvailableRvcModels.Clear();
        var models = _rvcService.GetAvailableVoiceModels();
        foreach (var m in models)
        {
            AvailableRvcModels.Add(m);
        }

        SelectedRvcModel = AvailableRvcModels.FirstOrDefault();
    }

    [RelayCommand]
    public void OpenRvcModelsFolder()
    {
        var dir = RvcInferenceService.DefaultModelsDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    [RelayCommand]
    public async Task ImportRvcModelAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync([
            new FilePickerFileType("RVC Voice Model (*.pth)") { Patterns = ["*.pth"] },
        ]);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var modelName = Path.GetFileNameWithoutExtension(filePath);
            var targetDir = Path.Combine(RvcInferenceService.DefaultModelsDirectory, modelName);
            Directory.CreateDirectory(targetDir);

            var destPth = Path.Combine(targetDir, Path.GetFileName(filePath));
            File.Copy(filePath, destPth, overwrite: true);

            // Also copy companion .index if present in source folder
            var sourceDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(sourceDir))
            {
                var indexFile = Directory.EnumerateFiles(sourceDir, "*.index").FirstOrDefault();
                if (indexFile != null && File.Exists(indexFile))
                {
                    var destIndex = Path.Combine(targetDir, Path.GetFileName(indexFile));
                    File.Copy(indexFile, destIndex, overwrite: true);
                }
            }

            RefreshRvcModels();

            var imported = AvailableRvcModels.FirstOrDefault(m =>
                m.PthPath.Equals(destPth, StringComparison.OrdinalIgnoreCase)
                || m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)
            );
            if (imported != null)
            {
                SelectedRvcModel = imported;
                if (SelectedCharacter != null)
                {
                    SelectedCharacter.EnableRvc = true;
                    SelectedCharacter.RvcModelPath = imported.PthPath;
                    SelectedCharacter.RvcIndexPath = imported.IndexPath;
                }
            }

            StatusMessage = $"Imported RVC voice model '{modelName}' successfully.";
            _snackbarManager.Notify($"Added RVC voice model '{modelName}' to project!");
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to import RVC model: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseVideoFileAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync([
            new FilePickerFileType("Video Files")
            {
                Patterns = ["*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi", "*.ts"],
            },
        ]);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        await LoadVideoAsync(filePath);
    }

    public async Task LoadVideoAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        VideoFilePath = filePath;

        var dir = Path.GetDirectoryName(VideoFilePath) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(VideoFilePath);
        OutputFilePath = Path.Combine(dir, $"{nameWithoutExt}_khmer_dubbed.mp4");

        // Scan for existing embedded or sidecar subtitles
        try
        {
            var ffmpeg = GetFfmpegPath() ?? "ffmpeg";
            var discovered = await new SubtitleTranslationService().ExtractOrGenerateSubtitlesAsync(
                ffmpeg,
                VideoFilePath,
                SourceLanguage
            );

            Segments.Clear();
            foreach (var seg in discovered)
            {
                Segments.Add(seg);
            }

            if (Segments.Count > 0)
            {
                StatusMessage =
                    $"Loaded {Segments.Count} dialogue lines from {Path.GetFileName(VideoFilePath)}. Click '⚡ Auto-Prep Studio' to setup cast & translate in 1 click.";
                _snackbarManager.Notify(
                    $"Loaded {Segments.Count} dialogue lines. Click '⚡ Auto-Prep Studio' to setup cast & translate in 1 click."
                );
                _ = ExtractSceneThumbnailsAsync();
            }
            else
            {
                StatusMessage =
                    $"Loaded video: {Path.GetFileName(VideoFilePath)}. Click '⚡ Auto-Prep Studio' to transcribe speech and setup cast in 1 click.";
                _snackbarManager.Notify(
                    $"Video loaded: {Path.GetFileName(VideoFilePath)}. Click '⚡ Auto-Prep Studio' to auto-transcribe & setup cast."
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Selected: {Path.GetFileName(VideoFilePath)} ({ex.Message})";
        }
        finally
        {
            BrowseVideoFileCommand.NotifyCanExecuteChanged();
            ExtendCurrentVideoCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    public async Task ExtendCurrentVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            await BrowseVideoFileAsync();
            return;
        }

        var files = await _dialogManager.PromptOpenFilePathsAsync([
            new FilePickerFileType("Video Files")
            {
                Patterns = ["*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi", "*.ts"],
            },
        ]);

        if (files == null || files.Count == 0)
            return;

        await ExtendVideoWithFilesAsync(files);
    }

    public async Task ExtendVideoWithFilesAsync(IReadOnlyList<string> additionalVideoPaths)
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            if (additionalVideoPaths.Count > 0)
            {
                await LoadVideoAsync(additionalVideoPaths[0]);
                if (additionalVideoPaths.Count > 1)
                {
                    await ExtendVideoWithFilesAsync(additionalVideoPaths.Skip(1).ToList());
                }
            }
            return;
        }

        var validPaths = additionalVideoPaths
            .Where(p =>
                !string.IsNullOrWhiteSpace(p)
                && File.Exists(p)
                && !string.Equals(p, VideoFilePath, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        if (validPaths.Count == 0)
        {
            _snackbarManager.Notify("No new video files selected to extend timeline.");
            return;
        }

        var ffmpeg = GetFfmpegPath() ?? "ffmpeg";
        StatusMessage =
            $"Measuring current video duration and preparing to extend timeline with {validPaths.Count} clip(s)...";

        try
        {
            var currentDuration = await DubbingPipeline.GetAudioDurationAsync(
                ffmpeg,
                VideoFilePath
            );

            var currentDir = Path.GetDirectoryName(VideoFilePath) ?? Path.GetTempPath();
            var baseName = Path.GetFileNameWithoutExtension(VideoFilePath);
            if (baseName.EndsWith("_extended", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^9];
            }
            var ext = Path.GetExtension(VideoFilePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".mp4";

            var targetCombinedPath = Path.Combine(currentDir, $"{baseName}_extended{ext}");
            if (
                string.Equals(VideoFilePath, targetCombinedPath, StringComparison.OrdinalIgnoreCase)
            )
            {
                targetCombinedPath = Path.Combine(
                    currentDir,
                    $"{baseName}_extended_{DateTime.Now:yyyyMMdd_HHmmss}{ext}"
                );
            }

            var allVideos = new List<string> { VideoFilePath };
            allVideos.AddRange(validPaths);

            var progress = new Progress<string>(msg => StatusMessage = msg);
            var success = await DubbingPipeline.ConcatenateVideosAsync(
                ffmpeg,
                allVideos,
                targetCombinedPath,
                progress
            );

            if (!success || !File.Exists(targetCombinedPath))
            {
                StatusMessage = "Failed to extend video clips with FFmpeg.";
                _snackbarManager.Notify(
                    "Failed to extend video. Check video codecs and FFmpeg logs."
                );
                return;
            }

            // Offset and append dialogue lines from newly attached videos
            var cumulativeDuration = currentDuration;
            var subService = new SubtitleTranslationService();
            var defaultChar = Characters.FirstOrDefault();
            int newSegmentsAdded = 0;

            foreach (var addPath in validPaths)
            {
                var thisVidDuration = await DubbingPipeline.GetAudioDurationAsync(ffmpeg, addPath);

                try
                {
                    var discovered = await subService.ExtractOrGenerateSubtitlesAsync(
                        ffmpeg,
                        addPath,
                        SourceLanguage
                    );

                    foreach (var seg in discovered)
                    {
                        seg.StartTime += cumulativeDuration;
                        seg.EndTime += cumulativeDuration;
                        seg.Index = Segments.Count + 1;
                        if (defaultChar != null && seg.CharacterId == null)
                        {
                            seg.CharacterId = defaultChar.Id;
                            seg.SpeakerName = defaultChar.Name;
                            seg.SpeakerColor = defaultChar.ColorTag;
                        }
                        Segments.Add(seg);
                        newSegmentsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ExtendVideo] Subtitle extract note: {ex.Message}"
                    );
                }

                cumulativeDuration += thisVidDuration;
            }

            VideoFilePath = targetCombinedPath;
            var outDir = Path.GetDirectoryName(targetCombinedPath) ?? string.Empty;
            var outName = Path.GetFileNameWithoutExtension(targetCombinedPath);
            OutputFilePath = Path.Combine(outDir, $"{outName}_khmer_dubbed.mp4");
            IsProjectDirty = true;

            if (HasActiveMedia)
            {
                CurrentPlayingMediaPath = VideoFilePath;
                ActiveMediaTitle = $"Movie (Extended): {Path.GetFileName(VideoFilePath)}";
                PlayMediaRequested?.Invoke(VideoFilePath, true, ActiveMediaTitle);
            }

            if (newSegmentsAdded > 0)
            {
                _ = ExtractSceneThumbnailsAsync();
                StatusMessage =
                    $"Timeline extended! Duration: {cumulativeDuration:hh\\:mm\\:ss}. Loaded {newSegmentsAdded} new dialogue lines (Total: {Segments.Count}).";
                _snackbarManager.Notify(
                    $"Video extended with {validPaths.Count} clip(s) and {newSegmentsAdded} dialogue lines."
                );
            }
            else
            {
                StatusMessage =
                    $"Timeline extended! Duration: {cumulativeDuration:hh\\:mm\\:ss}. You can click 'Scan Audio' to transcribe new segments.";
                _snackbarManager.Notify(
                    $"Video extended with {validPaths.Count} clip(s). Click 'Scan Audio' to transcribe speech."
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error extending video: {ex.Message}";
            _snackbarManager.Notify($"Error extending video: {ex.Message}");
        }
        finally
        {
            BrowseVideoFileCommand.NotifyCanExecuteChanged();
            ExtendCurrentVideoCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths == null || paths.Count == 0)
            return;

        // 1. Check for project file (.855dub or .json)
        var projectFile = paths.FirstOrDefault(p =>
            p.EndsWith(".855dub", StringComparison.OrdinalIgnoreCase)
            || (
                p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".voice.json", StringComparison.OrdinalIgnoreCase)
            )
        );
        if (!string.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile))
        {
            await LoadProjectInternalAsync(projectFile);
            return;
        }

        // 2. Check for subtitle files (.srt, .vtt, .sub, .ass)
        var subFile = paths.FirstOrDefault(p =>
            p.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".ass", StringComparison.OrdinalIgnoreCase)
        );
        if (!string.IsNullOrWhiteSpace(subFile) && File.Exists(subFile))
        {
            await ImportSubtitlesFromFileAsync(subFile);
            return;
        }

        // 3. Check for video files (.mp4, .mkv, .mov, .webm, .avi, .ts)
        var videoFiles = paths
            .Where(p =>
            {
                var ext = Path.GetExtension(p).ToLowerInvariant();
                return ext is ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".ts";
            })
            .ToList();

        if (videoFiles.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            await LoadVideoAsync(videoFiles[0]);
            if (videoFiles.Count > 1)
            {
                await ExtendVideoWithFilesAsync(videoFiles.Skip(1).ToList());
            }
        }
        else
        {
            await ExtendVideoWithFilesAsync(videoFiles);
        }
    }

    [RelayCommand]
    public async Task ExtractSceneThumbnailsAsync()
    {
        if (
            string.IsNullOrWhiteSpace(VideoFilePath)
            || !File.Exists(VideoFilePath)
            || Segments.Count == 0
        )
        {
            return;
        }

        if (IsExtractingScenes)
            return;

        IsExtractingScenes = true;
        StatusMessage = "Extracting video scene snapshots for dialogue segments...";

        try
        {
            var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
            var cacheDir = Path.Combine(
                Path.GetTempPath(),
                "855Media_Dubbing_Scenes",
                Path.GetFileNameWithoutExtension(VideoFilePath)
            );
            Directory.CreateDirectory(cacheDir);

            var segmentsToProcess = Segments.ToList();
            int total = segmentsToProcess.Count;
            int processedCount = 0;
            int degreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

            await Parallel.ForEachAsync(
                segmentsToProcess,
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism },
                async (seg, ct) =>
                {
                    long startMs = (long)seg.StartTime.TotalMilliseconds;
                    long endMs = (long)seg.EndTime.TotalMilliseconds;
                    var thumbPath = Path.Combine(
                        cacheDir,
                        $"scene_{seg.Index:D4}_{startMs}_{endMs}.jpg"
                    );
                    if (!File.Exists(thumbPath) || new FileInfo(thumbPath).Length == 0)
                    {
                        try
                        {
                            var ssSec = Math.Max(0, seg.StartTime.TotalSeconds)
                                .ToString(
                                    "0.000",
                                    System.Globalization.CultureInfo.InvariantCulture
                                );
                            using var proc = new Process();
                            proc.StartInfo.FileName = ffmpeg;
                            proc.StartInfo.ArgumentList.Add("-y");
                            proc.StartInfo.ArgumentList.Add("-ss");
                            proc.StartInfo.ArgumentList.Add(ssSec);
                            proc.StartInfo.ArgumentList.Add("-i");
                            proc.StartInfo.ArgumentList.Add(VideoFilePath);
                            proc.StartInfo.ArgumentList.Add("-vframes");
                            proc.StartInfo.ArgumentList.Add("1");
                            proc.StartInfo.ArgumentList.Add("-vf");
                            proc.StartInfo.ArgumentList.Add(
                                "scale=240:135:force_original_aspect_ratio=decrease,pad=240:135:(ow-iw)/2:(oh-ih)/2"
                            );
                            proc.StartInfo.ArgumentList.Add("-q:v");
                            proc.StartInfo.ArgumentList.Add("2");
                            proc.StartInfo.ArgumentList.Add(thumbPath);
                            proc.StartInfo.UseShellExecute = false;
                            proc.StartInfo.CreateNoWindow = true;

                            proc.Start();
                            ChildProcessTracker.Track(proc);
                            await proc.WaitForExitWithCancellationAsync(ct);
                        }
                        catch { }
                    }

                    if (File.Exists(thumbPath))
                    {
                        seg.ThumbnailPath = thumbPath;
                    }

                    int current = Interlocked.Increment(ref processedCount);
                    if (current % 5 == 0 || current == total)
                    {
                        StatusMessage = $"Extracting scene thumbnails {current}/{total}...";
                    }
                }
            );

            int extracted = Segments.Count(s => !string.IsNullOrEmpty(s.ThumbnailPath));
            StatusMessage = $"Ready. Extracted {extracted}/{total} scene snapshots.";
            _snackbarManager.Notify($"Extracted {extracted} scene snapshots!");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scene snapshot error: {ex.Message}";
        }
        finally
        {
            IsExtractingScenes = false;
        }
    }

    [RelayCommand]
    public async Task PlayScene(SubtitleSegment? seg)
    {
        var target = seg ?? SelectedSegment;
        if (target == null)
            return;

        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            _snackbarManager.Notify("Please select an input video file first to play the scene.");
            return;
        }

        var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
        var cacheDir = Path.Combine(
            Path.GetTempPath(),
            "855Media_Dubbing_Scenes",
            Path.GetFileNameWithoutExtension(VideoFilePath)
        );
        Directory.CreateDirectory(cacheDir);

        var ext = Path.GetExtension(VideoFilePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || ext == ".webm")
            ext = ".mp4";

        long startMs = (long)target.StartTime.TotalMilliseconds;
        long endMs = (long)target.EndTime.TotalMilliseconds;
        var clipPath = Path.Combine(cacheDir, $"clip_{target.Index:D4}_{startMs}_{endMs}{ext}");
        if (!File.Exists(clipPath) || new FileInfo(clipPath).Length < 100)
        {
            var ssSec = Math.Max(0, target.StartTime.TotalSeconds)
                .ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            var durSec = Math.Max(0.5, (target.EndTime - target.StartTime).TotalSeconds)
                .ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

            try
            {
                using var proc = new Process();
                proc.StartInfo.FileName = ffmpeg;
                proc.StartInfo.ArgumentList.Add("-y");
                proc.StartInfo.ArgumentList.Add("-ss");
                proc.StartInfo.ArgumentList.Add(ssSec);
                proc.StartInfo.ArgumentList.Add("-i");
                proc.StartInfo.ArgumentList.Add(VideoFilePath);
                proc.StartInfo.ArgumentList.Add("-t");
                proc.StartInfo.ArgumentList.Add(durSec);
                proc.StartInfo.ArgumentList.Add("-c:v");
                proc.StartInfo.ArgumentList.Add("libx264");
                proc.StartInfo.ArgumentList.Add("-preset");
                proc.StartInfo.ArgumentList.Add("ultrafast");
                proc.StartInfo.ArgumentList.Add("-crf");
                proc.StartInfo.ArgumentList.Add("23");
                proc.StartInfo.ArgumentList.Add("-c:a");
                proc.StartInfo.ArgumentList.Add("aac");
                proc.StartInfo.ArgumentList.Add("-avoid_negative_ts");
                proc.StartInfo.ArgumentList.Add("make_zero");
                proc.StartInfo.ArgumentList.Add(clipPath);
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                proc.Start();
                await proc.WaitForExitAsync();
            }
            catch { }
        }

        if (File.Exists(clipPath))
        {
            CurrentPlayingMediaPath = clipPath;
            IsCurrentMediaVideo = true;
            HasActiveMedia = true;
            IsMonitorPlaying = true;
            ActiveMediaTitle =
                $"Scene #{target.Index:D4} ({target.StartTime:mm\\:ss} - {target.EndTime:mm\\:ss})";
            PlayMediaRequested?.Invoke(clipPath, true, ActiveMediaTitle);
            _snackbarManager.Notify($"Playing Scene #{target.Index:D4} in Studio Monitor.");
        }
        else
        {
            _snackbarManager.Notify($"Scene clip not available for line {target.Index}.");
        }
    }

    [RelayCommand]
    public async Task PlayDubbedClip(SubtitleSegment? seg)
    {
        var target = seg ?? SelectedSegment;
        if (target == null)
            return;

        if (
            string.IsNullOrWhiteSpace(target.KhmerText)
            && string.IsNullOrWhiteSpace(target.OriginalText)
        )
        {
            _snackbarManager.Notify("Please enter dialogue text for this line first.");
            return;
        }

        // If audio clip not yet synthesized by pipeline, generate on demand
        if (string.IsNullOrWhiteSpace(target.AudioClipPath) || !File.Exists(target.AudioClipPath))
        {
            try
            {
                StatusMessage = $"Synthesizing voice for line {target.Index}...";
                var tts = new KhmerTtsService();
                var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing_AudioPreview");
                Directory.CreateDirectory(tempDir);

                var character =
                    Characters.FirstOrDefault(c =>
                        string.Equals(
                            c.Name,
                            target.SpeakerName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    ) ?? Characters.FirstOrDefault();
                var voiceToUse = character?.BaseVoice ?? SelectedVoice.Id;
                var textToSpeak = !string.IsNullOrWhiteSpace(target.KhmerText)
                    ? target.KhmerText
                    : target.OriginalText;

                var emotionName =
                    !string.IsNullOrWhiteSpace(target.Emotion)
                    && target.Emotion != ActorEmotionEngine.EmotionNormal
                        ? target.Emotion
                        : (character?.EmotionPreset ?? ActorEmotionEngine.EmotionNormal);
                var emotionCfg = ActorEmotionEngine.GetConfig(emotionName);

                var effectiveRate = ActorEmotionEngine.ComputeEffectiveRate(
                    character?.SpeechRate,
                    emotionCfg.TtsRateOffset
                );
                var effectivePitch = ActorEmotionEngine.ComputeEffectiveTtsPitch(
                    character?.PitchShift ?? 0,
                    emotionCfg.TtsPitch,
                    emotionCfg.PitchShiftOffset
                );

                var clipPath = Path.Combine(tempDir, $"preview_{target.Index:D4}.mp3");
                await tts.SynthesizeKhmerSpeechAsync(
                    textToSpeak,
                    clipPath,
                    voiceToUse,
                    rate: effectiveRate,
                    pitch: effectivePitch,
                    volume: emotionCfg.TtsVolume
                );

                if (
                    character != null
                    && character.EnableRvc
                    && !string.IsNullOrWhiteSpace(character.RvcModelPath)
                    && File.Exists(character.RvcModelPath)
                )
                {
                    StatusMessage = $"Applying RVC voice clone for {character.Name}...";
                    var clonedClip = Path.Combine(tempDir, $"preview_{target.Index:D4}_rvc.wav");
                    int effectivePitchShift = character.PitchShift + emotionCfg.PitchShiftOffset;
                    await _rvcService.ConvertVoiceAsync(
                        clipPath,
                        clonedClip,
                        character.RvcModelPath,
                        character.RvcIndexPath,
                        effectivePitchShift,
                        null,
                        CancellationToken.None
                    );
                    if (File.Exists(clonedClip) && new FileInfo(clonedClip).Length > 1024)
                    {
                        clipPath = clonedClip;
                    }
                }

                // Apply character tone & emotion acoustic filter
                var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
                var filteredClip = Path.Combine(tempDir, $"preview_{target.Index:D4}_tone.wav");
                var warmth = character?.ToneWarmth ?? 0.0;
                var clarity = character?.ToneClarity ?? 0.0;
                var archetype = character?.ToneArchetype;

                var applied = await ActorEmotionEngine.ApplyActorAcousticFilterAsync(
                    ffmpeg,
                    clipPath,
                    filteredClip,
                    emotionCfg.Name,
                    warmth,
                    clarity,
                    archetype,
                    CancellationToken.None
                );
                if (
                    applied
                    && File.Exists(filteredClip)
                    && new FileInfo(filteredClip).Length > 1024
                )
                {
                    clipPath = filteredClip;
                }

                target.AudioClipPath = clipPath;
                MeasureAudioDuration(target);
                StatusMessage = "Khmer voice preview ready.";
            }
            catch (Exception ex)
            {
                _snackbarManager.Notify($"Voice synthesis error: {ex.Message}");
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(target.AudioClipPath) && File.Exists(target.AudioClipPath))
        {
            CurrentPlayingMediaPath = target.AudioClipPath;
            IsCurrentMediaVideo = false;
            HasActiveMedia = true;
            IsMonitorPlaying = true;
            ActiveMediaTitle = $"Khmer Voice: #{target.Index:D4} - {target.SpeakerName}";
            PlayMediaRequested?.Invoke(target.AudioClipPath, false, ActiveMediaTitle);
            _snackbarManager.Notify($"Playing Khmer voice in Studio Monitor.");
        }
        else
        {
            _snackbarManager.Notify($"Khmer speech audio not available for line {target.Index}.");
        }
    }

    private void MeasureAudioDuration(SubtitleSegment seg)
    {
        if (string.IsNullOrWhiteSpace(seg.AudioClipPath) || !File.Exists(seg.AudioClipPath))
            return;

        try
        {
            var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
            using var proc = new Process();
            proc.StartInfo.FileName = ffmpeg;
            proc.StartInfo.ArgumentList.Add("-i");
            proc.StartInfo.ArgumentList.Add(seg.AudioClipPath);
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.Start();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            var match = Regex.Match(err, @"Duration:\s*(\d+):(\d+):(\d+\.\d+)");
            if (match.Success)
            {
                var h = int.Parse(match.Groups[1].Value);
                var m = int.Parse(match.Groups[2].Value);
                var s = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                seg.AudioDurationSeconds = h * 3600 + m * 60 + s;
            }
            else
            {
                seg.AudioDurationSeconds = seg.DurationSeconds;
            }
        }
        catch
        {
            seg.AudioDurationSeconds = seg.DurationSeconds;
        }
    }

    [RelayCommand]
    public void SelectStudioTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out var idx))
        {
            SelectedStudioTab = idx;
        }
    }

    [RelayCommand]
    public void ZoomInTimeline()
    {
        TimelineZoom = Math.Min(3.0, TimelineZoom + 0.25);
    }

    [RelayCommand]
    public void ZoomOutTimeline()
    {
        TimelineZoom = Math.Max(0.5, TimelineZoom - 0.25);
    }

    [RelayCommand]
    public void NudgeStartTime(string deltaStr)
    {
        if (
            SelectedSegment == null
            || !double.TryParse(deltaStr, CultureInfo.InvariantCulture, out var delta)
        )
            return;

        var newStart = SelectedSegment.StartTime + TimeSpan.FromSeconds(delta);
        if (newStart < TimeSpan.Zero)
            newStart = TimeSpan.Zero;
        if (newStart < SelectedSegment.EndTime)
        {
            SelectedSegment.StartTime = newStart;
        }
    }

    [RelayCommand]
    public void NudgeEndTime(string deltaStr)
    {
        if (
            SelectedSegment == null
            || !double.TryParse(deltaStr, CultureInfo.InvariantCulture, out var delta)
        )
            return;

        var newEnd = SelectedSegment.EndTime + TimeSpan.FromSeconds(delta);
        if (newEnd > SelectedSegment.StartTime)
        {
            SelectedSegment.EndTime = newEnd;
        }
    }

    [RelayCommand]
    public async Task AutoAlignSpeechStart(SubtitleSegment? seg = null)
    {
        var target = seg ?? SelectedSegment;
        if (target == null)
        {
            _snackbarManager.Notify("Please select a dialogue segment to align speech start.");
            return;
        }

        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            _snackbarManager.Notify("Please load a video file first to analyze speech timing.");
            return;
        }

        var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
        StatusMessage = $"Analyzing audio speech onset for line #{target.Index}...";

        var detectedStart = await AudioTranscriptionService.DetectActualSpeechStartAsync(
            ffmpeg,
            VideoFilePath,
            target.StartTime,
            target.EndTime
        );

        if (
            detectedStart.HasValue
            && detectedStart.Value > target.StartTime + TimeSpan.FromMilliseconds(150)
        )
        {
            var oldStart = target.StartTime;
            target.StartTime = detectedStart.Value;
            IsProjectDirty = true;
            StatusMessage =
                $"Aligned line #{target.Index} speech start: {oldStart:mm\\:ss\\.ff} ➔ {target.StartTime:mm\\:ss\\.ff} (trimmed opening silence/music).";
            _snackbarManager.Notify(
                $"Aligned line #{target.Index} start to {target.StartTime:mm\\:ss\\.ff} (trimmed opening silence)."
            );
        }
        else
        {
            _snackbarManager.Notify(
                $"Line #{target.Index} is already aligned to the speaker's audio onset."
            );
        }
    }

    [RelayCommand]
    public async Task TestCharacterVoiceAsync(MovieCharacter? character)
    {
        var target = character ?? SelectedCharacter ?? Characters.FirstOrDefault();
        if (target == null)
            return;

        try
        {
            StatusMessage = $"Auditioning voice for {target.Name}...";
            var tts = new KhmerTtsService();
            var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing_VoiceTest");
            Directory.CreateDirectory(tempDir);

            var arch = ActorEmotionEngine.GetArchetype(target.ToneArchetype);
            var emotionCfg = ActorEmotionEngine.GetConfig(target.EmotionPreset);
            var sampleText = !string.IsNullOrWhiteSpace(arch.SampleLine)
                ? arch.SampleLine
                : "ជំរាបសួរ! ខ្ញុំកំពុងសាកល្បងសំឡេងបញ្ចូលតួអង្គ។";

            var voiceToUse = target.BaseVoice ?? SelectedVoice.Id;
            var speechRate = target.SpeechRate ?? "+12%";
            var effectivePitch = ActorEmotionEngine.ComputeEffectiveTtsPitch(
                target.PitchShift,
                emotionCfg.TtsPitch,
                emotionCfg.PitchShiftOffset
            );

            var rawTts = Path.Combine(tempDir, $"test_{target.Id:N}.mp3");
            await tts.SynthesizeKhmerSpeechAsync(
                sampleText,
                rawTts,
                voiceToUse,
                rate: speechRate,
                pitch: effectivePitch,
                volume: emotionCfg.TtsVolume
            );

            string finalClip = rawTts;
            if (
                target.EnableRvc
                && !string.IsNullOrWhiteSpace(target.RvcModelPath)
                && File.Exists(target.RvcModelPath)
            )
            {
                StatusMessage =
                    $"Applying {Path.GetFileNameWithoutExtension(target.RvcModelPath)} voice clone...";
                var clonedClip = Path.Combine(tempDir, $"cloned_{target.Id:N}.wav");
                int effectivePitchShift = target.PitchShift + emotionCfg.PitchShiftOffset;
                await _rvcService.ConvertVoiceAsync(
                    rawTts,
                    clonedClip,
                    target.RvcModelPath,
                    target.RvcIndexPath,
                    effectivePitchShift,
                    null,
                    CancellationToken.None
                );
                if (File.Exists(clonedClip) && new FileInfo(clonedClip).Length > 1024)
                {
                    finalClip = clonedClip;
                }
            }

            // Apply acoustic tone filter (warmth, clarity, emotion DSP)
            var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
            var filteredClip = Path.Combine(tempDir, $"tone_{target.Id:N}.wav");
            var applied = await ActorEmotionEngine.ApplyActorAcousticFilterAsync(
                ffmpeg,
                finalClip,
                filteredClip,
                emotionCfg.Name,
                target.ToneWarmth,
                target.ToneClarity,
                target.ToneArchetype,
                CancellationToken.None
            );
            if (applied && File.Exists(filteredClip) && new FileInfo(filteredClip).Length > 1024)
            {
                finalClip = filteredClip;
            }

            if (File.Exists(finalClip))
            {
                CurrentPlayingMediaPath = finalClip;
                IsCurrentMediaVideo = false;
                HasActiveMedia = true;
                IsMonitorPlaying = true;
                ActiveMediaTitle = $"Audition: {target.Name} [{arch.DisplayName}]";
                PlayMediaRequested?.Invoke(finalClip, false, ActiveMediaTitle);
                _snackbarManager.Notify(
                    $"Auditioning {target.Name} ({arch.DisplayName}) in Studio Monitor!"
                );
                StatusMessage =
                    $"Auditioning {target.Name} ({arch.DisplayName}) in Studio Monitor.";
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Voice audition failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public void StopMonitor()
    {
        IsMonitorPlaying = false;
        HasActiveMedia = false;
        ActiveMediaTitle = "Studio Monitor Standby";
        StopMediaRequested?.Invoke();
    }

    [RelayCommand]
    public void OpenInExternalPlayer()
    {
        if (
            !string.IsNullOrWhiteSpace(CurrentPlayingMediaPath)
            && File.Exists(CurrentPlayingMediaPath)
        )
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = CurrentPlayingMediaPath,
                        UseShellExecute = true,
                    }
                );
            }
            catch (Exception ex)
            {
                _snackbarManager.Notify($"Unable to launch external player: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void PlayFullMovie()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            _snackbarManager.Notify("Please select an input video file first.");
            return;
        }

        CurrentPlayingMediaPath = VideoFilePath;
        IsCurrentMediaVideo = true;
        HasActiveMedia = true;
        IsMonitorPlaying = true;
        ActiveMediaTitle = $"Movie: {Path.GetFileName(VideoFilePath)}";
        PlayMediaRequested?.Invoke(VideoFilePath, true, ActiveMediaTitle);
        _snackbarManager.Notify("Playing full movie in Studio Monitor.");
    }

    [RelayCommand]
    public async Task SynthesizeAllLinesAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue segments to synthesize.");
            return;
        }

        if (IsPreSynthesizing || IsProcessing)
            return;

        // Auto-translation guard: ensure no empty Khmer lines try to be spoken
        if (
            Segments.Any(s =>
                string.IsNullOrWhiteSpace(s.KhmerText) && !string.IsNullOrWhiteSpace(s.OriginalText)
            )
        )
        {
            StatusMessage = "Translating dialogue lines to Khmer before speech synthesis...";
            await AutoTranslateAllAsync();
        }

        // Auto-cast guard: ensure characters are assigned
        if (Characters.Count == 0 || Segments.All(s => s.AssignedCharacter == null))
        {
            StatusMessage = "Assigning character voice cast before speech synthesis...";
            await AutoDetectSpeakersAsync();
        }

        IsPreSynthesizing = true;
        StatusMessage = "Batch synthesizing dubbed speech for all scenes...";
        var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing_StudioAudio");
        Directory.CreateDirectory(tempDir);
        var tts = new KhmerTtsService();

        try
        {
            for (int i = 0; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                var text = !string.IsNullOrWhiteSpace(seg.KhmerText)
                    ? seg.KhmerText
                    : seg.OriginalText;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                StatusMessage = $"Dubbing scene {i + 1}/{Segments.Count}: \"{text}\"...";
                PreSynthesizeProgress = (double)(i + 1) / Segments.Count * 100.0;

                var charObj =
                    Characters.FirstOrDefault(c => c.Id == seg.CharacterId)
                    ?? Characters.FirstOrDefault(c =>
                        string.Equals(c.Name, seg.SpeakerName, StringComparison.OrdinalIgnoreCase)
                    )
                    ?? Characters.FirstOrDefault();

                var voice = charObj?.BaseVoice ?? SelectedVoice.Id;
                var rate = charObj?.SpeechRate ?? "+15%";
                var rawPath = Path.Combine(tempDir, $"dub_{seg.Index:D4}_tts.mp3");

                await tts.SynthesizeKhmerSpeechAsync(text, rawPath, voice, rate);

                string finalAudio = rawPath;
                if (
                    charObj != null
                    && charObj.EnableRvc
                    && !string.IsNullOrWhiteSpace(charObj.RvcModelPath)
                    && File.Exists(charObj.RvcModelPath)
                )
                {
                    var rvcOut = Path.Combine(tempDir, $"dub_{seg.Index:D4}_rvc.wav");
                    await _rvcService.ConvertVoiceAsync(
                        rawPath,
                        rvcOut,
                        charObj.RvcModelPath,
                        charObj.RvcIndexPath,
                        charObj.PitchShift,
                        null,
                        CancellationToken.None
                    );
                    if (File.Exists(rvcOut) && new FileInfo(rvcOut).Length > 1024)
                    {
                        finalAudio = rvcOut;
                    }
                }

                seg.AudioClipPath = finalAudio;
                MeasureAudioDuration(seg);
            }

            StatusMessage =
                $"All {Segments.Count} dialogue lines synthesized with character cloning!";
            _snackbarManager.Notify(
                $"Batch synthesized all {Segments.Count} scenes! You can now audition each line."
            );
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Batch synthesis error: {ex.Message}");
        }
        finally
        {
            IsPreSynthesizing = false;
        }
    }

    [RelayCommand]
    public async Task ScanAudioToTextAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            _snackbarManager.Notify("Please select a video file first to scan speech.");
            return;
        }

        if (IsScanningAudio || IsProcessing)
            return;

        IsScanningAudio = true;
        StatusMessage = "Extracting audio and scanning speech to text...";

        string? tempScanDir = null;
        try
        {
            if (!IsWhisperAvailable)
            {
                StatusMessage = "Setting up Whisper AI speech engine...";
                await DownloadWhisperAiAsync();
            }

            var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
            var progress = new Progress<string>(msg => StatusMessage = msg);

            string audioToTranscribe = VideoFilePath;

            // If stem separation is enabled, extract vocal stem to eliminate background rain, thunder & music
            if (EnableAiStemSeparation)
            {
                try
                {
                    StatusMessage = "Isolating dialogue vocal stem for clean speech recognition...";
                    tempScanDir = Path.Combine(
                        Path.GetTempPath(),
                        "855Media_Scan_" + Guid.NewGuid().ToString("N")
                    );
                    Directory.CreateDirectory(tempScanDir);
                    var fullAudio = Path.Combine(tempScanDir, "raw_audio.wav");
                    var vocalPath = Path.Combine(tempScanDir, "vocals.wav");
                    var bgmPath = Path.Combine(tempScanDir, "bgm.wav");

                    await _stemService.ExtractFullAudioAsync(ffmpeg, VideoFilePath, fullAudio);
                    await _stemService.SeparateStemsAsync(
                        ffmpeg,
                        fullAudio,
                        vocalPath,
                        bgmPath,
                        useAiDemucs: true,
                        progressLogger: msg => StatusMessage = msg
                    );

                    if (File.Exists(vocalPath))
                    {
                        audioToTranscribe = vocalPath;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage =
                        $"AI vocal isolation note ({ex.Message}), scanning audio directly...";
                }
            }

            var segments = await _transcriptionService.TranscribeAudioAsync(
                ffmpeg,
                audioToTranscribe,
                SourceLanguage,
                progress
            );

            var defaultChar = Characters.FirstOrDefault();
            Segments.Clear();
            foreach (var seg in segments)
            {
                if (defaultChar != null && seg.CharacterId == null)
                {
                    seg.CharacterId = defaultChar.Id;
                    seg.SpeakerName = defaultChar.Name;
                    seg.SpeakerColor = defaultChar.ColorTag;
                }
                Segments.Add(seg);
            }

            if (Segments.Count > 0)
            {
                // If first segment starts near 00:00 but video has opening silence/music, auto-align start
                if (
                    Segments[0].StartTime < TimeSpan.FromSeconds(0.4)
                    && !string.IsNullOrWhiteSpace(VideoFilePath)
                    && File.Exists(VideoFilePath)
                )
                {
                    var actualStart = await AudioTranscriptionService.DetectActualSpeechStartAsync(
                        ffmpeg,
                        VideoFilePath,
                        Segments[0].StartTime,
                        Segments[0].EndTime
                    );
                    if (
                        actualStart.HasValue
                        && actualStart.Value
                            > Segments[0].StartTime + TimeSpan.FromMilliseconds(150)
                    )
                    {
                        Segments[0].StartTime = actualStart.Value;
                    }
                }

                StatusMessage = $"Scanned {Segments.Count} dialogue lines. Translating to Khmer...";
                await AutoTranslateAllAsync();
                await AutoDetectSpeakersAsync();
                _ = ExtractSceneThumbnailsAsync();
            }
            else
            {
                StatusMessage =
                    "No dialogue detected in audio. You can click 'Load Example' or 'Add Line'.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Audio scan error: {ex.Message}";
            _snackbarManager.Notify($"Audio scan error: {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempScanDir) && Directory.Exists(tempScanDir))
            {
                try
                {
                    Directory.Delete(tempScanDir, true);
                }
                catch { }
            }
            IsScanningAudio = false;
        }
    }

    [RelayCommand]
    public async Task DownloadWhisperAiAsync()
    {
        if (IsDownloadingWhisper)
            return;

        IsDownloadingWhisper = true;
        StatusMessage = "Downloading offline Whisper AI model and binary...";

        try
        {
            var progress = new Progress<double>(p => WhisperDownloadProgress = p);
            var status = new Progress<string>(s => StatusMessage = s);

            await _transcriptionService.DownloadWhisperEngineAsync(progress, status);
            OnPropertyChanged(nameof(IsWhisperAvailable));
            _snackbarManager.Notify("Whisper AI speech engine installed successfully!");
            StatusMessage = "Whisper AI speech engine ready!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Whisper download failed: {ex.Message}";
            _snackbarManager.Notify($"Whisper download failed: {ex.Message}");
        }
        finally
        {
            IsDownloadingWhisper = false;
        }
    }

    [RelayCommand]
    public void NewProject()
    {
        try
        {
            _activeCts?.Cancel();
            _activeCts?.Dispose();
        }
        catch { }
        finally
        {
            _activeCts = null;
        }

        IsProcessing = false;
        IsScanningAudio = false;
        IsExtractingScenes = false;
        IsTranslating = false;
        IsPreSynthesizing = false;
        Progress = 0;

        StopMonitor();
        CurrentPlayingMediaPath = string.Empty;

        CurrentProjectPath = null;
        ProjectName = "Untitled Project";
        VideoFilePath = string.Empty;
        OutputFilePath = string.Empty;
        Segments.Clear();
        InitDefaultCharacters();
        IsProjectDirty = false;

        BrowseVideoFileCommand.NotifyCanExecuteChanged();
        ExtendCurrentVideoCommand.NotifyCanExecuteChanged();
        StartDubbingCommand.NotifyCanExecuteChanged();
        CancelDubbingCommand.NotifyCanExecuteChanged();
        ScanAudioToTextCommand.NotifyCanExecuteChanged();

        StatusMessage = "Started new dubbing project.";
        _snackbarManager.Notify("New project started.");
    }

    [RelayCommand]
    public async Task SaveProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentProjectPath))
        {
            await SaveProjectAsAsync();
            return;
        }

        await SaveProjectInternalAsync(CurrentProjectPath);
    }

    [RelayCommand]
    public async Task SaveProjectAsAsync()
    {
        var defaultFileName =
            !string.IsNullOrWhiteSpace(ProjectName) && ProjectName != "Untitled Project"
                ? $"{ProjectName}.855dub"
                : (
                    !string.IsNullOrWhiteSpace(VideoFilePath)
                        ? $"{Path.GetFileNameWithoutExtension(VideoFilePath)}.855dub"
                        : "MyProject.855dub"
                );

        var filePath = await _dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType("855Media Dubbing Project (*.855dub)")
                {
                    Patterns = ["*.855dub"],
                },
                new FilePickerFileType("JSON Project (*.json)") { Patterns = ["*.json"] },
            ],
            defaultFileName
        );

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        await SaveProjectInternalAsync(filePath);
    }

    [RelayCommand]
    public async Task OpenProjectAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync([
            new FilePickerFileType("855Media Dubbing Project (*.855dub, *.json)")
            {
                Patterns = ["*.855dub", "*.json"],
            },
        ]);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        await LoadProjectInternalAsync(filePath);
    }

    private async Task SaveProjectInternalAsync(string filePath)
    {
        try
        {
            var project = new DubbingProject
            {
                ProjectName = Path.GetFileNameWithoutExtension(filePath),
                VideoFilePath = VideoFilePath,
                OutputFilePath = OutputFilePath,
                SourceLanguage = SourceLanguage,
                SelectedVoice = SelectedVoice.Id,
                EnableVoiceCloning = EnableVoiceCloning,
                RvcModelPath = SelectedRvcModel?.PthPath,
                RvcIndexPath = SelectedRvcModel?.IndexPath,
                PitchShift = PitchShift,
                RvcConcurrency = RvcConcurrency,
                BgmVolume = BgmVolume,
                VoiceVolume = VoiceVolume,
                EnableDynamicDucking = EnableDynamicDucking,
                EnableAiStemSeparation = EnableAiStemSeparation,
                EnableLoudnessNormalization = EnableLoudnessNormalization,
                EnableSmartTimeStretch = EnableSmartTimeStretch,
            };

            foreach (var c in Characters)
            {
                project.Characters.Add(
                    new MovieCharacterData
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Gender = c.Gender,
                        BaseVoice = c.BaseVoice,
                        SpeechRate = c.SpeechRate,
                        EnableRvc = c.EnableRvc,
                        RvcModelPath = c.RvcModelPath,
                        RvcIndexPath = c.RvcIndexPath,
                        PitchShift = c.PitchShift,
                        EmotionPreset = c.EmotionPreset,
                        ToneArchetype = c.ToneArchetype,
                        ToneWarmth = c.ToneWarmth,
                        ToneClarity = c.ToneClarity,
                        ColorTag = c.ColorTag,
                    }
                );
            }

            foreach (var seg in Segments)
            {
                project.Segments.Add(
                    new SubtitleSegmentData
                    {
                        Index = seg.Index,
                        StartSeconds = seg.StartTime.TotalSeconds,
                        EndSeconds = seg.EndTime.TotalSeconds,
                        OriginalText = seg.OriginalText,
                        KhmerText = seg.KhmerText,
                        CharacterId = seg.CharacterId,
                        SpeakerName = seg.SpeakerName,
                        SpeakerColor = seg.SpeakerColor,
                        Emotion = seg.Emotion,
                        DetectedGender = seg.DetectedGender,
                        AudioClipPath = seg.AudioClipPath,
                        ThumbnailPath = seg.ThumbnailPath,
                    }
                );
            }

            await project.SaveAsync(filePath);
            CurrentProjectPath = filePath;
            ProjectName = project.ProjectName;
            IsProjectDirty = false;

            StatusMessage = $"Saved project to {Path.GetFileName(filePath)}";
            _snackbarManager.Notify($"Project saved: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to save project: {ex.Message}");
        }
    }

    private async Task LoadProjectInternalAsync(string filePath)
    {
        try
        {
            StatusMessage = $"Loading project {Path.GetFileName(filePath)}...";
            var project = await DubbingProject.LoadAsync(filePath);

            CurrentProjectPath = filePath;
            ProjectName = !string.IsNullOrWhiteSpace(project.ProjectName)
                ? project.ProjectName
                : Path.GetFileNameWithoutExtension(filePath);

            if (
                !string.IsNullOrWhiteSpace(project.VideoFilePath)
                && File.Exists(project.VideoFilePath)
            )
            {
                VideoFilePath = project.VideoFilePath;
            }

            if (!string.IsNullOrWhiteSpace(project.OutputFilePath))
            {
                OutputFilePath = project.OutputFilePath;
            }

            SourceLanguage = project.SourceLanguage ?? "Auto";
            SelectedVoice =
                AvailableVoices.FirstOrDefault(v => v.Id == project.SelectedVoice) ?? SelectedVoice;
            EnableVoiceCloning = project.EnableVoiceCloning;
            PitchShift = project.PitchShift;
            RvcConcurrency = project.RvcConcurrency > 0 ? project.RvcConcurrency : 4;
            BgmVolume = project.BgmVolume;
            VoiceVolume = project.VoiceVolume;
            EnableDynamicDucking = project.EnableDynamicDucking;
            EnableAiStemSeparation = project.EnableAiStemSeparation;
            EnableLoudnessNormalization = project.EnableLoudnessNormalization;
            EnableSmartTimeStretch = project.EnableSmartTimeStretch;

            if (project.Characters.Count > 0)
            {
                Characters.Clear();
                foreach (var cd in project.Characters)
                {
                    Characters.Add(
                        new MovieCharacter
                        {
                            Id = cd.Id,
                            Name = cd.Name,
                            Gender = cd.Gender ?? "Male",
                            BaseVoice = cd.BaseVoice,
                            SpeechRate = cd.SpeechRate,
                            EnableRvc = cd.EnableRvc,
                            RvcModelPath = cd.RvcModelPath,
                            RvcIndexPath = cd.RvcIndexPath,
                            PitchShift = cd.PitchShift,
                            EmotionPreset = cd.EmotionPreset ?? "Normal",
                            ToneArchetype = cd.ToneArchetype ?? "Hero",
                            ToneWarmth = cd.ToneWarmth,
                            ToneClarity = cd.ToneClarity,
                            ColorTag = cd.ColorTag,
                        }
                    );
                }
                SelectedCharacter = Characters.FirstOrDefault();
            }

            Segments.Clear();
            foreach (var sd in project.Segments)
            {
                Segments.Add(
                    new SubtitleSegment
                    {
                        Index = sd.Index,
                        StartTime = TimeSpan.FromSeconds(sd.StartSeconds),
                        EndTime = TimeSpan.FromSeconds(sd.EndSeconds),
                        OriginalText = sd.OriginalText,
                        KhmerText = sd.KhmerText,
                        CharacterId = sd.CharacterId,
                        SpeakerName = sd.SpeakerName,
                        SpeakerColor = sd.SpeakerColor,
                        Emotion = sd.Emotion ?? "Normal",
                        DetectedGender = sd.DetectedGender ?? "Unknown",
                        AudioClipPath = sd.AudioClipPath,
                        ThumbnailPath = sd.ThumbnailPath,
                    }
                );
            }

            IsProjectDirty = false;
            StatusMessage = $"Loaded project '{ProjectName}' with {Segments.Count} dialogue lines.";
            _snackbarManager.Notify(
                $"Opened project: {Path.GetFileName(filePath)} ({Segments.Count} lines)"
            );

            if (!string.IsNullOrWhiteSpace(VideoFilePath) && File.Exists(VideoFilePath))
            {
                _ = ExtractSceneThumbnailsAsync();
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to load project: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ImportSubtitlesAsync()
    {
        var filePath = await _dialogManager.PromptOpenFilePathAsync([
            new FilePickerFileType("Subtitle Files") { Patterns = ["*.srt", "*.vtt"] },
        ]);

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        await ImportSubtitlesFromFileAsync(filePath);
    }

    public async Task ImportSubtitlesFromFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var parsed = new SubtitleTranslationService().ParseSrt(content);

            Segments.Clear();
            foreach (var seg in parsed)
            {
                Segments.Add(seg);
            }

            // Auto-align first segment if video is loaded and first segment is at 00:00
            if (
                Segments.Count > 0
                && Segments[0].StartTime < TimeSpan.FromSeconds(0.4)
                && !string.IsNullOrWhiteSpace(VideoFilePath)
                && File.Exists(VideoFilePath)
            )
            {
                var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
                var actualStart = await AudioTranscriptionService.DetectActualSpeechStartAsync(
                    ffmpeg,
                    VideoFilePath,
                    Segments[0].StartTime,
                    Segments[0].EndTime
                );
                if (
                    actualStart.HasValue
                    && actualStart.Value > Segments[0].StartTime + TimeSpan.FromMilliseconds(150)
                )
                {
                    Segments[0].StartTime = actualStart.Value;
                }
            }

            StatusMessage =
                $"Imported {Segments.Count} dialogue lines from {Path.GetFileName(filePath)}";
            _snackbarManager.Notify($"Imported {Segments.Count} lines of dialogue.");
            if (!string.IsNullOrWhiteSpace(VideoFilePath) && File.Exists(VideoFilePath))
            {
                _ = ExtractSceneThumbnailsAsync();
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to import subtitles: {ex.Message}");
        }
    }

    [RelayCommand]
    public void AddSegment()
    {
        var lastEnd = Segments.LastOrDefault()?.EndTime ?? TimeSpan.Zero;
        var newSeg = new SubtitleSegment
        {
            Index = Segments.Count + 1,
            StartTime = lastEnd,
            EndTime = lastEnd + TimeSpan.FromSeconds(3),
            OriginalText = "New dialogue",
            KhmerText = "ពាក្យថ្មី",
        };
        Segments.Add(newSeg);
        SelectedSegment = newSeg;
    }

    [RelayCommand]
    public void RemoveSegment(SubtitleSegment? seg = null)
    {
        var target = seg ?? SelectedSegment;
        if (target != null)
        {
            Segments.Remove(target);
            for (int i = 0; i < Segments.Count; i++)
            {
                Segments[i].Index = i + 1;
            }
            IsProjectDirty = true;
        }
    }

    [RelayCommand]
    public void MergeSentences()
    {
        if (Segments.Count < 2)
        {
            _snackbarManager.Notify("At least 2 dialogue lines are needed to merge.");
            return;
        }

        int originalCount = Segments.Count;
        var reconstructed = DialogueSenseEngine.ReconstructSentences(
            Segments,
            maxGapSeconds: 2.2,
            maxDurationSeconds: 12.0
        );
        int mergedCount = originalCount - reconstructed.Count;

        if (mergedCount > 0)
        {
            Segments.Clear();
            foreach (var seg in reconstructed)
            {
                Segments.Add(seg);
            }

            IsProjectDirty = true;
            SelectedSegment = Segments.FirstOrDefault();
            StatusMessage =
                $"Merged {mergedCount} sentence fragments into complete dialogue sentences (sense-to-sense matched).";
            _snackbarManager.Notify(
                $"Merged {mergedCount} sentence fragments into complete dialogue sentences (sense-to-sense matched)."
            );
        }
        else
        {
            _snackbarManager.Notify(
                "All segments are already complete sentences with continuous speaker dialogue."
            );
        }
    }

    [RelayCommand]
    public void ImproveOriginalDialogue(SubtitleSegment? seg = null)
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue lines to improve. Import or add lines first.");
            return;
        }

        var target = seg ?? SelectedSegment;
        if (target != null && seg != null)
        {
            target.OriginalText = DialogueSenseEngine.ImproveOriginalDialogue(
                target.OriginalText,
                SourceLanguage
            );
            IsProjectDirty = true;
            _snackbarManager.Notify($"Improved original dialogue for line #{target.Index}.");
            return;
        }

        int improvedCount = 0;
        foreach (var s in Segments)
        {
            var improved = DialogueSenseEngine.ImproveOriginalDialogue(
                s.OriginalText,
                SourceLanguage
            );
            if (improved != s.OriginalText)
            {
                s.OriginalText = improved;
                improvedCount++;
            }
        }

        IsProjectDirty = true;
        StatusMessage =
            $"Improved {improvedCount} original dialogue lines (noise tags removed, stutters cleaned, sense formatted).";
        _snackbarManager.Notify(
            $"Improved {improvedCount} original dialogue lines (noise tags removed, stutters cleaned, sense formatted)."
        );
    }

    [RelayCommand]
    public async Task AutoPrepareAllAsync()
    {
        if (IsAutoPreparing || IsProcessing)
            return;

        IsAutoPreparing = true;
        try
        {
            // Stage 1: Transcription / Subtitle Detection
            if (Segments.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
                {
                    _snackbarManager.Notify(
                        "Please load a video file or import subtitles first to auto-prepare."
                    );
                    return;
                }

                StatusMessage = "⚡ [1/5] Transcribing video speech with Whisper AI...";
                await ScanAudioToTextAsync();

                if (Segments.Count == 0)
                {
                    _snackbarManager.Notify("No speech dialogue detected in video.");
                    return;
                }
            }
            else
            {
                StatusMessage = "⚡ [1/5] Dialogue segments ready.";
            }

            // Stage 2: Clean dialogue text & polish formatting (preserving segment timestamps)
            if (Segments.Count > 0)
            {
                StatusMessage = "⚡ [2/5] Cleaning dialogue text, stutters, and formatting...";
                ImproveOriginalDialogue();
                PolishKhmerDialogue();

                // If first segment starts near 00:00 but video has opening silence/music, auto-align start
                if (
                    Segments[0].StartTime < TimeSpan.FromSeconds(0.4)
                    && !string.IsNullOrWhiteSpace(VideoFilePath)
                    && File.Exists(VideoFilePath)
                )
                {
                    var ffmpeg = _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
                    var actualStart = await AudioTranscriptionService.DetectActualSpeechStartAsync(
                        ffmpeg,
                        VideoFilePath,
                        Segments[0].StartTime,
                        Segments[0].EndTime
                    );
                    if (
                        actualStart.HasValue
                        && actualStart.Value
                            > Segments[0].StartTime + TimeSpan.FromMilliseconds(150)
                    )
                    {
                        Segments[0].StartTime = actualStart.Value;
                    }
                }
            }

            // Stage 3: Contextual Khmer Translation
            var untranslated = Segments.Any(s =>
                string.IsNullOrWhiteSpace(s.KhmerText) && !string.IsNullOrWhiteSpace(s.OriginalText)
            );
            if (untranslated)
            {
                StatusMessage = "⚡ [3/5] Contextually translating dialogue lines into Khmer...";
                await AutoTranslateAllAsync();
            }
            else
            {
                StatusMessage = "⚡ [3/5] Dialogue translation already up-to-date.";
            }

            // Stage 4: Cast Diarization & Voice Actor Assignment
            StatusMessage = "⚡ [4/5] Auto-detecting cast members, voices & dialogue turns...";
            await AutoDetectSpeakersAsync();

            // Stage 5: Emotion Tone Detection
            StatusMessage = "⚡ [5/5] Detecting dramatic acting tones & nuances...";
            await AutoDetectEmotionsAsync();

            // Stage 6: Video Scene Snapshots
            if (!string.IsNullOrWhiteSpace(VideoFilePath) && File.Exists(VideoFilePath))
            {
                _ = ExtractSceneThumbnailsAsync();
            }

            IsProjectDirty = true;
            StatusMessage =
                $"⚡ Studio Auto-Prep Complete! {Segments.Count} scenes prepared across {Characters.Count} voice actors and acting tones.";
            _snackbarManager.Notify(
                $"⚡ Studio Auto-Prep Complete! {Segments.Count} scenes ready to audition or dub."
            );
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-Prep Note: {ex.Message}";
            _snackbarManager.Notify($"Auto-Prep Note: {ex.Message}");
        }
        finally
        {
            IsAutoPreparing = false;
        }
    }

    [RelayCommand]
    public async Task AutoTranslateAllAsync()
    {
        if (Segments.Count == 0)
        {
            _snackbarManager.Notify("No dialogue lines to translate. Add or import lines first.");
            return;
        }

        if (IsTranslating)
            return;

        IsTranslating = true;

        try
        {
            var segmentsToTranslate = Segments
                .Where(s => !string.IsNullOrWhiteSpace(s.OriginalText))
                .ToList();
            int total = segmentsToTranslate.Count;
            int translatedCount = 0;

            if (total == 0)
            {
                StatusMessage = "No lines with text to translate.";
                return;
            }

            bool useGemini = !string.IsNullOrWhiteSpace(_settingsService.GeminiApiKey);
            StatusMessage = useGemini
                ? $"Translating {total} dialogue lines with Gemini AI ({_settingsService.GeminiModel})..."
                : $"Translating {total} dialogue lines to Khmer (ភាសាខ្មែរ)...";

            if (useGemini)
            {
                await _subService.TranslateSegmentsWithGeminiAsync(
                    _settingsService.GeminiApiKey!,
                    segmentsToTranslate,
                    SourceLanguage,
                    _settingsService.GeminiModel,
                    (curr, tot) =>
                    {
                        StatusMessage = $"Translated {curr}/{tot} lines with Gemini AI...";
                    }
                );
            }
            else
            {
                await Parallel.ForEachAsync(
                    segmentsToTranslate,
                    new ParallelOptions { MaxDegreeOfParallelism = 5 },
                    async (seg, ct) =>
                    {
                        try
                        {
                            seg.KhmerText = await _subService.TranslateToKhmerAsync(
                                seg.OriginalText,
                                SourceLanguage,
                                cancellationToken: ct
                            );
                        }
                        catch
                        {
                            // Fallback: preserve original text if API error
                        }

                        int current = Interlocked.Increment(ref translatedCount);
                        if (current % 5 == 0 || current == total)
                        {
                            StatusMessage = $"Translated {current}/{total} lines into Khmer...";
                        }
                    }
                );
            }

            StatusMessage = useGemini
                ? $"Gemini AI translation completed ({total} lines with cinema naturalization)."
                : $"Auto translation completed ({total} lines).";
            _snackbarManager.Notify(
                useGemini
                    ? $"Successfully translated {total} dialogue lines using Gemini AI!"
                    : $"Successfully translated {total} dialogue lines to Khmer!"
            );

            await AutoDetectEmotionsAsync();
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Translation error: {ex.Message}");
        }
        finally
        {
            IsTranslating = false;
        }
    }

    [RelayCommand]
    public async Task TranslateSelectedLineAsync()
    {
        if (SelectedSegment == null || string.IsNullOrWhiteSpace(SelectedSegment.OriginalText))
            return;

        try
        {
            SelectedSegment.KhmerText = await _subService.TranslateToKhmerAsync(
                SelectedSegment.OriginalText,
                SourceLanguage,
                _settingsService.GeminiApiKey,
                SelectedSegment.SpeakerName,
                SelectedSegment.DetectedGender,
                _settingsService.GeminiModel
            );
            _snackbarManager.Notify(
                !string.IsNullOrWhiteSpace(_settingsService.GeminiApiKey)
                    ? "Line translated to Khmer via Gemini AI."
                    : "Line translated to Khmer."
            );
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Translation error: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task StartDubbingAsync()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            _snackbarManager.Notify("Please select an input video file first.");
            return;
        }

        if (IsProcessing)
            return;

        IsProcessing = true;
        Progress = 0;
        StatusMessage = "Initializing dubbing pipeline...";
        _activeCts = new CancellationTokenSource();

        var job = new DubbingJob
        {
            VideoFilePath = VideoFilePath,
            OutputFilePath = OutputFilePath,
            SourceLanguage = SourceLanguage,
            SelectedVoice = SelectedVoice.Id,
            EnableVoiceCloning = EnableVoiceCloning,
            RvcModelPath = SelectedRvcModel?.PthPath,
            RvcIndexPath = SelectedRvcModel?.IndexPath,
            PitchShift = PitchShift,
            RvcConcurrency = RvcConcurrency,
            BgmVolume = BgmVolume,
            VoiceVolume = VoiceVolume,
            EnableAiStemSeparation = EnableAiStemSeparation,
            EnableDynamicDucking = EnableDynamicDucking,
            EnableLoudnessNormalization = EnableLoudnessNormalization,
            EnableSmartTimeStretch = EnableSmartTimeStretch,
            GeminiApiKey = _settingsService.GeminiApiKey,
            GeminiModel = _settingsService.GeminiModel,
        };

        foreach (var c in Characters)
        {
            job.Characters.Add(c);
        }

        foreach (var seg in Segments)
        {
            job.Segments.Add(seg);
        }

        job.PropertyChanged += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (e.PropertyName == nameof(DubbingJob.Progress))
                {
                    Progress = job.Progress;
                }
                else if (e.PropertyName == nameof(DubbingJob.StatusMessage))
                {
                    StatusMessage = job.StatusMessage;
                }
                else if (e.PropertyName == nameof(DubbingJob.DetailedLog))
                {
                    DetailedLog = job.DetailedLog;
                }
            });
        };

        try
        {
            await _pipeline.ExecuteAsync(job, null, _activeCts.Token);

            // Sync back any translated segments to the UI
            Segments.Clear();
            foreach (var seg in job.Segments)
            {
                Segments.Add(seg);
            }

            _snackbarManager.Notify("Khmer dubbing successfully completed!");
        }
        catch (OperationCanceledException)
        {
            _snackbarManager.Notify("Dubbing was canceled.");
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Dubbing failed: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    [RelayCommand]
    public void CancelDubbing()
    {
        if (_activeCts != null && !_activeCts.IsCancellationRequested)
        {
            _activeCts.Cancel();
            StatusMessage = "Canceling dubbing process...";
        }
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        if (File.Exists(OutputFilePath))
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{OutputFilePath}\"",
                    UseShellExecute = true,
                }
            );
        }
        else if (!string.IsNullOrWhiteSpace(OutputFilePath))
        {
            var dir = Path.GetDirectoryName(OutputFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
        }
    }

    [RelayCommand]
    public async Task AddVideosToBatchAsync()
    {
        var files = await _dialogManager.PromptOpenFilePathsAsync([
            new FilePickerFileType("Video Files")
            {
                Patterns =
                [
                    "*.mp4",
                    "*.mkv",
                    "*.mov",
                    "*.avi",
                    "*.webm",
                    "*.flv",
                    "*.wmv",
                    "*.m4v",
                ],
            },
        ]);

        if (files == null || files.Count == 0)
            return;

        int added = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                continue;

            if (
                BatchQueue.Any(j =>
                    string.Equals(j.VideoFilePath, file, StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;

            var dir = Path.GetDirectoryName(file) ?? string.Empty;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
            var outPath = Path.Combine(dir, $"{nameWithoutExt}_khmer_dubbed.mp4");

            var job = new DubbingJob
            {
                VideoFilePath = file,
                OutputFilePath = outPath,
                SourceLanguage = SourceLanguage,
                SelectedVoice = SelectedVoice.Id,
                EnableVoiceCloning = EnableVoiceCloning,
                RvcModelPath = SelectedRvcModel?.PthPath,
                RvcIndexPath = SelectedRvcModel?.IndexPath,
                PitchShift = PitchShift,
                RvcConcurrency = RvcConcurrency,
                BgmVolume = BgmVolume,
                VoiceVolume = VoiceVolume,
                EnableAiStemSeparation = EnableAiStemSeparation,
                EnableDynamicDucking = EnableDynamicDucking,
                EnableLoudnessNormalization = EnableLoudnessNormalization,
                EnableSmartTimeStretch = EnableSmartTimeStretch,
                GeminiApiKey = _settingsService.GeminiApiKey,
                GeminiModel = _settingsService.GeminiModel,
                Status = DubbingJobStatus.Queued,
                StatusMessage = "Queued in batch list",
            };

            foreach (var c in Characters)
            {
                job.Characters.Add(c);
            }

            BatchQueue.Add(job);
            added++;
        }

        if (added > 0)
        {
            _snackbarManager.Notify($"Added {added} video(s) to Batch Dubbing Queue.");
            StatusMessage = $"Batch queue: {BatchQueue.Count} video(s) ready.";
        }
    }

    [RelayCommand]
    public void AddCurrentProjectToBatch()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath) || !File.Exists(VideoFilePath))
        {
            _snackbarManager.Notify("Please open a video file first to add to the batch queue.");
            return;
        }

        var dir = Path.GetDirectoryName(VideoFilePath) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(VideoFilePath);
        var outPath = !string.IsNullOrWhiteSpace(OutputFilePath)
            ? OutputFilePath
            : Path.Combine(dir, $"{nameWithoutExt}_khmer_dubbed.mp4");

        var job = new DubbingJob
        {
            VideoFilePath = VideoFilePath,
            OutputFilePath = outPath,
            SourceLanguage = SourceLanguage,
            SelectedVoice = SelectedVoice.Id,
            EnableVoiceCloning = EnableVoiceCloning,
            RvcModelPath = SelectedRvcModel?.PthPath,
            RvcIndexPath = SelectedRvcModel?.IndexPath,
            PitchShift = PitchShift,
            RvcConcurrency = RvcConcurrency,
            BgmVolume = BgmVolume,
            VoiceVolume = VoiceVolume,
            EnableAiStemSeparation = EnableAiStemSeparation,
            EnableDynamicDucking = EnableDynamicDucking,
            EnableLoudnessNormalization = EnableLoudnessNormalization,
            EnableSmartTimeStretch = EnableSmartTimeStretch,
            GeminiApiKey = _settingsService.GeminiApiKey,
            GeminiModel = _settingsService.GeminiModel,
            Status = DubbingJobStatus.Queued,
            StatusMessage = "Queued from current project",
        };

        foreach (var c in Characters)
        {
            job.Characters.Add(c);
        }

        foreach (var seg in Segments)
        {
            job.Segments.Add(seg);
        }

        BatchQueue.Add(job);
        _snackbarManager.Notify($"Added current project ({job.FileName}) to Batch Dubbing Queue.");
        StatusMessage = $"Added {job.FileName} to Batch Dubbing Queue.";
    }

    [RelayCommand]
    public void RemoveFromBatch(DubbingJob? job)
    {
        var target = job ?? SelectedBatchJob;
        if (target != null)
        {
            BatchQueue.Remove(target);
            _snackbarManager.Notify($"Removed {target.FileName} from queue.");
        }
    }

    [RelayCommand]
    public void ClearCompletedBatch()
    {
        var toRemove = BatchQueue
            .Where(j =>
                j.Status == DubbingJobStatus.Completed
                || j.Status == DubbingJobStatus.Failed
                || j.Status == DubbingJobStatus.Canceled
            )
            .ToList();

        foreach (var item in toRemove)
        {
            BatchQueue.Remove(item);
        }

        _snackbarManager.Notify(
            $"Cleared {toRemove.Count} completed/finished item(s) from batch queue."
        );
    }

    [RelayCommand]
    public async Task StartBatchDubbingAsync()
    {
        if (IsBatchProcessing)
            return;

        var queuedJobs = BatchQueue
            .Where(j =>
                j.Status == DubbingJobStatus.Queued
                || j.Status == DubbingJobStatus.Failed
                || j.Status == DubbingJobStatus.Canceled
            )
            .ToList();

        if (queuedJobs.Count == 0)
        {
            _snackbarManager.Notify(
                "No queued videos in batch list. Click 'Add Videos' to queue files."
            );
            return;
        }

        IsBatchProcessing = true;
        _batchCts = new CancellationTokenSource();
        StatusMessage =
            $"Running batch dubbing on {queuedJobs.Count} video(s) (Concurrency: {BatchMaxConcurrency})...";
        _snackbarManager.Notify(
            $"Started batch dubbing: {queuedJobs.Count} video(s), concurrency: {BatchMaxConcurrency}"
        );

        try
        {
            await Parallel.ForEachAsync(
                queuedJobs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(BatchMaxConcurrency, 1, 3),
                    CancellationToken = _batchCts.Token,
                },
                async (job, ct) =>
                {
                    try
                    {
                        job.Status = DubbingJobStatus.Queued;
                        job.Progress = 0;
                        job.StatusMessage = "Starting pipeline...";

                        if (job.Characters.Count == 0 && Characters.Count > 0)
                        {
                            foreach (var c in Characters)
                            {
                                job.Characters.Add(c);
                            }
                        }

                        await _pipeline.ExecuteAsync(job, null, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        job.Status = DubbingJobStatus.Canceled;
                        job.StatusMessage = "Canceled";
                    }
                    catch (Exception ex)
                    {
                        job.Status = DubbingJobStatus.Failed;
                        job.StatusMessage = $"Failed: {ex.Message}";
                    }
                }
            );

            _snackbarManager.Notify("Batch dubbing completed!");
            StatusMessage = "All batch dubbing tasks finished.";
        }
        catch (OperationCanceledException)
        {
            _snackbarManager.Notify("Batch dubbing queue canceled.");
            StatusMessage = "Batch dubbing canceled.";
        }
        finally
        {
            IsBatchProcessing = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    [RelayCommand]
    public void CancelBatchDubbing()
    {
        if (_batchCts != null && !_batchCts.IsCancellationRequested)
        {
            _batchCts.Cancel();
            StatusMessage = "Canceling batch dubbing queue...";
            _snackbarManager.Notify("Canceling batch dubbing...");
        }
    }

    [RelayCommand]
    public void OpenBatchJobFolder(DubbingJob? job)
    {
        var target = job ?? SelectedBatchJob;
        if (target == null)
            return;

        var outPath = target.OutputFilePath;
        if (File.Exists(outPath))
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{outPath}\"",
                    UseShellExecute = true,
                }
            );
        }
        else
        {
            var dir = Path.GetDirectoryName(target.VideoFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
        }
    }

    [RelayCommand]
    public void LoadBatchJobToStudio(DubbingJob? job)
    {
        var target = job ?? SelectedBatchJob;
        if (target == null)
            return;

        VideoFilePath = target.VideoFilePath;
        OutputFilePath = target.OutputFilePath;
        SourceLanguage = target.SourceLanguage;
        SelectedVoice =
            AvailableVoices.FirstOrDefault(v => v.Id == target.SelectedVoice) ?? SelectedVoice;
        EnableVoiceCloning = target.EnableVoiceCloning;
        PitchShift = target.PitchShift;
        RvcConcurrency = target.RvcConcurrency > 0 ? target.RvcConcurrency : 4;
        BgmVolume = target.BgmVolume;
        VoiceVolume = target.VoiceVolume;
        EnableAiStemSeparation = target.EnableAiStemSeparation;
        EnableDynamicDucking = target.EnableDynamicDucking;

        if (target.Segments.Count > 0)
        {
            Segments.Clear();
            foreach (var seg in target.Segments)
            {
                Segments.Add(seg);
            }
        }

        SelectedStudioTab = 0; // Switch to Dialogue & Script tab
        _snackbarManager.Notify($"Loaded {target.FileName} into Studio editor.");
    }
}
