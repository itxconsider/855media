using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Upscaling;

/// <summary>
/// Service responsible for persisting and reloading upscale queue state to/from disk across application sessions.
/// </summary>
public static class QueuePersistenceService
{
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string? CustomQueueFilePath { get; set; }

    public static string QueueFilePath =>
        CustomQueueFilePath
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media",
            "upscale_queue.json"
        );

    public static async Task SaveQueueAsync(IEnumerable<UpscaleJob> jobs)
    {
        await SyncLock.WaitAsync();
        try
        {
            var filePath = QueueFilePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var snapshot = new List<UpscaleJob>(jobs);
            var tempFile = filePath + ".tmp";

            using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
            }

            File.Move(tempFile, filePath, overwrite: true);
        }
        catch
        {
            // Non-fatal persistence error
        }
        finally
        {
            SyncLock.Release();
        }
    }

    public static async Task<List<UpscaleJob>> LoadQueueAsync()
    {
        await SyncLock.WaitAsync();
        try
        {
            var filePath = QueueFilePath;
            if (!File.Exists(filePath))
                return [];

            using var stream = File.OpenRead(filePath);
            var jobs = await JsonSerializer.DeserializeAsync<List<UpscaleJob>>(stream, JsonOptions);
            if (jobs == null)
                return [];

            var validJobs = new List<UpscaleJob>();
            foreach (var job in jobs)
            {
                // Discard invalid or non-existent files (e.g. test dummy files or deleted videos)
                if (string.IsNullOrWhiteSpace(job.FilePath) || !File.Exists(job.FilePath))
                {
                    continue;
                }

                job.Cts = new CancellationTokenSource();
                job.ActiveProcess = null;

                // If job was actively processing when app exited, restore as Paused
                if (job.Status == UpscaleJobStatus.Processing)
                {
                    job.Status = UpscaleJobStatus.Paused;
                }

                validJobs.Add(job);
            }

            return validJobs;
        }
        catch
        {
            return [];
        }
        finally
        {
            SyncLock.Release();
        }
    }
}
