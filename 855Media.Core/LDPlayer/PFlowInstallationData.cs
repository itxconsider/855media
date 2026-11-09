using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace _855Media.Core.LDPlayer;

/// <summary>Independent durable data for one canonical LDPlayer installation.</summary>
public sealed class PFlowInstallationData
{
    public string DirectoryPath { get; }
    public string Installation { get; }

    public string FilePath(string name) => Path.Combine(DirectoryPath, name);

    public PFlowInstallationData(string installation, string? applicationDirectory = null)
    {
        Installation = PFlowInstallationRouting.Normalize(installation);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Installation)));
        DirectoryPath = Path.Combine(applicationDirectory ?? LegacyDirectory, "installations", key);
    }

    public static string LegacyDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media"
        );

    // Stage the entire migration and publish by directory rename. Existing profiles
    // are never reimported; deleting an account cannot resurrect it from legacy data.
    public async Task InitializeAsync(
        IReadOnlySet<string> identities,
        bool ownsUnboundLegacy,
        string? legacyDirectory = null,
        CancellationToken ct = default
    )
    {
        if (Directory.Exists(DirectoryPath))
            return;
        var legacy = legacyDirectory ?? LegacyDirectory;
        Directory.CreateDirectory(Path.GetDirectoryName(DirectoryPath)!);
        var staging = DirectoryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            void RememberTarget(JsonNode? node)
            {
                var value = node?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                    targets.Add(value);
            }
            bool Owns(JsonNode? binding) =>
                binding == null
                    ? ownsUnboundLegacy
                    : identities.Contains(binding["InstanceIdentity"]?.GetValue<string>() ?? "");
            async Task FilterJson(string name, bool accounts)
            {
                var source = Path.Combine(legacy, name);
                if (!File.Exists(source) && !File.Exists(source + ".bak"))
                    return;
                JsonNode node;
                try
                {
                    node =
                        JsonNode.Parse(await System.IO.File.ReadAllTextAsync(source, ct))
                        ?? throw new InvalidDataException("Empty legacy data: " + name);
                }
                catch (Exception ex)
                    when (ex is IOException or System.Text.Json.JsonException
                        && File.Exists(source + ".bak")
                    )
                {
                    node =
                        JsonNode.Parse(await System.IO.File.ReadAllTextAsync(source + ".bak", ct))
                        ?? throw new InvalidDataException("Empty legacy backup: " + name);
                }
                if (accounts)
                {
                    if (node["Version"]?.GetValue<int>() != 1)
                        throw new InvalidDataException("Unsupported legacy account version.");
                    foreach (var field in new[] { "Accounts", "RemovedBindings" })
                    {
                        if (node[field] is not JsonArray rows)
                            continue;
                        foreach (var row in rows.ToArray())
                            if (!Owns(field == "Accounts" ? row?["Binding"] : row))
                                rows.Remove(row);
                    }
                    foreach (var row in node["Accounts"]?.AsArray() ?? [])
                    {
                        RememberTarget(row?["Binding"]?["OwnerUid"]);
                        foreach (var page in row?["Pages"]?.AsArray() ?? [])
                            RememberTarget(page?["PageId"]);
                        var reference = row?["CredentialReference"]?.GetValue<string>();
                        if (
                            Guid.TryParseExact(reference, "N", out var parsed)
                            && parsed.ToString("N") == reference
                        )
                            CopyProtected("credentials", reference + ".bin");
                        if (row?["Binding"] is { } binding)
                        {
                            var value =
                                System.Text.Json.JsonSerializer.Deserialize<PFlowAccountBinding>(
                                    binding.ToJsonString()
                                );
                            if (value != null)
                                CopyProtected("mailboxes", value.AccountKey + ".bin");
                        }
                    }
                }
                else
                {
                    var rows = node.AsArray();
                    foreach (var row in rows.ToArray())
                        if (!Owns(row?["AccountBinding"]))
                            rows.Remove(row);
                    foreach (var row in rows)
                        RememberTarget(row?["PageId"]);
                }
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(staging, name),
                    node.ToJsonString(),
                    ct
                );
            }
            void CopyProtected(string folder, string name)
            {
                var source = Path.Combine(legacy, folder, name);
                if (!System.IO.File.Exists(source))
                    return;
                Directory.CreateDirectory(Path.Combine(staging, folder));
                System.IO.File.Copy(source, Path.Combine(staging, folder, name));
            }
            void CopyDatabase(string name, string? table)
            {
                var sourcePath = Path.Combine(legacy, name);
                if (!System.IO.File.Exists(sourcePath))
                    return;
                using var source = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = sourcePath,
                        Mode = SqliteOpenMode.ReadOnly,
                        Pooling = false,
                    }.ToString()
                );
                using var destination = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(staging, name),
                        Pooling = false,
                    }.ToString()
                );
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
                if (table == null)
                    return;
                var remove = new List<string>();
                using (var read = destination.CreateCommand())
                {
                    read.CommandText = "SELECT id,json FROM " + table;
                    using var reader = read.ExecuteReader();
                    while (reader.Read())
                        if (!Owns(JsonNode.Parse(reader.GetString(1))?["AccountBinding"]))
                            remove.Add(reader.GetString(0));
                }
                using var transaction = destination.BeginTransaction();
                foreach (var id in remove)
                {
                    using var delete = destination.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM " + table + " WHERE id=$id";
                    delete.Parameters.AddWithValue("$id", id);
                    delete.ExecuteNonQuery();
                }
                if (table == "jobs")
                {
                    using var cleanup = destination.CreateCommand();
                    cleanup.Transaction = transaction;
                    cleanup.CommandText =
                        "DELETE FROM events WHERE job NOT IN (SELECT id FROM jobs); "
                        + "DELETE FROM daily_baseline WHERE target NOT IN (SELECT json_extract(json,'$.TargetId') FROM jobs) "
                        + "AND target NOT IN (SELECT value FROM json_each($targets));";
                    cleanup.Parameters.AddWithValue(
                        "$targets",
                        System.Text.Json.JsonSerializer.Serialize(targets)
                    );
                    cleanup.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            await FilterJson("pflow_accounts.json", true);
            await FilterJson("facebook_pages.json", false);
            CopyDatabase("facebook_post_history.db", "jobs");
            CopyDatabase("facebook_dispatch_queue.db", "dispatch_queue");
            // Legacy ScreenTime rows contain only index/owner, no installation proof.
            if (ownsUnboundLegacy)
                CopyDatabase("screentime_history.db", null);
            var config = Path.Combine(legacy, "autopost_config.json");
            if (ownsUnboundLegacy && System.IO.File.Exists(config))
                System.IO.File.Copy(config, Path.Combine(staging, "autopost_config.json"));
            await System.IO.File.WriteAllTextAsync(
                Path.Combine(staging, "installation.txt"),
                Installation,
                ct
            );
            ct.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(staging, DirectoryPath);
            }
            catch (IOException) when (Directory.Exists(DirectoryPath))
            { /* Another initializer won. */
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }
}
