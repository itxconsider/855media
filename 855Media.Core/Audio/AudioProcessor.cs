using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Audio;

public enum AudioProcessingMode
{
    None,
    IsolateSpeech,
    RemoveVocals,
}

public static class AudioProcessor
{
    public static string GetSpeechIsolationFilter()
    {
        // Stereo center extraction (dialogue is typically panned to center: c0=0.5*c0+0.5*c1, c1=0.5*c0+0.5*c1)
        // High-pass filter at 120Hz (cuts deep bass/drums)
        // Low-pass filter at 3400Hz (cuts high synth noise)
        // Speech band boost around 1000Hz with noise reduction
        return "pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1, highpass=f=120, lowpass=f=3400, equalizer=f=1000:width_type=h:width=2000:g=4, afftdn";
    }

    public static string GetKaraokeFilter()
    {
        // Center channel vocal cancellation (L - R phase cancellation) + vocal frequency notch filter
        return "pan=stereo|c0=c0-c1|c1=c1-c0, equalizer=f=1500:width_type=h:width=1200:g=-12";
    }

    public static async Task ProcessAudioAsync(
        string ffmpegPath,
        string inputFilePath,
        AudioProcessingMode mode,
        CancellationToken cancellationToken = default
    )
    {
        if (mode == AudioProcessingMode.None || !File.Exists(inputFilePath))
            return;

        var filter = mode switch
        {
            AudioProcessingMode.IsolateSpeech => GetSpeechIsolationFilter(),
            AudioProcessingMode.RemoveVocals => GetKaraokeFilter(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(filter))
            return;

        var tempOutput = inputFilePath + ".audio_processed.mp4";

        try
        {
            await FileUtils.TryDeleteWithRetryAsync(
                tempOutput,
                cancellationToken: cancellationToken
            );

            using var process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(inputFilePath);
            process.StartInfo.ArgumentList.Add("-c:v");
            process.StartInfo.ArgumentList.Add("copy");
            process.StartInfo.ArgumentList.Add("-af");
            process.StartInfo.ArgumentList.Add(filter);
            process.StartInfo.ArgumentList.Add(tempOutput);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            ChildProcessTracker.Track(process);
            await process.WaitForExitAsync(cancellationToken);

            if (
                process.ExitCode == 0
                && File.Exists(tempOutput)
                && new FileInfo(tempOutput).Length > 0
            )
            {
                await FileUtils.ReplaceFileWithRetryAsync(
                    tempOutput,
                    inputFilePath,
                    cancellationToken
                );
            }
        }
        catch
        {
            // Audio processing failed; preserve original file
        }
        finally
        {
            await FileUtils.TryDeleteWithRetryAsync(
                tempOutput,
                cancellationToken: cancellationToken
            );
        }
    }
}
