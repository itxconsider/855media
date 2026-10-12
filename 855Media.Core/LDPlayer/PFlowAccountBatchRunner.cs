using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowAccountBatchRunner
{
    // The device lifecycle owns one exclusive lease across every account and its tasks.
    public static async Task RunAsync(
        IReadOnlyList<PFlowPersonalAccount> accounts,
        System.Func<PFlowPersonalAccount, CancellationToken, Task> ensure,
        System.Func<PFlowPersonalAccount, CancellationToken, Task> screenTime,
        System.Func<PFlowPersonalAccount, CancellationToken, Task> post,
        System.Func<PFlowPersonalAccount, CancellationToken, Task> returnPersonal,
        System.Func<CancellationToken, Task> requireIdle,
        CancellationToken ct,
        bool returnLastPersonal = true
    )
    {
        for (var index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];
            ct.ThrowIfCancellationRequested();
            await requireIdle(ct);
            await ensure(account, ct);
            if (account.SelectedForScreenTime)
                await screenTime(account, ct);
            await requireIdle(ct);
            if (account.SelectedForPosting)
                await post(account, ct);
            // Never leave an unresolved composer/upload to rotate to another login.
            await requireIdle(ct);
            if (index < accounts.Count - 1 || returnLastPersonal)
                await returnPersonal(account, ct);
        }
    }
}
