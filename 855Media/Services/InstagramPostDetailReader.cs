using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using Microsoft.Web.WebView2.Core;

namespace _855Media.Services;

internal sealed class InstagramPostDetailReader : IDisposable
{
    private readonly CoreWebView2Controller _controller;
    private readonly string _script;

    private InstagramPostDetailReader(CoreWebView2Controller controller)
    {
        _controller = controller;
        var assembly = typeof(InstagramPostDetailReader).Assembly;
        using var reader = new StreamReader(
            assembly.GetManifestResourceStream(
                assembly
                    .GetManifestResourceNames()
                    .Single(n => n.EndsWith("instagram-post-detail.js", StringComparison.Ordinal))
            )!
        );
        _script = reader.ReadToEnd();
    }

    public static async Task<InstagramPostDetailReader> CreateAsync(
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
            controller.IsVisible = true;
            controller.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            controller.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (
                    !InstagramQueryResolver.IsInstagramQuery(e.Uri)
                    || !e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                )
                    e.Cancel = true;
            };
            return new InstagramPostDetailReader(controller);
        }
        catch
        {
            controller.Close();
            throw;
        }
    }

    public async Task<IReadOnlyList<VideoInfo>> ResolveAsync(
        string pageUrl,
        InstagramMediaKind kind,
        CancellationToken cancellationToken
    )
    {
        var code =
            InstagramBrowserCollection.GetPostCode(pageUrl)
            ?? throw new InvalidOperationException("Invalid Instagram post URL.");
        var browser = _controller.CoreWebView2;
        var loaded = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) =>
            loaded.TrySetResult(e.IsSuccess);
        browser.NavigationCompleted += Completed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            browser.Navigate(pageUrl);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
                throw new InvalidOperationException(
                    "Instagram could not open this post. Check the page in the browser and retry Import."
                );
            var collected = new List<JsonElement>();
            var seen = new HashSet<string>();
            string caption = "",
                author = "Instagram";
            var stagnant = 0;
            var advancePending = false;
            var emptyCaptionRounds = 0;
            for (var round = 0; round < 60; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = await browser.ExecuteScriptAsync(
                    "(" + _script + ")(" + JsonSerializer.Serialize(new { code }) + ")"
                );
                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;
                var state = root.GetProperty("state").GetString();
                if (state is "login" or "rate_limited" or "wrong_post")
                    throw new InvalidOperationException(
                        state == "rate_limited"
                            ? "Instagram is limiting activity in the browser. Import stopped; wait for Instagram to allow access before retrying."
                            : "Instagram needs sign-in or changed the open post. Check the browser before importing again."
                    );
                if (state == "ready")
                {
                    var observedCaption = root.GetProperty("caption").GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(observedCaption))
                        caption = observedCaption;
                    author = root.GetProperty("author").GetString() ?? "Instagram";
                    var before = collected.Count;
                    foreach (var item in root.GetProperty("items").EnumerateArray())
                    {
                        var url = item.GetProperty("url").GetString()!;
                        if (seen.Add(new Uri(url).GetLeftPart(UriPartial.Path)))
                            collected.Add(item.Clone());
                    }
                    var progressed = collected.Count > before;
                    if (
                        root.GetProperty("complete").GetBoolean() && (!advancePending || progressed)
                    )
                    {
                        advancePending = false;
                        if (string.IsNullOrWhiteSpace(caption) && ++emptyCaptionRounds < 3)
                        {
                            await Task.Delay(500, cancellationToken);
                            continue;
                        }
                        var json = JsonSerializer.Serialize(
                            new
                            {
                                code,
                                caption,
                                author,
                                items = collected,
                            }
                        );
                        return InstagramBrowserCollection.ParsePost(pageUrl, json, kind);
                    }
                    if (
                        progressed
                        && root.TryGetProperty("canAdvance", out var next)
                        && next.GetBoolean()
                    )
                    {
                        await browser.ExecuteScriptAsync(
                            "(() => { const a=document.querySelector('[role=dialog] article,main article'); const b=[...(a?.querySelectorAll('button,[role=button]')||[])].find(b=>!b.disabled&&b.getAttribute('aria-disabled')!=='true'&&(b.getAttribute('aria-label')==='Next'||b.querySelector('svg[aria-label=Next]'))); b?.click(); })()"
                        );
                        advancePending = true;
                    }
                    stagnant = progressed ? 0 : stagnant + 1;
                    if (stagnant >= 12)
                        break;
                }
                await Task.Delay(500, cancellationToken);
            }
            throw new InvalidOperationException(
                "Instagram did not expose complete full-size media for this post. It was not imported; no gallery thumbnail was substituted."
            );
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                "Instagram post loading timed out. Check the browser, then retry Import.",
                ex
            );
        }
        finally
        {
            browser.NavigationCompleted -= Completed;
            browser.Stop();
        }
    }

    public void Dispose() => _controller.Close();
}
