using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using Microsoft.Web.WebView2.Core;

namespace _855Media.Services;

// Observe responses the user's profile browser already requested. No additional
// feed API requests, cookie extraction or parallel post navigation are introduced.
internal sealed class InstagramProfileCapture : IDisposable
{
    private readonly CoreWebView2 _browser;
    private readonly Func<bool> _hasAccess;
    private readonly Action? _onRateLimited;
    private readonly InstagramRequestCooldown _cooldown;
    public bool IsRateLimited => _cooldown.IsActive;
    private readonly Dictionary<string, (InstagramCapturedPost Post, DateTimeOffset At)> _posts =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _pending = [];
    private string? _source;
    private int _generation;
    private int _retainedCharacters;

    public InstagramProfileCapture(
        CoreWebView2 browser,
        Func<bool> hasAccess,
        Action? onRateLimited = null,
        InstagramRequestCooldown? cooldown = null
    )
    {
        _browser = browser;
        _hasAccess = hasAccess;
        _onRateLimited = onRateLimited;
        _cooldown = cooldown ?? new InstagramRequestCooldown();
        _source = Scope(browser.Source);
        browser.SourceChanged += SourceChanged;
        browser.NavigationStarting += NavigationStarting;
        browser.DOMContentLoaded += DocumentReady;
        browser.WebResourceResponseReceived += ResponseReceived;
    }

    private static string? Scope(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && InstagramQueryResolver.GetProfileUsername(url) is { } username
            ? username.ToLowerInvariant()
            : null;

    private void Reset()
    {
        _generation++;
        _posts.Clear();
        _retainedCharacters = 0;
        _source = Scope(_browser.Source);
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) =>
        Reset();

    private void DocumentReady(object? sender, CoreWebView2DOMContentLoadedEventArgs e) =>
        _ = ObserveAsync(ReadInlineAsync(_lifetime.Token));

    private void SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (_source != Scope(_browser.Source))
            Reset();
    }

    internal static bool IsFeedResponse(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && uri.UserInfo.Length == 0
        && uri.IsDefaultPort
        && (uri.Host == "www.instagram.com" || uri.Host == "instagram.com")
        && (
            uri.AbsolutePath.StartsWith("/api/v1/feed/", StringComparison.Ordinal)
            || uri.AbsolutePath is "/api/v1/clips/user" or "/api/v1/clips/user/"
            || uri.AbsolutePath
                is "/graphql/query"
                    or "/graphql/query/"
                    or "/api/graphql"
                    or "/api/graphql/"
        );

    private void ResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs e
    )
    {
        if (
            _lifetime.IsCancellationRequested
            || !_hasAccess()
            || _source is null
            || !IsFeedResponse(e.Request.Uri)
        )
            return;
        if (e.Response.StatusCode == 429)
        {
            var wasLimited = IsRateLimited;
            _cooldown.Observe(
                e.Response.Headers.Contains("Retry-After")
                    ? e.Response.Headers.GetHeader("Retry-After")
                    : null
            );
            if (!wasLimited)
                _onRateLimited?.Invoke();
            return;
        }
        if (IsRateLimited || _pending.Count >= 2 || e.Response.StatusCode != 200)
            return;
        var contentType = e.Response.Headers.Contains("Content-Type")
            ? e.Response.Headers.GetHeader("Content-Type")
            : "";
        if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            return;
        var task = ReadResponseAsync(e, _source, _generation);
        _pending.Add(task);
        _ = ObserveAsync(task);
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        finally
        {
            _pending.Remove(task);
        }
    }

    private async Task ReadResponseAsync(
        CoreWebView2WebResourceResponseReceivedEventArgs e,
        string source,
        int generation
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var stream = await e.Response.GetContentAsync().WaitAsync(timeout.Token);
        if (stream is null)
            return;
        using var reader = new StreamReader(stream);
        var buffer = new char[8192];
        var json = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
        {
            if (json.Length + count > InstagramProfileMedia.MaximumJsonLength)
                return;
            json.Append(buffer, 0, count);
        }
        var posts = await Task.Run(
            () => InstagramProfileMedia.Parse(json.ToString(), source),
            timeout.Token
        );
        Remember(posts, source, generation);
    }

    private void Remember(
        IReadOnlyList<InstagramCapturedPost> posts,
        string source,
        int generation,
        bool refreshAge = true
    )
    {
        if (
            _lifetime.IsCancellationRequested
            || IsRateLimited
            || !_hasAccess()
            || generation != _generation
            || source != _source
            || source != Scope(_browser.Source)
        )
            return;
        foreach (var post in posts)
        {
            if (
                !refreshAge
                && _posts.TryGetValue(post.Code, out var existing)
                && existing.Post.Json == post.Json
            )
                continue;
            // Bound retained structured data, independently of the collected link count.
            // Older entries fall back to the ordinary post reader.
            if (post.Json.Length > 1024 * 1024)
                continue;
            if (_posts.Remove(post.Code, out var old))
                _retainedCharacters -= old.Post.Json.Length;
            while (
                _posts.Count > 0
                && (
                    _posts.Count >= 5000 || _retainedCharacters + post.Json.Length > 8 * 1024 * 1024
                )
            )
            {
                var first = _posts.Keys.First();
                _retainedCharacters -= _posts[first].Post.Json.Length;
                _posts.Remove(first);
            }
            _posts[post.Code] = (post, DateTimeOffset.UtcNow);
            _retainedCharacters += post.Json.Length;
        }
    }

    public async Task ReadInlineAsync(CancellationToken ct)
    {
        var source = _source;
        var generation = _generation;
        if (source is null || !_hasAccess())
            return;
        var raw = await _browser
            .ExecuteScriptAsync(
                """
                (() => { let budget=8*1024*1024; const result=[];
                  for(const s of document.querySelectorAll('script[type="application/json"],script[type="application/ld+json"]')) {
                    const text=s.textContent; if(text.length>budget) continue;
                    budget-=text.length; result.push(text);
                  } return result; })()
                """
            )
            .WaitAsync(ct);
        using var document = JsonDocument.Parse(raw);
        foreach (var json in document.RootElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var text = json.GetString()!;
            var posts = await Task.Run(() => InstagramProfileMedia.Parse(text, source), ct);
            Remember(posts, source, generation, refreshAge: false);
        }
        // Let already-delivered response captures finish, without blocking import
        // on an unrelated stalled response. A miss always uses the detail reader.
        try
        {
            await Task.WhenAll(_pending.ToArray()).WaitAsync(TimeSpan.FromSeconds(2), ct);
        }
        catch (TimeoutException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (COMException) { }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }

    public IReadOnlyList<VideoInfo>? TryGet(string pageUrl, InstagramMediaKind kind)
    {
        var code = InstagramBrowserCollection.GetPostCode(pageUrl);
        var now = DateTimeOffset.UtcNow;
        if (
            _lifetime.IsCancellationRequested
            || IsRateLimited
            || !_hasAccess()
            || _source != Scope(_browser.Source)
            || code is null
            || !_posts.TryGetValue(code, out var entry)
            || now - entry.At > TimeSpan.FromHours(2)
        )
            return null;
        var all = InstagramBrowserCollection.ParsePost(
            pageUrl,
            entry.Post.Json,
            InstagramMediaKind.All
        );
        if (
            all.Any(v =>
            {
                var url = v.DirectMediaUrl ?? v.ThumbnailUrls.Single();
                return !InstagramProfileMedia.IsFreshMediaUrl(url, now)
                    || now - entry.At > TimeSpan.FromMinutes(15)
                        && !InstagramProfileMedia.HasExplicitExpiry(url);
            })
        )
            return null;
        return InstagramBrowserCollection.ParsePost(pageUrl, entry.Post.Json, kind);
    }

    public void Clear() => Reset();

    public void Dispose()
    {
        _lifetime.Cancel();
        _browser.SourceChanged -= SourceChanged;
        _browser.NavigationStarting -= NavigationStarting;
        _browser.DOMContentLoaded -= DocumentReady;
        _browser.WebResourceResponseReceived -= ResponseReceived;
        _posts.Clear();
        // Pending response tasks own their linked cancellation sources.
    }
}
