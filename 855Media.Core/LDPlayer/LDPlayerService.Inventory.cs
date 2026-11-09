using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    private static readonly LDInventoryScanCoordinator InventoryScans = new();
    private static readonly LDInventoryCommandCoordinator InventoryCommands = new();

    public Task<IReadOnlyList<LDInstanceInfo>> GetAssignedInstancesAsync(
        int index,
        string? path,
        CancellationToken ct
    ) => ScanInventoryAsync(ResolveLdConsolePath(path), assignedIndex: index, caller: ct);

    internal static async Task<IReadOnlyList<LDInstanceInfo>> ScanInventoryAsync(
        string console,
        Func<CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>? command = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int? assignedIndex = null,
        CancellationToken caller = default
    )
    {
        if (!File.Exists(console))
            throw new LDInventoryUnavailableException("console-missing");
        caller.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(TimeSpan.FromSeconds(7));
        command ??= ct =>
            InventoryCommands.ReadAsync(
                console,
                token => RunProcessAsync(console, "list2", token),
                ct
            );
        delay ??= Task.Delay;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var reason = "scan-timeout";
            var exit = -1;
            var stdoutLength = 0;
            var stderrLength = 0;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var response = await command(timeout.Token);
                exit = response.ExitCode;
                stdoutLength = response.StdOut.Length;
                stderrLength = response.StdErr.Length;
                if (exit != 0)
                    reason = "nonzero-exit";
                else
                    return assignedIndex is { } selected
                        ? ParseAssignedInventory(response.StdOut, selected)
                        : ParseInventory(response.StdOut);
            }
            catch (LDInventoryUnavailableException ex)
            {
                reason = ex.Reason;
            }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested)
            {
                reason = "scan-timeout";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                reason = "scan-process-failed";
            }
            await PFlowTrace.SessionAsync(
                -1,
                "ld.inventory.retry",
                $"installation={Path.GetFileName(Path.GetDirectoryName(console))}; attempt={attempt}/3; reason={reason}; exit={exit}; stdoutChars={stdoutLength}; stderrChars={stderrLength}",
                CancellationToken.None
            );
            if (attempt == 3 || deadline.IsCancellationRequested)
                throw new LDInventoryUnavailableException(reason);
            try
            {
                await delay(TimeSpan.FromMilliseconds(400), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                caller.ThrowIfCancellationRequested();
                throw new LDInventoryUnavailableException(reason);
            }
        }
        throw new LDInventoryUnavailableException("scan-unavailable");
    }

    internal static IReadOnlyList<LDInstanceInfo> ParseAssignedInventory(string stdout, int index)
    {
        if (index < 0)
            throw new LDInventoryUnavailableException("invalid-assigned-index");
        var lines = stdout.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (lines.Length == 0)
            throw new LDInventoryUnavailableException("empty-response");
        var indices = new HashSet<int>();
        string? assigned = null;
        var ignored = 0;
        foreach (var line in lines)
        {
            var first = line.Split(',')[0];
            if (
                !int.TryParse(
                    first,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var rowIndex
                )
                || rowIndex < 0
            )
                throw new LDInventoryUnavailableException(
                    "unidentifiable-row; assigned-index-not-provable"
                );
            if (!indices.Add(rowIndex))
                throw new LDInventoryUnavailableException("duplicate-indices");
            if (rowIndex == index)
                assigned = line;
            else
            {
                try
                {
                    ParseInventory(line);
                }
                catch (LDInventoryUnavailableException)
                {
                    ignored++;
                }
            }
        }
        if (assigned == null)
            throw new LDInventoryUnavailableException("assigned-index-missing");
        var result = ParseInventory(assigned); // Never relax the assigned row's fields.
        if (ignored > 0)
            _ = PFlowTrace.SessionAsync(
                index,
                "ld.inventory.assigned-row",
                $"fresh=true; assignedRowValid=true; unrelatedMalformedRows={ignored}; completeInventory=false; launchAuthorized=false",
                CancellationToken.None
            );
        return result;
    }

    internal static IReadOnlyList<LDInstanceInfo> ParseInventory(string stdout)
    {
        var lines = stdout.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (lines.Length == 0)
            throw new LDInventoryUnavailableException("empty-response");
        var instances = new List<LDInstanceInfo>();
        foreach (var line in lines)
        {
            var p = line.Split(',');
            if (
                p.Length < 7
                || !int.TryParse(
                    p[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index
                )
                || index < 0
                || string.IsNullOrWhiteSpace(p[1])
                || !long.TryParse(
                    p[2],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var top
                )
                || !long.TryParse(
                    p[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var bind
                )
                || p[4].Trim() is not ("0" or "1")
                || !int.TryParse(
                    p[5],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var pid
                )
                || !int.TryParse(
                    p[6],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var vm
                )
            )
                throw new LDInventoryUnavailableException(
                    $"malformed-response; lines={lines.Length}; parsed={instances.Count}; fields={p.Length}"
                );
            int Optional(int position, int fallback) =>
                p.Length > position
                && int.TryParse(
                    p[position],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value
                )
                && value > 0
                    ? value
                    : fallback;
            instances.Add(
                new()
                {
                    Index = index,
                    Name = p[1].Trim(),
                    TopWindowHandle = top,
                    BindWindowHandle = bind,
                    IsAndroidStarted = p[4].Trim() == "1",
                    Pid = pid,
                    VboxPid = vm,
                    Width = Optional(7, 720),
                    Height = Optional(8, 1280),
                    Dpi = Optional(9, 320),
                    Status = p[4].Trim() == "1" ? "Running" : "Idle",
                }
            );
        }
        if (instances.Select(i => i.Index).Distinct().Count() != instances.Count)
            throw new LDInventoryUnavailableException("duplicate-indices");
        return instances;
    }
}
