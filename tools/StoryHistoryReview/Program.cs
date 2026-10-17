using System.Text.Json;
using _855Media.Core.LDPlayer;
using Microsoft.Data.Sqlite;

if (args.Length < 1)
    return 2;
var store = new PFlowHistoryStore();
var all = await store.ReadAsync();
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            Batch = args[0],
            States = all.Where(j => j.BatchId == args[0])
                .GroupBy(j => j.State.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
        }
    )
);
var jobs = all.Where(j =>
        j.BatchId == args[0] && j.PostType == "Story" && j.State == PFlowState.Unknown
    )
    .ToArray();
foreach (var job in jobs)
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                job.JobId,
                job.DeviceIndex,
                job.TargetName,
                File = Path.GetFileName(job.SourcePath),
                job.SubmittedUtc,
                job.Error,
            }
        )
    );
if (!args.Contains("--confirm-user"))
    return 0;
if (
    jobs.Length != 4
    || jobs.Any(j => !j.SubmittedUtc.HasValue)
    || all.Any(j =>
        jobs.Any(u => u.DeviceIndex == j.DeviceIndex)
        && j.State
            is PFlowState.SubmitRequested
                or PFlowState.Submitted
                or PFlowState.Reserved
                or PFlowState.MediaReady
                or PFlowState.ComposerReady
                or PFlowState.IdentityVerified
    )
)
    throw new InvalidOperationException(
        "Expected four finished, submitted Unknown jobs and no active workers."
    );
Directory.CreateDirectory("artifacts/story-user-review");
using (var source = new SqliteConnection("Data Source=" + store.FilePath))
using (
    var backup = new SqliteConnection(
        "Data Source="
            + Path.GetFullPath(
                "artifacts/story-user-review/history-before-"
                    + DateTime.UtcNow.ToString("yyyyMMddHHmmss")
                    + ".db"
            )
    )
)
{
    source.Open();
    backup.Open();
    source.BackupDatabase(backup);
}
foreach (var job in jobs)
{
    using var lease = await PFlowDeviceLease.AcquireAsync(job.DeviceIndex, default);
    var fresh = (await store.ReadAsync()).Single(j => j.JobId == job.JobId);
    if (fresh.State != PFlowState.Unknown)
        throw new InvalidOperationException("Job changed during review.");
    fresh.State = PFlowState.Confirmed;
    fresh.NoPublicationVerified = false;
    fresh.ConfirmedUtc = DateTime.UtcNow;
    fresh.Evidence =
        "Manual review: user confirmed all Story posts in batch "
        + args[0]
        + " completed, with the provided outcome screenshot on 2026-10-15. ConfirmedUtc records this review time; exact publication time was not supplied.";
    fresh.Error = "";
    fresh.CurrentStep = "Confirmed by user review";
    fresh.StepStartedUtc = DateTime.UtcNow;
    await store.SaveAsync(fresh);
    Console.WriteLine("Confirmed LD-" + fresh.DeviceIndex + " " + fresh.JobId);
}
return 0;
