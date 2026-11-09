using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowNativeFetchDevice
{
    internal async Task PrepareSwitcherAfterLinkLookupAsync(
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!_linkLookupNeedsFeed)
            return;
        await ReturnToFeedAfterPersonalDetailsAsync(ct, delay);
        // A failed return must not permit selection from the stale navigation stack.
        _linkLookupNeedsFeed = false;
    }

    internal async Task ReturnToFeedAfterPersonalDetailsAsync(
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= Task.Delay;
        await RequireAsync(ct);
        var current = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        if (
            !PFlowPostingNameVerification.SafeNameResponseContext(current, "com.facebook.katana")
            || PFlowConfirmation.HasPendingPublication(current)
        )
            throw new InvalidOperationException(
                "Facebook's screen changed before returning to Feed; Fetch stopped."
            );
        if (!await ld.OpenDeepLinkAsync(index, "fb://feed", "com.facebook.katana", path, ct))
            throw new InvalidOperationException(
                "Could not return to Feed before continuing Fetch."
            );
        for (var poll = 0; poll < 20; poll++)
        {
            await delay(TimeSpan.FromMilliseconds(500), ct);
            await RequireTransitionAsync(ct);
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
            if (PFlowConfirmation.HasPendingPublication(xml))
                throw new InvalidOperationException(
                    "An upload appeared while returning to Feed; Fetch stopped."
                );
            if (FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
            {
                _linkLookupNeedsFeed = false;
                return;
            }
        }
        throw new InvalidOperationException(
            "Feed did not become ready before continuing Fetch; retry Fetch."
        );
    }
}
