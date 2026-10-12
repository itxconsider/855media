using System.Net;
using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AtxTouchGestureTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public List<int[]> Events = new();
        public Func<int, CancellationToken, HttpResponseMessage>? Reply;
        public long Now;
        public int OverheadMs;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Assert.Equal("http://127.0.0.1:7914/jsonrpc/0", request.RequestUri!.ToString());
            Assert.NotNull(request.Content!.Headers.ContentLength);
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            Assert.Equal("injectInputEvent", json.RootElement.GetProperty("method").GetString());
            Events.Add(
                json.RootElement.GetProperty("params")
                    .EnumerateArray()
                    .Select(v => v.GetInt32())
                    .ToArray()
            );
            Now += OverheadMs;
            return Reply?.Invoke(Events.Count, ct) ?? Response("{\"result\":true}");
        }
    }

    private static HttpResponseMessage Response(
        string body,
        HttpStatusCode status = HttpStatusCode.OK
    ) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(300, 100)]
    [InlineData(100, 300)]
    public async Task ExactHoldFourMovesAndRelease(int hold, int move)
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        var delays = new List<int>();
        Assert.True(
            await client.HoldAndSwipeAsync(
                2,
                360,
                960,
                360,
                213,
                hold,
                move,
                (duration, ct) =>
                {
                    delays.Add((int)duration.TotalMilliseconds);
                    handler.Now += (long)duration.TotalMilliseconds;
                    return Task.CompletedTask;
                },
                () => handler.Now
            )
        );
        Assert.Equal(new[] { 0, 2, 2, 2, 2, 1 }, handler.Events.Select(v => v[0]));
        Assert.Equal(new[] { 960, 774, 587, 400, 213, 213 }, handler.Events.Select(v => v[2]));
        Assert.All(
            handler.Events,
            v =>
            {
                Assert.Equal(360, v[1]);
                Assert.Equal(0, v[3]);
            }
        );
        Assert.Equal(new[] { hold, move / 4, move / 4, move / 4, move / 4 }, delays);
    }

    [Fact]
    public async Task TransportOverheadDoesNotAddAnotherFullDelayPerMove()
    {
        var handler = new Handler { OverheadMs = 50 };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        var delays = new List<int>();
        Assert.True(
            await client.HoldAndSwipeAsync(
                2,
                360,
                960,
                360,
                213,
                300,
                100,
                (duration, ct) =>
                {
                    delays.Add((int)duration.TotalMilliseconds);
                    handler.Now += (long)duration.TotalMilliseconds;
                    return Task.CompletedTask;
                },
                () => handler.Now
            )
        );
        Assert.Equal(new[] { 300, 25 }, delays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task RejectedEventReturnsFalseAndAlwaysAttemptsRelease(int rejectedEvent)
    {
        var handler = new Handler
        {
            Reply = (number, ct) =>
                Response(number == rejectedEvent ? "{\"result\":false}" : "{\"result\":true}"),
        };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        Assert.False(
            await client.HoldAndSwipeAsync(
                2,
                360,
                960,
                360,
                213,
                300,
                100,
                (duration, ct) =>
                {
                    handler.Now += (long)duration.TotalMilliseconds;
                    return Task.CompletedTask;
                },
                () => handler.Now
            )
        );
        Assert.Equal(1, handler.Events.Last()[0]);
        Assert.Equal(handler.Events[^2][1..3], handler.Events[^1][1..3]);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":-1},\"result\":true}")]
    [InlineData("{\"result\":null}")]
    [InlineData("{\"result\":\"true\"}")]
    [InlineData("not json")]
    public async Task InvalidAcknowledgementReleasesTouch(string body)
    {
        var handler = new Handler
        {
            Reply = (number, ct) => Response(number == 1 ? body : "{\"result\":true}"),
        };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        Assert.False(await client.HoldAndSwipeAsync(2, 360, 960, 360, 213, 300, 100));
        Assert.Equal(new[] { 0, 1 }, handler.Events.Select(v => v[0]));
    }

    [Fact]
    public async Task NetworkFailureStillAttemptsRelease()
    {
        var handler = new Handler
        {
            Reply = (number, ct) =>
                number == 1
                    ? throw new HttpRequestException("unavailable")
                    : Response("{\"result\":true}"),
        };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        Assert.False(await client.HoldAndSwipeAsync(2, 360, 960, 360, 213, 300, 100));
        Assert.Equal(new[] { 0, 1 }, handler.Events.Select(v => v[0]));
    }

    [Fact]
    public async Task CancellationDuringHoldReleasesWithIndependentToken()
    {
        using var cancelled = new CancellationTokenSource();
        var handler = new Handler
        {
            Reply = (number, ct) =>
            {
                Assert.False(ct.IsCancellationRequested);
                return Response("{\"result\":true}");
            },
        };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.HoldAndSwipeAsync(
                2,
                360,
                960,
                360,
                213,
                300,
                100,
                (duration, ct) =>
                {
                    cancelled.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                () => 0,
                cancelled.Token
            )
        );
        Assert.Equal(new[] { 0, 1 }, handler.Events.Select(v => v[0]));
    }

    [Fact]
    public async Task AlreadyCancelledSendsNoTouch()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.HoldAndSwipeAsync(2, 360, 960, 360, 213, 300, 100, new CancellationToken(true))
        );
        Assert.Empty(handler.Events);
    }

    [Fact]
    public async Task CancellationAfterMoveReleasesAtLastAttemptedPoint()
    {
        using var cancelled = new CancellationTokenSource();
        var handler = new Handler
        {
            Reply = (number, ct) =>
            {
                if (number == 2)
                    cancelled.Cancel();
                if (number == 3)
                    Assert.False(ct.IsCancellationRequested);
                return Response("{\"result\":true}");
            },
        };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.HoldAndSwipeAsync(
                2,
                360,
                960,
                360,
                213,
                300,
                100,
                (duration, ct) =>
                {
                    handler.Now += (long)duration.TotalMilliseconds;
                    return Task.CompletedTask;
                },
                () => handler.Now,
                cancelled.Token
            )
        );
        Assert.Equal(new[] { 0, 2, 1 }, handler.Events.Select(v => v[0]));
        Assert.Equal(774, handler.Events.Last()[2]);
    }

    [Fact]
    public async Task HttpFailureIsNotSuccessfulInput()
    {
        var handler = new Handler
        {
            Reply = (number, ct) =>
                Response(
                    "{\"result\":true}",
                    number == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK
                ),
        };
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        Assert.False(await client.HoldAndSwipeAsync(2, 360, 960, 360, 213, 300, 100));
        Assert.Equal(new[] { 0, 1 }, handler.Events.Select(v => v[0]));
    }
}
