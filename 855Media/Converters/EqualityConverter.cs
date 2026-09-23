using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace _855Media.Converters;

public class EqualityConverter(bool isInverted) : IValueConverter
{
    public static EqualityConverter IsEqual { get; } = new(false);
    public static EqualityConverter IsNotEqual { get; } = new(true);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null && parameter is null)
            return !isInverted;
        if (value is null || parameter is null)
            return isInverted;

        if (EqualityComparer<object>.Default.Equals(value, parameter))
            return !isInverted;

        if (
            string.Equals(
                value.ToString(),
                parameter.ToString(),
                StringComparison.OrdinalIgnoreCase
            )
        )
            return !isInverted;

        return isInverted;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
