using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public sealed class MetaDubbingService
{
    // Batch jobs share the GPU; each worker loads a model once for the complete request.
    private static readonly SemaphoreSlim WorkerGate = new(1, 1);

    internal static string ToWorkerPath(string path, bool useWsl)
    {
        if (!useWsl)
            return path;
        if (
            path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/')
        )
            return $"/mnt/{char.ToLowerInvariant(path[0])}/{path[3..].Replace('\\', '/')}";
        if (path.StartsWith('/'))
            return path;
        throw new NotSupportedException(
            "The Meta WSL worker requires files on a local Windows drive."
        );
    }

    private static string FindWorker()
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (
                DirectoryInfo? directory = new(root);
                directory != null;
                directory = directory.Parent
            )
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "tools",
                    "meta_dubbing",
                    "worker.py"
                );
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        throw new FileNotFoundException(
            "Meta worker is missing. See tools/meta_dubbing/README.md for setup."
        );
    }

    private static async Task<JsonDocument> RunAsync(
        MetaDubbingOptions options,
        string operation,
        Dictionary<string, object?> arguments,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        var python = string.IsNullOrWhiteSpace(options.PythonPath)
            ? Environment.GetEnvironmentVariable("MEDIA855_META_PYTHON")
            : options.PythonPath;
        if (string.IsNullOrWhiteSpace(python))
            throw new InvalidOperationException(
                "Set the Meta Python path in Dubbing settings. See tools/meta_dubbing/README.md."
            );
        python = python.Trim();
        bool useWsl = OperatingSystem.IsWindows() && python.StartsWith('/');
        var worker = FindWorker();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "855Media_Meta",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var requestPath = Path.Combine(directory, "request.json");
        var responsePath = Path.Combine(directory, "response.json");
        bool acquired = false;
        try
        {
            arguments["operation"] = operation;
            arguments["response"] = ToWorkerPath(responsePath, useWsl);
            foreach (var key in new[] { "input", "vocals", "background" })
                if (arguments.TryGetValue(key, out var value) && value is string path)
                    arguments[key] = ToWorkerPath(Path.GetFullPath(path), useWsl);

            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(arguments),
                cancellationToken
            );
            log?.Invoke($"Waiting for Meta {operation} worker...");
            await WorkerGate.WaitAsync(cancellationToken);
            acquired = true;
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = useWsl ? "wsl.exe" : python,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (useWsl)
            {
                process.StartInfo.ArgumentList.Add("--exec");
                process.StartInfo.ArgumentList.Add(python);
            }
            process.StartInfo.ArgumentList.Add(ToWorkerPath(worker, useWsl));
            process.StartInfo.ArgumentList.Add("--request");
            process.StartInfo.ArgumentList.Add(ToWorkerPath(requestPath, useWsl));
            process.StartInfo.Environment["PYTHONUNBUFFERED"] = "1";
            process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            var errors = new Queue<string>();
            var logLock = new object();
            void Receive(string? line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                lock (logLock)
                {
                    errors.Enqueue(line);
                    if (errors.Count > 20)
                        errors.Dequeue();
                }
                log?.Invoke($"[Meta] {line}");
            }
            process.OutputDataReceived += (_, e) => Receive(e.Data);
            process.ErrorDataReceived += (_, e) => Receive(e.Data);
            cancellationToken.ThrowIfCancellationRequested();
            process.Start();
            ChildProcessTracker.Track(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitWithCancellationAsync(cancellationToken);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            if (process.ExitCode != 0 || !File.Exists(responsePath))
            {
                string error;
                lock (logLock)
                    error = string.Join(Environment.NewLine, errors);
                throw new InvalidOperationException(
                    $"Meta {operation} failed (exit {process.ExitCode}). {error}"
                );
            }
            return JsonDocument.Parse(await File.ReadAllTextAsync(responsePath, cancellationToken));
        }
        finally
        {
            if (acquired)
                WorkerGate.Release();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
        }
    }

    public async Task<string> CheckAsync(
        MetaDubbingOptions options,
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var result = await RunAsync(options, "check", new(), null, timeout.Token);
        return result.RootElement.GetProperty("summary").GetString() ?? "No diagnostic result.";
    }

    public async Task SeparateAsync(
        MetaDubbingOptions options,
        string input,
        string vocals,
        string background,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        using var result = await RunAsync(
            options,
            "separate",
            new()
            {
                ["input"] = input,
                ["vocals"] = vocals,
                ["background"] = background,
            },
            log,
            cancellationToken
        );
        foreach (var path in new[] { vocals, background })
            if (!File.Exists(path) || new FileInfo(path).Length <= 44)
                throw new InvalidDataException("SAM Audio did not produce both audio stems.");
    }

    public async Task<List<SubtitleSegment>> TranscribeAsync(
        MetaDubbingOptions options,
        string input,
        string language,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        using var result = await RunAsync(
            options,
            "transcribe",
            new() { ["input"] = input, ["language"] = language },
            log,
            cancellationToken
        );
        return ParseSegments(result.RootElement);
    }

    internal static List<SubtitleSegment> ParseSegments(JsonElement response)
    {
        var segments = new List<SubtitleSegment>();
        foreach (var item in response.GetProperty("segments").EnumerateArray())
        {
            double start = item.GetProperty("start").GetDouble();
            double end = item.GetProperty("end").GetDouble();
            var text = item.GetProperty("text").GetString();
            if (
                !double.IsFinite(start)
                || !double.IsFinite(end)
                || start < 0
                || end <= start
                || string.IsNullOrWhiteSpace(text)
            )
                continue;
            segments.Add(
                new SubtitleSegment
                {
                    Index = segments.Count + 1,
                    StartTime = TimeSpan.FromSeconds(start),
                    EndTime = TimeSpan.FromSeconds(end),
                    OriginalText = DialogueSenseEngine.ImproveOriginalDialogue(text),
                }
            );
        }
        segments = segments.OrderBy(s => s.StartTime).ToList();
        for (int index = 0; index < segments.Count; index++)
            segments[index].Index = index + 1;
        return segments;
    }
}
