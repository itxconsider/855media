using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public class VoxCpmService
{
    public static string? FindPythonExecutable()
    {
        var candidates = new List<string>
        {
            @"C:\Applio-3.6.4\env\python.exe",
            Path.Combine(AppContext.BaseDirectory, "python", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "env", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "env", "Scripts", "python.exe"),
        };

        DirectoryInfo? cur = null;
        try
        {
            cur = new DirectoryInfo(AppContext.BaseDirectory);
        }
        catch { }
        for (int i = 0; i < 6 && cur?.Parent != null; i++)
        {
            cur = cur.Parent;
            candidates.Add(Path.Combine(cur.FullName, "env", "Scripts", "python.exe"));
            candidates.Add(Path.Combine(cur.FullName, "env", "python.exe"));
            candidates.Add(Path.Combine(cur.FullName, "python", "python.exe"));
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;
                var py = Path.Combine(part.Trim(), "python.exe");
                if (File.Exists(py))
                    return py;
            }
        }

        return null;
    }

    public static string? FindRunnerScript()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "voxcpm_tts.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "voxcpm_tts.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "tools", "voxcpm_tts.py"),
        };

        DirectoryInfo? cur = null;
        try
        {
            cur = new DirectoryInfo(AppContext.BaseDirectory);
        }
        catch { }
        for (int i = 0; i < 6 && cur?.Parent != null; i++)
        {
            cur = cur.Parent;
            candidates.Add(Path.Combine(cur.FullName, "tools", "voxcpm_tts.py"));
        }

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

    public async Task<bool> SynthesizeSpeechAsync(
        string text,
        string outputPath,
        string? referenceAudioPath = null,
        int timesteps = 10,
        float cfg = 2.0f,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var pythonExe = FindPythonExecutable();
        if (string.IsNullOrWhiteSpace(pythonExe) || !File.Exists(pythonExe))
            throw new FileNotFoundException(
                "Python executable not found for VoxCPM runner.",
                pythonExe ?? "python.exe"
            );

        var runnerScript = FindRunnerScript();
        if (string.IsNullOrWhiteSpace(runnerScript) || !File.Exists(runnerScript))
            throw new FileNotFoundException(
                "voxcpm_tts.py script not found.",
                runnerScript ?? "voxcpm_tts.py"
            );

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        using var process = new Process();
        process.StartInfo.FileName = pythonExe;
        process.StartInfo.ArgumentList.Add(runnerScript);
        process.StartInfo.ArgumentList.Add("--text");
        process.StartInfo.ArgumentList.Add(text);
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.ArgumentList.Add("--timesteps");
        process.StartInfo.ArgumentList.Add(timesteps.ToString());
        process.StartInfo.ArgumentList.Add("--cfg");
        process.StartInfo.ArgumentList.Add(cfg.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("--device");
        process.StartInfo.ArgumentList.Add("cuda");

        if (!string.IsNullOrWhiteSpace(referenceAudioPath) && File.Exists(referenceAudioPath))
        {
            process.StartInfo.ArgumentList.Add("--ref-audio");
            process.StartInfo.ArgumentList.Add(referenceAudioPath);
        }

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                log?.Invoke($"[VoxCPM] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                stderr.AppendLine(e.Data);
                log?.Invoke($"[VoxCPM] {e.Data}");
            }
        };

        process.Start();
        ChildProcessTracker.Track(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (
            process.ExitCode != 0
            || !File.Exists(outputPath)
            || new FileInfo(outputPath).Length < 100
        )
        {
            throw new InvalidOperationException(
                $"VoxCPM failed with exit code {process.ExitCode}: {stderr}"
            );
        }

        return true;
    }
}
