using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

/// <summary>An immutable, validated rules snapshot, backed by trusted C# templates.</summary>
public sealed class FacebookLayoutCatalog
{
    public const int MaximumFileBytes = 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        WriteIndented = true,
    };

    internal sealed record Origin(
        string BaseId,
        int Version,
        IReadOnlyDictionary<FacebookControlRole, IReadOnlyList<string>> Aliases
    );

    private readonly IReadOnlyDictionary<string, Origin> _origins;
    public Guid Generation { get; } = Guid.NewGuid();
    public IReadOnlyList<FacebookLayoutDefinition> Definitions { get; }
    public IReadOnlyList<string> DashboardHeaderLabels { get; }
    public IReadOnlyList<string> SectionExpansionLabels { get; }
    public string Source { get; }

    private FacebookLayoutCatalog(
        FacebookLayoutDefinition[] definitions,
        Dictionary<string, Origin> origins,
        string[] headers,
        string[] expansions,
        string source
    )
    {
        Definitions = Array.AsReadOnly(definitions);
        _origins = new ReadOnlyDictionary<string, Origin>(origins);
        DashboardHeaderLabels = Array.AsReadOnly(headers);
        SectionExpansionLabels = Array.AsReadOnly(expansions);
        Source = source;
    }

    public FacebookLayoutMatch Match(
        string? xml,
        FacebookScreenKind? expected = null,
        FacebookLayoutContext? context = null
    ) => FacebookLayoutRegistry.MatchCatalog(this, xml, expected, context);

    internal Origin? FindOrigin(string id) =>
        _origins.TryGetValue(id, out var origin) ? origin : null;

    internal bool IsSectionExpansion(string? label) =>
        SectionExpansionLabels.Any(value =>
            string.Equals(value, Normalize(label ?? ""), StringComparison.OrdinalIgnoreCase)
        );

    public static string CreateDefaultJson() =>
        JsonSerializer.Serialize(
            new FacebookLayoutConfiguration
            {
                Variants = FacebookLayoutRegistry
                    .BuiltInDefinitions.Select(d => new FacebookLayoutVariantConfiguration
                    {
                        Id = d.Id,
                        BaseId = d.Id,
                    })
                    .ToList(),
            },
            JsonOptions
        );

    public static FacebookLayoutCatalog Parse(string json) => Parse(json, "JSON rules");

    internal static FacebookLayoutCatalog Parse(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            throw new FormatException("Layout JSON is empty or exceeds the 1 MiB limit.");
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = 16 }
            );
            RejectDuplicateProperties(document.RootElement);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("formatVersion", out _)
                || !document.RootElement.TryGetProperty("variants", out _)
            )
                throw new FormatException("formatVersion and variants are required.");
            var config =
                document.Deserialize<FacebookLayoutConfiguration>(JsonOptions)
                ?? throw new FormatException("A layout configuration object is required.");
            return Compile(config, source);
        }
        catch (JsonException ex)
        {
            // Do not include raw JSON values in diagnostics.
            throw new FormatException(
                $"Invalid layout JSON at line {ex.LineNumber ?? 0}, byte {ex.BytePositionInLine ?? 0}."
            );
        }
    }

    internal static FacebookLayoutCatalog BuiltIn() => Parse(CreateDefaultJson(), "C# fallback");

    private static FacebookLayoutCatalog Compile(FacebookLayoutConfiguration config, string source)
    {
        if (config.FormatVersion != 1)
            throw new FormatException("Unsupported layout formatVersion; expected 1.");
        if (config.Variants == null || config.Variants.Count > 256)
            throw new FormatException("variants must be an array of at most 256 entries.");
        var headers = Labels(config.DashboardHeaderAliases, "dashboardHeaderAliases", 32);
        var expansions = Labels(config.SectionExpansionAliases, "sectionExpansionAliases", 32);
        var templates = FacebookLayoutRegistry.BuiltInDefinitions.ToDictionary(d => d.Id);
        // Omitted families retain C# defaults, including newly shipped templates.
        var definitions = templates.ToDictionary(p => p.Key, p => Freeze(p.Value));
        var origins = templates.ToDictionary(
            p => p.Key,
            p => new Origin(
                p.Key,
                1,
                new ReadOnlyDictionary<FacebookControlRole, IReadOnlyList<string>>(
                    new Dictionary<FacebookControlRole, IReadOnlyList<string>>()
                )
            )
        );
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in config.Variants)
        {
            if (variant == null || !ValidId(variant.Id) || !ids.Add(variant.Id))
                throw new FormatException(
                    "Variant IDs must be unique lowercase versioned identifiers."
                );
            if (
                variant.Version < 1
                || variant.Version > 10000
                || !variant.Id.EndsWith(".v" + variant.Version, StringComparison.Ordinal)
            )
                throw new FormatException(
                    "Variant version must match its .vN ID suffix (1–10000)."
                );
            if (!templates.TryGetValue(variant.BaseId ?? "", out var template))
                throw new FormatException(
                    "Variant baseId must name an existing C# layout template."
                );
            if (templates.ContainsKey(variant.Id) && variant.Id != variant.BaseId)
                throw new FormatException("A built-in ID cannot be assigned to another template.");
            if (
                variant.RequiredLabels == null
                || variant.RequiredLabels.Count > 32
                || variant.ControlAliases == null
                || variant.ControlAliases.Count > 2
            )
                throw new FormatException("Too many or missing label groups/control aliases.");
            var required = variant
                .RequiredLabels.Select(
                    (group, index) =>
                        new FacebookLabelRequirement(
                            "json-required-" + index,
                            Labels(group, "requiredLabels", 32, requireNonEmpty: true)
                        )
                )
                .ToArray();
            var excluded = Labels(variant.ExcludedLabels, "excludedLabels", 32);
            var aliases = new Dictionary<FacebookControlRole, IReadOnlyList<string>>();
            foreach (var pair in variant.ControlAliases)
            {
                if (
                    !Enum.TryParse<FacebookControlRole>(pair.Key, false, out var role)
                    || role is not (FacebookControlRole.Analytics or FacebookControlRole.Dashboard)
                    || !template.ControlRoles.Contains(role)
                )
                    throw new FormatException(
                        "Control aliases may only extend an existing Dashboard or Analytics navigation role."
                    );
                aliases[role] = Array.AsReadOnly(
                    Labels(pair.Value, "controlAliases", 32, requireNonEmpty: true)
                );
            }
            if (!variant.Enabled)
            {
                definitions.Remove(variant.Id);
                origins.Remove(variant.Id);
                continue;
            }
            var extraExclusions = excluded.Select(
                (label, index) =>
                    new FacebookLayoutRule(
                        "json-excluded-" + index,
                        "Excluded label from JSON",
                        o => o.Has(label)
                    )
            );
            definitions[variant.Id] = Freeze(
                template with
                {
                    Id = variant.Id,
                    RequiredLabels = template.RequiredLabels.Concat(required).ToArray(),
                    // C# structure, target checks and exclusions are always inherited.
                    Exclusions = template.Exclusions.Concat(extraExclusions).ToArray(),
                }
            );
            origins[variant.Id] = new(
                template.Id,
                variant.Version,
                new ReadOnlyDictionary<FacebookControlRole, IReadOnlyList<string>>(aliases)
            );
        }
        if (origins.Values.GroupBy(o => (o.BaseId, o.Version)).Any(g => g.Count() > 1))
            throw new FormatException(
                "Two enabled variants cannot share the same baseId and version."
            );
        return new(
            definitions.Values.ToArray(),
            origins,
            new[] { "Professional dashboard", "Professional tools", "Dashboard" }
                .Concat(headers)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            new[] { "See more", "See all" }
                .Concat(expansions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            source
        );
    }

    private static FacebookLayoutDefinition Freeze(FacebookLayoutDefinition d) =>
        d with
        {
            RequiredLabels = Array.AsReadOnly(
                d.RequiredLabels.Select(l => new FacebookLabelRequirement(
                        l.Id,
                        l.Alternatives.ToArray()
                    ))
                    .ToArray()
            ),
            RequiredStructure = Array.AsReadOnly(d.RequiredStructure.ToArray()),
            Exclusions = Array.AsReadOnly(d.Exclusions.ToArray()),
            ControlRoles = Array.AsReadOnly(d.ControlRoles.ToArray()),
        };

    private static string[] Labels(
        List<string>? labels,
        string field,
        int maximum,
        bool requireNonEmpty = false
    )
    {
        if (
            labels == null
            || labels.Count > maximum
            || (requireNonEmpty && labels.Count == 0)
            || labels.Any(l =>
                string.IsNullOrWhiteSpace(l) || l.Length > 128 || l.Any(char.IsControl)
            )
        )
            throw new FormatException(
                $"{field} must contain bounded, nonempty literal labels (maximum 128 characters each)."
            );
        return labels.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Normalize(string label) => Regex.Replace(label.Trim(), @"\s+", " ");

    private static bool ValidId(string? id) =>
        id != null && id.Length <= 120 && Regex.IsMatch(id, @"^[a-z][a-z0-9.-]+\.v[1-9][0-9]*$");

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new FormatException("Duplicate JSON property names are not allowed.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                RejectDuplicateProperties(item);
    }
}
