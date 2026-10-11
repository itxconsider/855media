using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace _855Media.Core.LDPlayer;

public sealed class ScreenTimeHistoryStore
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media",
            "screentime_history.db"
        );
    public string FilePath { get; }

    public ScreenTimeHistoryStore(string? path = null) => FilePath = path ?? DefaultPath;

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var db = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Pooling = false,
                DefaultTimeout = 15,
            }.ToString()
        );
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY, owner TEXT, device INTEGER, state TEXT, started TEXT, finished TEXT, detail TEXT); CREATE TABLE IF NOT EXISTS actions(id TEXT PRIMARY KEY,session TEXT,owner TEXT,target TEXT,kind TEXT,state TEXT,evidence TEXT,updated TEXT);";
        cmd.ExecuteNonQuery();
        using var index = db.CreateCommand();
        index.CommandText =
            "CREATE UNIQUE INDEX IF NOT EXISTS one_active_interaction ON actions(owner,target,kind) WHERE state IN ('Planned','Submitted','Unknown','Confirmed')";
        index.ExecuteNonQuery();
        return db;
    }

    public Task StartAsync(string id, string owner, int device) =>
        Execute(
            "INSERT INTO sessions VALUES($id,$owner,$device,'Running',$time,NULL,'')",
            ("$id", id),
            ("$owner", owner),
            ("$device", device),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );

    public Task FinishAsync(string id, string state, string detail) =>
        Execute(
            "UPDATE sessions SET state=$state,finished=$time,detail=CASE WHEN detail='' THEN $detail ELSE detail || char(10) || $detail END WHERE id=$id",
            ("$id", id),
            ("$state", state),
            ("$time", DateTimeOffset.UtcNow.ToString("O")),
            ("$detail", detail)
        );

    public Task NoteAsync(string session, string detail) =>
        Execute(
            "UPDATE sessions SET detail=CASE WHEN detail='' THEN $detail ELSE detail || char(10) || $detail END WHERE id=$id",
            ("$id", session),
            ("$detail", detail)
        );

    public Task<bool> ReserveAsync(
        string id,
        string session,
        string owner,
        string target,
        string kind
    )
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO actions VALUES($id,$session,$owner,$target,$kind,'Planned','',$time)";
        foreach (
            var p in new[]
            {
                ("$id", id),
                ("$session", session),
                ("$owner", owner),
                ("$target", target),
                ("$kind", kind),
                ("$time", DateTimeOffset.UtcNow.ToString("O")),
            }
        )
            cmd.Parameters.AddWithValue(p.Item1, p.Item2);
        return Task.FromResult(cmd.ExecuteNonQuery() == 1);
    }

    public Task ActionAsync(string id, string state, string evidence) =>
        Execute(
            "UPDATE actions SET state=$state,evidence=$evidence,updated=$time WHERE id=$id",
            ("$id", id),
            ("$state", state),
            ("$evidence", evidence),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );

    private Task Execute(string sql, params (string Name, object Value)[] args)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in args)
            cmd.Parameters.AddWithValue(p.Name, p.Value);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public string ReadReport()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT started,device,owner,state,detail FROM sessions ORDER BY started DESC LIMIT 100";
        using var reader = cmd.ExecuteReader();
        var lines = new System.Collections.Generic.List<string>();
        while (reader.Read())
            lines.Add(
                $"{reader.GetString(0)} LD-{reader.GetInt32(1)} {reader.GetString(2)} {reader.GetString(3)}\n{reader.GetString(4)}"
            );
        using var actions = db.CreateCommand();
        actions.CommandText =
            "SELECT owner,target,kind,state,evidence FROM actions ORDER BY updated DESC LIMIT 200";
        using var a = actions.ExecuteReader();
        while (a.Read())
            lines.Add(
                $"Action: {a.GetString(0)} · {a.GetString(1)} · {a.GetString(2)} · {a.GetString(3)}\n{a.GetString(4)}"
            );
        return string.Join("\n\n", lines);
    }

    public Task ResolveAsync(string id, bool confirmed, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("Review evidence is required.");
        return Execute(
            "UPDATE actions SET state=$state,evidence=$evidence,updated=$time WHERE id=$id AND state IN ('Unknown','Submitted','Planned')",
            ("$id", id),
            ("$state", confirmed ? "Confirmed" : "NotSubmitted"),
            ("$evidence", evidence),
            ("$time", DateTimeOffset.UtcNow.ToString("O"))
        );
    }

    public bool NeedsReview(int device)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM actions a JOIN sessions s ON s.id=a.session WHERE s.device=$device AND a.state IN ('Planned','Submitted','Unknown')";
        cmd.Parameters.AddWithValue("$device", device);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public System.Collections.Generic.List<ScreenTimeHistoryEntry> ReadActions()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT a.id,s.device,a.owner,a.target,a.kind,a.state,a.evidence FROM actions a JOIN sessions s ON s.id=a.session ORDER BY a.updated DESC LIMIT 200";
        using var r = cmd.ExecuteReader();
        var entries = new System.Collections.Generic.List<ScreenTimeHistoryEntry>();
        while (r.Read())
            entries.Add(
                new(
                    r.GetString(0),
                    r.GetInt32(1),
                    r.GetString(2),
                    r.GetString(3),
                    r.GetString(4),
                    r.GetString(5),
                    r.GetString(6)
                )
            );
        return entries;
    }
}

public sealed record ScreenTimeHistoryEntry(
    string Id,
    int Device,
    string Owner,
    string Target,
    string Kind,
    string State,
    string Evidence
)
{
    public override string ToString() => $"LD-{Device} · {Kind} · {Target} · {State}";
}
