using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public class DubbingPipeline
{
    private readonly AudioStemSeparationService _stemService = new();
    private readonly SubtitleTranslationService _subService = new();
    private readonly KhmerTtsService _ttsService = new();
    private readonly RvcInferenceService _rvcService = new();
    private readonly AudioTranscriptionService _transcriptionService = new();

    public async Task ExecuteAsync(
        DubbingJob job,
        string? ffmpegPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ffmpeg = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
            throw new FileNotFoundException("FFmpeg executable could not be found.");

        if (!File.Exists(job.VideoFilePath))
            throw new FileNotFoundException("Input video file not found.", job.VideoFilePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "855Media_Dubbing", job.Id.ToString("N"));
        Directory.CreateDirectory(tempDir);

        var logLock = new object();
        var logBuilder = new StringBuilder();
        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            lock (logLock)
            {
                logBuilder.AppendLine(line);
                job.DetailedLog = logBuilder.ToString();
            }
        }

        try
        {
            Log($"Starting Khmer Dubbing Pipeline for {job.FileName}...");
            job.Progress = 5;

            // Stage 1: Audio Extraction & Stem Separation
            job.Status = DubbingJobStatus.ExtractingAudio;
            job.StatusMessage = "Extracting and separating audio stems...";
            var fullAudioPath = Path.Combine(tempDir, "original_audio.wav");
            var vocalPath = Path.Combine(tempDir, "vocals.wav");
            var bgmPath = Path.Combine(tempDir, "bgm.wav");

            Log("Extracting audio from input video...");
            await _stemService.ExtractFullAudioAsync(
                ffmpeg,
                job.VideoFilePath,
                fullAudioPath,
                cancellationToken
            );

            Log(
                job.EnableAiStemSeparation
                    ? "Separating dialogue from action sounds & music with GPU AI Demucs..."
                    : "Separating vocal and background instrumental stems..."
            );
            job.Status = DubbingJobStatus.SeparatingStems;
            await _stemService.SeparateStemsAsync(
                ffmpeg,
                fullAudioPath,
                vocalPath,
                bgmPath,
                useAiDemucs: job.EnableAiStemSeparation,
                progressLogger: Log,
                cancellationToken: cancellationToken
            );
            job.ExtractedDialoguePath = vocalPath;
            job.ExtractedBgmPath = bgmPath;
            job.Progress = 20;

            // Stage 2: Subtitle Extraction / Transcription
            job.Status = DubbingJobStatus.Transcribing;
            job.StatusMessage = "Analyzing subtitles and speech...";
            Log("Detecting subtitles and speech segments...");

            if (job.Segments.Count == 0)
            {
                var discovered = await _subService.ExtractOrGenerateSubtitlesAsync(
                    ffmpeg,
                    job.VideoFilePath,
                    job.SourceLanguage,
                    cancellationToken
                );

                foreach (var seg in discovered)
                {
                    job.Segments.Add(seg);
                }
            }

            if (job.Segments.Count == 0)
            {
                Log("No embedded subtitles found. Scanning video audio speech to text...");
                var transcribed = await _transcriptionService.TranscribeAudioAsync(
                    ffmpeg,
                    job.VideoFilePath,
                    job.SourceLanguage,
                    new Progress<string>(Log),
                    cancellationToken
                );

                foreach (var seg in transcribed)
                {
                    job.Segments.Add(seg);
                }
            }

            if (job.Segments.Count == 0)
            {
                throw new InvalidOperationException(
                    "No dialogue lines found to dub. Please click 'Add Line' or 'Import Subtitles' in the table to add dialogue for this video."
                );
            }

            // Improve original dialogue and reconstruct fragmented sentences into complete sense-to-sense dialogue
            if (job.Segments.Count > 1)
            {
                var reconstructed = DialogueSenseEngine.ReconstructSentences(job.Segments);
                if (reconstructed.Count != job.Segments.Count)
                {
                    Log(
                        $"Reconstructed {job.Segments.Count} speech fragments into {reconstructed.Count} complete sense-to-sense dialogue sentences."
                    );
                    job.Segments.Clear();
                    foreach (var s in reconstructed)
                    {
                        job.Segments.Add(s);
                    }
                }
            }

            foreach (var s in job.Segments)
            {
                s.OriginalText = DialogueSenseEngine.ImproveOriginalDialogue(
                    s.OriginalText,
                    job.SourceLanguage
                );
            }

            job.Progress = 35;

            // Stage 3: Translation to Khmer
            job.Status = DubbingJobStatus.Translating;
            job.StatusMessage = "Translating dialogue into Khmer (ភាសាខ្មែរ)...";
            Log($"Translating {job.Segments.Count} subtitle segment(s) to Khmer...");

            if (job.Segments.Count > 0)
            {
                int segCount = job.Segments.Count;
                for (int i = 0; i < segCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var seg = job.Segments[i];
                    if (string.IsNullOrWhiteSpace(seg.KhmerText))
                    {
                        seg.KhmerText = await _subService.TranslateToKhmerAsync(
                            seg.OriginalText,
                            job.SourceLanguage,
                            cancellationToken
                        );
                    }

                    job.Progress = 35 + ((double)(i + 1) / segCount * 25.0);
                }
            }
            else
            {
                Log(
                    "Notice: No subtitle tracks found. You can add or edit subtitle lines in the editor."
                );
            }

            // Stage 4: Synthesizing Khmer Speech for all Movie Characters
            job.Status = DubbingJobStatus.SynthesizingSpeech;
            job.StatusMessage = "Synthesizing Khmer speech across characters...";
            Log("Generating speech audio across character voice assignments...");

            var dubbedVocalClips = new List<string>();
            var segsToSynthesize = job.Segments;
            bool hasPerCharacterRvc = job.Characters.Any(c =>
                c.EnableRvc
                && !string.IsNullOrWhiteSpace(c.RvcModelPath)
                && File.Exists(c.RvcModelPath)
            );

            if (segsToSynthesize.Count > 0)
            {
                int rvcWorkers = Math.Clamp(job.RvcConcurrency > 0 ? job.RvcConcurrency : 4, 1, 8);
                using var rvcSemaphore = new SemaphoreSlim(rvcWorkers, rvcWorkers);
                int ttsWorkers = Math.Clamp(
                    Math.Max(rvcWorkers * 2, Environment.ProcessorCount),
                    4,
                    12
                );
                int completedCount = 0;

                Log(
                    $"Synthesizing dialogue using {ttsWorkers} parallel TTS workers and {rvcWorkers} GPU RVC workers..."
                );

                await Parallel.ForEachAsync(
                    segsToSynthesize,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = ttsWorkers,
                        CancellationToken = cancellationToken,
                    },
                    async (seg, ct) =>
                    {
                        var textToSpeak = !string.IsNullOrWhiteSpace(seg.KhmerText)
                            ? seg.KhmerText
                            : seg.OriginalText;

                        var character = ResolveCharacter(seg, job);
                        var voiceToUse = character?.BaseVoice ?? job.SelectedVoice;

                        // Resolve acting emotion tone: segment override takes precedence over character preset
                        var emotionName =
                            !string.IsNullOrWhiteSpace(seg.Emotion)
                            && seg.Emotion != ActorEmotionEngine.EmotionNormal
                                ? seg.Emotion
                                : (character?.EmotionPreset ?? ActorEmotionEngine.EmotionNormal);

                        var emotionCfg = ActorEmotionEngine.GetConfig(emotionName);
                        var effectiveRate = ActorEmotionEngine.ComputeEffectiveRate(
                            character?.SpeechRate,
                            emotionCfg.TtsRateOffset
                        );

                        // Unified pitch: Combines character's pitch shift (semitones) + emotion pitch offset
                        var effectivePitch = ActorEmotionEngine.ComputeEffectiveTtsPitch(
                            character?.PitchShift ?? 0,
                            emotionCfg.TtsPitch,
                            emotionCfg.PitchShiftOffset
                        );

                        var clipPath = Path.Combine(tempDir, $"clip_{seg.Index:D4}.mp3");
                        await _ttsService.SynthesizeKhmerSpeechAsync(
                            textToSpeak,
                            clipPath,
                            voiceToUse,
                            rate: effectiveRate,
                            pitch: effectivePitch,
                            volume: emotionCfg.TtsVolume,
                            cancellationToken: ct
                        );

                        // Check if this character has a dedicated RVC voice model
                        if (
                            character != null
                            && character.EnableRvc
                            && !string.IsNullOrWhiteSpace(character.RvcModelPath)
                            && File.Exists(character.RvcModelPath)
                        )
                        {
                            var clonedClipPath = Path.Combine(
                                tempDir,
                                $"clip_{seg.Index:D4}_rvc.wav"
                            );
                            await rvcSemaphore.WaitAsync(ct);
                            try
                            {
                                int effectivePitchShift =
                                    character.PitchShift + emotionCfg.PitchShiftOffset;
                                Log(
                                    $"Cloning character voice for '{character.Name}' with {emotionCfg.DisplayName} tone (line {seg.Index}, Pitch: {effectivePitchShift})..."
                                );
                                await _rvcService.ConvertVoiceAsync(
                                    clipPath,
                                    clonedClipPath,
                                    character.RvcModelPath,
                                    character.RvcIndexPath,
                                    effectivePitchShift,
                                    Log,
                                    ct
                                );

                                if (
                                    File.Exists(clonedClipPath)
                                    && new FileInfo(clonedClipPath).Length > 1024
                                )
                                {
                                    clipPath = clonedClipPath;
                                }
                            }
                            catch (Exception rvcEx)
                            {
                                Log(
                                    $"Warning: Character RVC conversion failed for line {seg.Index}: {rvcEx.Message}. Using base voice."
                                );
                            }
                            finally
                            {
                                rvcSemaphore.Release();
                            }
                        }

                        // Apply character acoustic tone & acting emotion DSP filter (chest warmth, clarity presence, sobbing quiver, etc.)
                        var warmth = character?.ToneWarmth ?? 0.0;
                        var clarity = character?.ToneClarity ?? 0.0;
                        var archetype = character?.ToneArchetype;
                        var filterString = ActorEmotionEngine.BuildActorAcousticFilter(
                            emotionCfg.Name,
                            warmth,
                            clarity,
                            archetype
                        );

                        if (!string.IsNullOrWhiteSpace(filterString))
                        {
                            var emotionalClipPath = Path.Combine(
                                tempDir,
                                $"clip_{seg.Index:D4}_tone.wav"
                            );
                            var applied = await ActorEmotionEngine.ApplyActorAcousticFilterAsync(
                                ffmpeg,
                                clipPath,
                                emotionalClipPath,
                                emotionCfg.Name,
                                warmth,
                                clarity,
                                archetype,
                                ct
                            );
                            if (
                                applied
                                && File.Exists(emotionalClipPath)
                                && new FileInfo(emotionalClipPath).Length > 1024
                            )
                            {
                                clipPath = emotionalClipPath;
                            }
                        }

                        seg.AudioClipPath = clipPath;

                        var done = Interlocked.Increment(ref completedCount);
                        job.Progress = 40 + ((double)done / segsToSynthesize.Count * 30.0);
                        job.StatusMessage =
                            $"Synthesizing speech: {done}/{segsToSynthesize.Count} lines completed...";
                    }
                );
            }

            job.Progress = 75;

            // Stage 5: Assemble Full Khmer Vocal Track
            var assembledVocalPath = Path.Combine(tempDir, "assembled_khmer_vocals.wav");
            await AssembleVocalTrackAsync(ffmpeg, job, assembledVocalPath, cancellationToken);

            // Stage 6: Optional Global Voice Cloning (RVC)
            var finalVocalTrack = assembledVocalPath;
            if (
                job.EnableVoiceCloning
                && !string.IsNullOrWhiteSpace(job.RvcModelPath)
                && !hasPerCharacterRvc
            )
            {
                job.Status = DubbingJobStatus.ApplyingVoiceClone;
                job.StatusMessage = "Applying RVC voice clone to Khmer vocals...";
                Log(
                    $"Transforming vocal timbre with RVC model {Path.GetFileName(job.RvcModelPath)}..."
                );

                var clonedVocalPath = Path.Combine(tempDir, "cloned_khmer_vocals.wav");
                await _rvcService.ConvertVoiceAsync(
                    assembledVocalPath,
                    clonedVocalPath,
                    job.RvcModelPath,
                    job.RvcIndexPath,
                    job.PitchShift,
                    Log,
                    cancellationToken
                );

                if (File.Exists(clonedVocalPath) && new FileInfo(clonedVocalPath).Length > 1024)
                {
                    finalVocalTrack = clonedVocalPath;
                    Log("RVC voice transformation completed successfully.");
                }
            }

            job.Progress = 85;

            // Stage 7: Audio Mix & Lossless Video Remuxing with Dynamic Ducking
            job.Status = DubbingJobStatus.Remuxing;
            job.StatusMessage = "Mixing audio and exporting final video...";
            Log(
                job.EnableDynamicDucking
                    ? "Mixing dubbed vocals with dynamic sidechain ducking (action sounds remain 100% loud)..."
                    : "Mixing dubbed Khmer vocal track with original background music..."
            );

            var outDir = Path.GetDirectoryName(job.OutputFilePath);
            if (!string.IsNullOrWhiteSpace(outDir))
                Directory.CreateDirectory(outDir);

            await RemuxVideoAsync(
                ffmpeg,
                job.VideoFilePath,
                finalVocalTrack,
                bgmPath,
                job.OutputFilePath,
                job.VoiceVolume,
                job.BgmVolume,
                job.EnableDynamicDucking,
                job.EnableLoudnessNormalization,
                cancellationToken
            );

            // Save accompanying Khmer .srt file if segments exist
            if (job.Segments.Count > 0)
            {
                try
                {
                    var srtOut = Path.ChangeExtension(job.OutputFilePath, ".srt");
                    var srtContent = BuildSrt(job.Segments);
                    await File.WriteAllTextAsync(
                        srtOut,
                        srtContent,
                        Encoding.UTF8,
                        cancellationToken
                    );
                    Log($"Saved Khmer subtitle file to {srtOut}");
                }
                catch { }
            }

            job.Progress = 100;
            job.Status = DubbingJobStatus.Completed;
            job.StatusMessage = "Dubbing completed successfully!";
            Log($"Khmer Dubbed video successfully exported to: {job.OutputFilePath}");
        }
        catch (OperationCanceledException)
        {
            job.Status = DubbingJobStatus.Canceled;
            job.StatusMessage = "Dubbing job was canceled.";
            Log("Job canceled by user.");
            throw;
        }
        catch (Exception ex)
        {
            job.Status = DubbingJobStatus.Failed;
            job.StatusMessage = $"Failed: {ex.Message}";
            Log($"Error: {ex.Message}");
            throw;
        }
        finally
        {
            // Cleanup working files upon completion
            if (job.Status == DubbingJobStatus.Completed)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Concatenates multiple video files into a single unified extended video file.
    /// First attempts fast stream copy (-c copy). If codecs/formats differ, re-encodes with fast x264/aac.
    /// </summary>
    public static async Task<bool> ConcatenateVideosAsync(
        string ffmpegPath,
        IReadOnlyList<string> videoPaths,
        string outputPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (videoPaths == null || videoPaths.Count == 0)
            return false;

        var existingFiles = videoPaths.Where(File.Exists).ToList();
        if (existingFiles.Count == 0)
            return false;

        if (existingFiles.Count == 1)
        {
            if (!string.Equals(existingFiles[0], outputPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(existingFiles[0], outputPath, overwrite: true);
            }
            return true;
        }

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "855Media_Concat_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempDir);
        var listFile = Path.Combine(tempDir, "concat_list.txt");

        try
        {
            var sb = new StringBuilder();
            foreach (var path in existingFiles)
            {
                var escaped = path.Replace("'", "'\\''");
                sb.AppendLine($"file '{escaped}'");
            }
            await File.WriteAllTextAsync(listFile, sb.ToString(), cancellationToken);

            progress?.Report($"Joining {existingFiles.Count} videos with stream copy...");

            // Pass 1: Try ultra-fast stream copy
            using (var copyProc = new Process())
            {
                copyProc.StartInfo.FileName = ffmpegPath;
                copyProc.StartInfo.ArgumentList.Add("-y");
                copyProc.StartInfo.ArgumentList.Add("-f");
                copyProc.StartInfo.ArgumentList.Add("concat");
                copyProc.StartInfo.ArgumentList.Add("-safe");
                copyProc.StartInfo.ArgumentList.Add("0");
                copyProc.StartInfo.ArgumentList.Add("-i");
                copyProc.StartInfo.ArgumentList.Add(listFile);
                copyProc.StartInfo.ArgumentList.Add("-c");
                copyProc.StartInfo.ArgumentList.Add("copy");
                copyProc.StartInfo.ArgumentList.Add(outputPath);
                copyProc.StartInfo.UseShellExecute = false;
                copyProc.StartInfo.CreateNoWindow = true;

                copyProc.Start();
                ChildProcessTracker.Track(copyProc);
                await copyProc.WaitForExitWithCancellationAsync(cancellationToken);

                if (
                    copyProc.ExitCode == 0
                    && File.Exists(outputPath)
                    && new FileInfo(outputPath).Length > 1000
                )
                {
                    progress?.Report("Videos concatenated successfully (Stream Copy).");
                    return true;
                }
            }

            // Pass 2: Fallback re-encode if video containers/codecs/dimensions differ
            progress?.Report(
                "Stream copy failed; re-encoding videos for seamless timeline extension..."
            );
            using (var encProc = new Process())
            {
                encProc.StartInfo.FileName = ffmpegPath;
                encProc.StartInfo.ArgumentList.Add("-y");
                encProc.StartInfo.ArgumentList.Add("-f");
                encProc.StartInfo.ArgumentList.Add("concat");
                encProc.StartInfo.ArgumentList.Add("-safe");
                encProc.StartInfo.ArgumentList.Add("0");
                encProc.StartInfo.ArgumentList.Add("-i");
                encProc.StartInfo.ArgumentList.Add(listFile);
                encProc.StartInfo.ArgumentList.Add("-c:v");
                encProc.StartInfo.ArgumentList.Add("libx264");
                encProc.StartInfo.ArgumentList.Add("-preset");
                encProc.StartInfo.ArgumentList.Add("veryfast");
                encProc.StartInfo.ArgumentList.Add("-crf");
                encProc.StartInfo.ArgumentList.Add("19");
                encProc.StartInfo.ArgumentList.Add("-c:a");
                encProc.StartInfo.ArgumentList.Add("aac");
                encProc.StartInfo.ArgumentList.Add("-b:a");
                encProc.StartInfo.ArgumentList.Add("192k");
                encProc.StartInfo.ArgumentList.Add(outputPath);
                encProc.StartInfo.UseShellExecute = false;
                encProc.StartInfo.CreateNoWindow = true;

                encProc.Start();
                ChildProcessTracker.Track(encProc);
                await encProc.WaitForExitWithCancellationAsync(cancellationToken);

                if (
                    encProc.ExitCode == 0
                    && File.Exists(outputPath)
                    && new FileInfo(outputPath).Length > 1000
                )
                {
                    progress?.Report("Videos concatenated successfully (Re-encoded).");
                    return true;
                }
            }

            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    public static async Task<TimeSpan> GetAudioDurationAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(filePath))
            return TimeSpan.Zero;

        using var proc = new Process();
        proc.StartInfo.FileName = ffmpegPath;
        proc.StartInfo.ArgumentList.Add("-i");
        proc.StartInfo.ArgumentList.Add(filePath);
        proc.StartInfo.ArgumentList.Add("-f");
        proc.StartInfo.ArgumentList.Add("null");
        proc.StartInfo.ArgumentList.Add("-");
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.StartInfo.RedirectStandardError = true;

        var errSb = new StringBuilder();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errSb.AppendLine(e.Data);
        };

        proc.Start();
        ChildProcessTracker.Track(proc);
        proc.BeginErrorReadLine();
        await proc.WaitForExitWithCancellationAsync(cancellationToken);

        var match = System.Text.RegularExpressions.Regex.Match(
            errSb.ToString(),
            @"Duration:\s*(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)"
        );
        if (match.Success)
        {
            var h = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            var s = double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
            return TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s);
        }

        return TimeSpan.Zero;
    }

    public static async Task AssembleVocalTrackAsync(
        string ffmpegPath,
        DubbingJob job,
        string outputPath,
        CancellationToken cancellationToken = default
    )
    {
        var tempDir = Path.GetDirectoryName(outputPath)!;
        var orderedSegments = job
            .Segments.Where(s =>
                !string.IsNullOrWhiteSpace(s.AudioClipPath) && File.Exists(s.AudioClipPath)
            )
            .OrderBy(s => s.StartTime)
            .ToList();

        if (orderedSegments.Count == 0)
        {
            var sb = new StringBuilder();
            foreach (var seg in job.Segments)
            {
                sb.AppendLine(
                    !string.IsNullOrWhiteSpace(seg.KhmerText) ? seg.KhmerText : seg.OriginalText
                );
            }

            var text = sb.ToString();
            if (string.IsNullOrWhiteSpace(text))
                text = " ";

            var tts = new KhmerTtsService();
            await tts.SynthesizeKhmerSpeechAsync(
                text,
                outputPath,
                voiceName: job.SelectedVoice,
                rate: "+15%",
                cancellationToken: cancellationToken
            );
            return;
        }

        var totalVideoDuration = await GetAudioDurationAsync(
            ffmpegPath,
            job.VideoFilePath,
            cancellationToken
        );
        if (totalVideoDuration <= TimeSpan.Zero)
        {
            totalVideoDuration = orderedSegments.Max(s => s.EndTime) + TimeSpan.FromSeconds(2);
        }

        // 1. Concurrently prepare standardized 44.1kHz 16-bit stereo WAV clips (applying tempo stretch if needed)
        var processedClips = new (string Path, TimeSpan StartTime)[orderedSegments.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, orderedSegments.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
                CancellationToken = cancellationToken,
            },
            async (i, ct) =>
            {
                var seg = orderedSegments[i];
                var clipPath = seg.AudioClipPath!;
                var slotDuration =
                    (seg.EndTime > seg.StartTime)
                        ? seg.EndTime - seg.StartTime
                        : TimeSpan.FromSeconds(2);

                var clipDuration = await GetAudioDurationAsync(ffmpegPath, clipPath, ct);
                if (clipDuration == TimeSpan.Zero)
                    clipDuration = slotDuration;

                var normClip = Path.Combine(tempDir, $"timeline_norm_{seg.Index:D4}.wav");

                using var proc = new Process();
                proc.StartInfo.FileName = ffmpegPath;
                proc.StartInfo.ArgumentList.Add("-y");
                proc.StartInfo.ArgumentList.Add("-i");
                proc.StartInfo.ArgumentList.Add(clipPath);

                // If speech exceeds slot duration by more than 5%, adjust tempo so it matches scene lip action
                if (clipDuration > slotDuration * 1.05 && slotDuration.TotalSeconds > 0.3)
                {
                    var ratio = clipDuration.TotalSeconds / slotDuration.TotalSeconds;
                    var speed = Math.Clamp(ratio, 1.0, 1.85);
                    var speedStr = speed.ToString("0.00", CultureInfo.InvariantCulture);
                    proc.StartInfo.ArgumentList.Add("-filter:a");
                    proc.StartInfo.ArgumentList.Add(
                        $"atempo={speedStr},aformat=sample_rates=44100:channel_layouts=stereo"
                    );
                }
                else
                {
                    proc.StartInfo.ArgumentList.Add("-filter:a");
                    proc.StartInfo.ArgumentList.Add(
                        "aformat=sample_rates=44100:channel_layouts=stereo"
                    );
                }

                proc.StartInfo.ArgumentList.Add("-ar");
                proc.StartInfo.ArgumentList.Add("44100");
                proc.StartInfo.ArgumentList.Add("-ac");
                proc.StartInfo.ArgumentList.Add("2");
                proc.StartInfo.ArgumentList.Add("-c:a");
                proc.StartInfo.ArgumentList.Add("pcm_s16le");
                proc.StartInfo.ArgumentList.Add(normClip);
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;

                proc.Start();
                ChildProcessTracker.Track(proc);
                await proc.WaitForExitWithCancellationAsync(ct);

                if (File.Exists(normClip) && new FileInfo(normClip).Length > 44)
                {
                    processedClips[i] = (normClip, seg.StartTime);
                }
                else
                {
                    processedClips[i] = (clipPath, seg.StartTime);
                }
            }
        );

        // 2. Direct C# PCM Timeline Mixer - Assembles all clips without hitting Windows CLI argument limits
        MixTimelineToWav(processedClips, totalVideoDuration, outputPath);
    }

    private static byte[]? ExtractPcmDataFromWav(string wavPath)
    {
        try
        {
            using var fs = File.OpenRead(wavPath);
            using var br = new BinaryReader(fs);

            if (fs.Length < 44)
                return null;

            var riffBytes = br.ReadBytes(4);
            if (Encoding.ASCII.GetString(riffBytes) != "RIFF")
                return null;

            br.ReadUInt32(); // total size

            var waveBytes = br.ReadBytes(4);
            if (Encoding.ASCII.GetString(waveBytes) != "WAVE")
                return null;

            while (fs.Position <= fs.Length - 8)
            {
                var chunkIdBytes = br.ReadBytes(4);
                var chunkSize = br.ReadUInt32();
                var chunkId = Encoding.ASCII.GetString(chunkIdBytes);

                if (chunkId == "data")
                {
                    var actualBytesToRead = (int)Math.Min(chunkSize, fs.Length - fs.Position);
                    return br.ReadBytes(actualBytesToRead);
                }

                if (chunkSize > 0 && fs.Position + chunkSize <= fs.Length)
                {
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }
                else
                {
                    break;
                }

                // RIFF chunk padding
                if ((chunkSize % 2) == 1 && fs.Position < fs.Length)
                {
                    fs.Seek(1, SeekOrigin.Current);
                }
            }
        }
        catch
        {
            // Fallback gracefully
        }

        return null;
    }

    private static void MixTimelineToWav(
        IReadOnlyList<(string WavPath, TimeSpan StartTime)> clips,
        TimeSpan totalDuration,
        string outputWavPath
    )
    {
        const int sampleRate = 44100;
        const int channels = 2;
        const int bytesPerSample = 2; // 16-bit
        const int bytesPerFrame = channels * bytesPerSample; // 4 bytes

        long totalFrames = (long)Math.Ceiling(totalDuration.TotalSeconds * sampleRate);
        if (totalFrames <= 0)
        {
            totalFrames = sampleRate * 2;
        }
        long totalDataBytes = totalFrames * bytesPerFrame;

        var outDir = Path.GetDirectoryName(outputWavPath);
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        using (
            var outFs = new FileStream(
                outputWavPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                65536
            )
        )
        {
            // 1. Write initial 44-byte WAV header
            WriteWavHeader(outFs, sampleRate, channels, totalDataBytes);

            // Pre-allocate full timeline duration so silence is 0
            outFs.SetLength(44 + totalDataBytes);

            foreach (var (wavPath, startTime) in clips)
            {
                if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
                    continue;

                var pcmBytes = ExtractPcmDataFromWav(wavPath);
                if (pcmBytes == null || pcmBytes.Length < bytesPerFrame)
                    continue;

                long startFrame = (long)Math.Max(0, startTime.TotalSeconds * sampleRate);
                long startByte = 44 + (startFrame * bytesPerFrame);

                long neededLength = startByte + pcmBytes.Length;
                if (neededLength > outFs.Length)
                {
                    outFs.SetLength(neededLength);
                }

                int bytesToWrite = pcmBytes.Length;

                // Read existing bytes in target range
                outFs.Seek(startByte, SeekOrigin.Begin);
                byte[] rentedBytes = ArrayPool<byte>.Shared.Rent(bytesToWrite);
                try
                {
                    int read = outFs.Read(rentedBytes, 0, bytesToWrite);

                    // Mix 16-bit PCM samples with clipping protection
                    Span<short> existingSamples = MemoryMarshal.Cast<byte, short>(
                        rentedBytes.AsSpan(0, read)
                    );
                    ReadOnlySpan<short> newSamples = MemoryMarshal.Cast<byte, short>(
                        pcmBytes.AsSpan(0, read)
                    );

                    for (int s = 0; s < existingSamples.Length; s++)
                    {
                        int mixed = existingSamples[s] + newSamples[s];
                        existingSamples[s] = (short)
                            Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    }

                    // Write mixed bytes back to the timeline
                    outFs.Seek(startByte, SeekOrigin.Begin);
                    outFs.Write(rentedBytes, 0, read);

                    // If clip extended past previously allocated length
                    if (bytesToWrite > read)
                    {
                        outFs.Write(pcmBytes, read, bytesToWrite - read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBytes);
                }
            }

            // Finalize header with actual length
            long finalDataLength = outFs.Length - 44;
            outFs.Seek(0, SeekOrigin.Begin);
            WriteWavHeader(outFs, sampleRate, channels, finalDataLength);
        }
    }

    private static void WriteWavHeader(Stream stream, int sampleRate, int channels, long dataLength)
    {
        using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        long totalFileLength = 36 + dataLength;
        uint riffSize = totalFileLength > uint.MaxValue ? uint.MaxValue : (uint)totalFileLength;
        uint dataChunkSize = dataLength > uint.MaxValue ? uint.MaxValue : (uint)dataLength;

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(riffSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write((uint)16); // subchunk size
        bw.Write((ushort)1); // PCM format
        bw.Write((ushort)channels);
        bw.Write((uint)sampleRate);
        bw.Write((uint)(sampleRate * channels * 2)); // byte rate
        bw.Write((ushort)(channels * 2)); // block align
        bw.Write((ushort)16); // bits per sample

        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataChunkSize);
    }

    private static async Task GenerateSilenceAsync(
        string ffmpegPath,
        double durationSeconds,
        string outputPath,
        CancellationToken cancellationToken
    )
    {
        var dStr = durationSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        using var proc = new Process();
        proc.StartInfo.FileName = ffmpegPath;
        proc.StartInfo.ArgumentList.Add("-y");
        proc.StartInfo.ArgumentList.Add("-f");
        proc.StartInfo.ArgumentList.Add("lavfi");
        proc.StartInfo.ArgumentList.Add("-i");
        proc.StartInfo.ArgumentList.Add("anullsrc=r=44100:cl=stereo");
        proc.StartInfo.ArgumentList.Add("-t");
        proc.StartInfo.ArgumentList.Add(dStr);
        proc.StartInfo.ArgumentList.Add("-c:a");
        proc.StartInfo.ArgumentList.Add("pcm_s16le");
        proc.StartInfo.ArgumentList.Add(outputPath);
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.Start();
        ChildProcessTracker.Track(proc);
        await proc.WaitForExitWithCancellationAsync(cancellationToken);
    }

    private static MovieCharacter? ResolveCharacter(SubtitleSegment seg, DubbingJob job)
    {
        if (seg.CharacterId.HasValue)
        {
            var match = job.Characters.FirstOrDefault(c => c.Id == seg.CharacterId.Value);
            if (match != null)
                return match;
        }

        if (!string.IsNullOrWhiteSpace(seg.SpeakerName))
        {
            var match = job.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, seg.SpeakerName, StringComparison.OrdinalIgnoreCase)
            );
            if (match != null)
                return match;
        }

        return null;
    }

    private static async Task RemuxVideoAsync(
        string ffmpegPath,
        string inputVideoPath,
        string vocalTrackPath,
        string bgmTrackPath,
        string outputVideoPath,
        double vocalVolume,
        double bgmVolume,
        bool enableDynamicDucking,
        bool enableLoudnessNormalization,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(vocalTrackPath))
            throw new FileNotFoundException(
                "Synthesized vocal track was not created.",
                vocalTrackPath
            );

        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.ArgumentList.Add("-y");

        // Input 0: Original video
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(inputVideoPath);

        // Input 1: Khmer Vocal track
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(vocalTrackPath);

        bool hasBgm = File.Exists(bgmTrackPath) && new FileInfo(bgmTrackPath).Length > 1024;
        if (hasBgm)
        {
            // Input 2: Background Music & Action SFX track
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(bgmTrackPath);

            var vVol = vocalVolume.ToString("0.00", CultureInfo.InvariantCulture);
            var bVol = bgmVolume.ToString("0.00", CultureInfo.InvariantCulture);

            string filter;
            if (enableDynamicDucking)
            {
                // Dynamic sidechain compression:
                // Dialogue dynamically lowers background SFX/Music during speech,
                // and action sounds (explosions, gunshots, punches) swell back to 100% when speech stops!
                filter =
                    $"[1:a]volume={vVol},asplit=2[sc][vocal];"
                    + $"[2:a]volume={bVol}[bgm_norm];"
                    + $"[bgm_norm][sc]sidechaincompress=threshold=0.08:ratio=4:attack=20:release=350[bgm_ducked];"
                    + $"[vocal][bgm_ducked]amix=inputs=2:duration=first:dropout_transition=2";
            }
            else
            {
                filter =
                    $"[1:a]volume={vVol}[vocal];[2:a]volume={bVol}[bgm];[vocal][bgm]amix=inputs=2:duration=first:dropout_transition=2";
            }

            if (enableLoudnessNormalization)
            {
                filter += "[mix_raw];[mix_raw]loudnorm=I=-16:TP=-1.5:LRA=11[aout]";
            }
            else
            {
                filter += "[aout]";
            }

            process.StartInfo.ArgumentList.Add("-filter_complex");
            process.StartInfo.ArgumentList.Add(filter);
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add("0:v:0");
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add("[aout]");
        }
        else
        {
            if (enableLoudnessNormalization)
            {
                process.StartInfo.ArgumentList.Add("-filter_complex");
                process.StartInfo.ArgumentList.Add("[1:a]loudnorm=I=-16:TP=-1.5:LRA=11[aout]");
                process.StartInfo.ArgumentList.Add("-map");
                process.StartInfo.ArgumentList.Add("0:v:0");
                process.StartInfo.ArgumentList.Add("-map");
                process.StartInfo.ArgumentList.Add("[aout]");
            }
            else
            {
                process.StartInfo.ArgumentList.Add("-map");
                process.StartInfo.ArgumentList.Add("0:v:0");
                process.StartInfo.ArgumentList.Add("-map");
                process.StartInfo.ArgumentList.Add("1:a:0");
            }
        }

        // Lossless video copy - instantaneous and highest visual quality
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("copy");

        // AAC audio encoding for maximum Windows/device compatibility
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("aac");
        process.StartInfo.ArgumentList.Add("-b:a");
        process.StartInfo.ArgumentList.Add("192k");
        process.StartInfo.ArgumentList.Add("-movflags");
        process.StartInfo.ArgumentList.Add("+faststart");
        process.StartInfo.ArgumentList.Add("-shortest");

        process.StartInfo.ArgumentList.Add(outputVideoPath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stderr.AppendLine(e.Data);
        };

        process.Start();
        ChildProcessTracker.Track(process);
        process.BeginErrorReadLine();
        await process.WaitForExitWithCancellationAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputVideoPath))
            throw new InvalidOperationException(
                $"FFmpeg failed to remux dubbed video (Exit code {process.ExitCode}): {stderr}"
            );
    }

    private static string BuildSrt(IEnumerable<SubtitleSegment> segments)
    {
        var sb = new StringBuilder();
        int idx = 1;
        foreach (var seg in segments)
        {
            sb.AppendLine(idx++.ToString());
            sb.AppendLine($"{FormatTime(seg.StartTime)} --> {FormatTime(seg.EndTime)}");
            sb.AppendLine(
                !string.IsNullOrWhiteSpace(seg.KhmerText) ? seg.KhmerText : seg.OriginalText
            );
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatTime(TimeSpan ts) =>
        $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";

    /// <summary>
    /// Scales an audio clip duration to fit precisely within a visual scene timecode window using FFmpeg atempo.
    /// Clamps tempo between 0.70x and 1.60x to avoid unnatural artifacts while ensuring dialogue does not bleed across scene cuts.
    /// </summary>
    public static async Task<bool> ScaleAudioClipDurationAsync(
        string ffmpegPath,
        string inputClipPath,
        string outputClipPath,
        double targetDurationSeconds,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(inputClipPath) || targetDurationSeconds <= 0.2)
            return false;

        var clipDuration = await GetAudioDurationAsync(
            ffmpegPath,
            inputClipPath,
            cancellationToken
        );
        if (clipDuration <= TimeSpan.Zero)
            return false;

        var ratio = clipDuration.TotalSeconds / targetDurationSeconds;
        // If within 3% tolerance, no stretch needed
        if (Math.Abs(ratio - 1.0) < 0.03)
        {
            if (inputClipPath != outputClipPath)
                File.Copy(inputClipPath, outputClipPath, overwrite: true);
            return true;
        }

        // Clamp ratio between 0.70x (slow down) and 1.60x (speed up)
        var speed = Math.Clamp(ratio, 0.70, 1.60);

        string filterStr;
        if (speed >= 0.5 && speed <= 2.0)
        {
            filterStr =
                $"atempo={speed.ToString("0.000", CultureInfo.InvariantCulture)},aformat=sample_rates=44100:channel_layouts=stereo";
        }
        else
        {
            filterStr = "aformat=sample_rates=44100:channel_layouts=stereo";
        }

        using var proc = new Process();
        proc.StartInfo.FileName = ffmpegPath;
        proc.StartInfo.ArgumentList.Add("-y");
        proc.StartInfo.ArgumentList.Add("-i");
        proc.StartInfo.ArgumentList.Add(inputClipPath);
        proc.StartInfo.ArgumentList.Add("-filter:a");
        proc.StartInfo.ArgumentList.Add(filterStr);
        proc.StartInfo.ArgumentList.Add("-ar");
        proc.StartInfo.ArgumentList.Add("44100");
        proc.StartInfo.ArgumentList.Add("-ac");
        proc.StartInfo.ArgumentList.Add("2");
        proc.StartInfo.ArgumentList.Add("-c:a");
        proc.StartInfo.ArgumentList.Add("pcm_s16le");
        proc.StartInfo.ArgumentList.Add(outputClipPath);
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;

        proc.Start();
        ChildProcessTracker.Track(proc);
        await proc.WaitForExitWithCancellationAsync(cancellationToken);

        return proc.ExitCode == 0
            && File.Exists(outputClipPath)
            && new FileInfo(outputClipPath).Length > 1024;
    }
}
