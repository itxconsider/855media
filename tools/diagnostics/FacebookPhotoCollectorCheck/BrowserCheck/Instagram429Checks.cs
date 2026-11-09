using System.Reflection;
using System.Text;
using _855Media.Core.Resolving;
using _855Media.Services;
using Microsoft.Web.WebView2.Core;

internal static class Instagram429Checks
{
    public static async Task RunAsync(
        CoreWebView2Environment environment,
        CoreWebView2Controller source
    )
    {
        var cooldown = new InstagramRequestCooldown();
        using var reader = await InstagramPostDetailReader.CreateAsync(
            source.CoreWebView2,
            source,
            cooldown
        );
        var controller = (CoreWebView2Controller)
            typeof(InstagramPostDetailReader)
                .GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(reader)!;
        var browser = controller.CoreWebView2;
        var requests = 0;
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += (_, e) =>
        {
            requests++;
            e.Response = environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes("Too many requests")),
                429,
                "Too Many Requests",
                "Content-Type: text/html\r\nRetry-After: 120\r\n"
            );
        };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await reader.ResolveAsync(
                    "https://www.instagram.com/p/LIMIT/",
                    InstagramMediaKind.All,
                    CancellationToken.None
                );
                throw new Exception("429 accepted");
            }
            catch (InstagramRateLimitException e) when (e.Message.Contains("429")) { }
            if (
                attempt == 0
                && (!cooldown.IsActive || cooldown.RetryAt < DateTimeOffset.UtcNow.AddSeconds(100))
            )
                throw new Exception("Retry-After was not honored");
            if (attempt == 0)
                requests = 0;
        }
        if (requests != 0)
            throw new Exception("Early retry made a request");
        Console.WriteLine(
            "PASS Instagram 429: actual HTTP Retry-After deadline and zero requests on early retry"
        );
    }
}
