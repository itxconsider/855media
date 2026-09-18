using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace _855Media.Core.Dubbing;

public class MovieCharacterData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Hero (Male)";
    public string Gender { get; set; } = "Male";
    public string BaseVoice { get; set; } = "km-KH-PisethNeural";
    public string SpeechRate { get; set; } = "+15%";
    public bool EnableRvc { get; set; }
    public string? RvcModelPath { get; set; }
    public string? RvcIndexPath { get; set; }
    public int PitchShift { get; set; }
    public string EmotionPreset { get; set; } = "Normal";
    public string ToneArchetype { get; set; } = "Hero";
    public double ToneWarmth { get; set; } = 0.25;
    public double ToneClarity { get; set; } = 0.35;
    public string ColorTag { get; set; } = "#3B82F6";
}

public class SubtitleSegmentData
{
    public int Index { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string KhmerText { get; set; } = string.Empty;
    public Guid? CharacterId { get; set; }
    public string SpeakerName { get; set; } = "Hero (Male)";
    public string SpeakerColor { get; set; } = "#3B82F6";
    public string Emotion { get; set; } = "Normal";
    public string DetectedGender { get; set; } = "Unknown";
    public string? AudioClipPath { get; set; }
    public string? ThumbnailPath { get; set; }
}

public class DubbingProject
{
    public const string ProjectExtension = ".855dub";

    public string ProjectName { get; set; } = "Untitled Dubbing Project";
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    // Media & Pipeline Settings
    public string VideoFilePath { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = "Auto";
    public string SelectedVoice { get; set; } = "km-KH-PisethNeural";
    public bool EnableVoiceCloning { get; set; }
    public string? RvcModelPath { get; set; }
    public string? RvcIndexPath { get; set; }
    public int PitchShift { get; set; }
    public int RvcConcurrency { get; set; } = 4;
    public double BgmVolume { get; set; } = 0.25;
    public double VoiceVolume { get; set; } = 1.0;
    public bool EnableDynamicDucking { get; set; } = true;
    public bool EnableAiStemSeparation { get; set; } = true;
    public bool EnableLoudnessNormalization { get; set; } = true;
    public bool EnableSmartTimeStretch { get; set; } = true;

    // Cast & Dialogue Timelines
    public List<MovieCharacterData> Characters { get; set; } = [];
    public List<SubtitleSegmentData> Segments { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task SaveAsync(string filePath)
    {
        LastModifiedAt = DateTime.UtcNow;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions);
    }

    public static async Task<DubbingProject> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file not found", filePath);

        using var stream = File.OpenRead(filePath);
        var project = await JsonSerializer.DeserializeAsync<DubbingProject>(stream, JsonOptions);
        return project ?? throw new InvalidDataException("Failed to deserialize dubbing project.");
    }
}
