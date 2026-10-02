using System.Collections.Generic;

namespace _855Media.Core.Dubbing;

public class DubbingReviewSession
{
    public string VideoFilePath { get; set; } = string.Empty;
    public double TotalDurationSeconds { get; set; }
    public double OriginalAudioVolume { get; set; } = 0.6;
    public double DubbedAudioVolume { get; set; } = 1.0;
    public string AudioMode { get; set; } = "Dual"; // "Dual", "Original", "Dubbed"
    public bool EnableDucking { get; set; } = true;
    public double DuckingVolumeRatio { get; set; } = 0.25;
    public List<ReviewSegmentData> Segments { get; set; } = [];
}

public class ReviewSegmentData
{
    public int Index { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string SpeakerName { get; set; } = string.Empty;
    public string SpeakerColor { get; set; } = "#1152b9ff";
    public string OriginalText { get; set; } = string.Empty;
    public string KhmerText { get; set; } = string.Empty;
    public string? AudioClipPath { get; set; }
    public string? ThumbnailPath { get; set; }
}
