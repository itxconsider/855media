using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace _855Media.Services;

// One temporary renderer shares the embedded Facebook session and is closed after import.
// Resolving sequentially bounds browser/network work on low-resource PCs.
internal sealed class FacebookPhotoDetailReader : IDisposable
{
    internal sealed record PhotoDetail(string Url, string Caption);

    private readonly CoreWebView2Controller _controller;
    private readonly string _script;
    private readonly string _captionScript;

    private FacebookPhotoDetailReader(CoreWebView2Controller controller)
    {
        _controller = controller;
        var assembly = typeof(FacebookPhotoDetailReader).Assembly;
        var resource = assembly
            .GetManifestResourceNames()
            .Single(n => n.EndsWith("facebook-photo-detail.js", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        _script = reader.ReadToEnd();
        var captionResource = assembly
            .GetManifestResourceNames()
            .Single(n => n.EndsWith("facebook-photo-caption.js", StringComparison.Ordinal));
        using var captionReader = new StreamReader(
            assembly.GetManifestResourceStream(captionResource)!
        );
        _captionScript = captionReader.ReadToEnd();
    }

    public static async Task<FacebookPhotoDetailReader> CreateAsync(
        CoreWebView2 source,
        CoreWebView2Controller parent
    )
    {
        var options = source.Environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = source.Profile.ProfileName;
        options.IsInPrivateModeEnabled = source.Profile.IsInPrivateModeEnabled;
        var controller = await source.Environment.CreateCoreWebView2ControllerAsync(
            parent.ParentWindow,
            options
        );
        try
        {
            controller.Bounds = new Rectangle(-1400, -1100, 1280, 960);
            controller.IsVisible = true; // Offscreen, with a real viewport so the viewer loads its image.
            controller.CoreWebView2.Settings.IsScriptEnabled = true;
            controller.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            controller.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (
                    !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                    || uri.Scheme != "https"
                    || !(
                        uri.Host == "facebook.com"
                        || uri.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
                    )
                )
                    e.Cancel = true;
            };
            return new FacebookPhotoDetailReader(controller);
        }
        catch
        {
            controller.Close();
            throw;
        }
    }

    public async Task<PhotoDetail?> ResolveAsync(
        MediaPayloadItem item,
        CancellationToken cancellationToken
    )
    {
        if (
            !Uri.TryCreate(item.PhotoPageUrl, UriKind.Absolute, out var page)
            || page.Scheme != "https"
            || !(
                page.Host == "facebook.com"
                || page.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
            )
        )
            return null;
        var browser = _controller.CoreWebView2;
        var loaded = new TaskCompletionSource<bool>();
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
            loaded.TrySetResult(e.IsSuccess);
        browser.NavigationCompleted += Completed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            browser.Navigate(page.AbsoluteUri);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken))
                return null;
            var expected = JsonSerializer.Serialize(new { id = item.Id, url = item.Url });
            var script =
                "(() => { const photo = ("
                + _script
                + ")("
                + expected
                + "); "
                + "if (!photo) return null; return {...photo, ...("
                + _captionScript
                + ")({expand:true,imageUrl:photo.url})}; })();";
            PhotoDetail? imageWithoutCaption = null;
            var emptyCaptionRounds = 0;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = await browser.ExecuteScriptAsync(script);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    using var result = JsonDocument.Parse(raw);
                    var detail = new PhotoDetail(
                        result.RootElement.GetProperty("url").GetString()!,
                        result.RootElement.GetProperty("caption").GetString() ?? string.Empty
                    );
                    if (result.RootElement.GetProperty("captionReady").GetBoolean())
                    {
                        // Facebook can render the author header before its caption arrives.
                        if (!string.IsNullOrWhiteSpace(detail.Caption) || ++emptyCaptionRounds >= 3)
                            return detail;
                    }
                    imageWithoutCaption = detail with { Caption = string.Empty };
                }
                await Task.Delay(500, cancellationToken);
            }
            return imageWithoutCaption;
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            browser.NavigationCompleted -= Completed;
            browser.Stop();
        }
    }

    public void Dispose() => _controller.Close();
}
