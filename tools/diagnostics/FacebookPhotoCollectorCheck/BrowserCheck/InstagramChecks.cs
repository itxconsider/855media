using System.Reflection;
using System.Text;
using System.Text.Json;
using _855Media.Core.Resolving;
using _855Media.Services;
using Microsoft.Web.WebView2.Core;

internal static class InstagramChecks
{
    public static async Task RunAsync(
        CoreWebView2Environment environment,
        CoreWebView2Controller source
    )
    {
        using var reader = await InstagramPostDetailReader.CreateAsync(source.CoreWebView2, source);
        var controller = (CoreWebView2Controller)
            typeof(InstagramPostDetailReader)
                .GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(reader)!;
        var browser = controller.CoreWebView2;
        var assembly = typeof(InstagramChecks).Assembly;
        using var scriptReader = new StreamReader(
            assembly.GetManifestResourceStream(
                assembly
                    .GetManifestResourceNames()
                    .Single(n => n.EndsWith("instagram-scraper-bridge.js"))
            )!
        );
        var script = scriptReader.ReadToEnd();
        const string full =
            "https://scontent.fixture.cdninstagram.com/full.jpg?token=signed%2Bvalue";
        const string second = "https://scontent.fixture.cdninstagram.com/second.jpg?token=two";
        const string thumb = "https://scontent.fixture.cdninstagram.com/thumb.jpg";
        const string video = "https://video.fixture.fbcdn.net/clip.mp4?token=signed";
        var html = "<main></main>";
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += (_, e) =>
        {
            var page = new Uri(e.Request.Uri).Host == "www.instagram.com";
            var size = e.Request.Uri.Contains("thumb") ? (206, 206) : (1536, 2048);
            var content = page
                ? html
                : $"<svg xmlns='http://www.w3.org/2000/svg' width='{size.Item1}' height='{size.Item2}'></svg>";
            e.Response = environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes(content)),
                200,
                "OK",
                "Content-Type: " + (page ? "text/html; charset=utf-8" : "image/svg+xml")
            );
        };
        async Task Load(string url, string body)
        {
            html = body;
            var loaded = new TaskCompletionSource<bool>();
            void Done(object? _, CoreWebView2NavigationCompletedEventArgs e) =>
                loaded.TrySetResult(e.IsSuccess);
            browser.NavigationCompleted += Done;
            try
            {
                browser.Navigate(url);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                browser.NavigationCompleted -= Done;
            }
        }
        void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("Instagram browser regression: " + description);
            Console.WriteLine("PASS Instagram: " + description);
        }
        await Load(
            "https://www.instagram.com/nample/reels/",
            "<main><a href='/reel/A/'>A</a><a href='/nample/reel/B/'>B</a><a href='/reel/A/'>duplicate</a><a href='https://instagram.com.evil.test/reel/C/'>foreign</a></main>"
        );
        await browser.ExecuteScriptAsync(script);
        await browser.ExecuteScriptAsync("window.__ig855.start(); window.__ig855.stop();");
        using (
            var payload = JsonDocument.Parse(
                await browser.ExecuteScriptAsync("window.__ig855.payload()")
            )
        )
            Check(
                payload.RootElement.GetProperty("items").GetArrayLength() == 2,
                "collects canonical Reel links, deduplicates and excludes foreign hosts"
            );
        await browser.ExecuteScriptAsync(
            "document.querySelector('main').innerHTML='<a href=/p/C/>new virtualized post</a>'; window.__ig855.collect();"
        );
        using (
            var payload = JsonDocument.Parse(
                await browser.ExecuteScriptAsync("window.__ig855.payload()")
            )
        )
            Check(
                payload.RootElement.GetProperty("items").GetArrayLength() == 3,
                "retains links after virtualized rows disappear"
            );
        await browser.ExecuteScriptAsync(
            "history.pushState({},'', '/other/'); document.querySelector('main').innerHTML='<a href=/p/D/>new profile</a>'; window.__ig855.collect();"
        );
        using (
            var payload = JsonDocument.Parse(
                await browser.ExecuteScriptAsync("window.__ig855.payload()")
            )
        )
            Check(
                payload.RootElement.GetProperty("items").GetArrayLength() == 1,
                "profile changes clear earlier collected links"
            );
        await browser.ExecuteScriptAsync(
            "document.body.innerHTML='<input name=password>'; window.__ig855.start();"
        );
        Check(
            await browser.ExecuteScriptAsync("window.__ig855.payload().items.length") == "1",
            "login page cannot add posts to collection"
        );
        await browser.ExecuteScriptAsync("window.__ig855.clear();");
        Check(
            await browser.ExecuteScriptAsync("window.__ig855.payload().items.length") == "0",
            "Clear resets the collected list"
        );
        var post = new
        {
            code = "ABC",
            user = new { username = "nample" },
            caption = new { text = "Original caption\nខ្មែរ 😊" },
            carousel_media = new object[]
            {
                new
                {
                    media_type = 1,
                    image_versions2 = new
                    {
                        candidates = new[]
                        {
                            new
                            {
                                url = thumb,
                                width = 206,
                                height = 206,
                            },
                            new
                            {
                                url = full,
                                width = 1536,
                                height = 2048,
                            },
                        },
                    },
                },
                new
                {
                    media_type = 2,
                    video_versions = new[]
                    {
                        new
                        {
                            url = video,
                            width = 1080,
                            height = 1920,
                        },
                    },
                },
                new
                {
                    media_type = 1,
                    image_versions2 = new
                    {
                        candidates = new[]
                        {
                            new
                            {
                                url = second,
                                width = 1536,
                                height = 2048,
                            },
                        },
                    },
                },
            },
        };
        html =
            "<main><script type='application/json'>"
            + JsonSerializer.Serialize(new { data = post })
            + "</script></main>";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var media = await reader.ResolveAsync(
            "https://www.instagram.com/p/ABC/",
            InstagramMediaKind.All,
            timeout.Token
        );
        Check(
            media.Count == 3
                && media[0].ThumbnailUrls.Single() == full
                && media[2].ThumbnailUrls.Single() == second,
            "resolves full-size mixed carousels without importing gallery thumbnails"
        );
        Check(
            media[1].DirectMediaUrl == video && !media[1].IsPhoto,
            "Reel uses its direct signed video instead of the cover image"
        );
        Check(
            media.All(m => m.Caption == "Original caption\nខ្មែរ 😊"),
            "preserves original post caption and Unicode for every carousel item"
        );
        Check(
            source.CoreWebView2.Profile.ProfileName == browser.Profile.ProfileName,
            "temporary renderer shares the embedded browser profile"
        );
        var photos = await reader.ResolveAsync(
            "https://www.instagram.com/p/ABC/",
            InstagramMediaKind.Photos,
            timeout.Token
        );
        Check(
            photos.Count == 2 && photos.All(p => p.IsPhoto),
            "Photos only excludes video covers and video items"
        );
        html =
            $"<main><article><header><img width=32 height=32 src='{thumb}'></header><h1>Carousel caption</h1><img id=photo style='width:500px;height:650px' src='{full}'><button aria-label=Next onclick=\"document.getElementById('photo').src='{second}';this.remove();\">Next</button><ul><li>Unrelated comment</li></ul></article></main>";
        media = await reader.ResolveAsync(
            "https://www.instagram.com/p/ABC/",
            InstagramMediaKind.Photos,
            timeout.Token
        );
        Check(
            media.Count == 2
                && media[0].ThumbnailUrls.Single() == full
                && media[1].ThumbnailUrls.Single() == second,
            "DOM carousel advances only its own Next control and resolves both images"
        );
        html =
            $"<main><article><h1 id=caption></h1><img style='width:500px;height:650px' src='{full}'><ul><li>Wrong comment caption</li></ul></article></main><script>setTimeout(()=>document.getElementById('caption').textContent='Delayed post caption',700)</script>";
        media = await reader.ResolveAsync(
            "https://www.instagram.com/p/ABC/",
            InstagramMediaKind.Photos,
            timeout.Token
        );
        Check(
            media.Single().Caption == "Delayed post caption",
            "waits for delayed post caption and excludes comments"
        );
        html =
            "<main><article><video src='blob:https://www.instagram.com/unavailable'></video><img style='width:500px;height:650px' src='"
            + full
            + "'></article></main>";
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(750)))
        {
            try
            {
                await reader.ResolveAsync(
                    "https://www.instagram.com/reel/ABC/",
                    InstagramMediaKind.All,
                    cancel.Token
                );
                throw new Exception("Blob video imported a cover");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine(
                    "PASS Instagram: Stop cancels unsupported/blob video without cover fallback"
                );
            }
        }
        html =
            $"<main><video style='width:400px;height:700px' src='{video}'></video><video style='position:absolute;top:2000px;width:400px;height:700px' src='https://video.fixture.fbcdn.net/neighbor.mp4'></video></main>";
        media = await reader.ResolveAsync(
            "https://www.instagram.com/reel/ABC/",
            InstagramMediaKind.Videos,
            timeout.Token
        );
        Check(
            media.Single().DirectMediaUrl == video,
            "Reel layout without article excludes offscreen preloaded neighbors"
        );
        html =
            $"<main><article><h1>Short <button role=button onclick=\"this.parentElement.textContent='Expanded post caption'\">more</button></h1><img style='width:500px;height:650px' src='{full}'><ul><li><button onclick='window.commentClicked=true'>more</button></li></ul></article></main>";
        media = await reader.ResolveAsync(
            "https://www.instagram.com/p/ABC/",
            InstagramMediaKind.Photos,
            timeout.Token
        );
        Check(
            media.Single().Caption == "Expanded post caption"
                && await browser.ExecuteScriptAsync("window.commentClicked === true") == "false",
            "expands only post-caption More and leaves comments untouched"
        );
        html = "<div role=dialog>Try again later</div>";
        try
        {
            await reader.ResolveAsync(
                "https://www.instagram.com/p/ABC/",
                InstagramMediaKind.Photos,
                timeout.Token
            );
            throw new Exception("Rate limit was ignored");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("limiting"))
        {
            Console.WriteLine(
                "PASS Instagram: browser rate-limit prompt stops import without retries"
            );
        }
    }
}
