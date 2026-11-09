using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

[Collection("Layout configuration")]
public class FacebookSelectionRegistryTests
{
    private const string Selection = "switcher.selection-confirmation.v1";
    private static string Fixture =>
        FacebookLayoutRegistryTests.Fixture("ld3-selected-yeeyee-notifications");

    private static FacebookLayoutContext Target(string name = "YeeYee's Life") =>
        new(TargetName: name);

    private static FacebookLayoutCatalog Catalog(
        params FacebookLayoutVariantConfiguration[] variants
    ) =>
        FacebookLayoutCatalog.Parse(
            JsonSerializer.Serialize(
                new FacebookLayoutConfiguration { Variants = variants.ToList() },
                FacebookLayoutCatalog.JsonOptions
            )
        );

    [Fact]
    public void ExistingJsonInheritsNewSelectedComponentAndRequiresExactSelectedName()
    {
        var catalog = Catalog();
        var selected = catalog.Match(Fixture, FacebookScreenKind.SwitcherSelection, Target());
        Assert.Equal(Selection, selected.VariantId);
        Assert.Equal(
            (360, 490),
            (
                selected.Control(FacebookControlRole.SelectedActor)!.X,
                selected.Control(FacebookControlRole.SelectedActor)!.Y
            )
        );
        Assert.Null(
            catalog
                .Match(Fixture, FacebookScreenKind.SwitcherSelection)
                .Control(FacebookControlRole.SelectedActor)
        );
        Assert.Null(
            catalog.Match(Fixture, context: Target()).Control(FacebookControlRole.SelectedActor)
        );
        Assert.Null(
            catalog
                .Match(Fixture, FacebookScreenKind.SwitcherSelection, Target("Harch Jing"))
                .Control(FacebookControlRole.SelectedActor)
        );
        Assert.Null(
            catalog
                .Match(Fixture, FacebookScreenKind.SwitcherSelection, Target("YeeYee"))
                .Control(FacebookControlRole.SelectedActor)
        );
    }

    [Fact]
    public void JsonCanDisableOrTightenSelectionButCannotInventControls()
    {
        var disabled = Catalog(
            new FacebookLayoutVariantConfiguration()
            {
                Id = Selection,
                BaseId = Selection,
                Enabled = false,
            }
        );
        Assert.Null(
            disabled
                .Match(Fixture, FacebookScreenKind.SwitcherSelection, Target())
                .Control(FacebookControlRole.SelectedActor)
        );
        var restricted = Catalog(
            new FacebookLayoutVariantConfiguration()
            {
                Id = Selection,
                BaseId = Selection,
                RequiredLabels =
                [
                    ["Required evidence absent from this capture"],
                ],
            }
        );
        Assert.False(
            restricted.Match(Fixture, FacebookScreenKind.SwitcherSelection, Target()).IsMatch
        );
        foreach (var role in new[] { "SelectedActor", "Albums" })
            Assert.Throws<FormatException>(() =>
                Catalog(
                    new FacebookLayoutVariantConfiguration()
                    {
                        Id = Selection,
                        BaseId = Selection,
                        ControlAliases = new() { [role] = ["Edit profile"] },
                    }
                )
            );
    }

    [Fact]
    public void VersionedRulesKeepFreshSafetyAndAmbiguityChecks()
    {
        var catalog = Catalog(
            new FacebookLayoutVariantConfiguration()
            {
                Id = "switcher.selection-custom.v2",
                BaseId = Selection,
                Version = 2,
            }
        );
        Assert.Equal(
            "switcher.selection-custom.v2",
            catalog.Match(Fixture, FacebookScreenKind.SwitcherSelection, Target()).VariantId
        );
        var disabledRow = XDocument.Parse(Fixture);
        disabledRow
            .Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "YeeYee's Life")
            .SetAttributeValue("enabled", "false");
        Assert.Null(
            catalog
                .Match(disabledRow.ToString(), FacebookScreenKind.SwitcherSelection, Target())
                .Control(FacebookControlRole.SelectedActor)
        );
        Assert.Null(
            catalog
                .Match(
                    Fixture.Replace("com.facebook.katana", "foreign.app"),
                    FacebookScreenKind.SwitcherSelection,
                    Target()
                )
                .Control(FacebookControlRole.SelectedActor)
        );
        Assert.Throws<FormatException>(() =>
            Catalog(
                new FacebookLayoutVariantConfiguration()
                {
                    Id = "switcher.selection-a.v2",
                    BaseId = Selection,
                    Version = 2,
                },
                new FacebookLayoutVariantConfiguration()
                {
                    Id = "switcher.selection-b.v2",
                    BaseId = Selection,
                    Version = 2,
                }
            )
        );
    }

    [Fact]
    public void RealTabbedPickerExposesAlbumsThroughRegistry()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld14-reel-tabs-scrolled");
        var match = Catalog().Match(xml, FacebookScreenKind.ReelPicker);
        Assert.Equal("reel.picker-tabbed.v1", match.VariantId);
        Assert.NotNull(match.Control(FacebookControlRole.Albums));
        Assert.Null(
            Catalog(
                    new FacebookLayoutVariantConfiguration()
                    {
                        Id = "reel.picker-tabbed.v1",
                        BaseId = "reel.picker-tabbed.v1",
                        Enabled = false,
                    }
                )
                .Match(xml, FacebookScreenKind.ReelPicker)
                .Control(FacebookControlRole.Albums)
        );
    }

    [Fact]
    public void FetchActuallyHonorsDisabledRegistrySelectionComponent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "selection-layout-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "layouts.json");
        File.WriteAllText(
            file,
            JsonSerializer.Serialize(
                new FacebookLayoutConfiguration
                {
                    Variants =
                    [
                        new FacebookLayoutVariantConfiguration()
                        {
                            Id = Selection,
                            BaseId = Selection,
                            Enabled = false,
                        },
                    ],
                },
                FacebookLayoutCatalog.JsonOptions
            )
        );
        try
        {
            Assert.True(FacebookLayoutRegistry.ReloadConfiguration(file).Applied);
            PFlowPersonaSwitcherService.NativeSwitcherActor[] members =
            [
                new("YeeYee's Life", "Page", true),
                new("Harch Jing", "Profile", false),
                new("KC Waterheaters", "Page", false),
                new("Jungle Bushcraft", "Page", false),
                new("Breastfeeding Journey", "Page", false),
                new("SAM Outdoor", "Page", false),
            ];
            Assert.Null(PFlowPersonaSwitcherService.FetchSelectedRow(Fixture, members));
        }
        finally
        {
            FacebookLayoutRegistry.UseBuiltInConfiguration();
            Directory.Delete(directory, true);
        }
    }
}
