using System.Reflection;
using System.Text;
using _855Media.Services;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var host = new Form
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(-3000, -3000),
            Size = new System.Drawing.Size(1300, 1000),
        };
        host.Shown += async (_, _) =>
        {
            try
            {
                await CheckAsync(host.Handle);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
            }
            finally
            {
                host.Close();
            }
        };
        Application.Run(host);
    }

    private static async Task CheckAsync(IntPtr parent)
    {
        var userData = Path.Combine(
            Path.GetTempPath(),
            "855Media-photo-check-" + Guid.NewGuid().ToString("N")
        );
        var environment = await CoreWebView2Environment.CreateAsync(null!, userData);
        var source = await environment.CreateCoreWebView2ControllerAsync(parent);
        using var reader = await FacebookPhotoDetailReader.CreateAsync(source.CoreWebView2, source);
        var controller = (CoreWebView2Controller)
            typeof(FacebookPhotoDetailReader)
                .GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(reader)!;
        var browser = controller.CoreWebView2;
        const string asset =
            "https://scontent.fixture.fbcdn.net/v/t39/861234978_2238776080014473_n.jpg";
        const string full = asset + "?stp=dst-jpg_tt6&ctp=s1536x2048&oh=full";
        const string cropped = asset + "?stp=c0.169.1536.1536a_dst-jpg_tt6&ctp=s206x206&oh=crop";
        string Page(string caption) =>
            "<div><main role='main'><a href='/photo.php?fbid=101'><img src='"
            + cropped
            + "'></a><img src='"
            + full
            + "'></main><div><div><h2><a href='/author'>Author</a></h2><div role='button' aria-haspopup='menu'></div></div>"
            + caption
            + "<div role='article' aria-label='Comment by Other'><span dir='auto'>Do not copy this comment</span><div role='button' onclick='window.commentExpanded=true'>See more</div></div></div></div>";
        var html = Page(
            "<div><span dir='auto'>Caption line one<br>Caption line two 😊</span></div>"
        );
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += (_, e) =>
        {
            // All browser requests are intercepted. No real Facebook access or user cookies.
            var isPhotoPage = e.Request.Uri.StartsWith(
                "https://www.facebook.com/photo.php",
                StringComparison.Ordinal
            );
            var isFull = e.Request.Uri == full;
            var content =
                isPhotoPage ? html
                : e.Request.Uri.StartsWith(asset, StringComparison.Ordinal)
                    ? $"<svg xmlns='http://www.w3.org/2000/svg' width='{(isFull ? 1536 : 206)}' height='{(isFull ? 2048 : 206)}'><rect width='100%' height='100%' fill='blue'/></svg>"
                : "";
            e.Response = environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes(content)),
                200,
                "OK",
                "Content-Type: " + (isPhotoPage ? "text/html; charset=utf-8" : "image/svg+xml")
            );
        };
        try
        {
            await InstagramChecks.RunAsync(environment, source);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var item = new MediaPayloadItem
            {
                Id = "101",
                Url = cropped,
                PhotoPageUrl = "https://www.facebook.com/photo.php?fbid=101",
            };
            var result = await reader.ResolveAsync(item, timeout.Token);
            if (result?.Url != full)
                throw new Exception(
                    "Actual offscreen WebView did not resolve the full-size rendition."
                );
            Console.WriteLine(
                "PASS: real offscreen WebView resolves 1536x2048 image and excludes cropped grid"
            );
            if (result.Caption != "Caption line one\nCaption line two 😊")
                throw new Exception("Post caption or its line breaks were not retained.");
            Console.WriteLine(
                "PASS: real offscreen WebView reads adjacent caption panel with line breaks and Unicode"
            );
            if (source.CoreWebView2.Profile.ProfileName != browser.Profile.ProfileName)
                throw new Exception("Resolver did not reuse the source profile.");
            Console.WriteLine("PASS: temporary resolver reuses source session profile");
            html = Page(
                "<div data-ad-preview='message'><span dir='auto'>Short…<span role='button' onclick=\"this.parentElement.textContent='Expanded complete caption'\">See more</span></span></div>"
            );
            result = await reader.ResolveAsync(item, timeout.Token);
            if (result?.Caption != "Expanded complete caption")
                throw new Exception("Caption See more was not expanded.");
            if (await browser.ExecuteScriptAsync("window.commentExpanded === true") != "false")
                throw new Exception("A comment's See more was clicked.");
            Console.WriteLine(
                "PASS: expands only the post caption and excludes comments and their See more controls"
            );
            html = Page("<div></div>");
            result = await reader.ResolveAsync(item, timeout.Token);
            if (result?.Caption != string.Empty)
                throw new Exception("Captionless post picked up a comment or image alt.");
            Console.WriteLine("PASS: captionless posts do not borrow comment text or image alt");
            html = Page(
                "<div data-ad-comet-preview='message'><p>Paragraph one</p><p>Paragraph two <a href='https://example.test'>a link</a></p></div>"
            );
            result = await reader.ResolveAsync(item, timeout.Token);
            if (result?.Caption != "Paragraph one\nParagraph two a link")
                throw new Exception("Semantic post message paragraphs/links were not retained.");
            Console.WriteLine(
                "PASS: semantic post-message layout preserves paragraphs and link text"
            );
            html = Page(
                "<div id='caption-slot'></div><script>setTimeout(()=>document.getElementById('caption-slot').innerHTML='<span dir=auto>Delayed caption</span>',700)</script>"
            );
            result = await reader.ResolveAsync(item, timeout.Token);
            if (result?.Caption != "Delayed caption")
                throw new Exception(
                    "Late caption was returned as empty before rendering completed."
                );
            Console.WriteLine("PASS: waits for a caption rendered after the author header");
            html =
                "<main><h2><a href='/background'>Background profile</a></h2><div data-ad-preview='message'>Wrong background caption</div></main>"
                + "<div role='dialog' aria-label='Localized photo viewer'>"
                + Page("<div><span dir='auto'>Correct overlay caption</span></div>")
                + "</div>";
            result = await reader.ResolveAsync(item, timeout.Token);
            if (result?.Caption != "Correct overlay caption")
                throw new Exception("Overlay caption leaked background profile text.");
            Console.WriteLine(
                "PASS: localized photo dialog stays scoped to the selected image and excludes background posts"
            );
            item.PhotoPageUrl = "https://facebook.com.evil.test/photo.php?fbid=101";
            if (await reader.ResolveAsync(item, timeout.Token) is not null)
                throw new Exception("Untrusted photo page was accepted.");
            Console.WriteLine("PASS: untrusted photo page is rejected before navigation");
            item.PhotoPageUrl = "https://www.facebook.com/photo.php?fbid=101";
            html = "<main role='main'><img src='" + cropped + "'></main>";
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            try
            {
                await reader.ResolveAsync(item, cancel.Token);
                throw new Exception("Cancellation did not interrupt resolution.");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine(
                    "PASS: cancellation interrupts unresolved photo without thumbnail fallback"
                );
            }
        }
        finally
        {
            source.Close();
        }
    }
}
