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
