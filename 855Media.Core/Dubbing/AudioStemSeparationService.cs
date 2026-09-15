using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public class AudioStemSeparationService
{
    public async Task ExtractFullAudioAsync(
        string ffmpegPath,
        string inputVideoPath,
        string outputAudioPath,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(inputVideoPath))
            throw new FileNotFoundException("Input video file not found", inputVideoPath);

        var dir = Path.GetDirectoryName(outputAudioPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(inputVideoPath);
        process.StartInfo.ArgumentList.Add("-vn");
        process.StartInfo.ArgumentList.Add("-acodec");
        process.StartInfo.ArgumentList.Add("pcm_s16le");
        process.StartInfo.ArgumentList.Add("-ar");
        process.StartInfo.ArgumentList.Add("44100");
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add("2");
        process.StartInfo.ArgumentList.Add(outputAudioPath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        ChildProcessTracker.Track(process);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputAudioPath))
            throw new InvalidOperationException(
                $"FFmpeg failed to extract audio from {inputVideoPath} (exit code {process.ExitCode})"
            );
    }

    public async Task SeparateStemsAsync(
        string ffmpegPath,
        string inputAudioPath,
        string vocalOutputPath,
        string bgmOutputPath,
        bool useAiDemucs = true,
        Action<string>? progressLogger = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(inputAudioPath))
            throw new FileNotFoundException("Input audio file not found", inputAudioPath);

        var vocalDir = Path.GetDirectoryName(vocalOutputPath);
        if (!string.IsNullOrWhiteSpace(vocalDir))
            Directory.CreateDirectory(vocalDir);

        var bgmDir = Path.GetDirectoryName(bgmOutputPath);
        if (!string.IsNullOrWhiteSpace(bgmDir))
            Directory.CreateDirectory(bgmDir);

        // 1. Try AI Demucs Neural Stem Separation if enabled
        if (useAiDemucs)
        {
            try
            {
                var success = await TrySeparateStemsAiAsync(
                    inputAudioPath,
                    vocalOutputPath,
                    bgmOutputPath,
                    progressLogger,
                    cancellationToken
                );

                if (
                    success
                    && File.Exists(vocalOutputPath)
                    && new FileInfo(vocalOutputPath).Length > 1024
                    && File.Exists(bgmOutputPath)
                    && new FileInfo(bgmOutputPath).Length > 1024
                )
                {
                    progressLogger?.Invoke(
                        "AI Demucs stem separation completed with high-fidelity action sound preservation."
                    );
                    return;
                }
            }
            catch (Exception ex)
            {
                progressLogger?.Invoke(
                    $"AI Demucs separation encountered an error ({ex.Message}). Falling back to FFmpeg filter..."
                );
            }
        }

        // 2. Fallback: Generate Dialogue / Vocal Stem via FFmpeg
        {
            using var vocalProcess = new Process();
            vocalProcess.StartInfo.FileName = ffmpegPath;
            vocalProcess.StartInfo.ArgumentList.Add("-y");
            vocalProcess.StartInfo.ArgumentList.Add("-i");
            vocalProcess.StartInfo.ArgumentList.Add(inputAudioPath);
            vocalProcess.StartInfo.ArgumentList.Add("-af");
            vocalProcess.StartInfo.ArgumentList.Add(
                "highpass=f=100, lowpass=f=3800, equalizer=f=1200:width_type=h:width=1800:g=3, afftdn"
            );
            vocalProcess.StartInfo.ArgumentList.Add("-c:a");
            vocalProcess.StartInfo.ArgumentList.Add("pcm_s16le");
            vocalProcess.StartInfo.ArgumentList.Add(vocalOutputPath);

            vocalProcess.StartInfo.UseShellExecute = false;
            vocalProcess.StartInfo.CreateNoWindow = true;

            vocalProcess.Start();
            ChildProcessTracker.Track(vocalProcess);
            await vocalProcess.WaitForExitAsync(cancellationToken);
        }

        // 3. Fallback: Generate Background Music Stem via FFmpeg (center channel notch)
        {
            using var bgmProcess = new Process();
            bgmProcess.StartInfo.FileName = ffmpegPath;
            bgmProcess.StartInfo.ArgumentList.Add("-y");
            bgmProcess.StartInfo.ArgumentList.Add("-i");
            bgmProcess.StartInfo.ArgumentList.Add(inputAudioPath);
            bgmProcess.StartInfo.ArgumentList.Add("-af");
            bgmProcess.StartInfo.ArgumentList.Add(
                "pan=stereo|c0=c0-c1|c1=c1-c0, equalizer=f=1500:width_type=h:width=1200:g=-14"
            );
            bgmProcess.StartInfo.ArgumentList.Add("-c:a");
            bgmProcess.StartInfo.ArgumentList.Add("pcm_s16le");
            bgmProcess.StartInfo.ArgumentList.Add(bgmOutputPath);

            bgmProcess.StartInfo.UseShellExecute = false;
            bgmProcess.StartInfo.CreateNoWindow = true;

            bgmProcess.Start();
            ChildProcessTracker.Track(bgmProcess);
            await bgmProcess.WaitForExitAsync(cancellationToken);
        }
    }

    private static string? FindPythonExecutable()
    {
        var candidates = new[]
        {
            @"C:\Applio-3.6.4\env\python.exe",
            Path.Combine(AppContext.BaseDirectory, "python", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "env", "python.exe"),
        };

        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    private static string? FindSeparateScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "separate_vocals.py"),
            @"D:\repos\855Media\tools\separate_vocals.py",
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "separate_vocals.py"),
        };

        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    private static async Task<bool> TrySeparateStemsAiAsync(
        string inputAudioPath,
        string vocalOutputPath,
        string bgmOutputPath,
        Action<string>? progressLogger,
        CancellationToken cancellationToken
    )
    {
        var pythonExe = FindPythonExecutable();
        var scriptPath = FindSeparateScript();

        if (string.IsNullOrWhiteSpace(pythonExe) || string.IsNullOrWhiteSpace(scriptPath))
            return false;

        progressLogger?.Invoke(
            "Starting AI Demucs Stem Separation (Dialogue vs Action SFX/BGM) on GPU..."
        );

        using var process = new Process();
        process.StartInfo.FileName = pythonExe;
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("--input");
        process.StartInfo.ArgumentList.Add(inputAudioPath);
        process.StartInfo.ArgumentList.Add("--vocals");
        process.StartInfo.ArgumentList.Add(vocalOutputPath);
        process.StartInfo.ArgumentList.Add("--no-vocals");
        process.StartInfo.ArgumentList.Add(bgmOutputPath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                progressLogger?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                progressLogger?.Invoke(e.Data);
        };

        process.Start();
        ChildProcessTracker.Track(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }
}
