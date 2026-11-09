using System.Text.Json;
using System.Text.RegularExpressions;

internal static class LayoutAuditSummary
{
    internal static void Run(string beforeFile, string afterFile, string logs, string output)
    {
        Directory.CreateDirectory(output);
        using var before = JsonDocument.Parse(File.ReadAllText(beforeFile));
        using var after = JsonDocument.Parse(File.ReadAllText(afterFile));
        var old = before.RootElement.EnumerateArray().ToDictionary(r => S(r, "file"));
        var all = after.RootElement.EnumerateArray().ToArray();
        var comparable = all.Where(r => old.ContainsKey(S(r, "file"))).ToArray();
        string Status(JsonElement r) => S(r.GetProperty("result"), "status");
        string Screen(JsonElement r) => S(r.GetProperty("result"), "screen");
        object Counts(IEnumerable<JsonElement> rows) =>
            rows.GroupBy(Status).ToDictionary(g => g.Key, g => g.Count());
        bool Structured(JsonElement r) =>
            r.GetProperty("index").GetInt32() < 900
            && r.GetProperty("result").TryGetProperty("nodes", out var n)
            && n.GetInt32() > 5;
        var changes = comparable
            .Where(r =>
                Status(r) != Status(old[S(r, "file")]) || Screen(r) != Screen(old[S(r, "file")])
            )
            .Select(r => new
            {
                file = S(r, "file"),
                before = Status(old[S(r, "file")]),
                beforeScreen = Screen(old[S(r, "file")]),
                after = Status(r),
                afterScreen = Screen(r),
            })
            .ToArray();
        var unresolved = all.Where(r => Status(r) is "Unknown" or "Ambiguous" or "Blocked")
            .GroupBy(r => S(r, "hash"))
            .OrderByDescending(g => g.Count())
            .Select(g => new
            {
                hash = g.Key,
                count = g.Count(),
                files = g.Select(r => S(r, "file")).ToArray(),
                sample = g.Last(),
            })
            .ToArray();
        var jobs = new Dictionary<string, object>();
        var layoutFailures = new List<object>();
        var accountFailures = new List<object>();
        var fetchFailures = new List<object>();
        foreach (var file in Directory.GetFiles(logs, "*.log"))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var ld = Regex.Match(line, @"\bLD-(\d+)\b");
                if (!ld.Success || int.Parse(ld.Groups[1].Value) >= 900)
                    continue;
                var index = int.Parse(ld.Groups[1].Value);
                object Entry() =>
                    new
                    {
                        source = Path.GetFileName(file),
                        line = lineNumber,
                        index,
                        detail = line,
                    };
                if (
                    line.Contains("step=layout.")
                    && Regex.IsMatch(line, @"status=(Unknown|Blocked|Ambiguous)\b")
                )
                    layoutFailures.Add(Entry());
                if (Regex.IsMatch(line, @"step=account\.[^ ]*(?:unresolved|failed)"))
                    accountFailures.Add(Entry());
                if (
                    Path.GetFileName(file) == "facebook-fetch.log"
                    && Regex.IsMatch(
                        line,
                        "fail|stopped|unavailable|unresolved|unconfirmed|error",
                        RegexOptions.IgnoreCase
                    )
                )
                    fetchFailures.Add(Entry());
                var job = Regex.Match(line, @"\bjob=([a-f0-9]+)\b");
                var error = Regex.Match(line, @"\berror=(.+)$");
                if (job.Success && error.Success)
                    jobs[index + ":" + job.Groups[1].Value] = new
                    {
                        source = Path.GetFileName(file),
                        line = lineNumber,
                        index,
                        job = job.Groups[1].Value,
                        error = error.Groups[1].Value,
                        time = line[..Math.Min(line.Length, 33)],
                    };
            }
        }
        var summary = new
        {
            capturesBefore = old.Count,
            capturesAfter = all.Length,
            newCapturesDuringReview = all.Length - comparable.Length,
            uniqueExactXml = all.Select(r => S(r, "hash")).Distinct().Count(),
            before = Counts(old.Values),
            afterComparable = Counts(comparable),
            structuredDeviceRangeBefore = Counts(old.Values.Where(Structured)),
            structuredDeviceRangeAfter = Counts(comparable.Where(Structured)),
            changedCaptures = changes.Length,
            unresolvedExactGroups = unresolved.Length,
            jobFailures = jobs.Count,
            layoutLogFailures = layoutFailures.Count,
            accountLogFailures = accountFailures.Count,
            fetchLogFailures = fetchFailures.Count,
            notes = new[]
            {
                "Inventory includes old tests, negative cases and transitions; device indices below900 are a review bucket, not proof of production origin.",
                "Before/after counts use identical captured filenames; new captures are inventoried separately.",
                "Unknown may require target context or represent a blocked/partial/unsupported UI; replay does not establish live posting success.",
            },
        };
        void Write(string file, object data) =>
            File.WriteAllText(
                Path.Combine(output, file),
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
            );
        Write("summary.json", summary);
        Write("changes.json", changes);
        Write("unresolved-groups.json", unresolved);
        Write("job-failures.json", jobs.Values);
        Write("layout-log-failures.json", layoutFailures);
        Write("account-log-failures.json", accountFailures);
        Write("fetch-log-failures.json", fetchFailures);
        using var csv = new StreamWriter(Path.Combine(output, "capture-index.csv"));
        string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        csv.WriteLine("file,index,step,status,screen,variant,hash");
        foreach (var row in all)
            csv.WriteLine(
                string.Join(
                    ",",
                    new[]
                    {
                        S(row, "file"),
                        S(row, "index"),
                        S(row, "step"),
                        Status(row),
                        Screen(row),
                        S(row.GetProperty("result"), "VariantId"),
                        S(row, "hash"),
                    }.Select(Csv)
                )
            );
        File.WriteAllText(
            Path.Combine(output, "report.md"),
            "# Layout failure inventory\n\n"
                + $"Replayed {all.Length} captures ({summary.uniqueExactXml} exact XML groups). Compared {comparable.Length} baseline filenames; {changes.Length} changed classifications.\n\n"
                + "See summary.json for before/after counts, capture-index.csv for every capture, changes.json for improvements, and unresolved-groups.json for every unresolved exact group.\n\n"
                + "Job, layout, account and Fetch failure indexes are recorded separately.\n\n"
                + string.Join("\n\n", summary.notes)
        );
        Console.WriteLine(
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private static string S(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : "";
}
