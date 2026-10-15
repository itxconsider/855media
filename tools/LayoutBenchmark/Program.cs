using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;

if (args.Length != 3)
    throw new ArgumentException("Usage: LayoutBenchmark <fixture-root> <mode> <output.json>");
var fixtureRoot = Path.GetFullPath(args[0]);
var captured = Directory
    .GetFiles(Path.Combine(fixtureRoot, "pflow-screentime"), "*.xml")
    .Order(StringComparer.Ordinal)
    .Select(Read)
    .ToArray();
var layouts = Directory
    .GetFiles(Path.Combine(fixtureRoot, "pflow-layouts"), "*.xml")
    .Order(StringComparer.Ordinal)
    .Select(Read)
    .ToArray();
var inputs = captured.Concat(layouts).ToArray();
var assembly = typeof(LDPlayerService).Assembly;
var isScreen = assembly
    .GetType("_855Media.Core.LDPlayer.ScreenTimeService")!
    .GetMethod("IsScreen", BindingFlags.Static | BindingFlags.NonPublic)!
    .CreateDelegate<Func<string?, string, bool>>();
var isComposer = typeof(PFlowAutoPostService)
    .GetMethod("IsComposer", BindingFlags.Static | BindingFlags.NonPublic)!
    .CreateDelegate<Func<string, string, bool>>();
var groups = new (string Name, Func<Input, bool> Check)[]
{
    ("Switcher", i => PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(i.Xml)),
    ("Own profile", i => LDPlayerService.IsExpectedOwnProfileVisible(i.Screen, "", "W500 PH")),
    ("Analytics", i => LDPlayerService.IsAnalyticsContentReady(i.Screen)),
    ("Reel composer", i => isComposer(i.Xml, "Reel")),
    ("Photo composer", i => isComposer(i.Xml, "Picture")),
    ("News Feed", i => isScreen(i.Xml, "Feed")),
    ("Reels feed", i => isScreen(i.Xml, "Reels")),
};
var results = new List<object>();
foreach (var group in groups)
{
    // Same immutable fixture corpus in both processes; file I/O is outside measurement.
    var positives = inputs.Where(group.Check).Select(i => i.Name).ToArray();
    for (var warmup = 0; warmup < 3; warmup++)
        foreach (var input in inputs)
            group.Check(input);
    var probe = Measure(group.Check, 1);
    var rounds = Math.Clamp((int)Math.Ceiling(30_000d / Math.Max(1, probe.Microseconds)), 1, 200);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var samples = Enumerable.Range(0, 9).Select(_ => Measure(group.Check, rounds)).ToArray();
    var count = rounds * inputs.Length;
    results.Add(
        new
        {
            group.Name,
            Fixtures = inputs.Length,
            CallsPerSample = count,
            Samples = samples.Length,
            MedianMicrosecondsPerCall = Median(samples.Select(s => s.Microseconds / count)),
            MinMicrosecondsPerCall = samples.Min(s => s.Microseconds / count),
            MaxMicrosecondsPerCall = samples.Max(s => s.Microseconds / count),
            MedianAllocatedBytesPerCall = Median(
                samples.Select(s => (double)s.AllocatedBytes / count)
            ),
            PositiveMatches = positives,
        }
    );
}
var output = new
{
    Mode = args[1],
    Runtime = Environment.Version.ToString(),
    ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    Environment.ProcessorCount,
    Stopwatch.Frequency,
    MeasuredUtc = DateTimeOffset.UtcNow,
    Method = "Release, isolated process, preloaded XML; 3 corpus warmups; calibrated rounds; median of 9 batch averages; current-thread allocations. Own profile and Analytics receive pre-parsed Bloks screens, matching their existing entry points.",
    Results = results,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(
    args[2],
    JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true })
);
Console.WriteLine(
    $"{args[1]}: {inputs.Length} fixtures, {groups.Length} recognizers; saved {args[2]}"
);

(double Microseconds, long AllocatedBytes) Measure(Func<Input, bool> check, int rounds)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var ticks = Stopwatch.GetTimestamp();
    var matches = 0;
    for (var round = 0; round < rounds; round++)
        foreach (var input in inputs)
            if (check(input))
                matches++;
    var elapsed = Stopwatch.GetTimestamp() - ticks;
    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    GC.KeepAlive(matches);
    return (elapsed * 1_000_000d / Stopwatch.Frequency, bytes);
}
static double Median(IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}
static Input Read(string path)
{
    var xml = File.ReadAllText(path);
    return new(Path.GetFileName(path), xml, AtxAgentClient.ParseBloksHierarchy(xml));
}

sealed record Input(string Name, string Xml, BloksScreenInfo Screen);
