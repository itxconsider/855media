using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

[CollectionDefinition("Layout configuration", DisableParallelization = true)]
public class LayoutConfigurationCollection { }

[Collection("Layout configuration")]
public class FacebookLayoutJsonTests
{
    private const string Dashboard =
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node text='Studio dashboard preview' bounds='[80,40][600,100]'/><node text='Insights' enabled='true' clickable='true' bounds='[100,180][420,260]'/></node></hierarchy>";

    private static FacebookLayoutConfiguration Config(int version = 2) =>
        new()
        {
            DashboardHeaderAliases = ["Studio dashboard preview"],
            Variants =
            [
                new()
                {
                    Id = "dashboard.custom.v" + version,
                    BaseId = "dashboard.analytics-tab.v1",
                    Version = version,
                    RequiredLabels =
                    [
                        ["Insights"],
                    ],
                    ControlAliases = new() { ["Analytics"] = ["Insights"] },
                },
            ],
        };

    private static string Json(FacebookLayoutConfiguration config) =>
        JsonSerializer.Serialize(config, FacebookLayoutCatalog.JsonOptions);

    [Fact]
    public void ConfiguredExpansionLabelsStillRequireTheirSectionContext()
    {
        var config = new FacebookLayoutConfiguration
        {
            SectionExpansionAliases = ["Show options"],
            Variants = [],
        };
        var catalog = FacebookLayoutCatalog.Parse(Json(config));
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld14-page-menu-spaced-name")
        );
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "What's on Facebook"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", "[24,480][300,520]")
            ),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "Show options"),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", "true"),
                new XAttribute("bounds", "[450,480][600,530]")
            )
        );
        var xml = doc.ToString();
        Assert.NotNull(
            catalog
                .Match(
                    xml,
                    FacebookScreenKind.Menu,
                    new("Target Page", SectionLabel: "What's on Facebook")
                )
                .Control(FacebookControlRole.ExpandSection)
        );
        Assert.Null(
            catalog
                .Match(xml, FacebookScreenKind.Menu, new("Target Page"))
                .Control(FacebookControlRole.ExpandSection)
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(
                    xml,
                    FacebookScreenKind.Menu,
                    new("Target Page", SectionLabel: "What's on Facebook")
                )
                .Control(FacebookControlRole.ExpandSection)
        );
    }

    [Fact]
    public void PageFetchEntryUsesConfiguredDashboardControlsAndRefreshHeaders()
    {
        var directory = Path.Combine(Path.GetTempPath(), "layout-fetch-entry-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "facebook-layouts.json");
        try
        {
            var config = new FacebookLayoutConfiguration
            {
                DashboardHeaderAliases = ["Studio dashboard preview"],
                Variants =
                [
                    new()
                    {
                        Id = "profile.dashboard-alias.v2",
                        BaseId = "profile.own.v1",
                        Version = 2,
                        ControlAliases = new() { ["Dashboard"] = ["Creator tools"] },
                    },
                ],
            };
            File.WriteAllText(file, Json(config));
            Assert.True(FacebookLayoutRegistry.ReloadConfiguration(file).Applied);
            var home = AtxAgentClient.ParseBloksHierarchy(
                FacebookLayoutRegistryTests
                    .Fixture("own-profile")
                    .Replace("Professional dashboard", "Creator tools")
            );
            Assert.NotNull(LDPlayerService.FindPageDashboardControl(home, "W500 PH", null));
            Assert.Null(LDPlayerService.FindPageDashboardControl(home, "Wrong Page", null));
            var loading = AtxAgentClient.ParseBloksHierarchy(
                Dashboard.Replace(
                    "</hierarchy>",
                    "<node class='android.widget.ProgressBar'/></hierarchy>"
                )
            );
            Assert.False(
                FacebookLayoutRegistry.MatchScreen(loading, FacebookScreenKind.Dashboard).IsMatch
            );
            Assert.True(FacebookLayoutRegistry.HasDashboardHeader(loading));
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DefaultJsonRoundTripsEveryBuiltInDefinition()
    {
        var shipped = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "facebook-layouts.default.json")
        );
        Assert.Equal(FacebookLayoutCatalog.CreateDefaultJson(), shipped);
        var catalog = FacebookLayoutCatalog.Parse(FacebookLayoutCatalog.CreateDefaultJson());
        Assert.Equal(
            FacebookLayoutRegistry.BuiltInDefinitions.Select(d => d.Id),
            catalog.Definitions.Select(d => d.Id)
        );
        foreach (
            var name in new[]
            {
                "dashboard",
                "analytics-summary",
                "reel-settings",
                "ld14-page-menu-spaced-name",
                "ld12-android9-recents",
            }
        )
        {
            var xml = FacebookLayoutRegistryTests.Fixture(name);
            Assert.Equal(FacebookLayoutRegistry.Match(xml).VariantId, catalog.Match(xml).VariantId);
        }
    }

    [Fact]
    public void NewLabelsAndVersionWorkWithoutChangingCodeOrGlobalRules()
    {
        var catalog = FacebookLayoutCatalog.Parse(Json(Config()));
        var match = catalog.Match(Dashboard, FacebookScreenKind.Dashboard);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("dashboard.custom.v2", match.VariantId);
        Assert.Equal(
            (260, 220),
            (
                match.Control(FacebookControlRole.Analytics)!.X,
                match.Control(FacebookControlRole.Analytics)!.Y
            )
        );
        Assert.Contains(
            match.Candidates,
            c =>
                c.VariantId == "dashboard.analytics-tab.v1"
                && c.ExcludedBy.Contains("superseded-by-matching-version")
        );
        Assert.False(FacebookLayoutRegistry.Match(Dashboard, FacebookScreenKind.Dashboard).IsMatch);
    }

    [Fact]
    public void ConditionalNewVersionFallsBackWhenItsRequirementsDoNotMatch()
    {
        var config = Config();
        var catalog = FacebookLayoutCatalog.Parse(Json(config));
        var native = FacebookLayoutRegistryTests.Fixture("dashboard");
        Assert.Equal(
            "dashboard.analytics-tab.v1",
            catalog.Match(native, FacebookScreenKind.Dashboard).VariantId
        );
        config.Variants![0].Enabled = false;
        Assert.DoesNotContain(
            FacebookLayoutCatalog.Parse(Json(config)).Definitions,
            d => d.Id == "dashboard.custom.v2"
        );
    }

    [Fact]
    public void NewerExcludedVersionDoesNotHideAnOlderMatchingVersion()
    {
        var config = Config();
        config.Variants!.Add(
            new()
            {
                Id = "dashboard.custom.v3",
                BaseId = "dashboard.analytics-tab.v1",
                Version = 3,
                ExcludedLabels = ["Insights"],
            }
        );
        Assert.Equal(
            "dashboard.custom.v2",
            FacebookLayoutCatalog
                .Parse(Json(config))
                .Match(Dashboard, FacebookScreenKind.Dashboard)
                .VariantId
        );
    }

    [Fact]
    public void NativeAmbiguityCannotBeMaskedByAnAlias()
    {
        var config = Config();
        config.Variants![0].RequiredLabels.Clear();
        var xml = FacebookLayoutRegistryTests
            .Fixture("dashboard")
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' content-desc='Analytics, tab 2 of 4' enabled='true' clickable='true' bounds='[10,300][200,370]'/><node package='com.facebook.katana' text='Insights' enabled='true' clickable='true' bounds='[10,400][200,470]'/></hierarchy>"
            );
        var match = FacebookLayoutCatalog
            .Parse(Json(config))
            .Match(xml, FacebookScreenKind.Dashboard);
        Assert.True(match.IsMatch);
        Assert.Null(match.Control(FacebookControlRole.Analytics));
        Assert.Contains("Analytics:ambiguous", match.MissingControls);
    }

    [Theory]
    [InlineData("enabled", "false")]
    [InlineData("visible-to-user", "false")]
    public void ConfigurableAliasesStillRequireUsableControls(string attribute, string value)
    {
        var xml = Dashboard.Replace("text='Insights'", $"text='Insights' {attribute}='{value}'");
        if (attribute == "enabled")
            xml = xml.Replace("enabled='false' enabled='true'", "enabled='false'");
        var match = FacebookLayoutCatalog
            .Parse(Json(Config()))
            .Match(xml, FacebookScreenKind.Dashboard);
        Assert.Null(match.Control(FacebookControlRole.Analytics));
    }

    [Fact]
    public void NewDashboardTitleStillCannotConfirmAnalyticsPreviewAsDestination()
    {
        var xml = Dashboard
            .Replace("text='Insights'", "text='Analytics'")
            .Replace("[100,180][420,260]", "[100,120][420,180]")
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' text='Views' bounds='[100,600][250,650]'/></hierarchy>"
            );
        Assert.True(
            LDPlayerService.HasAnalyticsContentEvidence(AtxAgentClient.ParseBloksHierarchy(xml))
        );
        Assert.False(
            FacebookLayoutCatalog
                .Parse(Json(Config()))
                .Match(xml, FacebookScreenKind.Analytics)
                .IsMatch
        );
    }

    [Fact]
    public void JsonCannotWeakenOwnProfileTargetChecks()
    {
        var config = new FacebookLayoutConfiguration
        {
            Variants =
            [
                new()
                {
                    Id = "profile.custom.v2",
                    BaseId = "profile.own.v1",
                    Version = 2,
                },
            ],
        };
        Assert.False(
            FacebookLayoutCatalog
                .Parse(Json(config))
                .Match(
                    FacebookLayoutRegistryTests.Fixture("own-profile"),
                    FacebookScreenKind.OwnProfile,
                    new("Wrong actor")
                )
                .IsMatch
        );
    }

    [Theory]
    [InlineData("{\"formatVersion\":2,\"variants\":[]}")]
    [InlineData("{\"variants\":[]}")]
    [InlineData("{\"formatVersion\":1,\"variants\":null}")]
    [InlineData("{\"formatVersion\":1,\"formatVersion\":1,\"variants\":[]}")]
    [InlineData("{\"formatVersion\":1,\"variants\":[],\"script\":\"run\"}")]
    [InlineData(
        "{\"formatVersion\":1,\"variants\":[{\"id\":\"custom.v2\",\"baseId\":\"missing.v1\",\"version\":2}]}"
    )]
    [InlineData(
        "{\"formatVersion\":1,\"variants\":[{\"id\":\"custom.v2\",\"baseId\":\"composer.reel-settings.v1\",\"version\":2,\"controlAliases\":{\"Publish\":[\"Next\"]}}]}"
    )]
    [InlineData(
        "{\"formatVersion\":1,\"variants\":[{\"id\":\"custom.v2\",\"baseId\":\"profile.own.v1\",\"version\":2,\"exclusions\":[]}]}"
    )]
    public void InvalidOrUnsafePacksAreRejected(string json) =>
        Assert.Throws<FormatException>(() => FacebookLayoutCatalog.Parse(json));

    [Fact]
    public void DuplicateFamilyVersionsAndOversizedDocumentsAreRejected()
    {
        var config = Config();
        config.Variants!.Add(
            new()
            {
                Id = "dashboard.other.v2",
                BaseId = "dashboard.analytics-tab.v1",
                Version = 2,
            }
        );
        Assert.Throws<FormatException>(() => FacebookLayoutCatalog.Parse(Json(config)));
        Assert.Throws<FormatException>(() =>
            FacebookLayoutCatalog.Parse(new string(' ', FacebookLayoutCatalog.MaximumFileBytes + 1))
        );
    }

    [Fact]
    public void ExplicitReloadInvalidatesMatchCacheAndBadReloadKeepsLastGoodRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), "layout-json-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "facebook-layouts.json");
        try
        {
            using var scope = FacebookHierarchyCache.Enter();
            var xml = FacebookLayoutRegistryTests.Fixture("dashboard");
            var old = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Dashboard);
            var oldObservation = new FacebookLayoutObservation(xml);
            var config = Config();
            config.Variants![0].RequiredLabels.Clear();
            File.WriteAllText(file, Json(config));
            var loaded = FacebookLayoutRegistry.ReloadConfiguration(file);
            Assert.True(loaded.Applied, loaded.Message);
            var current = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Dashboard);
            Assert.Equal("dashboard.custom.v2", current.VariantId);
            Assert.NotEqual(old.ObservationId, current.ObservationId);
            Assert.Equal(
                "dashboard.analytics-tab.v1",
                FacebookLayoutRegistry.Match(oldObservation, FacebookScreenKind.Dashboard).VariantId
            );
            File.WriteAllText(file, "{broken");
            var rejected = FacebookLayoutRegistry.ReloadConfiguration(file);
            Assert.False(rejected.Applied);
            Assert.Equal(loaded.Generation, rejected.Generation);
            Assert.Equal(
                "dashboard.custom.v2",
                FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Dashboard).VariantId
            );
            Assert.Equal("{broken", File.ReadAllText(file));
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StartupCreatesAFileOnceAndPreservesUserEdits()
    {
        var directory = Path.Combine(Path.GetTempPath(), "layout-startup-" + Guid.NewGuid());
        try
        {
            Assert.True(FacebookLayoutRegistry.InitializeConfiguration(directory).Applied);
            var file = Path.Combine(directory, "facebook-layouts.json");
            var edited = Json(Config());
            File.WriteAllText(file, edited);
            Assert.True(FacebookLayoutRegistry.InitializeConfiguration(directory).Applied);
            Assert.Equal(edited, File.ReadAllText(file));
            Assert.Equal(
                "dashboard.custom.v2",
                FacebookLayoutRegistry.Match(Dashboard, FacebookScreenKind.Dashboard).VariantId
            );
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReadersKeepCoherentSnapshotsDuringReloadAndReset()
    {
        var directory = Path.Combine(Path.GetTempPath(), "layout-concurrent-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "facebook-layouts.json");
        try
        {
            var config = Config();
            config.Variants![0].RequiredLabels.Clear();
            var json = Json(config);
            File.WriteAllText(file, json);
            var xml = FacebookLayoutRegistryTests.Fixture("dashboard");
            var readers = Enumerable
                .Range(0, 4)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        for (var i = 0; i < 40; i++)
                        {
                            var observation = new FacebookLayoutObservation(xml);
                            var match = FacebookLayoutRegistry.Match(
                                observation,
                                FacebookScreenKind.Dashboard
                            );
                            Assert.True(match.IsMatch, match.Diagnostic);
                            Assert.Equal(
                                match.VariantId,
                                FacebookLayoutRegistry
                                    .Match(observation, FacebookScreenKind.Dashboard)
                                    .VariantId
                            );
                        }
                    })
                )
                .ToArray();
            for (var i = 0; i < 10; i++)
            {
                Assert.True(FacebookLayoutRegistry.ReloadConfiguration(file).Applied);
                Assert.True(FacebookLayoutRegistry.UseBuiltInConfiguration().Applied);
            }
            await Task.WhenAll(readers);
            Assert.Equal(json, File.ReadAllText(file));
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UnreadableAndInvalidUtf8ReloadsKeepTheCurrentGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "layout-unreadable-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "facebook-layouts.json");
        try
        {
            File.WriteAllText(file, Json(Config()));
            var loaded = FacebookLayoutRegistry.ReloadConfiguration(file);
            Assert.True(loaded.Applied);
            File.WriteAllBytes(file, [0xff, 0xfe, 0x00]);
            var rejected = FacebookLayoutRegistry.ReloadConfiguration(file);
            Assert.False(rejected.Applied);
            Assert.Equal(loaded.Generation, rejected.Generation);
            File.Delete(file);
            rejected = FacebookLayoutRegistry.ReloadConfiguration(file);
            Assert.False(rejected.Applied);
            Assert.Equal(loaded.Generation, rejected.Generation);
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            Directory.Delete(directory, true);
        }
    }
}
