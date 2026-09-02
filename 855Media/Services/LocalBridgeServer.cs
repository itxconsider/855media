using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Services;

public class MediaPayloadItem
{
    public string Url { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class MediaPayload
{
    public string PageUrl { get; set; } = string.Empty;
    public List<MediaPayloadItem> Items { get; set; } = [];
}

public class LocalBridgeServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public event EventHandler<MediaPayload>? MediaPayloadReceived;

    public bool IsRunning => _isRunning;

    public void Start(int port = 5855)
    {
        if (_isRunning)
            return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _isRunning = true;

            Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch
        {
            // Port might be in use or missing HTTP listener permissions
            _isRunning = false;
        }
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // Ignore listener close exception
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (_isRunning && !token.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), token);
            }
            catch
            {
                if (token.IsCancellationRequested)
                    break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Enable CORS for browser extension requests
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.Close();
            return;
        }

        if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/api/receive-media")
        {
            try
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var json = await reader.ReadToEndAsync();
                var payload = JsonSerializer.Deserialize<MediaPayload>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload is not null && payload.Items.Count > 0)
                {
                    MediaPayloadReceived?.Invoke(this, payload);
                }

                byte[] buffer = Encoding.UTF8.GetBytes("{\"ok\":true}");
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
            }
            catch (Exception ex)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errBuffer = Encoding.UTF8.GetBytes(
                    $"{{\"ok\":false, \"error\":\"{ex.Message}\"}}"
                );
                await response.OutputStream.WriteAsync(errBuffer);
            }
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
        }

        response.Close();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
