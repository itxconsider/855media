using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowPageStorage
{
    private static readonly SemaphoreSlim StorageGate = new(1, 1);
    private static readonly HashSet<string> SuccessfullyLoadedPaths = new(
        StringComparer.OrdinalIgnoreCase
    );
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
        var filePath = Path.GetFullPath(customFilePath ?? GetDefaultFilePath());
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        // Copy each model before yielding so ongoing UI updates cannot alter
        // the snapshot while it is being written.
        var snapshot = pages.Select(page => page.CreateSnapshot()).ToList();
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        await StorageGate.WaitAsync();
        try
        {
            if (snapshot.Count == 0 && !SuccessfullyLoadedPaths.Contains(filePath))
                throw new InvalidDataException(
                    "Load the existing Facebook configuration successfully before saving an empty table."
                );

            var primaryReadable = false;
            if (File.Exists(filePath) || File.Exists(filePath + ".bak"))
            {
                try
                {
                    await ReadPagesAsync(filePath);
                    primaryReadable = true;
                }
                catch (Exception primaryError) when (primaryError is IOException or JsonException)
                {
                    try
                    {
                        await ReadPagesAsync(filePath + ".bak");
                    }
                    catch (Exception backupError) when (backupError is IOException or JsonException)
                    {
                        throw new InvalidDataException(
                            $"Saved Facebook data and its backup are unreadable; refusing to overwrite {filePath}.",
                            new AggregateException(primaryError, backupError)
                        );
                    }
                }
            }
            await File.WriteAllTextAsync(tempPath, json);
            using (
                var stream = new FileStream(
                    tempPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.None
                )
            )
                stream.Flush(flushToDisk: true);

            if (File.Exists(filePath))
            {
                // Replace atomically and retain the previous complete file.
                // A crash cannot leave the main file partially truncated.
                // Preserve a good backup when the primary file was damaged.
                string? backupPath = primaryReadable ? filePath + ".bak" : null;
                File.Replace(tempPath, filePath, backupPath);
            }
            else
            {
                File.Move(tempPath, filePath);
                File.Copy(filePath, filePath + ".bak", overwrite: true);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            finally
            {
                StorageGate.Release();
            }
        }
    }

    public static async Task<List<FacebookPageTarget>> LoadPagesAsync(string? customFilePath = null)
    {
        var filePath = Path.GetFullPath(customFilePath ?? GetDefaultFilePath());
        await StorageGate.WaitAsync();
        try
        {
            if (!File.Exists(filePath) && !File.Exists(filePath + ".bak"))
            {
                SuccessfullyLoadedPaths.Add(filePath);
                return [];
            }

            try
            {
                var loaded = await ReadPagesAsync(filePath);
                SuccessfullyLoadedPaths.Add(filePath);
                return loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                if (!File.Exists(filePath + ".bak"))
                {
                    SuccessfullyLoadedPaths.Remove(filePath);
                    throw new InvalidDataException(
                        $"Cannot load saved Facebook data from {filePath}.",
                        ex
                    );
                }

                // Recover from a previous interrupted or damaged save. The
                // original file remains available for diagnosis.
                try
                {
                    var recovered = await ReadPagesAsync(filePath + ".bak");
                    SuccessfullyLoadedPaths.Add(filePath);
                    await PFlowFetchTrace.WriteAsync(
                        -1,
                        "storage.recovered",
                        $"Recovered {recovered.Count} Facebook rows from {filePath}.bak; primary file error: {ex.Message}"
                    );
                    return recovered;
                }
                catch (Exception backupError) when (backupError is IOException or JsonException)
                {
                    SuccessfullyLoadedPaths.Remove(filePath);
                    throw new InvalidDataException(
                        $"Cannot load saved Facebook data or its backup from {filePath}.",
                        new AggregateException(ex, backupError)
                    );
                }
            }
        }
        finally
        {
            StorageGate.Release();
        }
    }

    private static async Task<List<FacebookPageTarget>> ReadPagesAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var pages =
            JsonSerializer.Deserialize<List<FacebookPageTarget>>(json, JsonOptions)
            ?? throw new JsonException("Saved Facebook data must contain an array of rows.");
        if (pages.Any(page => page == null))
            throw new JsonException("Saved Facebook data contains an empty row.");
        return pages;
    }

    public static List<FacebookPageTarget> GetDefaultPages()
    {
        return [];
    }
}
