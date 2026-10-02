using System.IO;
using _855Media.Core.Dubbing;

namespace _855Media.ViewModels.Components;

public class TimelineItemViewModel
{
    public int Index { get; set; }
    public SubtitleSegment? Segment { get; set; }
    public double LeftOffset { get; set; }
    public double Width { get; set; }
    public double TopOffset { get; set; }
    public double Height { get; set; }
    public string SpeakerName { get; set; } = string.Empty;
    public string SpeakerColor { get; set; } = "#1152b9ff";
    public string OriginalText { get; set; } = string.Empty;
    public string KhmerText { get; set; } = string.Empty;
    public string? AudioClipPath { get; set; }
    public string? ThumbnailPath { get; set; }

    public bool HasAudioClip =>
        !string.IsNullOrWhiteSpace(AudioClipPath) && File.Exists(AudioClipPath);

    public string FormattedStart =>
        Segment != null ? Segment.StartTime.ToString(@"mm\:ss\.ff") : string.Empty;

    public string FormattedDuration =>
        Segment != null ? $"{Segment.DurationSeconds:F1}s" : string.Empty;
}

public class TimelineRulerMark
{
    public double X { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsMajor { get; set; }
    public double Height => IsMajor ? 14 : 7;
}
