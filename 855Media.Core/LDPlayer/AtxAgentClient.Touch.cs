using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class AtxAgentClient
{
    /// <summary>Sends DOWN, a stationary hold, four scheduled MOVE events, then UP.
    /// Movement is a timing target; awaited device acknowledgements can take longer.</summary>
    public Task<bool> HoldAndSwipeAsync(
        int deviceIndex,
        int startX,
        int startY,
        int endX,
        int endY,
        int holdMs,
        int movementTargetMs,
        CancellationToken cancellationToken = default
    )
    {
        var clock = Stopwatch.StartNew();
        return HoldAndSwipeAsync(
            deviceIndex,
            startX,
            startY,
            endX,
            endY,
            holdMs,
            movementTargetMs,
            Task.Delay,
            () => clock.ElapsedMilliseconds,
            cancellationToken
        );
    }

    internal async Task<bool> HoldAndSwipeAsync(
        int deviceIndex,
        int startX,
        int startY,
        int endX,
        int endY,
        int holdMs,
        int movementTargetMs,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<long> milliseconds,
        CancellationToken cancellationToken = default
    )
    {
        if (startX < 0 || startY < 0 || endX < 0 || endY < 0)
            throw new ArgumentOutOfRangeException(nameof(startX));
        if (holdMs < 0 || holdMs > 60_000)
            throw new ArgumentOutOfRangeException(nameof(holdMs));
        if (movementTargetMs < 1 || movementTargetMs > 60_000)
            throw new ArgumentOutOfRangeException(nameof(movementTargetMs));
        cancellationToken.ThrowIfCancellationRequested();
        var lastX = startX;
        var lastY = startY;
        var downAttempted = false;
        var succeeded = false;
        long movementMeasuredMs = 0;
        try
        {
            // Even a failed HTTP response may have delivered DOWN to the device.
            downAttempted = true;
            if (await InjectTouchAsync(deviceIndex, 0, startX, startY, cancellationToken))
            {
                await delay(TimeSpan.FromMilliseconds(holdMs), cancellationToken);
                var movementStarted = milliseconds();
                succeeded = true;
                for (var step = 1; step <= 4; step++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remaining =
                        movementTargetMs * step / 4 - (milliseconds() - movementStarted);
                    if (remaining > 0)
                        await delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
                    lastX = startX + (endX - startX) * step / 4;
                    lastY = startY + (endY - startY) * step / 4;
                    if (!await InjectTouchAsync(deviceIndex, 2, lastX, lastY, cancellationToken))
                    {
                        succeeded = false;
                        break;
                    }
                }
                movementMeasuredMs = milliseconds() - movementStarted;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            succeeded = false;
            throw;
        }
        catch (Exception ex)
        {
            succeeded = false;
            Debug.WriteLine($"[ATX][LD-{deviceIndex}] Hold/swipe failed: {ex.GetType().Name}");
        }
        finally
        {
            if (downAttempted)
            {
                // Release the touch even when the caller cancelled or an event was rejected.
                using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    if (!await InjectTouchAsync(deviceIndex, 1, lastX, lastY, release.Token))
                        succeeded = false;
                }
                catch (Exception ex)
                {
                    succeeded = false;
                    Debug.WriteLine(
                        $"[ATX][LD-{deviceIndex}] Touch release failed: {ex.GetType().Name}"
                    );
                }
            }
            if (IsTapTracingEnabled)
                _ = PFlowFetchTrace.WriteAsync(
                    deviceIndex,
                    succeeded ? "ui.swipe.sent" : "ui.swipe.failed",
                    $"start=({startX},{startY}); end=({endX},{endY}); holdMs={holdMs}; movementTargetMs={movementTargetMs}; movementMeasuredMs={movementMeasuredMs}"
                );
        }
        return succeeded;
    }

    private async Task<bool> InjectTouchAsync(
        int deviceIndex,
        int action,
        int x,
        int y,
        CancellationToken cancellationToken
    )
    {
        // StringContent supplies a Content-Length; the installed RPC endpoint rejects chunked JSON.
        using var content = new StringContent(
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = Guid.NewGuid().ToString("N"),
                    method = "injectInputEvent",
                    @params = new[] { action, x, y, 0 },
                }
            ),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _httpClient.PostAsync(
            $"{GetDeviceBaseUrl(deviceIndex)}/jsonrpc/0",
            content,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
            return false;
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );
        return !body.RootElement.TryGetProperty("error", out _)
            && body.RootElement.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.True;
    }
}
