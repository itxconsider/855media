using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookTabbedReelLayoutTests
{
    [Fact]
    public void SettingsAfterNextRecognizesCaptionAndShareNow()
    {
        var xml = FacebookLayoutRegistryTests
            .Fixture("reel-settings")
            .Replace(
                "text=\"Describe your reel\"",
                "text=\"Describe your reel. You can also add hashtags here.\""
            );
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelComposer);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("composer.reel-settings.v1", match.VariantId);
        Assert.Single(match.Controls, c => c.Role == FacebookControlRole.Caption);
        Assert.Single(match.Controls, c => c.Role == FacebookControlRole.Publish);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelEditor).IsMatch);
    }

    [Fact]
    public void ScreenshotTabbedPickerHasOneVariantAndIsNotStoryPicker()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("reel-picker-tabbed-screenshot");
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelPicker);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("reel.picker-tabbed.v1", match.VariantId);
        Assert.Empty(match.Controls);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.StoryPicker).IsMatch);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelEditor).IsMatch);
        Assert.False(
            FacebookLayoutRegistry
                .Match(xml.Replace("Create reel", "Create story"), FacebookScreenKind.ReelPicker)
                .IsMatch
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScreenshotAudioEditEffectsTextEditorUsesExistingNextControl(bool stickers)
    {
        var xml =
            "<hierarchy><node package='com.facebook.katana' bounds='[0,0][318,566]'>"
            + "<node text='Add audio' bounds='[99,16][170,32]'/><node text='Discover suggestions' bounds='[99,32][220,46]'/>"
            + "<node text='Audio'/><node text='Edit'/><node text='Effects'/><node text='Text'/>"
            + (stickers ? "<node text='Stickers'/>" : "")
            + "<node text='Next' clickable='true' enabled='true' bounds='[269,501][315,561]'/>"
            + "</node></hierarchy>";
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelEditor);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("reel.editor.v1", match.VariantId);
        Assert.Equal(FacebookControlRole.Continue, Assert.Single(match.Controls).Role);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelPicker).IsMatch);
    }
}
