using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Native implementation for legacy public discovery entry points. Desktop Fetch
// owns its lease/history guards and uses the same domain service directly.
internal static class PFlowNativeDiscovery
{
    internal static async Task<FacebookAccountDetails?> ReadAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        string? storagePath = null
    )
    {
        ct.ThrowIfCancellationRequested();
        var instance = (await ld.GetInstancesAsync(path, ct)).Single(i => i.Index == index);
        var saved = (await PFlowPageStorage.LoadPagesAsync(storagePath))
            .Where(p =>
                p.AppClone == "FB-01"
                && (p.DeviceName == instance.Name || p.DeviceName == "LD-" + index)
            )
            .ToArray();
        using var policy = new PFlowNativeFetchScope(ld, index, path);
        var result = await new PFlowNativeFetch(
            new PFlowNativeFetchDevice(ld, index, path, _ => Task.CompletedTask)
        ).RunAsync(saved, _ => Task.CompletedTask, ct);
        if (result.Details != null)
            result.Details.DeviceName = instance.Name;
        return result.Details;
    }

    internal static async Task<List<FacebookAccountInfo>> ReadAccountsAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        string? storagePath = null
    )
    {
        var details = await ReadAsync(ld, index, path, ct, storagePath);
        if (details == null)
            return [];
        return new[]
        {
            new FacebookAccountInfo
            {
                Uid = details.Uid,
                UserUid = details.Uid,
                Name = details.Name,
                Type = "Profile",
                IsActive = true,
            },
        }
            .Concat(
                details.Pages.Select(p => new FacebookAccountInfo
                {
                    Uid = string.IsNullOrWhiteSpace(p.PersonaUid) ? p.PageId : p.PersonaUid,
                    PageId = p.PageId,
                    UserUid = details.Uid,
                    Name = p.PageName,
                    Type = "Page",
                })
            )
            .ToList();
    }
}
