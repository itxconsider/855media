using System;
using System.Globalization;
using System.Linq;
using _855Media.Core.Resolving;
using Avalonia.Data.Converters;

namespace _855Media.Converters;

public class VideoToLowestQualityThumbnailUrlStringConverter : IValueConverter
{
    public static VideoToLowestQualityThumbnailUrlStringConverter Instance { get; } = new();

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is VideoInfo video ? video.ThumbnailUrls.LastOrDefault() : null;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
