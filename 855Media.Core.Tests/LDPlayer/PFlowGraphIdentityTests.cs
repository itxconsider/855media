using System.Net;
using System.Reflection;
using System.Text;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowGraphIdentityTests
{
    [Fact]
    public async Task OperationStartRequestsOnlyIdentityFieldsAndStillChecksSessionStability()
    {
        var (ld, device) = Fake();
        var reads = 0;
        using var http = new HttpClient(
            new Handler(request =>
            {
                reads++;
                Assert.Equal("?fields=id,name", request.RequestUri!.Query);
                Assert.DoesNotContain(FakeToken, request.RequestUri.ToString());
                return Response(HttpStatusCode.OK, $$"""{"id":"{{Owner}}","name":"Fixture"}""");
            })
        );
        var identity = await new PFlowGraphIdentityReader(http).TryReadUidAsync(
            ld,
            12,
            null,
            default
        );
        Assert.NotNull(identity);
        Assert.Equal(Owner, identity.Id);
        Assert.Equal(Owner, identity.UnderlyingUid);
        Assert.Null(identity.PictureUrl);
        Assert.Equal(1, reads);
        Assert.Equal(2, device.AuthReads);
    }

    [Fact]
    public void CachedPictureFallbackAcceptsOnlyTheNamedSecurePictureField()
    {
        Assert.Equal(
            "https://example.test/picture.jpg",
            LDPlayerService.ReadRootProfilePicture(
                "profile_pic_url\0https://example.test/picture.jpg\0"
            )
        );
        Assert.Empty(
            LDPlayerService.ReadRootProfilePicture("website\nhttps://example.test/picture.jpg")
        );
        Assert.Empty(
            LDPlayerService.ReadRootProfilePicture(
                "profile_pic_url\nhttp://example.test/picture.jpg"
            )
        );
    }

    const string Owner = "61594257643120";
    const string FakeToken = "EAAfixtureTokenOnly123456789";

    static (ILDPlayerService Ld, Device Device) Fake()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    [Fact]
    public async Task ReadsFreshIdentityAndPictureWithoutUsingUnverifiedCreationFields()
    {
        var (ld, device) = Fake();
        using var http = new HttpClient(
            new Handler(request =>
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(FakeToken, request.Headers.Authorization?.Parameter);
                Assert.DoesNotContain(FakeToken, request.RequestUri!.ToString());
                return Response(
                    HttpStatusCode.OK,
                    """{"id":"61594257643120","name":"Example person","picture":{"data":{"url":"https://example.test/avatar.jpg"}},"created_time":"2020-01-01","founded":"1990"}"""
                );
            })
        );
        var result = await new PFlowGraphIdentityReader(http).TryReadAsync(
            ld,
            12,
            "fixture",
            default
        );
        Assert.NotNull(result);
        Assert.Equal(Owner, result.Id);
        Assert.Equal(Owner, result.UnderlyingUid);
        Assert.Equal("https://example.test/avatar.jpg", result.PictureUrl);
        Assert.Null(result.PageCreationDate);
        Assert.Equal(2, device.AuthReads);
    }

    [Fact]
    public async Task DeniedPictureRetriesIdentityOnly()
    {
        var (ld, _) = Fake();
        var requests = 0;
        using var http = new HttpClient(
            new Handler(request =>
            {
                requests++;
                return request.RequestUri!.Query.Contains("picture")
                    ? Response(HttpStatusCode.BadRequest, "{\"error\":{\"code\":100}}")
                    : Response(
                        HttpStatusCode.OK,
                        $$"""{"id":"{{Owner}}","name":"Example person"}"""
                    );
            })
        );
        var result = await new PFlowGraphIdentityReader(http).TryReadAsync(ld, 12, null, default);
        Assert.NotNull(result);
        Assert.Null(result.PictureUrl);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("no-token")]
    [InlineData("changed-session")]
    [InlineData("api-error")]
    [InlineData("malformed-json")]
    public async Task FailedApiEvidenceReturnsNullForNativeFallback(string failure)
    {
        var (ld, device) = Fake();
        device.NoToken = failure == "no-token";
        device.ChangeSession = failure == "changed-session";
        using var http = new HttpClient(
            new Handler(_ =>
                failure switch
                {
                    "api-error" => Response(
                        HttpStatusCode.Unauthorized,
                        "{\"error\":{\"code\":190}}"
                    ),
                    "malformed-json" => Response(HttpStatusCode.OK, "broken"),
                    _ => Response(
                        HttpStatusCode.OK,
                        $$"""{"id":"{{Owner}}","name":"Example person"}"""
                    ),
                }
            )
        );
        Assert.Null(await new PFlowGraphIdentityReader(http).TryReadAsync(ld, 12, null, default));
    }

    [Fact]
    public async Task PageIdentityIsKeptDistinctFromUnderlyingPersonalUid()
    {
        var (ld, _) = Fake();
        using var http = new HttpClient(
            new Handler(_ =>
                Response(HttpStatusCode.OK, "{\"id\":\"61594999402903\",\"name\":\"Example Page\"}")
            )
        );
        var result = await new PFlowGraphIdentityReader(http).TryReadAsync(ld, 12, null, default);
        Assert.NotNull(result);
        Assert.NotEqual(result.Id, result.UnderlyingUid);
    }

    static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromResult(respond(request));
    }

    public class Device : DispatchProxy
    {
        public bool NoToken,
            ChangeSession;
        public int AuthReads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "ExecuteAdbAsync")
                throw new NotSupportedException();
            var command = (string)args![1]!;
            if (command.EndsWith("authentication"))
            {
                AuthReads++;
                var token = ChangeSession && AuthReads > 1 ? FakeToken + "Changed" : FakeToken;
                return Task.FromResult(
                    NoToken ? "" : "access_token\0" + token + "\0\ufffd\nuser_id\n" + Owner
                );
            }
            return Task.FromResult("underlying_account_uid\0" + Owner);
        }
    }
}
