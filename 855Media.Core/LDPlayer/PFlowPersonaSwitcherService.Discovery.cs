using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    internal static string[] DiscoveryPageNames(string? xml) =>
        !IsNativeSwitcherSheetOpen(xml) || NativeNavigationBlocked(xml)
            ? []
            : FindNativeSwitcherRows(xml)
                .Where(IsNativePageRow)
                .Select(row =>
                    Regex.Replace(
                        row.Attribute("content-desc")?.Value?.Trim() ?? "",
                        @",\s*\d+\s+notifications?\s*$",
                        "",
                        RegexOptions.IgnoreCase
                    )
                )
                .Where(n =>
                    n.Length > 0 && !n.EndsWith('…') && !n.EndsWith("...", StringComparison.Ordinal)
                )
                .ToArray();

    // Warming is navigation only. Root records never grant live Page access.
    public async Task WarmPageDiscoveryAsync(
        int index,
        string ownerName,
        string ownerUid,
        string? path,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!await EnsureSwitchedToPersonalProfileAsync(index, ownerName, path, ct))
                throw new InvalidOperationException(
                    "Personal profile could not be restored before Page discovery."
                );
            var identity = await new PFlowGraphIdentityReader().TryReadUidAsync(
                _ldPlayer,
                index,
                path,
                ct
            );
            if (identity?.Id != ownerUid)
                throw new InvalidOperationException(
                    "Live personal UID could not be confirmed before Page discovery."
                );
            if (
                !await OpenFacebookMenuFromFeedAsync(index, path, ct)
                || !await ClickProfileSwitcherDropdownAsync(index, path, ct)
            )
                throw new InvalidOperationException("Page switcher could not be opened.");
            string? previous = null;
            var complete = false;
            for (var round = 0; round < 60; round++)
            {
                ct.ThrowIfCancellationRequested();
                var screen = await ReadNativeScreenAsync(index, path, ct);
                if (
                    screen == null
                    || !IsNativeSwitcherSheetOpen(screen.RawXml)
                    || NativeNavigationBlocked(screen.RawXml)
                )
                    throw new InvalidOperationException(
                        "Page discovery left the switcher; enumeration is incomplete."
                    );
                var rows = FindNativeSwitcherRows(screen.RawXml);
                if (rows.Any() && !rows.Any(IsNativePageRow))
                    throw new InvalidOperationException(
                        "Switcher has no explicit Page labels; automatic enumeration is unresolved. Existing cached Pages are retained."
                    );
                if (rows.Where(IsNativePageRow).Count() > DiscoveryPageNames(screen.RawXml).Length)
                    await progress(
                        "Some Page names are truncated or unreadable; those entries remain unresolved."
                    );
                var visible = DiscoveryPageNames(screen.RawXml);
                foreach (var label in visible)
                    names.Add(label);
                var signature = string.Join(
                    "\n",
                    FindNativeSwitcherRows(screen.RawXml)
                        .Select(r => r.Attribute("content-desc")?.Value)
                );
                if (signature == previous)
                {
                    complete = true;
                    break;
                }
                previous = signature;
                var (w, h) = ParseScreenDimensions(screen.RawXml);
                if (
                    w <= 0
                    || h <= 0
                    || !await _ldPlayer.SwipeAsync(
                        index,
                        w / 2,
                        h * 4 / 5,
                        w / 2,
                        h / 3,
                        450,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException("Page switcher scrolling failed.");
                await _nativeDelay(TimeSpan.FromMilliseconds(700), ct);
            }
            await progress(
                $"Page switcher: {names.Count} distinct full names; list scan {(complete ? "finished" : "limit reached, incomplete")}."
            );
            var warmed = 0;
            foreach (var page in names)
            {
                await progress($"Discovering Page {++warmed}/{names.Count}: {page}");
                var switched = await SwitchToPersonaAsync(
                    index,
                    new FacebookPageTarget { Type = "Page", PageName = page },
                    path,
                    ct
                );
                if (!switched)
                {
                    await progress($"Unresolved Page: {page}; switch could not be confirmed.");
                    continue;
                }
                var live = await new PFlowGraphIdentityReader().TryReadUidAsync(
                    _ldPlayer,
                    index,
                    path,
                    ct
                );
                var records = await _ldPlayer.ExtractFacebookAccountsFromRootShellAsync(
                    index,
                    "com.facebook.katana",
                    path,
                    ct
                );
                var matches = records
                    .Where(a =>
                        a.Type == "Page"
                        && a.UserUid == ownerUid
                        && (a.Uid == live?.Id || a.PageId == live?.Id)
                    )
                    .ToArray();
                await progress(
                    matches.Length == 1 && live?.UnderlyingUid == ownerUid
                        ? $"Page identity resolved: {page}."
                        : $"Unresolved Page: {page}; live identity or cached owner could not be matched. Existing Page verification will retry."
                );
            }
        }
        finally
        {
            // Stop/error must not leave an arbitrary Page silently active.
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (!await EnsureSwitchedToPersonalProfileAsync(index, ownerName, path, restore.Token))
                throw new InvalidOperationException(
                    "Personal profile could not be restored after Page discovery; fetch stopped."
                );
        }
    }
}
