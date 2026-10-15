using System.Collections.Generic;

namespace _855Media.Core.LDPlayer;

// Data only: no scripts, regex, coordinates, actions, credentials or AI updates.
public sealed class FacebookLayoutConfiguration
{
    public int FormatVersion { get; set; } = 1;
    public List<string> DashboardHeaderAliases { get; set; } = [];
    public List<string> SectionExpansionAliases { get; set; } = [];
    public List<FacebookLayoutVariantConfiguration>? Variants { get; set; }
}

public sealed class FacebookLayoutVariantConfiguration
{
    public string Id { get; set; } = "";
    public string BaseId { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;

    // Each group is required; at least one label within each group must match.
    public List<List<string>> RequiredLabels { get; set; } = [];
    public List<string> ExcludedLabels { get; set; } = [];

    // Additional fallback labels for existing Dashboard/Analytics navigation roles.
    public Dictionary<string, List<string>> ControlAliases { get; set; } = [];
}
