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
    private readonly InstagramRequestCooldown _cooldown;

    private InstagramPostDetailReader(
        CoreWebView2Controller controller,
        InstagramRequestCooldown cooldown
    )
    {
        _controller = controller;
        _cooldown = cooldown;
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
        CoreWebView2Controller parent,
        InstagramRequestCooldown? cooldown = null
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
            return new InstagramPostDetailReader(
                controller,
                cooldown ?? new InstagramRequestCooldown()
            );
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
        _cooldown.ThrowIfActive();
        var code =
            InstagramBrowserCollection.GetPostCode(pageUrl)
            ?? throw new InvalidOperationException("Invalid Instagram post URL.");
        var browser = _controller.CoreWebView2;
        var loaded = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        ulong? navigationId = null;
        var navigationFailed = false;
        var navigationError = "Unknown";
        var httpStatus = 0;
        var navigationCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs e) =>
            navigationId = e.NavigationId;
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.NavigationId == navigationId)
            {
                navigationFailed = !e.IsSuccess;
                navigationError = e.WebErrorStatus.ToString();
                if (e.HttpStatusCode > 0)
                    httpStatus = e.HttpStatusCode;
                navigationCompleted.TrySetResult(true);
                loaded.TrySetResult(e.IsSuccess);
            }
        }
        // Extraction validates the post and waits for complete media itself. Waiting
        // for window.load also waits for unrelated images, video and subframes.
        void DocumentReady(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            if (e.NavigationId == navigationId)
                loaded.TrySetResult(true);
        }
        browser.NavigationStarting += Starting;
        browser.NavigationCompleted += Completed;
        browser.DOMContentLoaded += DocumentReady;
        void ResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (string.Equals(e.Request.Uri, pageUrl, StringComparison.Ordinal))
            {
                httpStatus = e.Response.StatusCode;
                if (httpStatus == 429)
                    _cooldown.Observe(
                        e.Response.Headers.Contains("Retry-After")
                            ? e.Response.Headers.GetHeader("Retry-After")
                            : null
                    );
            }
        }
        browser.WebResourceResponseReceived += ResponseReceived;
        async Task<InvalidOperationException> LoadFailure(string state)
        {
            // Chromium's error DOM can precede NavigationCompleted. Await its actual
            // reason instead of reporting the uninitialized Unknown value.
            try
            {
                await navigationCompleted.Task.WaitAsync(
                    TimeSpan.FromSeconds(3),
                    cancellationToken
                );
            }
            catch (TimeoutException) { }
            var detail = httpStatus >= 400 ? $"HTTP {httpStatus}" : navigationError;
            if (httpStatus == 429)
            {
                if (!_cooldown.IsActive)
                    _cooldown.Observe(null);
                return new InstagramRateLimitException(_cooldown.Message);
            }
            if (navigationError == "Unknown" && httpStatus < 400)
            {
                var error = await browser.ExecuteScriptAsync(
                    "document.body?.innerText?.match(/\\b(?:ERR|DNS_PROBE)_[A-Z0-9_]+\\b/)?.[0] ?? ''"
                );
                var codeText = JsonSerializer.Deserialize<string>(error);
                if (!string.IsNullOrWhiteSpace(codeText))
                    detail = codeText;
                else
                    detail = "browser error page; completion unavailable";
            }
            try
            {
                var log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "855Media",
                    "logs",
                    "instagram-import.log"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                await File.AppendAllTextAsync(
                    log,
                    $"{DateTimeOffset.Now:O} post={code} state={state} error={detail}{Environment.NewLine}",
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new InvalidOperationException(
                $"Instagram post {code} could not open in the import browser ({detail}). Open this post in the browser to check access, then retry Import."
            );
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            browser.Navigate(pageUrl);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
                throw await LoadFailure("navigation_failed");
            var collected = new List<JsonElement>();
            var seen = new HashSet<string>();
            string caption = "",
                author = "Instagram";
            var stagnant = 0;
            var advancePending = false;
            var emptyCaptionRounds = 0;
            var offlineRounds = 0;
            for (var round = 0; round < 60; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = await browser.ExecuteScriptAsync(
                    "(" + _script + ")(" + JsonSerializer.Serialize(new { code }) + ")"
                );
                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;
                var state = root.GetProperty("state").GetString();
                if (state == "offline")
                {
                    if (++offlineRounds >= 6)
                        throw new InvalidOperationException(
                            "The import browser reports offline and has no usable post media. Check its connection, then retry Import."
                        );
                    await Task.Delay(500, cancellationToken);
                    continue;
                }
                offlineRounds = 0;
                if (state == "load_error" || navigationFailed && state is "loading" or "wrong_post")
                    throw await LoadFailure(state ?? "unavailable");
                if (state == "rate_limited")
                {
                    _cooldown.Observe(null);
                    _cooldown.ThrowIfActive();
                }
                if (state is "login" or "rate_limited" or "wrong_post")
                    throw new InvalidOperationException(
                        state == "rate_limited"
                            ? "Instagram is limiting activity in the browser. Import stopped; wait for Instagram to allow access before retrying."
                        : state == "login"
                            ? "Instagram is showing a sign-in screen. Check the browser before importing again."
                        : "The expected Instagram post did not open. Check your connection and the post in the browser, then retry Import."
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
            browser.NavigationStarting -= Starting;
            browser.DOMContentLoaded -= DocumentReady;
            browser.WebResourceResponseReceived -= ResponseReceived;
            browser.Stop();
        }
    }

    public void Dispose() => _controller.Close();
}
