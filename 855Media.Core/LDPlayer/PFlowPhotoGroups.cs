using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowPhotoGroups
{
    // Application batch bound, not a claim about Facebook's maximum.
    public const int MaxPhotos = 10;

    public static IReadOnlyList<string[]> Group(IEnumerable<string> files, bool useTextFile) =>
        files
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .GroupBy(
                p => CaptionKey(PFlowMediaCaption.Resolve(p, useTextFile)),
                StringComparer.Ordinal
            )
            .SelectMany(g => g.Chunk(MaxPhotos))
            .ToArray();

    public static string CaptionKey(string caption) =>
        caption.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    public static bool ComposerCountMatches(string xml, int count)
    {
        if (count < 2 || count > MaxPhotos)
            return false;
        var counts = ComposerCounts(xml);
        return counts.Length == 1 && counts[0] == count;
    }

    internal static bool ComposerMatchesGroup(string xml, PFlowJob job)
    {
        if (
            !job.IsPhotoGroup
            || !job.MediaSelectionEvidence.StartsWith(
                $"Native photo album verified: Photo_{job.JobId}; count={job.PhotoFiles.Count}; all selected once",
                StringComparison.Ordinal
            )
        )
            return false;
        var counts = ComposerCounts(xml);
        return counts.Length == 0 || counts.Length == 1 && counts[0] == job.PhotoFiles.Count;
    }

    private static int[] ComposerCounts(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value ?? "", n.Attribute("content-desc")?.Value ?? "" }
            )
            .SelectMany(s =>
                Regex.Matches(
                    s.Trim(),
                    @"^(?:(\d+) photos?(?: selected| attached)?|Edit all \((\d+)\)|Photo \d+ of (\d+)(?:, expand photo)?)$",
                    RegexOptions.IgnoreCase
                )
            )
            .Select(m => int.Parse(m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value))
            .Distinct()
            .ToArray();
}

public sealed class PFlowPhotoFile
{
    public string SourcePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string CaptionFileSha256 { get; set; } = "";
    public string BackupPath { get; set; } = "";
}

public sealed partial class PFlowJob
{
    public List<PFlowPhotoFile> PhotoFiles { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPhotoGroup => PostType == "Picture" && PhotoFiles.Count > 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<PFlowPhotoFile> MediaFiles =>
        IsPhotoGroup
            ? PhotoFiles
            :
            [
                new()
                {
                    SourcePath = SourcePath,
                    Sha256 = Sha256,
                    Size = Size,
                    ModifiedUtc = ModifiedUtc,
                    CaptionFileSha256 = CaptionFileSha256,
                },
            ];

    public bool IncludesSource(string path) =>
        MediaFiles.Any(f =>
            string.Equals(
                Path.GetFullPath(f.SourcePath),
                Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase
            )
        );

    public bool OverlapsMedia(PFlowJob other) =>
        TargetId == other.TargetId
        && IsProfile == other.IsProfile
        && PostType == other.PostType
        && MediaFiles.Any(f =>
            f.Sha256.Length > 0
            && other.MediaFiles.Any(o =>
                o.Sha256.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)
            )
        );

    public string PhotoRemotePath(int index) =>
        $"/sdcard/DCIM/855Media/Photo_{JobId}/{index + 1:D3}{Path.GetExtension(MediaFiles[index].SourcePath).ToLowerInvariant()}";

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> RemoteMediaPaths =>
        IsPhotoGroup
            ? Enumerable.Range(0, PhotoFiles.Count).Select(PhotoRemotePath).ToArray()
            : [RemotePath];

    private async Task FingerprintPhotoGroupAsync(CancellationToken ct)
    {
        if (
            !IsPhotoGroup
            || PhotoFiles.Count > PFlowPhotoGroups.MaxPhotos
            || PhotoFiles.Any(f =>
                !FacebookPageTarget.PhotoExtensions.Contains(
                    Path.GetExtension(f.SourcePath),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            || PhotoFiles
                .Select(f => Path.GetFullPath(f.SourcePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != PhotoFiles.Count
            || !string.Equals(
                Path.GetFullPath(SourcePath),
                Path.GetFullPath(PhotoFiles[0].SourcePath),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new InvalidDataException(
                "Invalid grouped photo snapshot; prepare the queue again."
            );
        var snapshots = new List<PFlowPhotoFile>();
        foreach (var file in PhotoFiles)
        {
            var single = new PFlowJob { SourcePath = file.SourcePath, PostType = "Picture" };
            await FingerprintAsync(single, ct);
            if (
                file.Sha256.Length > 0
                && !file.Sha256.Equals(single.Sha256, StringComparison.OrdinalIgnoreCase)
            )
                throw new IOException(
                    "A grouped photo changed after preparation; rebuild the job."
                );
            snapshots.Add(
                new()
                {
                    SourcePath = single.SourcePath,
                    Sha256 = single.Sha256,
                    Size = single.Size,
                    ModifiedUtc = single.ModifiedUtc,
                    CaptionFileSha256 = single.CaptionFileSha256,
                }
            );
        }
        PhotoFiles = snapshots;
        SourcePath = snapshots[0].SourcePath;
        Size = snapshots.Sum(f => f.Size);
        ModifiedUtc = snapshots.Max(f => f.ModifiedUtc);
        CaptionFileSha256 = "";
        Sha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    "PhotoGroup\n"
                        + string.Join(
                            "\n",
                            snapshots.Select(f => f.Sha256).OrderBy(s => s, StringComparer.Ordinal)
                        )
                )
            )
        );
    }
}
