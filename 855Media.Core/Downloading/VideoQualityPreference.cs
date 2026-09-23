using System;

namespace _855Media.Core.Downloading;

public enum VideoQualityPreference
{
    // ReSharper disable InconsistentNaming
    Lowest,
    UpTo360p,
    UpTo480p,
    UpTo720p,
    UpTo1080p,
    UpTo1440p,
    UpTo2160p,
    Highest,
    // ReSharper restore InconsistentNaming
}

public static class VideoQualityPreferenceExtensions
{
    extension(VideoQualityPreference preference)
    {
        public string GetDisplayName() =>
            preference switch
            {
                VideoQualityPreference.Lowest => "Lowest quality",
                VideoQualityPreference.UpTo360p => "≤ 360p",
                VideoQualityPreference.UpTo480p => "≤ 480p",
                VideoQualityPreference.UpTo720p => "≤ 720p",
                VideoQualityPreference.UpTo1080p => "≤ 1080p",
                VideoQualityPreference.UpTo1440p => "≤ 1440p (2K)",
                VideoQualityPreference.UpTo2160p => "≤ 2160p (4K)",
                VideoQualityPreference.Highest => "Highest quality",
                _ => throw new ArgumentOutOfRangeException(nameof(preference)),
            };
    }
}
