using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    // P-Flow verifies the root owner once before invoking this native switcher.
    public async Task<bool> SwitchToPersonaAsync(
        int index,
        FacebookPageTarget target,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        return await new PFlowPersonaSwitcherService(this, _atxClient).SwitchToPersonaAsync(
            index,
            target,
            customPath,
            cancellationToken
        );
    }

    public async Task<bool> SwitchToPersonaAsync(
        int index,
        string name,
        string? pageId,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        var saved = await PFlowPageStorage.LoadPagesAsync();
        var target = saved.FirstOrDefault(p =>
            p.PageId == pageId && p.PageName == name && p.DeviceName == $"LD-{index}"
        );
        return target != null
            && await SwitchToPersonaAsync(index, target, customPath, cancellationToken);
    }
}
