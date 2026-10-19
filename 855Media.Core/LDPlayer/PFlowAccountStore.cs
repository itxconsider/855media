using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed class PFlowAccountVersionException : Exception
{
    public PFlowAccountVersionException()
        : base("Unsupported PFlow account data version; no data was changed.") { }
}

public sealed record PFlowMappingIssue(string DeviceName, string OwnerUid, string Message);

public sealed record PFlowMappingCandidates(
    IReadOnlyList<PFlowPersonalAccount> Accounts,
    IReadOnlyList<PFlowMappingIssue> Issues
);

public sealed partial class PFlowAccountStore(
    string? path = null,
    IPFlowCredentialStore? credentials = null
)
{
    private readonly IPFlowCredentialStore _credentials = credentials ?? new PFlowCredentialStore();
    public string FilePath { get; } =
        Path.GetFullPath(
            path
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "855Media",
                    "pflow_accounts.json"
                )
        );

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<PFlowPersonalAccount> Accounts { get; set; } = [];
        public List<PFlowAccountBinding> RemovedBindings { get; set; } = [];
    }

    public async Task<List<PFlowPersonalAccount>> LoadAsync(CancellationToken ct = default)
    {
        using var gate = await LockAsync(ct);
        var document = await ReadAsync(ct);
        // Authentication/access evidence is run-scoped, never reusable after a restart.
        foreach (var account in document.Accounts)
        {
            account.Authentication = PFlowAuthenticationState.Cached;
            foreach (var page in account.Pages)
                page.VerifiedRunId = "";
        }
        return document.Accounts;
    }

    public Task UpdateAsync(
        Action<List<PFlowPersonalAccount>> update,
        CancellationToken ct = default
    ) => UpdateDocumentAsync(document => update(document.Accounts), ct);

    private async Task UpdateDocumentAsync(Action<Document> update, CancellationToken ct = default)
    {
        using var gate = await LockAsync(ct);
        var document = await ReadAsync(ct);
        update(document);
        document.RemovedBindings.RemoveAll(b => document.Accounts.Any(a => a.Binding == b));
        Validate(document);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                document,
                new JsonSerializerOptions { WriteIndented = true }
            );
            using (
                var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                await stream.WriteAsync(bytes, ct);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(FilePath))
            {
                // Preserve the good backup if the primary was recovered from corruption.
                string? backup = null;
                try
                {
                    await ReadFileAsync(FilePath, ct);
                    backup = FilePath + ".bak";
                }
                catch (Exception ex) when (ex is IOException or JsonException) { }
                File.Replace(temporary, FilePath, backup);
            }
            else
            {
                File.Move(temporary, FilePath);
                File.Copy(FilePath, FilePath + ".bak", true);
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static IReadOnlyList<PFlowPersonalAccount> MigrateLegacy(
        IEnumerable<FacebookPageTarget> rows,
        IReadOnlyList<LDInstanceInfo> instances,
        string ldDirectory
    )
    {
        var all = rows.ToArray();
        var result = new List<PFlowPersonalAccount>();
        var assignments = new List<(FacebookPageTarget Row, PFlowAccountBinding Binding)>();
        foreach (var profile in all.Where(p => p.IsPrimaryProfile))
        {
            var index = PFlowDeviceRouting.Resolve(profile.DeviceName, instances);
            if (
                index < 0
                || profile.AppClone != "FB-01"
                || profile.PageId != profile.UserUid
                || string.IsNullOrWhiteSpace(profile.UserUid)
            )
                continue;
            var instance = instances.Single(i => i.Index == index);
            var binding =
                profile.AccountBinding
                ?? new PFlowAccountBinding(
                    PFlowDeviceRouting.ReadIdentity(ldDirectory, index),
                    index,
                    instance.Name,
                    "com.facebook.katana",
                    profile.UserUid
                );
            binding.RequireWorker(
                index,
                instance.Name,
                PFlowDeviceRouting.ReadIdentity(ldDirectory, index),
                "com.facebook.katana"
            );
            if (binding.OwnerUid != profile.UserUid)
                throw new InvalidOperationException("Profile owner binding mismatch.");
            if (result.Any(a => a.Binding.AccountKey == binding.AccountKey))
                continue;
            var onlyProfile =
                all.Count(p =>
                    p.IsPrimaryProfile
                    && p.AppClone == profile.AppClone
                    && PFlowDeviceRouting.Resolve(p.DeviceName, instances) == index
                ) == 1;
            var account = new PFlowPersonalAccount
            {
                Binding = binding,
                Name = profile.PageName,
                Email = profile.Email,
                SelectedForDiscovery = onlyProfile,
                SelectedForPosting =
                    onlyProfile
                    && all.Any(p =>
                        p.IsSelected
                        && p.UserUid == profile.UserUid
                        && p.AppClone == profile.AppClone
                        && PFlowDeviceRouting.Resolve(p.DeviceName, instances) == index
                    ),
                // Single-account ScreenTime remains selected for compatibility. Multiple
                // candidates require explicit selection; a Page checkbox is never consulted.
                SelectedForScreenTime = onlyProfile,
            };
            foreach (
                var row in all.Where(p =>
                    p.UserUid == profile.UserUid
                    && p.AppClone == profile.AppClone
                    && PFlowDeviceRouting.Resolve(p.DeviceName, instances) == index
                )
            )
            {
                if (row.AccountBinding != null && row.AccountBinding != binding)
                    throw new InvalidOperationException("Target account binding mismatch.");
                assignments.Add((row, binding));
                if (!row.IsPrimaryProfile && !account.Pages.Any(p => p.PageId == row.PageId))
                    account.Pages.Add(
                        new()
                        {
                            PageId = row.PageId,
                            PersonaUid = row.PersonaUid,
                            Name = row.PageName,
                        }
                    );
            }
            result.Add(account);
        }
        foreach (var assignment in assignments)
            assignment.Row.AccountBinding = assignment.Binding;
        return result;
    }

    // A stale/missing instance affects only its owner candidates. Work on snapshots so
    // failed candidates and an uncommitted store update cannot alter configuration rows.
    public static PFlowMappingCandidates CollectLegacy(
        IEnumerable<FacebookPageTarget> rows,
        IReadOnlyList<LDInstanceInfo> instances,
        string ldDirectory
    )
    {
        var all = rows.ToArray();
        var accounts = new List<PFlowPersonalAccount>();
        var issues = new List<PFlowMappingIssue>();
        foreach (
            var profile in all.Where(p => p.IsPrimaryProfile)
                .DistinctBy(p => (p.DeviceName, p.AppClone, p.UserUid))
        )
        {
            try
            {
                var index = PFlowDeviceRouting.Resolve(profile.DeviceName, instances);
                if (index < 0 || profile.AppClone != "FB-01" || profile.PageId != profile.UserUid)
                    throw new InvalidOperationException(
                        "Review the personal UID, assigned LD instance and supported app."
                    );
                var scoped = all.Where(p =>
                        p.UserUid == profile.UserUid
                        && p.AppClone == profile.AppClone
                        && PFlowDeviceRouting.Resolve(p.DeviceName, instances) == index
                    )
                    .Select(p => p.CreateSnapshot())
                    .ToArray();
                var candidate = MigrateLegacy(scoped, instances, ldDirectory).Single();
                var multipleOwners =
                    all.Where(p =>
                            p.IsPrimaryProfile
                            && p.AppClone == profile.AppClone
                            && PFlowDeviceRouting.Resolve(p.DeviceName, instances) == index
                        )
                        .Select(p => p.UserUid)
                        .Distinct()
                        .Count() > 1;
                if (multipleOwners)
                {
                    candidate.SelectedForDiscovery = false;
                    candidate.SelectedForScreenTime = false;
                    candidate.SelectedForPosting = false;
                }
                if (!accounts.Any(a => a.Binding == candidate.Binding))
                    accounts.Add(candidate);
            }
            catch (Exception ex)
                when (ex
                        is InvalidOperationException
                            or IOException
                            or JsonException
                            or UnauthorizedAccessException
                )
            {
                issues.Add(new(profile.DeviceName, profile.UserUid, ex.Message));
            }
        }
        return new(accounts, issues);
    }

    public static void RequireCurrentBinding(
        PFlowAccountBinding binding,
        IReadOnlyList<LDInstanceInfo> instances,
        string ldDirectory
    )
    {
        var instance =
            instances.SingleOrDefault(i => i.Index == binding.DeviceIndex)
            ?? throw new InvalidOperationException(
                "Assigned LD instance is missing; no fallback was used."
            );
        binding.RequireWorker(
            instance.Index,
            instance.Name,
            PFlowDeviceRouting.ReadIdentity(ldDirectory, instance.Index),
            "com.facebook.katana"
        );
    }

    private async Task<FileStream> LockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    FilePath + ".lock",
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                await Task.Delay(100, ct);
            }
        }
    }

    private async Task<Document> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak"))
            return new();
        try
        {
            return await ReadFileAsync(FilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            try
            {
                return await ReadFileAsync(FilePath + ".bak", ct);
            }
            catch (Exception backup) when (backup is IOException or JsonException)
            {
                throw new InvalidDataException(
                    "PFlow account data and its backup are unreadable; no data was reset.",
                    new AggregateException(ex, backup)
                );
            }
        }
    }

    private static async Task<Document> ReadFileAsync(string path, CancellationToken ct)
    {
        var document =
            JsonSerializer.Deserialize<Document>(await File.ReadAllTextAsync(path, ct))
            ?? throw new JsonException("PFlow account document is empty.");
        Validate(document);
        return document;
    }

    private static void Validate(Document document)
    {
        if (document.Version != 1)
            throw new PFlowAccountVersionException();
        if (
            document.Accounts == null
            || document.RemovedBindings == null
            || document.Accounts.Any(a => a == null || a.Binding == null || a.Pages == null)
        )
            throw new JsonException("Invalid PFlow account records.");
        foreach (var removed in document.RemovedBindings)
        {
            if (removed == null)
                throw new JsonException("Invalid removed account binding.");
            try
            {
                removed.Validate();
            }
            catch (InvalidOperationException ex)
            {
                throw new JsonException("Invalid removed account binding.", ex);
            }
        }
        foreach (var account in document.Accounts)
        {
            try
            {
                account.Binding.Validate();
                if (account.PreviousBindings == null)
                    throw new InvalidOperationException("Missing remap history.");
                foreach (var previous in account.PreviousBindings)
                    PFlowAccountRemap.RequireSameAssignment(previous, account.Binding);
                if (account.TransferredFromBindings == null)
                    throw new InvalidOperationException("Missing transfer history.");
                foreach (var previous in account.TransferredFromBindings)
                    PFlowAccountRemap.RequireSameAccount(previous, account.Binding);
                if (
                    account.PreviousBindings.Distinct().Count() != account.PreviousBindings.Count
                    || account.PreviousBindings.Contains(account.Binding)
                )
                    throw new InvalidOperationException("Ambiguous remap history.");
            }
            catch (InvalidOperationException ex)
            {
                throw new JsonException("Invalid PFlow account binding.", ex);
            }
            if (
                account.Pages.Any(p => p == null || string.IsNullOrWhiteSpace(p.PageId))
                || account.Pages.GroupBy(p => p.PageId).Any(g => g.Count() > 1)
            )
                throw new JsonException("Ambiguous Page access records.");
        }
        if (
            document
                .Accounts.GroupBy(a => a.Binding.OwnerUid)
                .Any(g =>
                    g.Any(a => a.InstanceLocked) && g.Select(a => a.Binding).Distinct().Count() > 1
                )
        )
            throw new JsonException(
                "An instance-locked UID cannot be mapped to another LD or package."
            );
        if (document.Accounts.GroupBy(a => a.Binding.AccountKey).Any(g => g.Count() > 1))
            throw new JsonException("Duplicate PFlow account bindings.");
    }
}
