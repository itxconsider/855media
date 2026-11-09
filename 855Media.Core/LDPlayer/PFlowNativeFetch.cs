using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowFetchActor(string Name, string Type, bool Selected = false);

public sealed record PFlowFetchCollected(
    string OwnerName,
    string? OwnerId,
    PFlowFetchActor[] Actors,
    FacebookPageInfo? Page = null
);

public interface IPFlowNativeFetchDevice
{
    void Configure(IReadOnlyList<FacebookPageTarget> saved, string? expectedOwnerId) { }
    Task<PFlowFetchActor[]> ScanAsync(CancellationToken ct);
    Task<bool> SelectAsync(PFlowFetchActor actor, CancellationToken ct);
    Task<string?> ReadNumericIdAsync(PFlowFetchActor actor, CancellationToken ct);
    Task<FacebookAccountDetails?> PersonalDetailsAsync(
        string id,
        string name,
        CancellationToken ct
    );
    Task<FacebookPageInfo?> PageDetailsAsync(
        FacebookPageInfo page,
        string ownerId,
        string ownerName,
        CancellationToken ct
    );
}

public sealed record PFlowNativeFetchResult(
    FacebookAccountDetails? Details,
    string OwnerName,
    string[] Unresolved
)
{
    public string[] SkippedPageIds { get; init; } = [];
}

// Names select actors; only visible numeric IDs or an unambiguous, existing same-owner
// mapping supply routing IDs. This service never creates IDs from names or list positions.
public sealed class PFlowNativeFetch(IPFlowNativeFetchDevice device)
{
    internal static bool Numeric(string? id) =>
        id is { Length: >= 5 and <= 32 } && id.All(char.IsAsciiDigit) && id.Any(c => c != '0');

    public async Task<PFlowNativeFetchResult> RunAsync(
        IReadOnlyList<FacebookPageTarget> saved,
        Func<string, Task> progress,
        CancellationToken ct,
        string? expectedOwnerName = null,
        string? expectedOwnerId = null,
        string? onlyPageName = null,
        bool readPersonalDetails = true,
        Func<PFlowFetchCollected, Task>? collected = null,
        bool skipVerifiedPages = false
    )
    {
        device.Configure(saved, expectedOwnerId);
        var actors = await device.ScanAsync(ct);
        if (
            actors.Length == 0
            || actors.Count(a => a.Selected) != 1
            || actors.Any(a =>
                string.IsNullOrWhiteSpace(a.Name)
                || a.Name.EndsWith('…')
                || a.Name.EndsWith("...", StringComparison.Ordinal)
                || a.Type is not ("Page" or "Profile" or "Unknown")
            )
            || actors
                .GroupBy(a => PFlowNameMatch.Key(a.Name), StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1)
            || actors.Count(a => a.Type == "Profile") != 1
        )
            throw new InvalidOperationException(
                "The complete native Facebook actor list is unavailable or ambiguous; saved data was retained."
            );
        var owner = actors.Single(a => a.Type == "Profile");
        if (expectedOwnerName != null && !PFlowNameMatch.Same(owner.Name, expectedOwnerName))
            throw new InvalidOperationException(
                "The native personal account does not match the selected saved account."
            );
        if (collected != null)
            await collected(new(owner.Name, null, actors));
        var unresolved = actors.Where(a => a.Type == "Unknown").Select(a => a.Name).ToList();
        var skipped = new List<string>();
        try
        {
            if (!await device.SelectAsync(owner, ct))
                throw new InvalidOperationException(
                    "Personal account selection was not confirmed."
                );
            var nativeOwnerId = await device.ReadNumericIdAsync(owner, ct);
            var owners = saved
                .Where(s =>
                    s.IsPrimaryProfile
                    && PFlowNameMatch.Same(s.PageName, owner.Name)
                    && Numeric(s.UserUid)
                )
                .Select(s => s.UserUid)
                .Distinct()
                .ToArray();
            var ownerId =
                Numeric(nativeOwnerId) ? nativeOwnerId
                : owners.Length == 1 ? owners[0]
                : null;
            if (expectedOwnerId != null && ownerId != expectedOwnerId)
                throw new InvalidOperationException(
                    "The selected account routing ID could not be corroborated; no other owner was substituted."
                );
            if (ownerId == null)
                return new(null, owner.Name, actors.Select(a => a.Name).ToArray());
            if (collected != null)
                await collected(new(owner.Name, ownerId, actors));
            await progress(
                Numeric(nativeOwnerId)
                    ? $"Account confirmed: {owner.Name}."
                    : $"Account name confirmed: {owner.Name}; using saved account details."
            );
            var personal = readPersonalDetails
                ? await device.PersonalDetailsAsync(ownerId, owner.Name, ct)
                : new FacebookAccountDetails
                {
                    Uid = ownerId,
                    Name = owner.Name,
                    SwitchedToProfile = true,
                };
            if (
                personal?.SwitchedToProfile != true
                || personal.Uid != ownerId
                || !PFlowNameMatch.Same(personal.Name, owner.Name)
            )
                throw new InvalidOperationException(
                    "Personal profile details could not be bound to the selected native account."
                );
            personal.Pages.Clear();
            var requested = actors
                .Where(a =>
                    a.Type == "Page"
                    && (onlyPageName == null || PFlowNameMatch.Same(a.Name, onlyPageName))
                )
                .ToArray();
            if (onlyPageName != null && requested.Length == 0)
                unresolved.Add(onlyPageName);
            foreach (var actor in requested)
            {
                ct.ThrowIfCancellationRequested();
                var matches = saved
                    .Where(s =>
                        !s.IsPrimaryProfile
                        && s.UserUid == ownerId
                        && PFlowNameMatch.Same(s.PageName, actor.Name)
                        && (Numeric(s.PersonaUid) || Numeric(s.PageId))
                    )
                    .ToArray();
                var page = new FacebookPageInfo
                {
                    PageName = actor.Name,
                    Type = "Page",
                    UserUid = ownerId,
                    ManagedBy = owner.Name,
                    LivePageAccessVerified = false,
                };
                if (matches.Length > 1)
                {
                    unresolved.Add(actor.Name);
                    continue;
                }
                if (matches.Length == 1)
                {
                    if (
                        skipVerifiedPages
                        && onlyPageName == null
                        && PFlowFetchSkipPolicy.Page(matches[0])
                    )
                    {
                        skipped.Add(matches[0].PageId);
                        await progress(
                            "Skipped verified Page (saved data retained): " + actor.Name
                        );
                        continue;
                    }
                    page.PageId = matches[0].PageId;
                    page.PersonaUid = matches[0].PersonaUid;
                }
                await progress("Reading Page: " + actor.Name);
                if (!await device.SelectAsync(actor, ct))
                {
                    unresolved.Add(actor.Name);
                    if (Numeric(page.PageId))
                        personal.Pages.Add(page);
                    continue;
                }
                var nativePageUid = await device.ReadNumericIdAsync(actor, ct);
                if (Numeric(nativePageUid))
                {
                    if (
                        matches.Length == 1
                        && Numeric(page.PersonaUid)
                        && nativePageUid != page.PersonaUid
                    )
                    {
                        unresolved.Add(actor.Name);
                        personal.Pages.Add(page);
                        continue;
                    }
                    page.PersonaUid = nativePageUid!;
                }
                if (!Numeric(page.PersonaUid))
                {
                    unresolved.Add(actor.Name);
                    if (Numeric(page.PageId))
                        personal.Pages.Add(page);
                    continue;
                }
                // The existing key preserves queue/history/source associations.
                // New targets use their Page UID; no canonical Page ID is required.
                if (!Numeric(page.PageId))
                    page.PageId = page.PersonaUid;
                if (collected != null)
                    await collected(new(owner.Name, ownerId, actors, page));
                var details = await device.PageDetailsAsync(page, ownerId, owner.Name, ct);
                if (
                    details != null
                    && details.PersonaUid == page.PersonaUid
                    && PFlowNameMatch.Same(details.PageName, actor.Name)
                )
                {
                    details.PageId = page.PageId;
                    details.UserUid = ownerId;
                    details.ManagedBy = owner.Name;
                    details.LivePageAccessVerified = true;
                    personal.Pages.Add(details);
                    if (collected != null)
                        await collected(new(owner.Name, ownerId, actors, details));
                    await progress(
                        Numeric(nativePageUid)
                            ? $"Page details read: {actor.Name}."
                            : $"Page details read: {actor.Name}; using saved Page details."
                    );
                }
                else
                {
                    unresolved.Add(actor.Name);
                    personal.Pages.Add(page);
                }
            }
            return new(personal, owner.Name, unresolved.ToArray())
            {
                SkippedPageIds = skipped.ToArray(),
            };
        }
        finally
        {
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            if (!await device.SelectAsync(owner, restore.Token))
                throw new InvalidOperationException(
                    "Fetch could not switch back to the personal account; Facebook was left open."
                );
        }
    }
}

// Invocation-local policy; not a Graph proof, not persisted and never inherited by a later job.
public sealed class PFlowNativeFetchScope : IDisposable
{
    private static readonly AsyncLocal<PFlowNativeFetchScope?> Slot = new();
    private readonly PFlowNativeFetchScope? _previous;
    private readonly ILDPlayerService _ld;
    private readonly int _index;
    private readonly string? _path;
    private bool _disposed;
    private readonly bool _allowGraphSessionRead;

    public PFlowNativeFetchScope(
        ILDPlayerService ld,
        int index,
        string? path,
        bool allowGraphSessionRead = false
    )
    {
        _ld = ld;
        _index = index;
        _path = path;
        _allowGraphSessionRead = allowGraphSessionRead;
        _previous = Slot.Value;
        Slot.Value = this;
    }

    public static bool Active(ILDPlayerService ld, int index, string? path)
    {
        for (var current = Slot.Value; current != null; current = current._previous)
            if (
                !current._disposed
                && ReferenceEquals(current._ld, ld)
                && current._index == index
                && current._path == path
            )
                return true;
        return false;
    }

    internal static void RequireAllowed(
        ILDPlayerService ld,
        int index,
        string? path,
        string command
    )
    {
        if (AllowsGraphSessionRead(ld, index, path, command))
            return;
        if (
            Active(ld, index, path)
            && (
                System.Text.RegularExpressions.Regex.IsMatch(
                    command,
                    @"(?:^|\s)(?:su|run-as)(?:\s|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
                || command.Contains("/data/data/", StringComparison.OrdinalIgnoreCase)
                || command.Contains("/data/user/", StringComparison.OrdinalIgnoreCase)
                || command.Contains("authentication", StringComparison.OrdinalIgnoreCase)
                || command.Contains("underlying_account", StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new InvalidOperationException(
                "Native Fetch blocked a private-storage or privileged command."
            );
    }

    internal static bool AllowsGraphSessionRead(
        ILDPlayerService ld,
        int index,
        string? path,
        string command
    )
    {
        if (command == PFlowFetchGraphReader.SessionCommand)
        {
            for (var current = Slot.Value; current != null; current = current._previous)
                if (
                    !current._disposed
                    && ReferenceEquals(current._ld, ld)
                    && current._index == index
                    && current._path == path
                )
                {
                    return current._allowGraphSessionRead;
                }
        }
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        if (ReferenceEquals(Slot.Value, this))
            Slot.Value = _previous;
    }
}
