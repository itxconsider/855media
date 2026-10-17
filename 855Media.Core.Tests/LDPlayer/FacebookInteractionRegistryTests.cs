using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookInteractionRegistryTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    [Theory]
    [InlineData(
        "ld21-feed-icon-actions-captured",
        (int)FacebookScreenKind.PostActionBar,
        (int)FacebookControlRole.Comment
    )]
    [InlineData(
        "ld21-love-pressed-captured",
        (int)FacebookScreenKind.AppliedReaction,
        (int)FacebookControlRole.AppliedReaction
    )]
    [InlineData(
        "ld21-reaction-banner-captured",
        (int)FacebookScreenKind.ReactionPicker,
        (int)FacebookControlRole.ReactionLove
    )]
    [InlineData(
        "ld21-comments-empty-captured",
        (int)FacebookScreenKind.CommentSheet,
        (int)FacebookControlRole.CommentInput
    )]
    [InlineData(
        "ld21-comments-typed-captured",
        (int)FacebookScreenKind.CommentSheet,
        (int)FacebookControlRole.SendComment
    )]
    public void CapturedComponentsExposeScopedControls(string fixture, int kind, int role)
    {
        var match = FacebookLayoutRegistry.Match(
            Fixture(fixture),
            (FacebookScreenKind)kind,
            new("Robert Munro")
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control((FacebookControlRole)role));
    }

    [Fact]
    public void PublicationComponentRequiresExactCommentButDoesNotProvidePostIdentity()
    {
        var xml = Fixture("ld21-comments-published-captured");
        Assert.True(
            FacebookLayoutRegistry
                .Match(
                    xml,
                    FacebookScreenKind.PublishedComment,
                    new("Robert Munro", CommentText: "wow that's amazing !!")
                )
                .IsMatch
        );
        Assert.False(
            FacebookLayoutRegistry
                .Match(
                    xml,
                    FacebookScreenKind.PublishedComment,
                    new("Wrong Author", CommentText: "wow that's amazing !!")
                )
                .IsMatch
        );
        Assert.False(
            FacebookLayoutRegistry
                .Match(
                    xml,
                    FacebookScreenKind.PublishedComment,
                    new("Robert Munro", CommentText: "other text")
                )
                .IsMatch
        );
        Assert.Null(ScreenTimeHierarchy.VisiblePostId(xml));
    }

    [Fact]
    public void NewComponentsCanBeDisabledByJsonWithoutOverridingNativeGuards()
    {
        var config = new FacebookLayoutConfiguration
        {
            Variants =
            [
                new()
                {
                    Id = "reactions.picker.v1",
                    BaseId = "reactions.picker.v1",
                    Enabled = false,
                },
            ],
        };
        var catalog = FacebookLayoutCatalog.Parse(
            JsonSerializer.Serialize(config, FacebookLayoutCatalog.JsonOptions)
        );
        Assert.False(
            catalog
                .Match(Fixture("ld21-reaction-banner-captured"), FacebookScreenKind.ReactionPicker)
                .IsMatch
        );
        config.Variants[0].Enabled = true;
        config.Variants[0].ControlAliases["ReactionLove"] = ["Arbitrary button"];
        Assert.Throws<FormatException>(() =>
            FacebookLayoutCatalog.Parse(
                JsonSerializer.Serialize(config, FacebookLayoutCatalog.JsonOptions)
            )
        );
    }

    [Fact]
    public void ComponentMatchingDoesNotIntroduceWholeScreenAmbiguity()
    {
        var xml = Fixture("ld21-feed-icon-actions-captured");
        var catalog = FacebookLayoutCatalog.Parse(FacebookLayoutCatalog.CreateDefaultJson());
        var baselineConfig = new FacebookLayoutConfiguration
        {
            Variants = catalog
                .Definitions.Where(d =>
                    d.Screen
                        is FacebookScreenKind.PostActionBar
                            or FacebookScreenKind.AppliedReaction
                            or FacebookScreenKind.ReactionPicker
                            or FacebookScreenKind.CommentSheet
                            or FacebookScreenKind.PublishedComment
                )
                .Select(d => new FacebookLayoutVariantConfiguration
                {
                    Id = d.Id,
                    BaseId = d.Id,
                    Enabled = false,
                })
                .ToList(),
        };
        var withoutComponents = FacebookLayoutCatalog.Parse(
            JsonSerializer.Serialize(baselineConfig, FacebookLayoutCatalog.JsonOptions)
        );
        Assert.Equal(withoutComponents.Match(xml).Status, catalog.Match(xml).Status);
        Assert.Equal(withoutComponents.Match(xml).Screen, catalog.Match(xml).Screen);
    }
}
