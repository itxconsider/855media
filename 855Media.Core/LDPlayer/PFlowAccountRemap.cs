using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowAccountRemap
{
    public static int ReplayQueuedTasks(
        IEnumerable<PFlowPersonalAccount> accounts,
        IReadOnlyList<PFlowAccountBinding> removed,
        IList<AutoPostTaskItem> tasks
    )
    {
        static bool Matches(PFlowAccountBinding b, AutoPostTaskItem t) =>
            t.App == "FB-01"
            && (
                t.AccountBinding == b
                || t.AccountBinding == null
                    && t.OwnerUid == b.OwnerUid
                    && t.DeviceName == b.DeviceName
            );
        var changed = 0;
        foreach (var task in tasks.Where(t => t.Status == "Queued").ToArray())
        {
            if (removed.Any(b => Matches(b, task)))
            {
                tasks.Remove(task);
                changed++;
                continue;
            }
            var owners = accounts
                .Where(a => a.TransferredFromBindings.Any(b => Matches(b, task)))
                .ToArray();
            if (owners.Length != 1)
                continue;
            var next = owners[0].Binding;
            task.DeviceName = next.DeviceName;
            task.AccountBinding = next;
            task.OwnerUid = next.OwnerUid;
            task.PFlowJobId = "";
            tasks[tasks.IndexOf(task)] = task;
            changed++;
        }
        return changed;
    }

    public static void RequireSameAccount(PFlowAccountBinding before, PFlowAccountBinding after)
    {
        before.Validate();
        after.Validate();
        if (before.OwnerUid != after.OwnerUid || before.AppPackage != after.AppPackage)
            throw new InvalidOperationException("Transfer must retain the personal UID and app.");
    }

    public static bool OwnsRow(PFlowAccountBinding binding, FacebookPageTarget row) =>
        row.UserUid == binding.OwnerUid
        && row.AppClone == "FB-01"
        && (
            row.AccountBinding == binding
            || row.AccountBinding == null && row.DeviceName == binding.DeviceName
        );

    public static void RequireSameAssignment(PFlowAccountBinding before, PFlowAccountBinding after)
    {
        before.Validate();
        after.Validate();
        if (
            before.DeviceIndex != after.DeviceIndex
            || before.AppPackage != after.AppPackage
            || before.OwnerUid != after.OwnerUid
        )
            throw new InvalidOperationException(
                "Remapping cannot move an account to another LD index, app or personal UID."
            );
    }

    // Replay only persisted explicit remaps, and only onto the currently verified assignment.
    public static int ReplayRows(
        IEnumerable<PFlowPersonalAccount> accounts,
        IEnumerable<FacebookPageTarget> rows,
        IReadOnlyList<LDInstanceInfo> instances,
        string ldDirectory,
        Action<PFlowMappingIssue>? reportIssue = null
    )
    {
        var changes = new List<(FacebookPageTarget Row, PFlowAccountBinding Binding)>();
        var all = rows.ToArray();
        foreach (
            var account in accounts.Where(a =>
                a.PreviousBindings.Count > 0 || a.TransferredFromBindings.Count > 0
            )
        )
        {
            try
            {
                PFlowAccountStore.RequireCurrentBinding(account.Binding, instances, ldDirectory);
            }
            catch (Exception ex)
                when (ex
                        is InvalidOperationException
                            or System.IO.IOException
                            or System.Text.Json.JsonException
                            or UnauthorizedAccessException
                )
            {
                continue;
            }
            var local = new List<(FacebookPageTarget Row, PFlowAccountBinding Binding)>();
            try
            {
                foreach (
                    var row in all.Where(r =>
                        r.UserUid == account.Binding.OwnerUid && r.AppClone == "FB-01"
                    )
                )
                {
                    if (
                        !account
                            .PreviousBindings.Concat(account.TransferredFromBindings)
                            .Any(b =>
                                row.AccountBinding == b
                                || row.AccountBinding == null && row.DeviceName == b.DeviceName
                            )
                    )
                        continue;
                    if (row.AccountBinding == null)
                    {
                        var index = PFlowDeviceRouting.Resolve(row.DeviceName, instances);
                        if (
                            index >= 0
                            && index != account.Binding.DeviceIndex
                            && !account.TransferredFromBindings.Any(b => OwnsRow(b, row))
                        )
                            throw new InvalidOperationException(
                                "Legacy remap row now belongs to another LD; assign its binding explicitly."
                            );
                    }
                    local.Add((row, account.Binding));
                }
                changes.AddRange(local);
            }
            catch (InvalidOperationException ex) when (reportIssue != null)
            {
                reportIssue(new(account.Binding.DeviceName, account.Binding.OwnerUid, ex.Message));
            }
        }
        var ambiguous = changes
            .GroupBy(c => c.Row)
            .Where(g => g.Select(c => c.Binding).Distinct().Count() > 1)
            .SelectMany(g => g.Select(c => c.Binding))
            .Distinct()
            .ToArray();
        if (ambiguous.Length > 0)
        {
            if (reportIssue == null)
                throw new InvalidOperationException(
                    "Ambiguous remap row; no changes were applied."
                );
            foreach (var binding in ambiguous)
                reportIssue(
                    new(
                        binding.DeviceName,
                        binding.OwnerUid,
                        "Ambiguous remap row; this account's rows were not changed."
                    )
                );
            changes.RemoveAll(c => ambiguous.Contains(c.Binding));
        }
        foreach (var change in changes.Distinct())
        {
            change.Row.AccountBinding = change.Binding;
            change.Row.DeviceName = change.Binding.DeviceName;
            change.Row.IsSelected = false;
            change.Row.LivePageAccessVerified = false;
        }
        return changes.Select(c => c.Row).Distinct().Count();
    }
}

public sealed partial class PFlowAccountStore
{
    public Task<string> RemapAsync(
        PFlowAccountBinding expected,
        PFlowAccountBinding replacement,
        CancellationToken ct = default
    )
    {
        PFlowAccountRemap.RequireSameAssignment(expected, replacement);
        return ChangeBindingAsync(expected, replacement, false, ct);
    }

    public Task<string> TransferAsync(
        PFlowAccountBinding expected,
        PFlowAccountBinding replacement,
        CancellationToken ct = default
    )
    {
        PFlowAccountRemap.RequireSameAccount(expected, replacement);
        if (expected.DeviceIndex == replacement.DeviceIndex)
            throw new InvalidOperationException("Choose a different destination LD.");
        return ChangeBindingAsync(expected, replacement, true, ct);
    }

    private async Task<string> ChangeBindingAsync(
        PFlowAccountBinding expected,
        PFlowAccountBinding replacement,
        bool transfer,
        CancellationToken ct
    )
    {
        if (expected == replacement)
            throw new InvalidOperationException("The assigned mapping has not changed.");
        var snapshot =
            (await LoadAsync(ct)).SingleOrDefault(a => a.Binding == expected)
            ?? throw new InvalidOperationException(
                "Saved mapping changed. Refresh and review again."
            );
        var oldCredential = snapshot.CredentialReference;
        var replacementCredential = "";
        if (oldCredential.Length > 0)
        {
            using var credential = await _credentials.LoadAsync(expected, oldCredential, ct);
            replacementCredential = await _credentials.SaveAsync(replacement, credential, ct);
        }
        var committed = false;
        try
        {
            await UpdateAsync(
                accounts =>
                {
                    var account =
                        accounts.SingleOrDefault(a => a.Binding == expected)
                        ?? throw new InvalidOperationException(
                            "Saved mapping changed. Refresh and review again."
                        );
                    if (
                        accounts.Any(a =>
                            a != account
                            && a.Binding.OwnerUid == replacement.OwnerUid
                            && a.Binding.AppPackage == replacement.AppPackage
                            && a.Binding.DeviceIndex == replacement.DeviceIndex
                        )
                    )
                        throw new InvalidOperationException("Replacement mapping already exists.");
                    if (account.CredentialReference != oldCredential)
                        throw new InvalidOperationException(
                            "Stored credentials changed during remapping; refresh and review again."
                        );
                    account.PreviousBindings.Remove(replacement);
                    if (!account.PreviousBindings.Contains(expected))
                        account.PreviousBindings.Add(expected);
                    if (transfer)
                    {
                        var origins = account
                            .PreviousBindings.Concat(account.TransferredFromBindings)
                            .Distinct()
                            .Where(b => b != replacement)
                            .ToArray();
                        account.PreviousBindings = origins
                            .Where(b => b.DeviceIndex == replacement.DeviceIndex)
                            .ToList();
                        account.TransferredFromBindings = origins
                            .Where(b => b.DeviceIndex != replacement.DeviceIndex)
                            .ToList();
                    }
                    account.Binding = replacement;
                    account.CredentialReference = replacementCredential;
                    if (replacementCredential.Length == 0)
                    {
                        account.AllowAutomaticLogin = false;
                        account.AllowAccountRotation = false;
                    }
                    account.Authentication = PFlowAuthenticationState.Cached;
                    account.LastVerifiedUtc = null;
                    account.SelectedForDiscovery =
                        account.SelectedForScreenTime =
                        account.SelectedForPosting =
                            false;
                    foreach (var page in account.Pages)
                    {
                        page.State = PFlowPageAccessState.Cached;
                        page.LastVerifiedUtc = null;
                        page.VerifiedRunId = "";
                    }
                },
                ct
            );
            committed = true;
        }
        finally
        {
            if (!committed && replacementCredential.Length > 0)
            {
                try
                {
                    _credentials.Delete(replacementCredential);
                }
                catch
                { /* Keep the original credential reference authoritative. */
                }
            }
        }
        return oldCredential;
    }
}
