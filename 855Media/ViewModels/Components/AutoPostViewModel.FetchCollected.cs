using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private static bool SameCollectedName(string first, string second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && string.Concat(first.Where(c => !char.IsWhiteSpace(c)))
            .Equals(
                string.Concat(second.Where(c => !char.IsWhiteSpace(c))),
                StringComparison.OrdinalIgnoreCase
            );

    private async Task ApplyFetchCollectedAsync(
        LDInstanceItemViewModel worker,
        PFlowFetchCollected update,
        long removalRevision,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var removals = await _accountStore.ReadRemovalStateAsync(ct);
        if (removals.Revision != removalRevision)
            throw new InvalidOperationException(
                "Account choices changed during Fetch; collected data was retained."
            );
        // Explicit removed-account restoration still happens only at successful completion.
        if (
            removals.Bindings.Any(b =>
                b.DeviceIndex == worker.Index
                && (update.OwnerId == null || b.OwnerUid == update.OwnerId)
            )
        )
            return;
        PFlowAccountBinding? binding = null;
        if (update.OwnerId != null)
        {
            binding = new(
                PFlowDeviceRouting.ReadIdentity(LdFolder, worker.Index),
                worker.Index,
                worker.Name,
                "com.facebook.katana",
                update.OwnerId
            );
            PFlowAccountStore.RequireCurrentBinding(
                binding,
                (await ReadAssignedFetchInstanceAsync(worker.Index, ct)).ToArray(),
                LdFolder
            );
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var actor in update.Actors.Where(a => a.Type == "Page"))
            {
                // A visible saved name already serves as the early placeholder.
                // Wait for owner proof before deciding whether it is this account's row.
                if (
                    update.OwnerId == null
                    && Pages.Any(p =>
                        p.DeviceName == worker.Name
                        && p.AppClone == worker.FbClone
                        && SameCollectedName(p.PageName, actor.Name)
                    )
                )
                    continue;
                var matches = Pages
                    .Where(p =>
                        p.DeviceName == worker.Name
                        && p.AppClone == worker.FbClone
                        && SameCollectedName(p.PageName, actor.Name)
                        && (
                            p.Model.FetchDetailsPending && p.Model.ManagedBy == update.OwnerName
                            || update.OwnerId != null && p.UserUid == update.OwnerId
                        )
                    )
                    .ToArray();
                if (matches.Length > 1)
                    continue;
                var row = matches.SingleOrDefault();
                if (row == null)
                {
                    row = new(
                        new FacebookPageTarget
                        {
                            DeviceName = worker.Name,
                            AppClone = worker.FbClone,
                            PageName = actor.Name,
                            Type = "Page",
                            ManagedBy = update.OwnerName,
                            FetchDetailsPending = true,
                            LivePageAccessVerified = false,
                            IsSelected = false,
                        },
                        Pages.Count + 1
                    );
                    Pages.Add(row);
                }
                if (
                    binding != null
                    && (
                        row.Model.AccountBinding != null && row.Model.AccountBinding != binding
                        || !string.IsNullOrWhiteSpace(row.Model.UserUid)
                            && row.Model.UserUid != binding.OwnerUid
                    )
                )
                    throw new InvalidOperationException(
                        "Collected Page belongs to a different saved account mapping."
                    );
                if (binding != null)
                {
                    row.UserUid = binding.OwnerUid;
                    row.Model.UserUid = binding.OwnerUid;
                    row.Model.AccountBinding = binding;
                }
                if (update.Page is not { } page || !SameCollectedName(page.PageName, actor.Name))
                    continue;
                row.UserUid = update.OwnerId!;
                row.PageId = page.PageId;
                row.PersonaUid = page.PersonaUid;
                row.Model.UserUid = update.OwnerId!;
                row.Model.PageId = page.PageId;
                row.Model.PersonaUid = page.PersonaUid;
                row.Model.AccountBinding = binding;
                row.Model.FetchDetailsPending = page.LivePageAccessVerified != true;
                row.Model.LivePageAccessVerified = page.LivePageAccessVerified == true;
                if (page.LivePageAccessVerified == true)
                {
                    row.Model.FetchDetailsPending = false;
                    row.Model.LivePageAccessVerified = true;
                    row.Model.PageAccessIssue = "";
                    row.CreationDate = string.IsNullOrWhiteSpace(page.CreationDate)
                        ? row.CreationDate
                        : page.CreationDate;
                    if (page.FollowersKnown)
                    {
                        row.Followers = page.Followers;
                        row.FollowersKnown = true;
                    }
                    if (!string.IsNullOrWhiteSpace(page.Views))
                        row.Views = page.Views;
                }
                row.RefreshAccessStatus();
            }
            RefreshDisplayedManagePages();
        });
        ct.ThrowIfCancellationRequested();
        await PersistPagesSnapshotAsync();
    }
}
