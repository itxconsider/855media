using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowReelTitleTests
{
    [Theory]
    [InlineData("Repair the sink #plumbing #DIY", "Repair the sink")]
    [InlineData("#DIY Repair\n the sink #plumbing", "Repair the sink")]
    [InlineData("#plumbing #DIY", "")]
    [InlineData("No hashtags here", "No hashtags here")]
    [InlineData("Work ＃plumbing #", "Work")]
    public void TitleOmitsHashtagsAndNormalizesCaptionWhitespace(string caption, string expected) =>
        Assert.Equal(expected, PFlowAutoPostService.ReelTitleFromCaption(caption));

    [Fact]
    public void TitleFieldRemainsDistinctFromCaptionBeforeAndAfterEntry()
    {
        var xml =
            "<hierarchy><node bounds='[0,0][720,1280]'/><node text='Reel Settings'/><node text='Share now' clickable='true' enabled='true' bounds='[160,1168][688,1256]'/><node package='com.facebook.katana' class='android.widget.EditText' text='Add title' enabled='true' clickable='true' bounds='[220,168][720,214]'/><node package='com.facebook.katana' class='android.widget.EditText' text='Repair #DIY' content-desc='Describe your reel. You can also add hashtags here.' enabled='true' clickable='true' bounds='[220,216][720,448]'/></hierarchy>";
        Assert.Equal(
            "Add title",
            PFlowAutoPostService.FindReelTitleField(xml)!.Attribute("text")!.Value
        );
        Assert.Equal(
            "Repair",
            PFlowAutoPostService
                .FindReelTitleField(xml.Replace("Add title", "Repair"))!
                .Attribute("text")!
                .Value
        );
        Assert.Equal(
            "Repair #DIY",
            PFlowAutoPostService.FindPFlowCaptionField(xml)!.Attribute("text")!.Value
        );
        Assert.Null(
            PFlowAutoPostService.FindReelTitleField(xml.Replace("Reel Settings", "Create post"))
        );
        var passive = xml.Replace(
                "<hierarchy>",
                "<hierarchy><node class='android.view.ViewGroup' enabled='false'>"
            )
            .Replace("</hierarchy>", "</node></hierarchy>");
        Assert.NotNull(PFlowAutoPostService.FindReelTitleField(passive));
        Assert.Null(
            PFlowAutoPostService.FindReelTitleField(
                xml.Replace(
                    "enabled='true' clickable='true' bounds='[220,168]",
                    "enabled='false' clickable='true' bounds='[220,168]"
                )
            )
        );
    }
}
