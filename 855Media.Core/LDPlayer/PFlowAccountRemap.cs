using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowAccountRemap
{
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
        foreach (var account in accounts.Where(a => a.PreviousBindings.Count > 0))
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
                        !account.PreviousBindings.Any(b =>
                            row.AccountBinding == b
                            || row.AccountBinding == null && row.DeviceName == b.DeviceName
                        )
                    )
                        continue;
                    if (row.AccountBinding == null)
                    {
                        var index = PFlowDeviceRouting.Resolve(row.DeviceName, instances);
                        if (index >= 0 && index != account.Binding.DeviceIndex)
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
        }
        return changes.Select(c => c.Row).Distinct().Count();
    }
}

public sealed partial class PFlowAccountStore
{
    public async Task<string> RemapAsync(
        PFlowAccountBinding expected,
        PFlowAccountBinding replacement,
        CancellationToken ct = default
    )
    {
        PFlowAccountRemap.RequireSameAssignment(expected, replacement);
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
                    if (accounts.Any(a => a != account && a.Binding == replacement))
                        throw new InvalidOperationException("Replacement mapping already exists.");
                    if (account.CredentialReference != oldCredential)
                        throw new InvalidOperationException(
                            "Stored credentials changed during remapping; refresh and review again."
                        );
                    account.PreviousBindings.Remove(replacement);
                    if (!account.PreviousBindings.Contains(expected))
                        account.PreviousBindings.Add(expected);
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
