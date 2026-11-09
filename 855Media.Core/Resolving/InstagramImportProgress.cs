using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Resolving;

public sealed record InstagramImportItem(string Id, bool IsPhoto, bool HasCaption);

// A manifest of fully resolved post membership, not proof that a download succeeded.
// The caller must corroborate every item against active jobs or completed history/files.
// Signed media URLs, browser cookies and account tokens are never persisted here.
public sealed class InstagramImportProgress
{
    private readonly string _path;
    private readonly Dictionary<string, InstagramImportItem[]> _posts;

    private sealed record Entry(string Code, InstagramImportItem[] Items);

    public InstagramImportProgress(string storageDirectory, string scope)
    {
        _path = Path.Combine(
            storageDirectory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))) + ".jsonl"
        );
        _posts = [];
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length <= 16 * 1024 * 1024)
                foreach (var line in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<Entry>(line);
                        if (
                            entry is { Code: not null, Items: not null }
                            && (_posts.Count < 50_000 || _posts.ContainsKey(entry.Code))
                        )
                            _posts[entry.Code] = entry.Items;
                    }
                    catch (JsonException) { } // A cancelled append is not a completed checkpoint.
                }
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public bool CanSkip(string pageUrl, Func<string, InstagramImportItem, bool> corroborate)
    {
        var code = InstagramBrowserCollection.GetPostCode(pageUrl);
        return code is not null
            && _posts.TryGetValue(code, out var items)
            && items is { Length: > 0 and <= 100 }
            && items.All(i => i is not null && i.Id is not null)
            && items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() == items.Length
            && items.All(i =>
                i is not null
                && i.Id is not null
                && i.Id.StartsWith(code + "_", StringComparison.Ordinal)
                && int.TryParse(i.Id.AsSpan(code.Length + 1), out var index)
                && index > 0
                && corroborate(pageUrl, i)
            );
    }

    public async Task RememberAsync(
        string pageUrl,
        IReadOnlyList<VideoInfo> items,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var code = InstagramBrowserCollection.GetPostCode(pageUrl);
        if (code is null || items.Count == 0)
            return;
        var record = items
            .Select(v => new InstagramImportItem(
                v.Id,
                v.IsPhoto,
                !string.IsNullOrWhiteSpace(v.Caption)
            ))
            .ToArray();
        if (_posts.TryGetValue(code, out var old) && old.SequenceEqual(record))
            return;
        if (_posts.Count >= 50_000 && !_posts.ContainsKey(code))
            _posts.Remove(_posts.Keys.First());
        _posts[code] = record;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path) || new FileInfo(_path).Length < 8 * 1024 * 1024)
        {
            // Prepend a newline so a torn final record cannot swallow this record.
            await File.AppendAllTextAsync(
                _path,
                "\n" + JsonSerializer.Serialize(new Entry(code, record)) + "\n",
                ct
            );
            return;
        }
        // Compact rarely, rather than rewriting the whole profile after each post.
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var snapshot = _posts.ToArray();
            var json = await Task.Run(
                () =>
                    string.Join(
                        "\n",
                        snapshot.Select(p => JsonSerializer.Serialize(new Entry(p.Key, p.Value)))
                    ) + "\n",
                ct
            );
            await File.WriteAllTextAsync(temporary, json, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

public sealed record InstagramImportResult(
    int Posts,
    int Captured,
    int Opened,
    int Resumed,
    int Queued,
    int Total = 0
);

public sealed record InstagramCollectionSnapshot(IReadOnlyList<string> Posts, bool Running);

public static class InstagramImportRunner
{
    public static async Task<InstagramImportResult> RunAsync(
        IReadOnlyList<string> posts,
        Func<CancellationToken, Task> guard,
        Func<string, bool> canResume,
        Func<string, IReadOnlyList<VideoInfo>?> captured,
        Func<string, CancellationToken, Task<IReadOnlyList<VideoInfo>>> resolve,
        Func<IReadOnlyList<VideoInfo>, CancellationToken, Task<int>> enqueue,
        Func<string, IReadOnlyList<VideoInfo>, CancellationToken, Task> remember,
        Action<InstagramImportResult> progress,
        CancellationToken ct,
        TimeProvider? clock = null,
        TimeSpan? interval = null,
        Func<CancellationToken, Task<InstagramCollectionSnapshot>>? discover = null
    )
    {
        clock ??= TimeProvider.System;
        var pause = interval ?? TimeSpan.FromMilliseconds(2500);
        long? lastResolved = null;
        var result = new InstagramImportResult(0, 0, 0, 0, 0);
        var pending = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddPosts(IReadOnlyList<string> links)
        {
            foreach (var url in links)
                if (InstagramBrowserCollection.GetPostCode(url) is { } code && seen.Add(code))
                    pending.Enqueue(url);
            result = result with { Total = seen.Count };
        }
        AddPosts(posts);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await guard(ct);
            if (pending.Count == 0)
            {
                if (discover is null)
                    break;
                var snapshot = await discover(ct);
                AddPosts(snapshot.Posts);
                if (pending.Count == 0)
                {
                    if (!snapshot.Running)
                        break;
                    await Task.Delay(TimeSpan.FromMilliseconds(500), clock, ct);
                    continue;
                }
            }
            var post = pending.Dequeue();
            if (canResume(post))
                result = result with { Posts = result.Posts + 1, Resumed = result.Resumed + 1 };
            else
            {
                var items = captured(post);
                if (items is null)
                {
                    if (lastResolved is { } previous)
                    {
                        var remaining = pause - clock.GetElapsedTime(previous);
                        if (remaining > TimeSpan.Zero)
                            await Task.Delay(remaining, clock, ct);
                    }
                    await guard(ct);
                    items = await resolve(post, ct);
                    lastResolved = clock.GetTimestamp();
                    result = result with { Opened = result.Opened + 1 };
                }
                else
                    result = result with { Captured = result.Captured + 1 };
                ct.ThrowIfCancellationRequested();
                await guard(ct);
                var added = await enqueue(items, ct);
                await remember(post, items, ct);
                result = result with { Posts = result.Posts + 1, Queued = result.Queued + added };
            }
            progress(result);
        }
        return result;
    }
}
