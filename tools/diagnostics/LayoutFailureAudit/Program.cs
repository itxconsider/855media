using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

if (args.Length == 5 && args[0] == "--summarize")
{
    LayoutAuditSummary.Run(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length < 2)
    throw new ArgumentException(
        "LayoutFailureAudit <capture-directory> <output-directory> [user-catalog.json]"
    );
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var catalog = FacebookLayoutCatalog.Parse(
    args.Length > 2 ? File.ReadAllText(args[2]) : FacebookLayoutCatalog.CreateDefaultJson()
);
var cache = new Dictionary<string, object>();
var rows = new List<object>();
var errors = new List<object>();
var files = (File.Exists(args[0]) ? new[] { args[0] } : Directory.GetFiles(args[0], "*.xml"))
    .Order(StringComparer.Ordinal)
    .ToArray();
foreach (var file in files)
{
    var match = Regex.Match(Path.GetFileName(file), @"-LD-(\d+)-(.+)\.xml$");
    if (!match.Success)
        continue;
    var index = int.Parse(match.Groups[1].Value);
    var xml = File.ReadAllText(file);
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
    if (!cache.TryGetValue(hash, out var result))
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var nodes = doc.Descendants("node").ToArray();
            var labels = nodes
                .SelectMany(n =>
                    new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
                )
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToArray();
            var overall = catalog.Match(xml);
            var expected = new[]
            {
                FacebookScreenKind.Switcher,
                FacebookScreenKind.NewsFeed,
                FacebookScreenKind.Menu,
                FacebookScreenKind.OwnProfile,
                FacebookScreenKind.ReelPicker,
                FacebookScreenKind.ReelComposer,
                FacebookScreenKind.ProfileSettings,
                FacebookScreenKind.IdentitySwitching,
                FacebookScreenKind.Authentication,
                FacebookScreenKind.AndroidRecents,
            };
            result = new
            {
                nodes = nodes.Length,
                input = PFlowUiPrivacy.ContainsInput(xml),
                authentication = PFlowLoginScreen.Classify(xml).ToString(),
                packages = nodes
                    .Select(n => (string?)n.Attribute("package"))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .ToArray(),
                status = overall.Status.ToString(),
                screen = overall.Screen?.ToString(),
                overall.VariantId,
                matching = overall
                    .Candidates.Where(c => c.Matches)
                    .Select(c => new { c.VariantId, screen = c.Screen.ToString() })
                    .ToArray(),
                diagnostics = expected
                    .Select(kind => new
                    {
                        kind = kind.ToString(),
                        diagnostic = catalog.Match(xml, kind).Diagnostic,
                    })
                    .ToArray(),
                // Labels stay in this local review artifact; never sent to a network service.
                labels = labels.Take(65).ToArray(),
            };
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            result = new { invalid = true, error = ex.GetType().Name };
            errors.Add(new { file = Path.GetFileName(file), error = ex.GetType().Name });
        }
        cache[hash] = result;
    }
    rows.Add(
        new
        {
            file = Path.GetFileName(file),
            index,
            synthetic = index >= 900000,
            step = Regex.Replace(match.Groups[2].Value, @"(?:job|unconfirmed)-[a-f0-9]+", "job"),
            hash,
            result,
        }
    );
}
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(
    Path.Combine(output, "captures.json"),
    JsonSerializer.Serialize(rows, jsonOptions)
);
File.WriteAllText(
    Path.Combine(output, "parse-errors.json"),
    JsonSerializer.Serialize(errors, jsonOptions)
);
Console.WriteLine(
    $"Replayed {rows.Count} captures, {cache.Count} unique exact XMLs; {errors.Count} invalid XML groups. Local report: {output}"
);
