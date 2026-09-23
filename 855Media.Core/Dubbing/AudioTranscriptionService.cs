using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public class AudioTranscriptionService
{
    private static readonly HttpClient HttpClient = new();

    public static string GetWhisperDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        var dir = Path.Combine(localAppData, "855Media", "whisper");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IEnumerable<string> GetProbeRoots()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
        yield return GetWhisperDirectory();

        yield return Path.Combine(AppContext.BaseDirectory, "tools", "whisper");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "tools", "whisper");

        // Walk up from AppContext.BaseDirectory (e.g., bin/Debug/net10.0 -> project root)
        DirectoryInfo? current = null;
        try
        {
            current = new DirectoryInfo(AppContext.BaseDirectory);
        }
        catch { }
        for (int i = 0; i < 6 && current?.Parent != null; i++)
        {
            current = current.Parent;
            yield return current.FullName;
            yield return Path.Combine(current.FullName, "tools", "whisper");
        }

        // Walk up from CurrentDirectory
        DirectoryInfo? currDir = null;
        try
        {
            currDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        }
        catch { }
        for (int i = 0; i < 6 && currDir?.Parent != null; i++)
        {
            currDir = currDir.Parent;
            yield return currDir.FullName;
            yield return Path.Combine(currDir.FullName, "tools", "whisper");
        }
    }

    public static string? TryGetWhisperCliPath()
    {
        var cliNames = new[] { "whisper-cli.exe", "main.exe", "whisper.exe" };

        foreach (var root in GetProbeRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var name in cliNames)
            {
                var direct = Path.Combine(root, name);
                if (File.Exists(direct))
                    return direct;

                var inRelease = Path.Combine(root, "Release", name);
                if (File.Exists(inRelease))
                    return inRelease;

                var inTools = Path.Combine(root, "tools", "whisper", name);
                if (File.Exists(inTools))
                    return inTools;
            }
        }

        // Check system PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var p in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;

                foreach (var name in cliNames)
                {
                    var cli = Path.Combine(p.Trim(), name);
                    if (File.Exists(cli))
                        return cli;
                }
            }
        }

        return null;
    }

    public static string? TryGetWhisperModelPath()
    {
        var priorityModelNames = new[]
        {
            "ggml-large-v3-turbo.bin",
            "ggml-large-v3.bin",
            "ggml-medium.bin",
            "ggml-small.bin",
            "ggml-base.bin",
            "ggml-tiny.bin",
        };

        var roots = GetProbeRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Priority 1: Check highest quality models across all roots first
        foreach (var modelName in priorityModelNames)
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                var subDirs = new[]
                {
                    root,
                    Path.Combine(root, "models"),
                    Path.Combine(root, "tools", "whisper", "models"),
                    Path.Combine(root, "tools", "whisper"),
                };

                foreach (var dir in subDirs)
                {
                    if (!Directory.Exists(dir))
                        continue;

                    var full = Path.Combine(dir, modelName);
                    if (File.Exists(full))
                        return full;
                }
            }
        }

        // Priority 2: Any available ggml-*.bin
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            var subDirs = new[]
            {
                root,
                Path.Combine(root, "models"),
                Path.Combine(root, "tools", "whisper", "models"),
                Path.Combine(root, "tools", "whisper"),
            };

            foreach (var dir in subDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                try
                {
                    var anyBin = Directory.GetFiles(dir, "ggml-*.bin").FirstOrDefault();
                    if (anyBin != null)
                        return anyBin;
                }
                catch { }
            }
        }

        return null;
    }

    public async Task<List<SubtitleSegment>> TranscribeAudioAsync(
        string ffmpegPath,
        string mediaFilePath,
        string language = "auto",
        IProgress<string>? logProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(mediaFilePath))
            throw new FileNotFoundException("Media file not found", mediaFilePath);

        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "855Media_STT",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempDir);

        try
        {
            var audioWavPath = Path.Combine(tempDir, "extracted_speech.wav");
            logProgress?.Report("Extracting speech audio from media...");

            // Extract 16kHz mono 16-bit WAV for transcription
            using (var proc = new Process())
            {
                proc.StartInfo.FileName = ffmpegPath;
                proc.StartInfo.ArgumentList.Add("-y");
                proc.StartInfo.ArgumentList.Add("-i");
                proc.StartInfo.ArgumentList.Add(mediaFilePath);
                proc.StartInfo.ArgumentList.Add("-vn");
                proc.StartInfo.ArgumentList.Add("-acodec");
                proc.StartInfo.ArgumentList.Add("pcm_s16le");
                proc.StartInfo.ArgumentList.Add("-ar");
                proc.StartInfo.ArgumentList.Add("16000");
                proc.StartInfo.ArgumentList.Add("-ac");
                proc.StartInfo.ArgumentList.Add("1");
                proc.StartInfo.ArgumentList.Add(audioWavPath);
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;

                proc.Start();
                ChildProcessTracker.Track(proc);
                await proc.WaitForExitWithCancellationAsync(cancellationToken);

                if (proc.ExitCode != 0 || !File.Exists(audioWavPath))
                {
                    throw new InvalidOperationException(
                        $"FFmpeg failed to extract speech audio (exit code: {proc.ExitCode})"
                    );
                }
            }

            var whisperCli = TryGetWhisperCliPath();
            var whisperModel = TryGetWhisperModelPath();

            if (!string.IsNullOrWhiteSpace(whisperCli) && !string.IsNullOrWhiteSpace(whisperModel))
            {
                logProgress?.Report(
                    $"Running offline Whisper AI transcription ({Path.GetFileName(whisperModel)})..."
                );
                var segments = await RunWhisperCliAsync(
                    whisperCli,
                    whisperModel,
                    audioWavPath,
                    tempDir,
                    language,
                    logProgress,
                    cancellationToken
                );

                if (segments.Count > 0)
                    return segments;
            }

            // Fallback: Voice Activity Detection (VAD) audio silence scan
            logProgress?.Report(
                "Whisper AI not detected. Scanning audio speech timestamps via Voice Activity Detection (VAD)..."
            );
            return await ScanSpeechIntervalsViaVadAsync(
                ffmpegPath,
                audioWavPath,
                cancellationToken
            );
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private async Task<List<SubtitleSegment>> RunWhisperCliAsync(
        string whisperCli,
        string modelPath,
        string wavPath,
        string tempDir,
        string language,
        IProgress<string>? logProgress,
        CancellationToken cancellationToken
    )
    {
        var outputBase = Path.Combine(tempDir, "whisper_out");
        var outputSrt = $"{outputBase}.srt";

        using var proc = new Process();
        proc.StartInfo.FileName = whisperCli;
        proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(whisperCli) ?? string.Empty;
        proc.StartInfo.ArgumentList.Add("-m");
        proc.StartInfo.ArgumentList.Add(modelPath);
        proc.StartInfo.ArgumentList.Add("-f");
        proc.StartInfo.ArgumentList.Add(wavPath);
        proc.StartInfo.ArgumentList.Add("-osrt");
        proc.StartInfo.ArgumentList.Add("-of");
        proc.StartInfo.ArgumentList.Add(outputBase);

        // Threads
        var threads = Math.Clamp(Environment.ProcessorCount, 2, 8);
        proc.StartInfo.ArgumentList.Add("-t");
        proc.StartInfo.ArgumentList.Add(threads.ToString());

        var langCode = NormalizeLanguageCode(language);
        proc.StartInfo.ArgumentList.Add("-l");
        proc.StartInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(langCode) ? "auto" : langCode);

        // Suppress non-speech tokens (music, applause, laughter) and avoid inventing text on opening silence
        proc.StartInfo.ArgumentList.Add("-sns");
        proc.StartInfo.ArgumentList.Add("-nth");
        proc.StartInfo.ArgumentList.Add("0.65");

        // Allow Whisper to output natural, complete sentences based on speech pauses
        // rather than artificially chopping sentences into 50-character chunks

        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;

        var outputLog = new StringBuilder();
        proc.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputLog.AppendLine(e.Data);
                logProgress?.Report(e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputLog.AppendLine(e.Data);
                logProgress?.Report(e.Data);
            }
        };

        proc.Start();
        ChildProcessTracker.Track(proc);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitWithCancellationAsync(cancellationToken);

        if (File.Exists(outputSrt))
        {
            var content = await File.ReadAllTextAsync(outputSrt, Encoding.UTF8, cancellationToken);
            var parsed = new SubtitleTranslationService().ParseSrt(content);
            if (parsed.Count > 0)
                return parsed;
        }
        else
        {
            logProgress?.Report(
                $"Whisper process finished without producing .srt. Exit code: {proc.ExitCode}"
            );
        }

        return [];
    }

    public async Task<List<SubtitleSegment>> ScanSpeechIntervalsViaVadAsync(
        string ffmpegPath,
        string wavPath,
        CancellationToken cancellationToken = default
    )
    {
        var segments = new List<SubtitleSegment>();

        using var proc = new Process();
        proc.StartInfo.FileName = ffmpegPath;
        proc.StartInfo.ArgumentList.Add("-i");
        proc.StartInfo.ArgumentList.Add(wavPath);
        proc.StartInfo.ArgumentList.Add("-af");
        proc.StartInfo.ArgumentList.Add("silencedetect=noise=-30dB:d=0.4");
        proc.StartInfo.ArgumentList.Add("-f");
        proc.StartInfo.ArgumentList.Add("null");
        proc.StartInfo.ArgumentList.Add("-");
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.StartInfo.RedirectStandardError = true;

        var silenceLog = new StringBuilder();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                silenceLog.AppendLine(e.Data);
        };

        proc.Start();
        ChildProcessTracker.Track(proc);
        proc.BeginErrorReadLine();
        await proc.WaitForExitWithCancellationAsync(cancellationToken);

        // Parse silence intervals
        var silenceEndRegex = new Regex(@"silence_end:\s*([0-9\.]+)", RegexOptions.Compiled);
        var silenceStartRegex = new Regex(@"silence_start:\s*([0-9\.]+)", RegexOptions.Compiled);

        var silenceRanges = new List<(double Start, double End)>();
        double currentStart = 0.0;
        bool hasStart = false;

        foreach (
            var line in silenceLog
                .ToString()
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var mStart = silenceStartRegex.Match(line);
            if (
                mStart.Success
                && double.TryParse(
                    mStart.Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var sStart
                )
            )
            {
                currentStart = sStart;
                hasStart = true;
            }

            var mEnd = silenceEndRegex.Match(line);
            if (
                mEnd.Success
                && double.TryParse(
                    mEnd.Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var sEnd
                )
            )
            {
                // If audio starts in silence, FFmpeg outputs silence_end without silence_start
                var rangeStart = hasStart ? currentStart : 0.0;
                silenceRanges.Add((rangeStart, sEnd));
                hasStart = false;
            }
        }

        // Invert silences into speech intervals
        double lastSpeechStart = 0.0;
        int index = 1;

        foreach (var (silenceStart, silenceEnd) in silenceRanges)
        {
            if (silenceStart > lastSpeechStart + 0.3)
            {
                var speechStart = TimeSpan.FromSeconds(lastSpeechStart);
                var speechEnd = TimeSpan.FromSeconds(silenceStart);

                segments.Add(
                    new SubtitleSegment
                    {
                        Index = index++,
                        StartTime = speechStart,
                        EndTime = speechEnd,
                        OriginalText = $"[Dialogue segment {index - 1}]",
                        KhmerText = string.Empty,
                    }
                );
            }

            lastSpeechStart = silenceEnd;
        }

        // Capture trailing speech interval between the last silence and end of audio
        try
        {
            var totalDuration = await DubbingPipeline.GetAudioDurationAsync(
                ffmpegPath,
                wavPath,
                cancellationToken
            );
            if (totalDuration.TotalSeconds > lastSpeechStart + 0.4)
            {
                segments.Add(
                    new SubtitleSegment
                    {
                        Index = index++,
                        StartTime = TimeSpan.FromSeconds(lastSpeechStart),
                        EndTime = totalDuration,
                        OriginalText = $"[Dialogue segment {index - 1}]",
                        KhmerText = string.Empty,
                    }
                );
            }
        }
        catch { }

        // If no speech intervals detected, create an initial dialogue segment
        if (segments.Count == 0)
        {
            segments.Add(
                new SubtitleSegment
                {
                    Index = 1,
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromSeconds(4),
                    OriginalText = "Welcome to 855Media Voice Dubbing Studio.",
                    KhmerText = "សូមស្វាគមន៍មកកាន់ 855Media Voice Dubbing Studio។",
                }
            );
        }

        return segments;
    }

    public static List<SubtitleSegment> GetExampleDialogue()
    {
        return
        [
            new SubtitleSegment
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(0.5),
                EndTime = TimeSpan.FromSeconds(3.5),
                OriginalText = "Hello everyone! Welcome back to our channel.",
                KhmerText = "សួស្តីអ្នកទាំងអស់គ្នា! សូមស្វាគមន៍មកកាន់ឆានែលរបស់យើងខ្ញុំ។",
            },
            new SubtitleSegment
            {
                Index = 2,
                StartTime = TimeSpan.FromSeconds(4.0),
                EndTime = TimeSpan.FromSeconds(7.8),
                OriginalText =
                    "Today we will discover how AI voice dubbing can transform videos into Khmer automatically.",
                KhmerText =
                    "ថ្ងៃនេះយើងនឹងស្វែងយល់ពីរបៀបដែលបច្ចេកវិទ្យា AI អាចបកប្រែនិងបញ្ចូលសំឡេងជាភាសាខ្មែរដោយស្វ័យប្រវត្តិ។",
            },
            new SubtitleSegment
            {
                Index = 3,
                StartTime = TimeSpan.FromSeconds(8.2),
                EndTime = TimeSpan.FromSeconds(12.0),
                OriginalText =
                    "It separates the original background music, generates natural speech, and synchronizes the timing.",
                KhmerText =
                    "វាបំបែកតន្ត្រីផ្ទៃខាងក្រោយ បង្កើតសំឡេងនិយាយបែបធម្មជាតិ និងតម្រឹមពេលវេលាយ៉ាងត្រឹមត្រូវ។",
            },
            new SubtitleSegment
            {
                Index = 4,
                StartTime = TimeSpan.FromSeconds(12.5),
                EndTime = TimeSpan.FromSeconds(15.5),
                OriginalText = "Enjoy watching and do not forget to like and subscribe!",
                KhmerText = "សូមរីករាយទស្សនា ហើយកុំភ្លេចចុច Like និង Subscribe ផងណា!",
            },
        ];
    }

    public async Task DownloadWhisperEngineAsync(
        IProgress<double>? progress = null,
        IProgress<string>? statusProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        var targetDir = GetWhisperDirectory();
        var modelsDir = Path.Combine(targetDir, "models");
        Directory.CreateDirectory(modelsDir);

        statusProgress?.Report("Downloading Whisper AI binary (whisper.cpp)...");
        var zipUrl =
            "https://github.com/ggml-org/whisper.cpp/releases/download/b5130/whisper-bin-x64.zip";
        var zipPath = Path.Combine(targetDir, "whisper-bin-x64.zip");

        await DownloadFileAsync(zipUrl, zipPath, progress, cancellationToken);

        statusProgress?.Report("Extracting Whisper AI components...");
        ZipFile.ExtractToDirectory(zipPath, targetDir, true);
        try
        {
            File.Delete(zipPath);
        }
        catch { }

        // Flatten Release subdirectory if present in zip
        var releaseDir = Path.Combine(targetDir, "Release");
        if (Directory.Exists(releaseDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(releaseDir))
                {
                    var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }
            }
            catch { }
        }

        // Download ggml-tiny.bin (~75MB) as default lightweight model
        statusProgress?.Report("Downloading Whisper AI neural model (ggml-tiny.bin)...");
        var modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin";
        var modelPath = Path.Combine(modelsDir, "ggml-tiny.bin");

        if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < 10000000)
        {
            await DownloadFileAsync(modelUrl, modelPath, progress, cancellationToken);
        }

        statusProgress?.Report("Whisper AI speech engine ready!");
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken
    )
    {
        using var response = await HttpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            8192,
            true
        );

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while (
            (read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0
        )
        {
            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
            totalRead += read;

            if (totalBytes > 0 && progress != null)
            {
                progress.Report((double)totalRead / totalBytes);
            }
        }
    }

    private static string? NormalizeLanguageCode(string language)
    {
        if (
            string.IsNullOrWhiteSpace(language)
            || language.Equals("Auto", StringComparison.OrdinalIgnoreCase)
        )
            return "auto";

        return language.ToLowerInvariant() switch
        {
            "english" => "en",
            "chinese" => "zh",
            "vietnamese" => "vi",
            "thai" => "th",
            "japanese" => "ja",
            "korean" => "ko",
            "french" => "fr",
            "spanish" => "es",
            "khmer" => "km",
            _ => language.Length <= 3 ? language.ToLowerInvariant() : "auto",
        };
    }

    /// <summary>
    /// Analyzes the media audio within a time window to detect the actual onset of spoken speech,
    /// trimming leading silence, opening music, or noise so dialogue lines align with the actor's mouth.
    /// Returns the adjusted speech start TimeSpan if leading silence exceeds 150ms.
    /// </summary>
    public static async Task<TimeSpan?> DetectActualSpeechStartAsync(
        string ffmpegPath,
        string mediaPath,
        TimeSpan windowStart,
        TimeSpan windowEnd,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            return null;

        var startSec = Math.Max(0, windowStart.TotalSeconds)
            .ToString("0.000", CultureInfo.InvariantCulture);
        var durSec = Math.Max(0.5, (windowEnd - windowStart).TotalSeconds)
            .ToString("0.000", CultureInfo.InvariantCulture);

        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = ffmpegPath;
            proc.StartInfo.ArgumentList.Add("-y");
            proc.StartInfo.ArgumentList.Add("-ss");
            proc.StartInfo.ArgumentList.Add(startSec);
            proc.StartInfo.ArgumentList.Add("-t");
            proc.StartInfo.ArgumentList.Add(durSec);
            proc.StartInfo.ArgumentList.Add("-i");
            proc.StartInfo.ArgumentList.Add(mediaPath);
            proc.StartInfo.ArgumentList.Add("-af");
            proc.StartInfo.ArgumentList.Add(
                "highpass=f=150,lowpass=f=4000,silencedetect=noise=-28dB:d=0.20"
            );
            proc.StartInfo.ArgumentList.Add("-f");
            proc.StartInfo.ArgumentList.Add("null");
            proc.StartInfo.ArgumentList.Add("-");
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardError = true;

            var errLog = new StringBuilder();
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    errLog.AppendLine(e.Data);
            };

            proc.Start();
            ChildProcessTracker.Track(proc);
            proc.BeginErrorReadLine();
            await proc.WaitForExitWithCancellationAsync(cancellationToken);

            var silenceEndRegex = new Regex(@"silence_end:\s*([0-9\.]+)", RegexOptions.Compiled);
            var silenceStartRegex = new Regex(
                @"silence_start:\s*([0-9\.]+)",
                RegexOptions.Compiled
            );

            double? firstSilenceStart = null;
            foreach (
                var line in errLog
                    .ToString()
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            )
            {
                var mStart = silenceStartRegex.Match(line);
                if (
                    mStart.Success
                    && double.TryParse(
                        mStart.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var startVal
                    )
                )
                {
                    firstSilenceStart ??= startVal;
                }

                var mEnd = silenceEndRegex.Match(line);
                if (
                    mEnd.Success
                    && double.TryParse(
                        mEnd.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var sEnd
                    )
                )
                {
                    // If silence began at the start of the window (start <= 0.08s or no prior silence_start emitted)
                    if (
                        (firstSilenceStart == null || firstSilenceStart.Value <= 0.08)
                        && sEnd > 0.15
                    )
                    {
                        return windowStart + TimeSpan.FromSeconds(sEnd);
                    }
                    break;
                }
            }
        }
        catch { }

        return null;
    }
}
