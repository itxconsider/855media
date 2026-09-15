using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public record RvcModelInfo(string Name, string PthPath, string? IndexPath)
{
    public override string ToString() => Name;
}

public class RvcInferenceService
{
    public static string DefaultModelsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "models", "voices");

    public static IEnumerable<string> GetProbeDirectories()
    {
        yield return DefaultModelsDirectory;
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "855Media",
            "models",
            "voices"
        );
        yield return appData;

        DirectoryInfo? cur = null;
        try
        {
            cur = new DirectoryInfo(AppContext.BaseDirectory);
        }
        catch { }
        for (int i = 0; i < 5 && cur?.Parent != null; i++)
        {
            cur = cur.Parent;
            yield return Path.Combine(cur.FullName, "models", "voices");
        }
    }

    public IReadOnlyList<RvcModelInfo> GetAvailableVoiceModels()
    {
        var models = new List<RvcModelInfo>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in GetProbeDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch { }
                continue;
            }

            try
            {
                var pthFiles = Directory.EnumerateFiles(dir, "*.pth", SearchOption.AllDirectories);
                foreach (var pth in pthFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(pth);
                    if (!seenNames.Add(name))
                        continue;

                    var parentDir = Path.GetDirectoryName(pth) ?? dir;
                    var index = Directory.EnumerateFiles(parentDir, "*.index").FirstOrDefault();

                    models.Add(new RvcModelInfo(name, pth, index));
                }
            }
            catch { }
        }

        return models;
    }

    private static string? FindRvcCliScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "rvc", "infer_cli.py"),
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "tools",
                "rvc",
                "infer_cli.py"
            ),
            @"d:\repos\855Media\tools\rvc\infer_cli.py",
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full))
                    return full;
            }
            catch { }
        }

        return null;
    }

    private static string? FindPythonExecutable()
    {
        var candidates = new[]
        {
            @"C:\Applio-3.6.4\env\python.exe",
            Path.Combine(AppContext.BaseDirectory, "python", "python.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }

    public async Task ConvertVoiceAsync(
        string inputAudioPath,
        string outputAudioPath,
        string pthModelPath,
        string? indexPath = null,
        int pitchShift = 0,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(inputAudioPath))
            throw new FileNotFoundException("Input audio file not found", inputAudioPath);

        if (!File.Exists(pthModelPath))
            throw new FileNotFoundException("RVC model file not found", pthModelPath);

        // 1. Try Python RVC engine (Applio / infer_cli.py)
        var pythonExe = FindPythonExecutable();
        var inferScript = FindRvcCliScript();

        if (pythonExe != null && inferScript != null)
        {
            log?.Invoke(
                $"[RVC] Running AI Voice Conversion with model {Path.GetFileName(pthModelPath)} (Pitch Shift: {pitchShift})..."
            );

            using var process = new Process();
            process.StartInfo.FileName = pythonExe;
            process.StartInfo.ArgumentList.Add(inferScript);
            process.StartInfo.ArgumentList.Add("--input");
            process.StartInfo.ArgumentList.Add(inputAudioPath);
            process.StartInfo.ArgumentList.Add("--output");
            process.StartInfo.ArgumentList.Add(outputAudioPath);
            process.StartInfo.ArgumentList.Add("--model");
            process.StartInfo.ArgumentList.Add(pthModelPath);
            process.StartInfo.ArgumentList.Add("--pitch");
            process.StartInfo.ArgumentList.Add(pitchShift.ToString());

            if (!string.IsNullOrWhiteSpace(indexPath) && File.Exists(indexPath))
            {
                process.StartInfo.ArgumentList.Add("--index");
                process.StartInfo.ArgumentList.Add(indexPath);
            }

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    log?.Invoke($"[RVC] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                var line = e.Data.Trim();
                // tqdm progress bars (e.g. Loading weights: 100%|...|) write to stderr by design, not an error
                if (
                    line.Contains("Loading weights:")
                    || line.Contains("%|")
                    || line.Contains("it/s")
                    || line.Contains("UserWarning")
                )
                {
                    log?.Invoke($"[RVC] {line}");
                }
                else
                {
                    log?.Invoke($"[RVC Error] {line}");
                }
            };

            process.Start();
            ChildProcessTracker.Track(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (
                process.ExitCode == 0
                && File.Exists(outputAudioPath)
                && new FileInfo(outputAudioPath).Length > 100
            )
            {
                log?.Invoke("[RVC] Voice cloning completed successfully!");
                return;
            }
        }

        // 2. Check for standalone rvc_cli.exe if available
        var rvcExe = Path.Combine(AppContext.BaseDirectory, "tools", "rvc", "rvc_cli.exe");
        if (File.Exists(rvcExe))
        {
            using var process = new Process();
            process.StartInfo.FileName = rvcExe;
            process.StartInfo.ArgumentList.Add("--input");
            process.StartInfo.ArgumentList.Add(inputAudioPath);
            process.StartInfo.ArgumentList.Add("--output");
            process.StartInfo.ArgumentList.Add(outputAudioPath);
            process.StartInfo.ArgumentList.Add("--model");
            process.StartInfo.ArgumentList.Add(pthModelPath);
            process.StartInfo.ArgumentList.Add("--pitch");
            process.StartInfo.ArgumentList.Add(pitchShift.ToString());

            if (!string.IsNullOrWhiteSpace(indexPath) && File.Exists(indexPath))
            {
                process.StartInfo.ArgumentList.Add("--index");
                process.StartInfo.ArgumentList.Add(indexPath);
            }

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            log?.Invoke(
                $"[RVC] Executing standalone RVC with model {Path.GetFileName(pthModelPath)} (Pitch: {pitchShift})..."
            );
            process.Start();
            ChildProcessTracker.Track(process);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputAudioPath))
                return;
        }

        // Fallback: If no RVC runtime is found, copy audio and notify
        log?.Invoke(
            "[Notice] Standalone RVC runtime not detected. Using synthesized Khmer vocal track directly."
        );
        File.Copy(inputAudioPath, outputAudioPath, overwrite: true);
    }
}
