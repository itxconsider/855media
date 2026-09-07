using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace _855Media.Core.Upscaling;

public class ColorGradingSettings : INotifyPropertyChanged
{
    private double _brightness;
    private double _contrast = 1.0;
    private double _saturation = 1.0;
    private bool _autoNormalize;
    private int _colorTemperature = 6500;
    private double _shadowRed;
    private double _shadowGreen;
    private double _shadowBlue;
    private double _highlightRed;
    private double _highlightGreen;
    private double _highlightBlue;
    private string _toneCurve = "None";
    private int _filmGrain;
    private double _vignette;
    private string? _lutPath;
    private double _lutOpacity = 1.0;

    /// <summary>
    /// Brightness adjustment range from -1.0 to 1.0. Default is 0.0.
    /// </summary>
    public double Brightness
    {
        get => _brightness;
        set => SetField(ref _brightness, Math.Clamp(value, -1.0, 1.0));
    }

    /// <summary>
    /// Contrast adjustment range from 0.5 to 2.0. Default is 1.0.
    /// </summary>
    public double Contrast
    {
        get => _contrast;
        set => SetField(ref _contrast, Math.Clamp(value, 0.5, 2.0));
    }

    /// <summary>
    /// Saturation adjustment range from 0.0 to 2.0. Default is 1.0.
    /// </summary>
    public double Saturation
    {
        get => _saturation;
        set => SetField(ref _saturation, Math.Clamp(value, 0.0, 2.0));
    }

    /// <summary>
    /// Toggles automatic dynamic range normalization (FFmpeg 'normalize' filter).
    /// </summary>
    public bool AutoNormalize
    {
        get => _autoNormalize;
        set => SetField(ref _autoNormalize, value);
    }

    /// <summary>
    /// Correlated Color Temperature (Kelvin): 3500K (warm candle) to 9500K (cool daylight). Default 6500K.
    /// </summary>
    public int ColorTemperature
    {
        get => _colorTemperature;
        set => SetField(ref _colorTemperature, Math.Clamp(value, 3500, 9500));
    }

    // Shadow Tint (Color Balance)
    public double ShadowRed
    {
        get => _shadowRed;
        set => SetField(ref _shadowRed, Math.Clamp(value, -1.0, 1.0));
    }

    public double ShadowGreen
    {
        get => _shadowGreen;
        set => SetField(ref _shadowGreen, Math.Clamp(value, -1.0, 1.0));
    }

    public double ShadowBlue
    {
        get => _shadowBlue;
        set => SetField(ref _shadowBlue, Math.Clamp(value, -1.0, 1.0));
    }

    // Highlight Tint (Color Balance)
    public double HighlightRed
    {
        get => _highlightRed;
        set => SetField(ref _highlightRed, Math.Clamp(value, -1.0, 1.0));
    }

    public double HighlightGreen
    {
        get => _highlightGreen;
        set => SetField(ref _highlightGreen, Math.Clamp(value, -1.0, 1.0));
    }

    public double HighlightBlue
    {
        get => _highlightBlue;
        set => SetField(ref _highlightBlue, Math.Clamp(value, -1.0, 1.0));
    }

    /// <summary>
    /// Matte tone curve preset ("None", "Deep S-Curve", "Faded Black", "Cross Process", "Vintage").
    /// </summary>
    public string ToneCurve
    {
        get => _toneCurve;
        set => SetField(ref _toneCurve, string.IsNullOrWhiteSpace(value) ? "None" : value);
    }

    /// <summary>
    /// Organic 35mm film grain intensity (0 to 20). Default 0.
    /// </summary>
    public int FilmGrain
    {
        get => _filmGrain;
        set => SetField(ref _filmGrain, Math.Clamp(value, 0, 20));
    }

    /// <summary>
    /// Lens vignette falloff (0.0 to 1.0). Default 0.0.
    /// </summary>
    public double Vignette
    {
        get => _vignette;
        set => SetField(ref _vignette, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>
    /// Absolute path to a custom 3D LUT (.cube file).
    /// </summary>
    public string? LutPath
    {
        get => _lutPath;
        set => SetField(ref _lutPath, value);
    }

    /// <summary>
    /// Opacity / Mix percentage of the 3D LUT (0.0 to 1.0). Default 1.0.
    /// </summary>
    public double LutOpacity
    {
        get => _lutOpacity;
        set => SetField(ref _lutOpacity, Math.Clamp(value, 0.0, 1.0));
    }

    public string? LutFileName =>
        !string.IsNullOrWhiteSpace(_lutPath) ? Path.GetFileName(_lutPath) : null;

    public bool HasActiveGrading =>
        _autoNormalize
        || (!string.IsNullOrWhiteSpace(_lutPath) && File.Exists(_lutPath))
        || Math.Abs(_brightness) > 0.001
        || Math.Abs(_contrast - 1.0) > 0.001
        || Math.Abs(_saturation - 1.0) > 0.001
        || _colorTemperature != 6500
        || Math.Abs(_shadowRed) > 0.001
        || Math.Abs(_shadowGreen) > 0.001
        || Math.Abs(_shadowBlue) > 0.001
        || Math.Abs(_highlightRed) > 0.001
        || Math.Abs(_highlightGreen) > 0.001
        || Math.Abs(_highlightBlue) > 0.001
        || (
            !string.IsNullOrWhiteSpace(_toneCurve)
            && !string.Equals(_toneCurve, "None", StringComparison.OrdinalIgnoreCase)
        )
        || _filmGrain > 0
        || _vignette > 0.01;

    /// <summary>
    /// Builds the FFmpeg video filter chain string using AdvancedColorGradeService.
    /// Returns null if no active grading is configured.
    /// </summary>
    public string? BuildFilterString() => AdvancedColorGradeService.BuildFilterString(this);

    public void Reset()
    {
        Brightness = 0.0;
        Contrast = 1.0;
        Saturation = 1.0;
        AutoNormalize = false;
        ColorTemperature = 6500;
        ShadowRed = 0.0;
        ShadowGreen = 0.0;
        ShadowBlue = 0.0;
        HighlightRed = 0.0;
        HighlightGreen = 0.0;
        HighlightBlue = 0.0;
        ToneCurve = "None";
        FilmGrain = 0;
        Vignette = 0.0;
        LutPath = null;
        LutOpacity = 1.0;
    }

    public ColorGradingSettings Clone()
    {
        var clone = new ColorGradingSettings();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(ColorGradingSettings other)
    {
        Brightness = other.Brightness;
        Contrast = other.Contrast;
        Saturation = other.Saturation;
        AutoNormalize = other.AutoNormalize;
        ColorTemperature = other.ColorTemperature;
        ShadowRed = other.ShadowRed;
        ShadowGreen = other.ShadowGreen;
        ShadowBlue = other.ShadowBlue;
        HighlightRed = other.HighlightRed;
        HighlightGreen = other.HighlightGreen;
        HighlightBlue = other.HighlightBlue;
        ToneCurve = other.ToneCurve;
        FilmGrain = other.FilmGrain;
        Vignette = other.Vignette;
        LutPath = other.LutPath;
        LutOpacity = other.LutOpacity;
    }

    public void ApplyPreset(ColorPreset preset)
    {
        Brightness = preset.Brightness;
        Contrast = preset.Contrast;
        Saturation = preset.Saturation;
        AutoNormalize = preset.AutoNormalize;
        ColorTemperature = preset.ColorTemperature;
        ShadowRed = preset.ShadowRed;
        ShadowGreen = preset.ShadowGreen;
        ShadowBlue = preset.ShadowBlue;
        HighlightRed = preset.HighlightRed;
        HighlightGreen = preset.HighlightGreen;
        HighlightBlue = preset.HighlightBlue;
        ToneCurve = preset.ToneCurve;
        FilmGrain = preset.FilmGrain;
        Vignette = preset.Vignette;
        if (!string.IsNullOrWhiteSpace(preset.LutPath))
        {
            LutPath = preset.LutPath;
        }
        LutOpacity = preset.LutOpacity;
    }

    public ColorPreset ToPreset(string name, string description = "")
    {
        return new ColorPreset
        {
            Name = name,
            Description = description,
            IsBuiltIn = false,
            Brightness = Brightness,
            Contrast = Contrast,
            Saturation = Saturation,
            AutoNormalize = AutoNormalize,
            ColorTemperature = ColorTemperature,
            ShadowRed = ShadowRed,
            ShadowGreen = ShadowGreen,
            ShadowBlue = ShadowBlue,
            HighlightRed = HighlightRed,
            HighlightGreen = HighlightGreen,
            HighlightBlue = HighlightBlue,
            ToneCurve = ToneCurve,
            FilmGrain = FilmGrain,
            Vignette = Vignette,
            LutPath = LutPath,
            LutOpacity = LutOpacity,
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(HasActiveGrading));
        if (propertyName == nameof(LutPath))
        {
            OnPropertyChanged(nameof(LutFileName));
        }
        return true;
    }
}
