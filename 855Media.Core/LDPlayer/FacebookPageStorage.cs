using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class FacebookPageStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string GetDefaultFilePath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media"
        );
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "facebook_pages.json");
    }

    public static async Task SavePagesAsync(
        IEnumerable<FacebookPageTarget> pages,
        string? customFilePath = null
    )
    {
        var filePath = customFilePath ?? GetDefaultFilePath();
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(pages, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<List<FacebookPageTarget>> LoadPagesAsync(string? customFilePath = null)
    {
        var filePath = customFilePath ?? GetDefaultFilePath();
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var result = JsonSerializer.Deserialize<List<FacebookPageTarget>>(json, JsonOptions);
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static List<FacebookPageTarget> GetDefaultPages()
    {
        return [];
    }
}
