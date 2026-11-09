using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowAvailableMedia(
    IReadOnlyList<string> Files,
    int Total,
    int Posted,
    int Held,
    int Unreadable,
    string Error
)
{
    public string Display => Error.Length > 0 ? "Unavailable" : $"{Files.Count:N0} / {Total:N0}";
    public string Summary =>
        Error.Length > 0
            ? Error
            : $"Available / Total: {Files.Count:N0} / {Total:N0}\nAlready posted: {Posted:N0}\nQueued or needs review: {Held:N0}\nUnreadable: {Unreadable:N0}\nPosted originals are kept and excluded for this target. Total also includes verified removals by older versions from the current sources.";
}

// Available files are physical files; total also retains verified post-cleanup
// removals from the current sources. History is supplied by the installation scope.
public sealed class PFlowMediaAvailability
{
    private readonly ConcurrentDictionary<
        (string Path, long Size, DateTime Write, DateTime Created),
        string
    > _hashes = new();

    private static string Type(string value) =>
        value is "Video" or "Reel" ? "Reel"
        : value is "Photo" or "Picture" ? "Picture"
        : value;

    private static string? FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or NotSupportedException
                        or System.Security.SecurityException
            )
        {
            return null;
        }
    }

    private static Dictionary<string, bool> RemovedPostedSources(
        FacebookPageTarget target,
        string type,
        IReadOnlyList<PFlowJob> history,
        CancellationToken ct
    )
    {
        var removed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var sources = target.GetUploadMediaPaths(type).Select(FullPath).OfType<string>().ToArray();
        var extensions =
            type == "Story" ? FacebookPageTarget.StoryExtensions
            : type == "Picture" ? FacebookPageTarget.PhotoExtensions
            : FacebookPageTarget.ReelExtensions;
        foreach (
            var job in history.Where(j =>
                j.State == PFlowState.Confirmed
                && j.DeleteRequested
                && j.LocalDeleteState is "Verified" or "Verified; caption retained"
                && !j.RequiresPublicationReviewBeforeCleanup
            )
        )
        foreach (var file in job.MediaFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(file.SourcePath))
                    continue;
                var path = Path.GetFullPath(file.SourcePath);
                // Match the same top-level source semantics as ReadUploadMediaSnapshot.
                // A sibling or nested folder must not import unrelated posting history.
                if (
                    File.Exists(path)
                    || !extensions.Contains(
                        Path.GetExtension(path),
                        StringComparer.OrdinalIgnoreCase
                    )
                    || !sources.Any(source =>
                        path.Equals(source, StringComparison.OrdinalIgnoreCase)
                        || Directory.Exists(source)
                            && string.Equals(
                                Path.GetDirectoryName(path),
                                source.TrimEnd(Path.DirectorySeparatorChar),
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                )
                    continue;
                var postedHere =
                    job.TargetId == target.PageId
                    && job.IsProfile == target.IsPrimaryProfile
                    && Type(job.PostType) == Type(type);
                removed[path] = removed.GetValueOrDefault(path) || postedHere;
            }
            catch (Exception ex)
                when (ex
                        is ArgumentException
                            or NotSupportedException
                            or System.Security.SecurityException
                ) { }
        }
        return removed;
    }

    public async Task<PFlowAvailableMedia> ReadAsync(
        FacebookPageTarget target,
        string type,
        IReadOnlyList<PFlowJob> history,
        CancellationToken ct = default
    )
    {
        var snapshot = target.ReadUploadMediaSnapshot(type);
        var removed = RemovedPostedSources(target, type, history, ct);
        var verifiedMissingSources = target
            .GetUploadMediaPaths(type)
            .Where(source => FullPath(source) is { } full && removed.ContainsKey(full))
            .Select(source => "Source missing or inaccessible: " + source);
        if (
            snapshot.Error.Length > 0
            && snapshot.Error != string.Join("; ", verifiedMissingSources)
        )
            return new([], snapshot.Files.Count, 0, 0, 0, snapshot.Error);
        var blocked = history
            .Where(j =>
                j.TargetId == target.PageId
                && j.IsProfile == target.IsPrimaryProfile
                && Type(j.PostType) == Type(type)
                && !j.CanRetry
            )
            .ToArray();
        var postedHashes = blocked
            .Where(j => j.State == PFlowState.Confirmed && j.Sha256.Length > 0)
            .SelectMany(j => j.MediaFiles.Select(f => f.Sha256))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heldHashes = blocked
            .Where(j => j.State != PFlowState.Confirmed && j.Sha256.Length > 0)
            .SelectMany(j => j.MediaFiles.Select(f => f.Sha256))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sizes = blocked
            .Where(j => j.Sha256.Length > 0 && j.Size > 0)
            .SelectMany(j => j.MediaFiles.Select(f => f.Size))
            .ToHashSet();
        var unknownSize = blocked.Any(j => j.Sha256.Length > 0 && j.Size <= 0);
        var heldPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var job in blocked
                .Where(j => j.Sha256.Length == 0)
                .SelectMany(j => j.MediaFiles)
                .Where(f => f.SourcePath.Length > 0)
        )
        {
            try
            {
                heldPaths.Add(Path.GetFullPath(job.SourcePath));
            }
            catch (Exception ex)
                when (ex
                        is ArgumentException
                            or NotSupportedException
                            or System.Security.SecurityException
                )
            {
                return new(
                    [],
                    snapshot.Files.Count,
                    0,
                    0,
                    0,
                    "Posting history contains an invalid media path; review it before preparing posts."
                );
            }
        }
        var available = new List<string>();
        int posted = 0,
            held = 0,
            unreadable = 0;
        if (_hashes.Count > 10000)
            _hashes.Clear();
        foreach (var path in snapshot.Files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length == 0)
                {
                    unreadable++;
                    continue;
                }
                if (heldPaths.Contains(file.FullName))
                {
                    held++;
                    continue;
                }
                // Different byte lengths cannot be identical media. Hash only candidates
                // that could match history, sharing the cache across Pages and scans.
                if (!unknownSize && !sizes.Contains(file.Length))
                {
                    using var probe = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    );
                    available.Add(path);
                    continue;
                }
                var key = (
                    file.FullName.ToUpperInvariant(),
                    file.Length,
                    file.LastWriteTimeUtc,
                    file.CreationTimeUtc
                );
                if (!_hashes.TryGetValue(key, out var hash))
                {
                    await using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        65536,
                        FileOptions.Asynchronous | FileOptions.SequentialScan
                    );
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                    file.Refresh();
                    if (
                        !file.Exists
                        || file.Length != key.Length
                        || file.LastWriteTimeUtc != key.LastWriteTimeUtc
                    )
                    {
                        unreadable++;
                        continue;
                    }
                    _hashes[key] = hash;
                }
                if (postedHashes.Contains(hash))
                    posted++;
                else if (heldHashes.Contains(hash))
                    held++;
                else
                    available.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable++;
            }
        }
        return new(
            available,
            snapshot.Files.Count + removed.Count,
            posted + removed.Count(p => p.Value),
            held,
            unreadable,
            ""
        );
    }
}
