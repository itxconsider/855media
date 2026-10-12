using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowAccountSelectionDraft(
    PFlowAccountBinding Binding,
    bool Fetch,
    bool ScreenTime,
    bool Posting,
    bool AutomaticLogin,
    bool AccountRotation,
    int Order
);

public static class PFlowAccountSelections
{
    // Validate the complete draft before changing any account. A reset/remap cannot
    // silently redirect selections or enable login using the old device's secret.
    public static void Apply(
        IReadOnlyList<PFlowPersonalAccount> accounts,
        IEnumerable<PFlowAccountSelectionDraft> selections
    )
    {
        var drafts = selections.ToArray();
        if (drafts.GroupBy(s => s.Binding).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Duplicate account selections; refresh mappings.");
        var changes = drafts
            .Select(s =>
                (
                    Account: accounts.SingleOrDefault(a => a.Binding == s.Binding)
                        ?? throw new InvalidOperationException(
                            $"{s.Binding.DeviceName}: account mapping changed; refresh mappings before running."
                        ),
                    Selection: s
                )
            )
            .ToArray();
        foreach (var (account, selection) in changes)
        {
            var label = account.Binding.DeviceName + " / " + account.Name;
            if (selection.AutomaticLogin && string.IsNullOrWhiteSpace(account.CredentialReference))
                throw new InvalidOperationException(
                    label
                        + ": store login credentials or turn off Automatic login to use an existing Facebook session."
                );
            if (selection.AccountRotation && !selection.AutomaticLogin)
                throw new InvalidOperationException(
                    label + ": account rotation requires automatic login."
                );
            if (selection.Order < 0)
                throw new InvalidOperationException(label + ": run order must be zero or greater.");
        }
        foreach (var (account, selection) in changes)
        {
            account.SelectedForDiscovery = selection.Fetch;
            account.SelectedForScreenTime = selection.ScreenTime;
            account.SelectedForPosting = selection.Posting;
            account.AllowAutomaticLogin = selection.AutomaticLogin;
            account.AllowAccountRotation = selection.AccountRotation;
            account.RotationOrder = selection.Order;
        }
    }
}
