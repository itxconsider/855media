using System.Text;
using System.Text.Json;
using _855Media.Core.Resolving;
using _855Media.Services;
using Microsoft.Web.WebView2.Core;

internal static class InstagramProfileCaptureChecks
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static async Task RunAsync(
        CoreWebView2Environment environment,
        CoreWebView2Controller source
    )
    {
        var controller = await environment.CreateCoreWebView2ControllerAsync(source.ParentWindow);
        controller.Bounds = new System.Drawing.Rectangle(-1400, -1100, 1280, 960);
        controller.IsVisible = true;
        var browser = controller.CoreWebView2;
        var access = true;
        var limitedNotifications = 0;
        var clock = new Clock();
        using var capture = new InstagramProfileCapture(
            browser,
            () => access,
            () => limitedNotifications++,
            new InstagramRequestCooldown(clock)
        );
        var feedStatus = 200;
        const string full =
            "https://scontent.fixture.cdninstagram.com/full.jpg?token=signed%2Bexact";
        const string page = "https://www.instagram.com/p/FAST/";
        object Post(string code, string username = "owner", string url = full) =>
            new
            {
                code,
                user = new { username },
                caption = new { text = "Captured caption\nខ្មែរ 😊" },
                media_type = 1,
                original_width = 1200,
                original_height = 1600,
                image_versions2 = new
                {
                    candidates = new[]
                    {
                        new
                        {
                            url,
                            width = 1200,
                            height = 1600,
                        },
                    },
                },
            };
        var feed = JsonSerializer.Serialize(
            new { items = new[] { Post("NETWORK"), Post("FOREIGN", "other") } }
        );
        var html =
            "<main><a href='/p/FAST/'>Loaded post</a><script type='application/json'>"
            + JsonSerializer.Serialize(new { data = Post("FAST") })
            + "</script></main>";
        var navigations = 0;
        browser.NavigationStarting += (_, _) => navigations++;
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += (_, e) =>
        {
            var json = InstagramProfileCapture.IsFeedResponse(e.Request.Uri);
            e.Response = environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes(json ? feed : html)),
                json ? feedStatus : 200,
                "OK",
                "Content-Type: "
                    + (json ? "application/json; charset=utf-8" : "text/html; charset=utf-8")
            );
        };
        async Task Load(string url)
        {
            var done = new TaskCompletionSource<bool>();
            void Completed(object? _, CoreWebView2NavigationCompletedEventArgs e) =>
                done.TrySetResult(e.IsSuccess);
            browser.NavigationCompleted += Completed;
            try
            {
                browser.Navigate(url);
                await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                browser.NavigationCompleted -= Completed;
            }
            await capture.ReadInlineAsync(CancellationToken.None);
        }
        void Check(bool value, string description)
        {
            if (!value)
                throw new Exception("Instagram capture regression: " + description);
            Console.WriteLine("PASS Instagram capture: " + description);
        }
        try
        {
            await Load("https://www.instagram.com/owner/");
            var cached = capture.TryGet(page, InstagramMediaKind.Photos);
            Check(
                cached?.Single().ThumbnailUrls.Single() == full
                    && cached[0].Caption == "Captured caption\nខ្មែរ 😊",
                "inline profile data preserves full image, signed URL and original Unicode caption"
            );
            await browser.ExecuteScriptAsync("fetch('/api/v1/feed/user/owner/').then(r=>r.json())");
            for (
                var i = 0;
                i < 50
                    && capture.TryGet(page.Replace("FAST", "NETWORK"), InstagramMediaKind.All)
                        is null;
                i++
            )
                await Task.Delay(50);
            Check(
                capture.TryGet(page.Replace("FAST", "NETWORK"), InstagramMediaKind.All)?.Count == 1,
                "captures actual profile response without making a new feed request"
            );
            Check(
                capture.TryGet(page.Replace("FAST", "FOREIGN"), InstagramMediaKind.All) is null,
                "another author's response record is excluded"
            );
            var before = navigations;
            var result = await InstagramImportRunner.RunAsync(
                [page],
                _ => Task.CompletedTask,
                _ => false,
                url => capture.TryGet(url, InstagramMediaKind.All),
                (_, _) => throw new Exception("Unexpected detail navigation"),
                (items, _) => Task.FromResult(items.Count),
                (_, _, _) => Task.CompletedTask,
                _ => { },
                CancellationToken.None
            );
            Check(
                result is { Captured: 1, Opened: 0, Queued: 1 } && before == navigations,
                "imports only collected post codes and performs zero individual-post navigations"
            );
            using (
                var bridgeReader = new StreamReader(
                    typeof(InstagramProfileCaptureChecks).Assembly.GetManifestResourceStream(
                        typeof(InstagramProfileCaptureChecks)
                            .Assembly.GetManifestResourceNames()
                            .Single(n => n.EndsWith("instagram-scraper-bridge.js"))
                    )!
                )
            )
                await browser.ExecuteScriptAsync(bridgeReader.ReadToEnd());
            await browser.ExecuteScriptAsync("window.__ig855.start()");
            var queued = new List<string>();
            var streamed = await InstagramImportRunner.RunAsync(
                [page],
                _ => Task.CompletedTask,
                _ => false,
                url => capture.TryGet(url, InstagramMediaKind.All),
                (_, _) => throw new Exception("Unexpected streaming detail navigation"),
                async (items, _) =>
                {
                    queued.AddRange(items.Select(v => v.Id));
                    if (queued.Count == 1)
                        await browser.ExecuteScriptAsync(
                            "document.querySelector('main').insertAdjacentHTML('beforeend','<a href=/p/NETWORK/>Newly discovered post</a>'); window.__ig855.collect(); window.__ig855.stop();"
                        );
                    return items.Count;
                },
                (_, _, _) => Task.CompletedTask,
                _ => { },
                CancellationToken.None,
                discover: async _ =>
                {
                    using var payload = JsonDocument.Parse(
                        await browser.ExecuteScriptAsync("window.__ig855.payload()")
                    );
                    return new InstagramCollectionSnapshot(
                        payload
                            .RootElement.GetProperty("items")
                            .EnumerateArray()
                            .Select(item => item.GetProperty("url").GetString()!)
                            .ToArray(),
                        payload.RootElement.GetProperty("running").GetBoolean()
                    );
                }
            );
            Check(
                streamed is { Total: 2, Captured: 2, Opened: 0, Queued: 2 }
                    && queued.SequenceEqual(new[] { "FAST_1", "NETWORK_1" })
                    && before == navigations,
                "actual collector discoveries join the running import after downloads start, without duplicate navigation"
            );
            access = false;
            Check(
                capture.TryGet(page, InstagramMediaKind.All) is null,
                "access expiry disables cached media execution"
            );
            access = true;
            feedStatus = 429;
            await browser.ExecuteScriptAsync("fetch('/api/v1/feed/user/owner/').then(r=>r.json())");
            for (var i = 0; i < 50 && !capture.IsRateLimited; i++)
                await Task.Delay(50);
            Check(
                capture.IsRateLimited
                    && limitedNotifications == 1
                    && capture.TryGet(page, InstagramMediaKind.All) is null,
                "HTTP 429 stops cached execution and signals cancellation without automatic retry"
            );
            feedStatus = 200;
            capture.Clear();
            if (!capture.IsRateLimited)
                throw new Exception("Clear bypassed cooldown");
            clock.Now = clock.Now.AddMinutes(16);
            await Load("https://www.instagram.com/other/");
            Check(
                capture.TryGet(page, InstagramMediaKind.All) is null,
                "profile navigation discards previous profile capture"
            );
            await Load("https://www.instagram.com/owner/");
            Check(
                capture.TryGet(page, InstagramMediaKind.All) is not null,
                "fresh return rebuilds capture for the current profile"
            );
            capture.Clear();
            Check(
                capture.TryGet(page, InstagramMediaKind.All) is null,
                "Clear drops captured media"
            );
            html =
                "<script type='application/json'>"
                + JsonSerializer.Serialize(Post("FAST", url: full + "&expires=1"))
                + "</script>";
            await Load("https://www.instagram.com/owner/");
            Check(
                capture.TryGet(page, InstagramMediaKind.All) is null,
                "expired signed URL requires fresh detail resolution"
            );
            capture.Dispose();
            Check(
                capture.TryGet(page, InstagramMediaKind.All) is null,
                "disposed capture cannot authorize cached imports"
            );
            Check(
                !InstagramProfileCapture.IsFeedResponse(
                    "https://instagram.com.evil.test/api/v1/feed/user/owner/"
                )
                    && !InstagramProfileCapture.IsFeedResponse(
                        "https://www.instagram.com/direct/inbox/"
                    )
                    && !InstagramProfileCapture.IsFeedResponse(
                        "https://www.instagram.com/api/v1/accounts/current_user/"
                    )
                    && InstagramProfileCapture.IsFeedResponse(
                        "https://www.instagram.com/graphql/query/"
                    )
                    && InstagramProfileCapture.IsFeedResponse(
                        "https://www.instagram.com/api/v1/clips/user/"
                    ),
                "only owned feed/GraphQL response routes are eligible"
            );
        }
        finally
        {
            controller.Close();
        }
    }
}
