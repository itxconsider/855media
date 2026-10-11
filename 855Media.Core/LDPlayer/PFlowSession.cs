using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowSession
{
    public static bool HasInternet(string output) =>
        LDPlayerService.IsAndroidInternetConnected(output);

    public static async Task WaitInternetAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct
    )
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (
                LDPlayerService.IsAndroidInternetConnected(
                    await ld.ExecuteAdbAsync(index, "shell dumpsys connectivity", path, ct)
                )
            )
                return;
            await Task.Delay(2000, ct);
        }
        throw new InvalidOperationException(
            "Internet inside LD was not validated within 60 seconds."
        );
    }

    // Android's unfiltered dumpsys format: unsupported/error output is never empty proof.
    public static bool? RecentAppsEmpty(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;
        var text = output.Replace("\r", "");
        if (
            Regex
                .Matches(text, @"(?m)^ACTIVITY MANAGER RECENT TASKS \(dumpsys activity recents\)$")
                .Count != 1
            // LDPlayer Android 9 reports -1/null even with a complete task list.
            // Accept that observed metadata; task records still decide emptiness.
            || !Regex.IsMatch(text, @"(?m)^[ \t]*mRecentsUid=(?:-1|\d+)[ \t]*$")
            || !Regex.IsMatch(text, @"(?m)^[ \t]*mRecentsComponent=.+$")
            || Regex.IsMatch(
                text,
                @"(?im)permission denial|exception|error:|not found|unknown command|\(nothing\)"
            )
        )
            return null;
        var tasks = Regex.Matches(text, @"(?m)^[ \t]*\* Recent #\d+:");
        if (tasks.Count > 0)
        {
            for (var i = 0; i < tasks.Count; i++)
            {
                var record = text[
                    tasks[i].Index..(i + 1 < tasks.Count ? tasks[i + 1].Index : text.Length)
                ];
                // AOSP TaskRecord dump identifies Home (2) and Recents (3). These are not app cards.
                // Never infer these types from a launcher package/name or from missing fields.
                var type = Regex.Match(
                    record,
                    @"(?m)^[ \t]*autoRemoveRecents=(?:true|false) isPersistable=(?:true|false) numFullscreen=\d+ activityType=([23])[ \t]*$"
                );
                if (!type.Success)
                    return false;
            }
            return true;
        }
        // A truncated task section cannot prove an empty task list.
        return Regex.IsMatch(text, @"(?m)^[ \t]*Recent tasks:") ? null : true;
    }

    public static async Task ClearAppsAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct
    )
    {
        if (!await ld.StopFacebookAppAsync(index, path, ct))
            throw new InvalidOperationException("Facebook stop was not confirmed.");
        var failed = false;
        try
        {
            if (
                !await ld.SendKeyAsync(index, 3, path, ct)
                || !await ld.SendKeyAsync(index, 187, path, ct)
            )
                throw new InvalidOperationException("Recent-app navigation was not confirmed.");
            await Task.Delay(700, ct);
            var before = RecentAppsEmpty(
                await ld.ExecuteAdbAsync(index, "shell dumpsys activity -a recents", path, ct)
            );
            if (before == true)
                return;
            if (before == null)
                throw new InvalidOperationException(
                    "Recent-app state is unsupported or unavailable; clearing was not confirmed."
                );
            var labels = new[]
            {
                "Clear all",
                "Close all",
                "Clear all apps",
                "Close all apps",
                "Dismiss all",
            };
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            var points = ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    labels.Any(l =>
                        new[]
                        {
                            (string?)n.Attribute("text"),
                            (string?)n.Attribute("content-desc"),
                        }.Any(v => string.Equals(l, v, StringComparison.OrdinalIgnoreCase))
                    )
                )
                .Where(n => (string?)n.Attribute("enabled") != "false")
                .Select(ScreenTimeHierarchy.Center)
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            if (points.Length != 1)
                throw new InvalidOperationException(
                    "Recent-app Clear all control is missing or ambiguous; no guessed tap was made."
                );
            var point = points[0]!.Value;
            if (!await ld.TapAsync(index, point.X, point.Y, path, ct))
                throw new InvalidOperationException(
                    "Recent-app Clear all tap was not acknowledged."
                );
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(200, ct);
                if (
                    RecentAppsEmpty(
                        await ld.ExecuteAdbAsync(
                            index,
                            "shell dumpsys activity -a recents",
                            path,
                            ct
                        )
                    ) == true
                )
                    return;
            }
            throw new InvalidOperationException(
                "Recent apps remained or their state could not be verified after Clear all."
            );
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                if (!await ld.SendKeyAsync(index, 3, path, cleanup.Token))
                    throw new InvalidOperationException(
                        "Return to Home after recent-app clearing was not acknowledged."
                    );
            }
            catch when (failed)
            { /* Preserve the primary navigation/clearing failure. */
            }
        }
    }
}
