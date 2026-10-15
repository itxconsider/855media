using System.Net;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowProfileLinkTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Headers.Authorization);
            return respond(request);
        }
    }

    [Fact]
    public async Task ResolvesPublicShareRedirectToNumericOwnProfile()
    {
        var handler = new Handler(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers =
                    {
                        Location = new Uri("https://www.facebook.com/people/Fixture/123/"),
                    },
                }
            )
        );
        using var http = new HttpClient(handler);
        using var resolver = new PFlowProfileLinkResolver(http);
        Assert.Equal(
            "123",
            await resolver.ResolveUidAsync("https://www.facebook.com/share/Abc123/")
        );
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("https://outside.test/people/Fixture/123/")]
    [InlineData("http://www.facebook.com/people/Fixture/123/")]
    [InlineData("https://www.facebook.com/share/Abc123/")]
    public async Task ExternalInsecureOrLoopingRedirectCannotProveUid(string target)
    {
        var handler = new Handler(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri(target) },
                }
            )
        );
        using var http = new HttpClient(handler);
        using var resolver = new PFlowProfileLinkResolver(http);
        Assert.Null(await resolver.ResolveUidAsync("https://www.facebook.com/share/Abc123/"));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(
        "<link rel='canonical' href='https://www.facebook.com/profile.php?id=123&amp;ref=profile'>",
        "123"
    )]
    [InlineData(
        "<meta property='og:url' content='https://www.facebook.com/people/Fixture/123/'>",
        "123"
    )]
    [InlineData(
        "<link rel='canonical' href='https://www.facebook.com/123'><meta property='og:url' content='https://www.facebook.com/456'>",
        null
    )]
    [InlineData("<link rel='canonical' href='https://outside.test/123'>", null)]
    [InlineData("<a href='https://www.facebook.com/123'>Unrelated feed link</a>", null)]
    [InlineData("<form>Log in</form>", null)]
    public async Task OnlyUnambiguousFacebookCanonicalIdentityIsAccepted(
        string html,
        string? expected
    )
    {
        var handler = new Handler(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }
            )
        );
        using var http = new HttpClient(handler);
        using var resolver = new PFlowProfileLinkResolver(http);
        Assert.Equal(
            expected,
            await resolver.ResolveUidAsync("https://www.facebook.com/share/Abc123/")
        );
    }

    [Fact]
    public async Task DirectNumericLinkNeedsNoNetworkAndCancellationIsHonored()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("No request expected"));
        using var http = new HttpClient(handler);
        using var resolver = new PFlowProfileLinkResolver(http);
        Assert.Equal(
            "123",
            await resolver.ResolveUidAsync("https://www.facebook.com/profile.php?id=123")
        );
        Assert.Equal(0, handler.Calls);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveUidAsync("https://www.facebook.com/123", cancellation.Token)
        );
    }

    [Theory]
    [InlineData(
        "<meta property='al:android:url' content='fb://profile/123'><meta property='al:ios:url' content='fb://profile/123'>",
        "123"
    )]
    [InlineData(
        "<meta property='al:android:url' content='fb://profile/123'><meta property='al:ios:url' content='fb://profile/456'>",
        null
    )]
    [InlineData("<meta property='al:android:url' content='fb://page/123'>", null)]
    [InlineData("<script>var unrelated_profile_id = 123;</script>", null)]
    public async Task UsernameCanonicalRequiresUnambiguousPersonalProfileMetadata(
        string metadata,
        string? expected
    )
    {
        var html =
            "<html><head><link rel='canonical' href='https://www.facebook.com/fixture.username/'><meta property='og:url' content='https://www.facebook.com/fixture.username/'><meta property='og:type' content='video.other'>"
            + metadata
            + "</head></html>";
        var handler = new Handler(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }
            )
        );
        using var http = new HttpClient(handler);
        using var resolver = new PFlowProfileLinkResolver(http);
        Assert.Equal(
            expected,
            await resolver.ResolveUidAsync("https://www.facebook.com/fixture.username")
        );
    }

    [Theory]
    [InlineData("<meta property='al:android:url' content='fb://profile/123'>")]
    [InlineData(
        "<link rel='canonical' href='https://www.facebook.com/somebody.else'><meta property='al:android:url' content='fb://profile/123'>"
    )]
    [InlineData(
        "<link rel='canonical' href='https://www.facebook.com/123'><meta property='al:android:url' content='fb://profile/456'>"
    )]
    public async Task MissingOrMismatchedCanonicalCannotProveUsernameUid(string html)
    {
        var handler = new Handler(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }
            )
        );
        using var http = new HttpClient(handler);
        using var resolver = new PFlowProfileLinkResolver(http);
        Assert.Null(await resolver.ResolveUidAsync("https://www.facebook.com/fixture.username"));
    }

    [Theory]
    [InlineData("https://www.facebook.com/fixture.username", true)]
    [InlineData("https://www.facebook.com/fixtureusername/", true)]
    [InlineData("https://www.facebook.com/login", false)]
    [InlineData("https://www.facebook.com/checkpoint", false)]
    [InlineData("https://www.facebook.com/watch", false)]
    [InlineData("https://www.facebook.com/reel/123", false)]
    [InlineData("https://www.facebook.com/groups/123", false)]
    [InlineData("https://www.facebook.com@outside.test/fixture", false)]
    public void UsernameLinkValidationRejectsNonProfileRoutes(string link, bool expected) =>
        Assert.Equal(expected, PFlowProfileLinkResolver.IsSupportedLink(link));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClipboardResetRequiresMarkerReadback(bool resetWorks)
    {
        var clipboard = "https://www.facebook.com/profile.php?id=456";
        var methods = new List<string>();
        var handler = new Handler(async request =>
        {
            Assert.Equal("http://127.0.0.1:7947/jsonrpc/0", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var method = json.RootElement.GetProperty("method").GetString()!;
            methods.Add(method);
            if (method == "setClipboard" && resetWorks)
                clipboard = json.RootElement.GetProperty("params")[1].GetString()!;
            var result = method == "getClipboard" ? clipboard : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { result })),
            };
        });
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        Assert.Equal(resetWorks, await client.PrepareProfileLinkClipboardAsync(35));
        Assert.Equal(new[] { "setClipboard", "getClipboard" }, methods);
    }

    [Theory]
    [InlineData("https://www.facebook.com/share/Abc123/", true)]
    [InlineData("https://www.facebook.com/profile.php?id=123", true)]
    [InlineData("https://www.facebook.com/fixture.username", true)]
    [InlineData("fixture-password", false)]
    [InlineData("pflow-profile-proof-marker", false)]
    [InlineData("https://outside.test/123", false)]
    public async Task ClipboardGetterReturnsOnlySupportedPublicFacebookLinks(
        string clipboard,
        bool expected
    )
    {
        var handler = new Handler(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { result = clipboard })
                    ),
                }
            )
        );
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        Assert.Equal(expected ? clipboard : null, await client.ReadCopiedProfileLinkAsync(35));
    }
}
