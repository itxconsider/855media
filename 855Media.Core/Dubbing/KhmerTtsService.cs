using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Dubbing;

public class KhmerTtsService
{
    private static readonly HttpClient HttpClient = new();

    // Updated Microsoft Edge-TTS Trusted Client Token & Endpoint
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string EdgeWssUrl =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken="
        + TrustedClientToken;

    public async Task SynthesizeKhmerSpeechAsync(
        string text,
        string outputMp3Path,
        string voiceName = "km-KH-PisethNeural",
        string rate = "+15%",
        string pitch = "+0Hz",
        string volume = "+0%",
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var dir = Path.GetDirectoryName(outputMp3Path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        // 0. If Google Khmer Voice was selected explicitly, synthesize via Google TTS
        if (voiceName.Contains("google", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await SynthesizeViaGoogleTtsAsync(text, outputMp3Path, cancellationToken);
                if (File.Exists(outputMp3Path) && new FileInfo(outputMp3Path).Length > 100)
                    return;
            }
            catch
            {
                // Fall back to Microsoft Edge-TTS
                voiceName = "km-KH-SreymomNeural";
            }
        }

        // 1. Try local Edge-TTS CLI runner first (100% stable with exact gender voice)
        try
        {
            if (
                await TrySynthesizeViaEdgeTtsCliAsync(
                    text,
                    outputMp3Path,
                    voiceName,
                    rate,
                    pitch,
                    volume,
                    cancellationToken
                )
            )
            {
                if (File.Exists(outputMp3Path) && new FileInfo(outputMp3Path).Length > 100)
                    return;
            }
        }
        catch
        {
            // Fall through to native WebSocket
        }

        // 2. Try native C# Microsoft Edge-TTS WebSocket
        try
        {
            await SynthesizeViaEdgeTtsAsync(
                text,
                outputMp3Path,
                voiceName,
                rate,
                pitch,
                volume,
                cancellationToken
            );
            if (File.Exists(outputMp3Path) && new FileInfo(outputMp3Path).Length > 100)
                return;
        }
        catch
        {
            // Fallback to Google TTS below
        }

        // 3. Fallback: Google Translate Khmer TTS (Note: Google Khmer only has a female voice)
        await SynthesizeViaGoogleTtsAsync(text, outputMp3Path, cancellationToken);
    }

    private static string? FindEdgeTtsExecutable()
    {
        var candidates = new[]
        {
            @"C:\Applio-3.6.4\env\Scripts\edge-tts.exe",
            Path.Combine(AppContext.BaseDirectory, "tools", "edge-tts", "edge-tts.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "edge-tts.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }

    private static string? FindPythonWithEdgeTts()
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

    private static async Task<bool> TrySynthesizeViaEdgeTtsCliAsync(
        string text,
        string outputMp3Path,
        string voiceName,
        string rate,
        string pitch,
        string volume,
        CancellationToken cancellationToken
    )
    {
        var exe = FindEdgeTtsExecutable();
        var py = FindPythonWithEdgeTts();
        if (exe == null && py == null)
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe ?? py!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var rateArg = string.IsNullOrWhiteSpace(rate) ? "+15%" : rate;
        var pitchArg = string.IsNullOrWhiteSpace(pitch) ? "+0Hz" : pitch;
        var volArg = string.IsNullOrWhiteSpace(volume) ? "+0%" : volume;

        if (exe != null)
        {
            psi.ArgumentList.Add("--voice");
            psi.ArgumentList.Add(voiceName);
            psi.ArgumentList.Add("--rate");
            psi.ArgumentList.Add(rateArg);
            if (pitchArg != "+0Hz")
            {
                psi.ArgumentList.Add("--pitch");
                psi.ArgumentList.Add(pitchArg);
            }
            if (volArg != "+0%")
            {
                psi.ArgumentList.Add("--volume");
                psi.ArgumentList.Add(volArg);
            }
            psi.ArgumentList.Add("--text");
            psi.ArgumentList.Add(text);
            psi.ArgumentList.Add("--write-media");
            psi.ArgumentList.Add(outputMp3Path);
        }
        else
        {
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("edge_tts");
            psi.ArgumentList.Add("--voice");
            psi.ArgumentList.Add(voiceName);
            psi.ArgumentList.Add("--rate");
            psi.ArgumentList.Add(rateArg);
            if (pitchArg != "+0Hz")
            {
                psi.ArgumentList.Add("--pitch");
                psi.ArgumentList.Add(pitchArg);
            }
            if (volArg != "+0%")
            {
                psi.ArgumentList.Add("--volume");
                psi.ArgumentList.Add(volArg);
            }
            psi.ArgumentList.Add("--text");
            psi.ArgumentList.Add(text);
            psi.ArgumentList.Add("--write-media");
            psi.ArgumentList.Add(outputMp3Path);
        }

        using var proc = Process.Start(psi);
        if (proc == null)
            return false;

        ChildProcessTracker.Track(proc);
        await proc.WaitForExitWithCancellationAsync(cancellationToken);
        return proc.ExitCode == 0
            && File.Exists(outputMp3Path)
            && new FileInfo(outputMp3Path).Length > 100;
    }

    private static async Task SynthesizeViaEdgeTtsAsync(
        string text,
        string outputMp3Path,
        string voiceName,
        string rate,
        string pitch,
        string volume,
        CancellationToken cancellationToken
    )
    {
        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0"
        );
        client.Options.SetRequestHeader(
            "Origin",
            "chrome-extension://jdiccldimpdaibmpdkgikmbggipbgagd"
        );
        client.Options.SetRequestHeader("Pragma", "no-cache");
        client.Options.SetRequestHeader("Cache-Control", "no-cache");
        client.Options.SetRequestHeader("Cookie", $"muid={Guid.NewGuid():N};");

        // Compute Sec-MS-GEC header for Microsoft Edge authentication
        var fileTime = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11644473600L) * 10000000L;
        var roundedFileTime = fileTime - (fileTime % 3000000000L);
        var hashInput = $"{roundedFileTime}{TrustedClientToken}";
        var hashBytes = SHA256.HashData(Encoding.ASCII.GetBytes(hashInput));
        var secMsGec = Convert.ToHexString(hashBytes);

        client.Options.SetRequestHeader("Sec-MS-GEC", secMsGec);
        client.Options.SetRequestHeader("Sec-MS-GEC-Version", "1-130.0.2849.68");

        var connectionId = Guid.NewGuid().ToString("N");
        var uri = new Uri($"{EdgeWssUrl}&ConnectionId={connectionId}");

        await client.ConnectAsync(uri, cancellationToken);

        // Send Speech Config
        var configMessage =
            "Content-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n"
            + "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";

        await client.SendAsync(
            Encoding.UTF8.GetBytes(configMessage),
            WebSocketMessageType.Text,
            true,
            cancellationToken
        );

        // Send SSML Request
        var requestId = Guid.NewGuid().ToString("N");
        var escapedText = System.Security.SecurityElement.Escape(text);
        var rateStr = string.IsNullOrWhiteSpace(rate) ? "+15%" : rate;
        var pitchStr = string.IsNullOrWhiteSpace(pitch) ? "+0Hz" : pitch;
        var volStr = string.IsNullOrWhiteSpace(volume) ? "+0%" : volume;
        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='km-KH'>"
            + $"<voice name='{voiceName}'><prosody pitch='{pitchStr}' rate='{rateStr}' volume='{volStr}'>{escapedText}</prosody></voice></speak>";

        var ssmlMessage =
            $"X-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nPath:ssml\r\n\r\n{ssml}";

        await client.SendAsync(
            Encoding.UTF8.GetBytes(ssmlMessage),
            WebSocketMessageType.Text,
            true,
            cancellationToken
        );

        using var fileStream = File.Create(outputMp3Path);
        var buffer = new byte[8192];

        while (client.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await client.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken
            );
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType == WebSocketMessageType.Binary && result.Count > 2)
            {
                var headerLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0, 2));
                var audioStart = 2 + headerLength;
                if (result.Count > audioStart)
                {
                    await fileStream.WriteAsync(
                        buffer.AsMemory(audioStart, result.Count - audioStart),
                        cancellationToken
                    );
                }
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var textMsg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (textMsg.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        await client.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "Complete",
            cancellationToken
        );
    }

    private static async Task SynthesizeViaGoogleTtsAsync(
        string text,
        string outputMp3Path,
        CancellationToken cancellationToken
    )
    {
        var encodedText = Uri.EscapeDataString(text);
        var url =
            $"https://translate.google.com/translate_tts?ie=UTF-8&tl=km&client=tw-ob&q={encodedText}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(outputMp3Path);
        await contentStream.CopyToAsync(fileStream, cancellationToken);
    }
}
