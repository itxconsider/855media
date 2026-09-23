using System;
using System.Globalization;
using Avalonia.Data.Converters;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Converters;

public class VideoQualityToStringConverter : IValueConverter
{
    public static VideoQualityToStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VideoQuality vq)
        {
            var label = vq.Label;
            if (vq.MaxHeight >= 3800)
                return $"{label} (8K)";
            if (vq.MaxHeight >= 2160)
                return $"{label} (4K)";
            if (vq.MaxHeight >= 1440)
                return $"{label} (2K)";
            if (vq.MaxHeight >= 1080)
                return $"{label} (Full HD)";
            if (vq.MaxHeight >= 720)
                return $"{label} (HD)";

            return label;
        }

        return value?.ToString();
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
