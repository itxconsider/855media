using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace _855Media.Core.Upscaling;

public static class ColorPresetManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly object SyncLock = new();
    private static readonly List<ColorPreset> BuiltInPresets =
    [
        new ColorPreset
        {
            Name = "Teal & Orange (Hollywood)",
            Description =
                "Iconic blockbuster look with cool cyan/teal shadows and warm golden amber highlights.",
            IsBuiltIn = true,
            ColorTemperature = 6000,
            Contrast = 1.15,
            Saturation = 1.12,
            Brightness = 0.0,
            ShadowRed = -0.10,
            ShadowGreen = 0.04,
            ShadowBlue = 0.16,
            HighlightRed = 0.15,
            HighlightGreen = 0.05,
            HighlightBlue = -0.12,
            ToneCurve = "Deep S-Curve",
            Vignette = 0.18,
            FilmGrain = 0,
        },
        new ColorPreset
        {
            Name = "Vintage 35mm Film (Kodak Look)",
            Description =
                "Authentic analog film stock emulation with organic grain, lifted matte blacks, and warm highlights.",
            IsBuiltIn = true,
            ColorTemperature = 5800,
            Contrast = 1.05,
            Saturation = 1.05,
            Brightness = 0.0,
            ShadowRed = 0.05,
            ShadowGreen = 0.02,
            ShadowBlue = -0.04,
            HighlightRed = 0.08,
            HighlightGreen = 0.06,
            HighlightBlue = -0.02,
            ToneCurve = "Faded Black",
            FilmGrain = 7,
            Vignette = 0.22,
        },
        new ColorPreset
        {
            Name = "Noir B&W High-Contrast",
            Description =
                "Dramatic monochrome cinema look with crushed blacks, crisp grain, and deep vignette.",
            IsBuiltIn = true,
            ColorTemperature = 6500,
            Saturation = 0.0,
            Contrast = 1.45,
            Brightness = -0.04,
            ToneCurve = "Deep S-Curve",
            FilmGrain = 9,
            Vignette = 0.35,
        },
        new ColorPreset
        {
            Name = "Clean Modern / Vivid",
            Description =
                "Punchy, balanced commercial aesthetic with dynamic range normalization and vibrant color pop.",
            IsBuiltIn = true,
            AutoNormalize = true,
            ColorTemperature = 6500,
            Contrast = 1.08,
            Saturation = 1.20,
            Brightness = 0.02,
            ToneCurve = "None",
            FilmGrain = 0,
            Vignette = 0.0,
        },
        new ColorPreset
        {
            Name = "Kodak Portra 400 (Soft Warmth)",
            Description =
                "Legendary portrait film look with soft pastel highlight roll-off and warm, flattering skin tones.",
            IsBuiltIn = true,
            ColorTemperature = 6100,
            Contrast = 1.04,
            Saturation = 1.05,
            Gamma = 1.02,
            Vibrance = 0.08,
            Brightness = 0.01,
            ShadowRed = 0.04,
            ShadowGreen = 0.02,
            ShadowBlue = -0.02,
            MidtoneRed = 0.05,
            MidtoneGreen = 0.03,
            MidtoneBlue = -0.02,
            HighlightRed = 0.06,
            HighlightGreen = 0.04,
            HighlightBlue = -0.03,
            ToneCurve = "Kodak Portra 400",
            FilmGrain = 5,
            Vignette = 0.12,
        },
        new ColorPreset
        {
            Name = "Fuji Velvia Landscape (Vivid)",
            Description =
                "Vibrant landscape slide film stock with rich emerald greens, deep cyan skies, and bold contrast.",
            IsBuiltIn = true,
            ColorTemperature = 6600,
            Contrast = 1.18,
            Saturation = 1.22,
            Gamma = 0.98,
            Vibrance = 0.15,
            Brightness = 0.0,
            ShadowRed = -0.04,
            ShadowGreen = 0.02,
            ShadowBlue = 0.06,
            MidtoneRed = 0.02,
            MidtoneGreen = 0.06,
            MidtoneBlue = -0.02,
            HighlightRed = 0.05,
            HighlightGreen = 0.08,
            HighlightBlue = 0.02,
            ToneCurve = "Fuji Velvia",
            FilmGrain = 0,
            Vignette = 0.15,
        },
        new ColorPreset
        {
            Name = "Bleach Bypass (Cinema)",
            Description =
                "Gritty silver-retention cinema look with stark contrast, desaturated hues, and sharp edge definition.",
            IsBuiltIn = true,
            ColorTemperature = 6300,
            Contrast = 1.35,
            Saturation = 0.65,
            Gamma = 0.95,
            Vibrance = -0.15,
            Brightness = -0.02,
            ShadowRed = 0.02,
            ShadowGreen = 0.02,
            ShadowBlue = 0.04,
            HighlightRed = 0.04,
            HighlightGreen = 0.04,
            HighlightBlue = 0.02,
            ToneCurve = "Bleach Bypass",
            FilmGrain = 8,
            Vignette = 0.28,
        },
        new ColorPreset
        {
            Name = "Cyberpunk / Neon Glow",
            Description =
                "Futuristic neo-noir style with deep teal/cyan shadows, electric magenta highlights, and punchy saturation.",
            IsBuiltIn = true,
            ColorTemperature = 7200,
            Contrast = 1.25,
            Saturation = 1.20,
            Gamma = 1.05,
            Vibrance = 0.20,
            Brightness = 0.0,
            ShadowRed = -0.15,
            ShadowGreen = 0.04,
            ShadowBlue = 0.18,
            MidtoneRed = 0.08,
            MidtoneGreen = -0.05,
            MidtoneBlue = 0.12,
            HighlightRed = 0.18,
            HighlightGreen = -0.04,
            HighlightBlue = 0.14,
            ToneCurve = "Cyberpunk / Neon",
            FilmGrain = 4,
            Vignette = 0.25,
        },
        new ColorPreset
        {
            Name = "Golden Hour Sunset",
            Description =
                "Dreamy golden hour sunlight with glowing amber warmth, soft lifted shadows, and gentle contrast.",
            IsBuiltIn = true,
            ColorTemperature = 5400,
            Contrast = 1.08,
            Saturation = 1.10,
            Gamma = 1.04,
            Vibrance = 0.12,
            Brightness = 0.02,
            ShadowRed = 0.08,
            ShadowGreen = 0.04,
            ShadowBlue = -0.06,
            MidtoneRed = 0.10,
            MidtoneGreen = 0.05,
            MidtoneBlue = -0.08,
            HighlightRed = 0.14,
            HighlightGreen = 0.08,
            HighlightBlue = -0.10,
            ToneCurve = "Golden Hour Warmth",
            FilmGrain = 3,
            Vignette = 0.18,
        },
        new ColorPreset
        {
            Name = "Neutral / Custom",
            Description = "Default flat neutral baseline without color styling or effects.",
            IsBuiltIn = true,
            ColorTemperature = 6500,
            Contrast = 1.0,
            Saturation = 1.0,
            Brightness = 0.0,
            ToneCurve = "None",
            FilmGrain = 0,
            Vignette = 0.0,
        },
    ];

    private static List<ColorPreset>? _userPresets;

    public static string GetPresetsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "855Media");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "presets.json");
    }

    public static IReadOnlyList<ColorPreset> GetAllPresets()
    {
        lock (SyncLock)
        {
            EnsureLoaded();
            var list = new List<ColorPreset>(BuiltInPresets);
            if (_userPresets != null)
            {
                list.AddRange(_userPresets);
            }
            return list;
        }
    }

    public static IReadOnlyList<string> GetPresetNames() =>
        GetAllPresets().Select(p => p.Name).ToList();

    public static ColorPreset? GetPreset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return GetAllPresets()
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task SaveCustomPresetAsync(ColorPreset preset)
    {
        preset.IsBuiltIn = false;

        lock (SyncLock)
        {
            EnsureLoaded();
            _userPresets ??= [];

            var existingIndex = _userPresets.FindIndex(p =>
                string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (existingIndex >= 0)
            {
                _userPresets[existingIndex] = preset;
            }
            else
            {
                _userPresets.Add(preset);
            }
        }

        await PersistUserPresetsAsync();
    }

    public static async Task<bool> DeleteCustomPresetAsync(string presetName)
    {
        bool removed = false;
        lock (SyncLock)
        {
            EnsureLoaded();
            if (_userPresets != null)
            {
                var item = _userPresets.FirstOrDefault(p =>
                    string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase)
                );
                if (item != null)
                {
                    removed = _userPresets.Remove(item);
                }
            }
        }

        if (removed)
        {
            await PersistUserPresetsAsync();
        }
        return removed;
    }

    public static async Task ExportPresetsAsync(string targetFilePath)
    {
        var allPresets = GetAllPresets();
        using var stream = File.Create(targetFilePath);
        await JsonSerializer.SerializeAsync(stream, allPresets, JsonOptions);
    }

    public static async Task<int> ImportPresetsAsync(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            return 0;

        using var stream = File.OpenRead(sourceFilePath);
        var imported = await JsonSerializer.DeserializeAsync<List<ColorPreset>>(
            stream,
            JsonOptions
        );
        if (imported == null || imported.Count == 0)
            return 0;

        int addedCount = 0;
        lock (SyncLock)
        {
            EnsureLoaded();
            _userPresets ??= [];

            foreach (var preset in imported)
            {
                // Skip if built-in
                if (
                    BuiltInPresets.Any(b =>
                        string.Equals(b.Name, preset.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                    continue;

                preset.IsBuiltIn = false;
                var idx = _userPresets.FindIndex(p =>
                    string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)
                );
                if (idx >= 0)
                {
                    _userPresets[idx] = preset;
                }
                else
                {
                    _userPresets.Add(preset);
                    addedCount++;
                }
            }
        }

        await PersistUserPresetsAsync();
        return addedCount;
    }

    private static void EnsureLoaded()
    {
        if (_userPresets != null)
            return;

        _userPresets = [];
        var path = GetPresetsFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<ColorPreset>>(json, JsonOptions);
                if (loaded != null)
                {
                    _userPresets = loaded.Where(p => !p.IsBuiltIn).ToList();
                }
            }
            catch
            {
                _userPresets = [];
            }
        }
    }

    private static async Task PersistUserPresetsAsync()
    {
        string path = GetPresetsFilePath();
        List<ColorPreset> copy;
        lock (SyncLock)
        {
            copy = _userPresets?.ToList() ?? [];
        }

        using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, copy, JsonOptions);
    }
}
