using System.Net;
using System.Reflection;
using System.Text;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PagePostDeletionTests
{
    public class Device : DispatchProxy
    {
        public string Owner = "555555555555555";
        public bool ChangeToken;
        public int Reads;
        public string DelegatePageId = Page;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ILDPlayerService.ExecuteAdbAsync))
            {
                var command = (string)args![1]!;
                if (command.Contains("logged_in_"))
                    return Task.FromResult("delegate_page_id\n" + DelegatePageId);
                if (command.EndsWith("underlying_account"))
                    return Task.FromResult("underlying_account_uid\n" + Owner);
                Reads++;
                return Task.FromResult(
                    "access_token\n"
                        + (ChangeToken && Reads > 1 ? "EAAchangedToken123456789" : Secret)
                );
            }
            throw new NotSupportedException();
        }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task PersonaAliasRequiresExactSavedUidDelegationAndCanonicalGraphId(
        bool savedMatches,
        bool cacheMatches,
        bool graphMatches
    )
    {
        const string persona = "61594688834095";
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.DelegatePageId = cacheMatches ? Page : "999";
        var postReads = 0;
        using var http = new HttpClient(
            new Handler(r =>
            {
                Assert.Equal(HttpMethod.Get, r.Method);
                if (r.RequestUri!.AbsolutePath == "/me")
                    return Json(Me(persona));
                if (r.RequestUri.AbsolutePath == "/" + Page)
                    return Json(Me(graphMatches ? Page : "999"));
                postReads++;
                return Json("{\"data\":[" + Item() + "]}");
            })
        );
        var service = PagePostDeletionService.ForDevice(
            ld,
            19,
            "fixture",
            device.Owner,
            savedMatches ? persona : "888",
            http
        );
        if (savedMatches && cacheMatches && graphMatches)
        {
            var preview = await service.PreviewAsync(Page, default);
            Assert.Equal(Page, preview.PageId);
            Assert.Single(preview.Posts);
            Assert.Equal(1, postReads);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PreviewAsync(Page, default)
            );
            Assert.Equal(0, postReads);
        }
    }

    [Fact]
    public async Task ChangedPersonaDelegationBlocksDeletionAfterSuccessfulPreview()
    {
        const string persona = "61594688834095";
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        using var http = new HttpClient(
            new Handler(r =>
            {
                Assert.Equal(HttpMethod.Get, r.Method);
                if (r.RequestUri!.AbsolutePath == "/me")
                    return Json(Me(persona));
                if (r.RequestUri.AbsolutePath == "/" + Page)
                    return Json(Me());
                return Json("{\"data\":[" + Item() + "]}");
            })
        );
        var service = PagePostDeletionService.ForDevice(
            ld,
            19,
            "fixture",
            device.Owner,
            persona,
            http
        );
        var preview = await service.PreviewAsync(Page, default);
        device.DelegatePageId = "999";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(
                preview,
                [Post],
                _ => throw new Xunit.Sdk.XunitException("No audit or mutation expected"),
                default
            )
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeviceRejectsChangedSessionOrWrongUnderlyingOwner(bool changeToken)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.ChangeToken = changeToken;
        var service = PagePostDeletionService.ForDevice(
            ld,
            1,
            "fixture",
            changeToken ? device.Owner : "999"
        );
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreviewAsync(Page, default)
        );
        Assert.DoesNotContain(Secret, error.ToString());
    }

    private const string Page = "123456789012345";
    private const string Secret = "EAAfixtureSecret123456789";
    private const string Post = Page + "_111";
    private const string Other = Page + "_222";

    private static string Me(string id = Page) =>
        "{\"id\":\"" + id + "\",\"name\":\"Fixture Page\"}";

    private static string Item(string id = Post, string author = Page) =>
        "{\"id\":\"" + id + "\",\"from\":{\"id\":\"" + author + "\"},\"message\":\"Fixture\"}";

    private static HttpResponseMessage Json(
        string body,
        HttpStatusCode status = HttpStatusCode.OK
    ) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Assert.Equal("graph.facebook.com", request.RequestUri!.Host);
            Assert.DoesNotContain(Secret, request.RequestUri.ToString());
            Assert.Equal(Secret, request.Headers.Authorization?.Parameter);
            return Task.FromResult(handle(request));
        }
    }

    private static PagePostDeletionService Service(HttpClient http) =>
        new(_ => Task.FromResult(Secret), http);

    [Fact]
    public async Task PaginationUsesCursorInsteadOfUntrustedNextUrlAndDeduplicates()
    {
        var batches = 0;
        using var http = new HttpClient(
            new Handler(r =>
            {
                if (r.RequestUri!.AbsolutePath == "/me")
                    return Json(Me());
                Assert.Contains("/published_posts", r.RequestUri.AbsolutePath);
                batches++;
                if (batches == 1)
                    return Json(
                        "{\"data\":["
                            + Item()
                            + "],\"paging\":{\"next\":\"https://evil.test/?access_token=secret\",\"cursors\":{\"after\":\"a&b\"}}}"
                    );
                Assert.Contains("after=a%26b", r.RequestUri.Query);
                return Json("{\"data\":[" + Item() + "," + Item(Other) + "]}");
            })
        );
        var preview = await Service(http).PreviewAsync(Page, default);
        Assert.Equal(2, preview.Posts.Count);
        Assert.Equal(2, batches);
    }

    [Fact]
    public async Task WrongActorCannotListOrDelete()
    {
        var requests = 0;
        using var http = new HttpClient(
            new Handler(r =>
            {
                requests++;
                Assert.Equal(HttpMethod.Get, r.Method);
                return Json(Me("999"));
            })
        );
        var service = Service(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreviewAsync(Page, default)
        );
        var preview = new PagePostPreview(Page, "Fixture", [new(Post, "", "", "")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(preview, [Post], _ => Task.CompletedTask, default)
        );
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("999_111", Page)]
    [InlineData(Post, "999")]
    public async Task ForeignPostsAbortPreview(string id, string author)
    {
        using var http = new HttpClient(
            new Handler(r =>
                r.RequestUri!.AbsolutePath == "/me"
                    ? Json(Me())
                    : Json("{\"data\":[" + Item(id, author) + "]}")
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(http).PreviewAsync(Page, default)
        );
    }

    [Fact]
    public async Task UnreviewedPostRejectedBeforeAnyRequest()
    {
        using var http = new HttpClient(
            new Handler(_ => throw new Xunit.Sdk.XunitException("Unexpected network request"))
        );
        var preview = new PagePostPreview(Page, "Fixture", [new(Post, "", "", "")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(http).DeleteAsync(preview, [Other], _ => Task.CompletedTask, default)
        );
    }

    [Fact]
    public async Task AuditBoundaryPrecedesDeletionAndRecordsSuccess()
    {
        var events = new List<PagePostDeletionEvent>();
        using var http = new HttpClient(
            new Handler(r =>
            {
                if (r.Method == HttpMethod.Delete)
                {
                    Assert.Equal("Requested", Assert.Single(events).Status);
                    return Json("{\"success\":true}");
                }
                return Json(r.RequestUri!.AbsolutePath == "/me" ? Me() : Item());
            })
        );
        var preview = new PagePostPreview(Page, "Fixture", [new(Post, "", "", "")]);
        await Service(http)
            .DeleteAsync(
                preview,
                [Post, Post],
                e =>
                {
                    events.Add(e);
                    return Task.CompletedTask;
                },
                default
            );
        Assert.Equal(new[] { "Requested", "Deleted" }, events.Select(e => e.Status));
        Assert.Single(events.Select(e => e.RunId).Distinct());
        Assert.DoesNotContain(Secret, System.Text.Json.JsonSerializer.Serialize(events));
    }

    [Fact]
    public async Task LostDeleteResponseStopsWithoutRetryOrDeletingNextPost()
    {
        var deletes = 0;
        var events = new List<PagePostDeletionEvent>();
        using var http = new HttpClient(
            new Handler(r =>
            {
                if (r.Method == HttpMethod.Delete)
                {
                    deletes++;
                    throw new HttpRequestException("secret=" + Secret);
                }
                return Json(r.RequestUri!.AbsolutePath == "/me" ? Me() : Item());
            })
        );
        var preview = new PagePostPreview(
            Page,
            "Fixture",
            [new(Post, "", "", ""), new(Other, "", "", "")]
        );
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(http)
                .DeleteAsync(
                    preview,
                    [Post, Other],
                    e =>
                    {
                        events.Add(e);
                        return Task.CompletedTask;
                    },
                    default
                )
        );
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Equal(1, deletes);
        Assert.Equal(new[] { "Requested", "Unknown" }, events.Select(e => e.Status));
    }

    [Theory]
    [InlineData(403, "Rejected")]
    [InlineData(429, "Rejected")]
    [InlineData(500, "Unknown")]
    public async Task DeleteErrorsStopRemainingPosts(int status, string outcome)
    {
        var deletes = 0;
        var events = new List<PagePostDeletionEvent>();
        using var http = new HttpClient(
            new Handler(r =>
            {
                if (r.Method == HttpMethod.Delete)
                {
                    deletes++;
                    return Json("{\"error\":\"" + Secret + "\"}", (HttpStatusCode)status);
                }
                return Json(r.RequestUri!.AbsolutePath == "/me" ? Me() : Item());
            })
        );
        var preview = new PagePostPreview(
            Page,
            "Fixture",
            [new(Post, "", "", ""), new(Other, "", "", "")]
        );
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(http)
                .DeleteAsync(
                    preview,
                    [Post, Other],
                    e =>
                    {
                        events.Add(e);
                        return Task.CompletedTask;
                    },
                    default
                )
        );
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Equal(1, deletes);
        Assert.Equal(outcome, events.Last().Status);
    }

    [Fact]
    public async Task AuditFailurePreventsSendingDelete()
    {
        using var http = new HttpClient(
            new Handler(r =>
            {
                Assert.Equal(HttpMethod.Get, r.Method);
                return Json(r.RequestUri!.AbsolutePath == "/me" ? Me() : Item());
            })
        );
        var preview = new PagePostPreview(Page, "Fixture", [new(Post, "", "", "")]);
        await Assert.ThrowsAsync<IOException>(() =>
            Service(http)
                .DeleteAsync(
                    preview,
                    [Post],
                    _ => throw new IOException("Audit unavailable"),
                    default
                )
        );
    }

    [Fact]
    public async Task CancellationBeforeStartSendsNothing()
    {
        using var http = new HttpClient(
            new Handler(_ => throw new Xunit.Sdk.XunitException("Unexpected request"))
        );
        var preview = new PagePostPreview(Page, "Fixture", [new(Post, "", "", "")]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(http).DeleteAsync(preview, [Post], _ => Task.CompletedTask, cts.Token)
        );
    }

    [Fact]
    public async Task LoopingPaginationCannotProduceActionablePreview()
    {
        using var http = new HttpClient(
            new Handler(r =>
                r.RequestUri!.AbsolutePath == "/me"
                    ? Json(Me())
                    : Json(
                        "{\"data\":["
                            + Item()
                            + "],\"paging\":{\"next\":\"ignored\",\"cursors\":{\"after\":\"same\"}}}"
                    )
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(http).PreviewAsync(Page, default)
        );
    }
}
