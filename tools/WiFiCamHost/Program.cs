using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using QRCoder;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using WiFiCamHost;

var builder = WebApplication.CreateBuilder(args);

string localIp = GetLocalIPv4Address();
int port = 8080;
string httpsUrl = $"https://{localIp}:{port}";

// Configure Kestrel with self-signed certificate supporting IP SAN for iOS Safari
var serverCertificate = GetOrGenerateSelfSignedCertificate(localIp);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(
        IPAddress.Any,
        port,
        listenOptions =>
        {
            listenOptions.UseHttps(serverCertificate);
        }
    );
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

// Serve the Root Certificate directly for iOS Safari installation
app.MapGet(
    "/cert",
    () =>
    {
        var certBytes = serverCertificate.Export(X509ContentType.Cert);
        return Results.File(certBytes, "application/x-x509-ca-cert", "WiFiCamRoot.crt");
    }
);

PrintWelcomeAndQrCode(httpsUrl);

using var vCamBridge = new VirtualCameraBridge(1920, 1080);

app.Map(
    "/ws",
    async context =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        Console.WriteLine("[WS] iPhone client connected.");

        var pc = new RTCPeerConnection(new RTCConfiguration());

        var videoFormat = new VideoFormat(VideoCodecsEnum.VP8, 96);
        var videoTrack = new MediaStreamTrack(videoFormat, MediaStreamStatusEnum.RecvOnly);
        pc.addTrack(videoTrack);

        var vp8Codec = new SIPSorceryMedia.Encoders.Codecs.Vp8Codec();
        vp8Codec.InitialiseDecoder();

        pc.OnVideoFormatsNegotiated += (formats) =>
        {
            if (formats != null && formats.Count > 0)
            {
                Console.WriteLine($"[WebRTC] Video format negotiated: {formats[0].FormatName}");
            }
        };

        pc.OnVideoFrameReceived += (
            IPEndPoint remoteEP,
            uint timestamp,
            byte[] payload,
            VideoFormat format
        ) =>
        {
            try
            {
                uint width = 0;
                uint height = 0;
                var decodedFrames = vp8Codec.Decode(payload, payload.Length, out width, out height);
                if (decodedFrames != null && width > 0 && height > 0)
                {
                    foreach (var i420Buffer in decodedFrames)
                    {
                        if (i420Buffer != null && i420Buffer.Length > 0)
                        {
                            vCamBridge.PushI420Frame(i420Buffer, (int)width, (int)height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebRTC] Frame decode error: {ex.Message}");
            }
        };

        pc.onicecandidate += async (candidate) =>
        {
            if (candidate != null && webSocket.State == WebSocketState.Open)
            {
                var json = JsonSerializer.Serialize(
                    new
                    {
                        type = "candidate",
                        candidate = new
                        {
                            candidate = candidate.candidate,
                            sdpMid = candidate.sdpMid,
                            sdpMLineIndex = candidate.sdpMLineIndex,
                        },
                    }
                );
                await SendWsAsync(webSocket, json);
            }
        };

        var buffer = new byte[1024 * 16];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None
                );
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString();
                    if (type == "offer")
                    {
                        var sdp = root.GetProperty("sdp").GetString()!;
                        pc.setRemoteDescription(
                            new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp }
                        );

                        var answer = pc.createAnswer();
                        await pc.setLocalDescription(answer);

                        var answerJson = JsonSerializer.Serialize(
                            new { type = "answer", sdp = answer.sdp }
                        );
                        await SendWsAsync(webSocket, answerJson);
                        Console.WriteLine("[WebRTC] Answer generated and dispatched.");
                    }
                    else if (type == "candidate")
                    {
                        var cand = root.GetProperty("candidate");
                        pc.addIceCandidate(
                            new RTCIceCandidateInit
                            {
                                candidate = cand.GetProperty("candidate").GetString(),
                                sdpMid = cand.TryGetProperty("sdpMid", out var mid)
                                    ? mid.GetString()
                                    : null,
                                sdpMLineIndex = cand.TryGetProperty("sdpMLineIndex", out var idx)
                                    ? (ushort)idx.GetInt32()
                                    : (ushort)0,
                            }
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS] Session terminated: {ex.Message}");
        }
        finally
        {
            pc.Close("Client disconnected");
            Console.WriteLine("[WS] iPhone client disconnected.");
        }
    }
);

await app.RunAsync();

static async Task SendWsAsync(WebSocket ws, string message)
{
    var bytes = Encoding.UTF8.GetBytes(message);
    await ws.SendAsync(
        new ArraySegment<byte>(bytes),
        WebSocketMessageType.Text,
        true,
        CancellationToken.None
    );
}

static string GetLocalIPv4Address()
{
    // First try: active Wi-Fi or Ethernet interfaces with non-APIPA (not 169.254.*) address
    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (
            ni.OperationalStatus == OperationalStatus.Up
            && (
                ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet
            )
        )
        {
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (
                    ip.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(ip.Address)
                )
                {
                    string str = ip.Address.ToString();
                    if (!str.StartsWith("169.254."))
                    {
                        return str;
                    }
                }
            }
        }
    }

    // Second try: UDP socket probe towards external router to find the exact routing IP
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 65530);
        if (socket.LocalEndPoint is IPEndPoint endPoint && !IPAddress.IsLoopback(endPoint.Address))
        {
            return endPoint.Address.ToString();
        }
    }
    catch
    {
        // Ignore fallback
    }

    return "127.0.0.1";
}

static void PrintWelcomeAndQrCode(string url)
{
    Console.WriteLine("==================================================================");
    Console.WriteLine("  WiFi WebRTC Camera Server Started");
    Console.WriteLine($"  URL: {url}");
    Console.WriteLine($"  Download Root CA directly on iPhone: {url}/cert");
    Console.WriteLine("==================================================================");

    try
    {
        using var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new AsciiQRCode(qrData);
        string asciiQr = qrCode.GetGraphic(1);
        Console.WriteLine(asciiQr);
    }
    catch
    {
        Console.WriteLine($"[!] Open browser on iPhone to: {url}");
    }
}

static X509Certificate2 GetOrGenerateSelfSignedCertificate(string hostIp)
{
    string certPath = Path.Combine(AppContext.BaseDirectory, "wificam_server.pfx");
    if (File.Exists(certPath))
    {
        return X509CertificateLoader.LoadPkcs12FromFile(certPath, "wificam");
    }

    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest(
        $"CN={hostIp}",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1
    );

    var sanBuilder = new SubjectAlternativeNameBuilder();
    if (IPAddress.TryParse(hostIp, out var ipAddress))
    {
        sanBuilder.AddIpAddress(ipAddress);
    }
    sanBuilder.AddDnsName(hostIp);
    sanBuilder.AddDnsName("localhost");
    req.CertificateExtensions.Add(sanBuilder.Build());

    req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    req.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature
                | X509KeyUsageFlags.KeyEncipherment
                | X509KeyUsageFlags.KeyCertSign,
            true
        )
    );

    var cert = req.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(2)
    );
    var pfxBytes = cert.Export(X509ContentType.Pfx, "wificam");
    File.WriteAllBytes(certPath, pfxBytes);
    return X509CertificateLoader.LoadPkcs12(pfxBytes, "wificam", X509KeyStorageFlags.Exportable);
}
