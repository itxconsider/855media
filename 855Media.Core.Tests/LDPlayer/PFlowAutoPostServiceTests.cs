using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAutoPostServiceTests
{
    [Theory]
    [InlineData(0, 180)]
    [InlineData(104857600, 580)]
    [InlineData(524288000, 2180)]
    [InlineData(long.MaxValue, 3600)]
    public void LargeUploadsReceiveBoundedExtraConfirmationTime(long bytes, int seconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(seconds),
            PFlowAutoPostService.UploadConfirmationWindow(bytes, TimeSpan.FromSeconds(180))
        );
    }

    [Theory]
    [InlineData("ld9-reels-fullscreen", "Reels")]
    [InlineData("ld1-feed-finished-video-product", "Feed")]
    [InlineData("ld9-menu-over-notifications", null)]
    public void UploadWaitBrowsesOnlyRecognizedSurfaces(string fixture, string? expected)
    {
        var xml = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", fixture + ".xml")
        );
        Assert.Equal(expected, PFlowAutoPostService.WaitingBrowseSurface(xml));
    }

    [Theory]
    [InlineData("Share now")]
    [InlineData("Describe your reel")]
    [InlineData("Log in")]
    public void UploadWaitDoesNotScrollComposerLoginOrUnknownScreens(string label)
    {
        var xml = new XElement(
            "hierarchy",
            new XElement("node", new XAttribute("text", label))
        ).ToString();
        Assert.Null(PFlowAutoPostService.WaitingBrowseSurface(xml));
    }

    [Theory]
    [InlineData("Luke Kaz\n\u00a0\u00a0", "Luke Kaz", true)]
    [InlineData("Luke\u00a0\u00a0Kaz\r\n", " Luke Kaz ", true)]
    [InlineData("Posting as\nLuke Kaz", "Luke Kaz", true)]
    [InlineData(" Sharing as Luke\tKaz ", "Luke Kaz", true)]
    [InlineData("Luke Kaz\nGet verified", "Luke Kaz", false)]
    [InlineData("Luke Kaz Fans", "Luke Kaz", false)]
    [InlineData("Different Actor", "Luke Kaz", false)]
    [InlineData("Luke Kaz", " ", false)]
    public void ActorConfirmationNormalizesWhitespaceWithoutMatchingOtherNames(
        string label,
        string expected,
        bool matches
    )
    {
        foreach (var attribute in new[] { "text", "content-desc" })
        {
            var xml = new XElement(
                "hierarchy",
                new XElement("node", new XAttribute(attribute, label))
            ).ToString();
            Assert.Equal(matches, PFlowAutoPostService.ComposerMatchesTarget(xml, expected));
        }
    }

    [Fact]
    public void Constructor_NullLDPlayer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PFlowAutoPostService(null!));
    }

    [Fact]
    public void FindCaptionInputNode_ValidXml_FindsMatchingHintNode()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node text=""Describe your reel..."" bounds=""[50,150][670,300]"" />
            </node>
        </hierarchy>";

        var node = PFlowAutoPostService.FindCaptionInputNode(
            xml,
            ["Describe your reel", "Write a caption"]
        );
        Assert.NotNull(node);
        Assert.Equal("Describe your reel...", node?.Attribute("text")?.Value);
    }

    [Fact]
    public void FindCaptionInputNode_EditTextFallback_FindsEditTextNode()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node class=""android.widget.EditText"" bounds=""[50,200][650,400]"" />
            </node>
        </hierarchy>";

        var node = PFlowAutoPostService.FindCaptionInputNode(xml, ["NonExistentHint"]);
        Assert.NotNull(node);
        Assert.Equal("android.widget.EditText", node?.Attribute("class")?.Value);
    }

    [Fact]
    public void FindCaptionInputNode_NullOrEmptyXml_ReturnsNull()
    {
        Assert.Null(PFlowAutoPostService.FindCaptionInputNode(null, ["hint"]));
        Assert.Null(PFlowAutoPostService.FindCaptionInputNode(string.Empty, ["hint"]));
        Assert.Null(PFlowAutoPostService.FindCaptionInputNode("   ", ["hint"]));
    }

    [Fact]
    public void IsFacebookUploadNotificationActive_WithUploadChannel_ReturnsTrue()
    {
        var dumpsys =
            @"
NotificationRecord(0x12345678: pkg=com.facebook.katana id=100 tag=null)
  channel=media_upload_notification_channel_id
  flags=0x62
";
        var result = PFlowAutoPostService.IsFacebookUploadNotificationActive(dumpsys);
        Assert.True(result);
    }

    [Fact]
    public void IsFacebookUploadNotificationActive_WithPostingFlag_ReturnsTrue()
    {
        var dumpsys =
            @"
NotificationRecord(0x87654321: pkg=com.facebook.katana id=200 tag=null)
  title=Posting to Facebook
  flags=0x6a
";
        var result = PFlowAutoPostService.IsFacebookUploadNotificationActive(dumpsys);
        Assert.True(result);
    }

    [Fact]
    public void IsFacebookUploadNotificationActive_EmptyOrNoKatana_ReturnsFalse()
    {
        Assert.False(PFlowAutoPostService.IsFacebookUploadNotificationActive(string.Empty));
        Assert.False(PFlowAutoPostService.IsFacebookUploadNotificationActive("random text"));

        var otherPkg =
            @"
NotificationRecord(0x1111: pkg=com.google.android.youtube id=1 tag=null)
  channel=media_upload_notification_channel_id
";
        Assert.False(PFlowAutoPostService.IsFacebookUploadNotificationActive(otherPkg));
    }

    [Fact]
    public async Task PostReelAsync_NonExistentFile_ReturnsFalse()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);
        var autoPostService = new PFlowAutoPostService(ldPlayer, client);

        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".mp4"
        );
        var result = await autoPostService.PostReelAsync(0, nonExistentPath);

        Assert.False(result);
    }

    [Fact]
    public async Task PostPictureAsync_NonExistentFile_ReturnsFalse()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);
        var autoPostService = new PFlowAutoPostService(ldPlayer, client);

        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".jpg"
        );
        var result = await autoPostService.PostPictureAsync(0, nonExistentPath);

        Assert.False(result);
    }

    [Fact]
    public async Task PostVideoAsync_NonExistentFile_ReturnsFalse()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);
        var autoPostService = new PFlowAutoPostService(ldPlayer, client);

        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".mp4"
        );
        var result = await autoPostService.PostVideoAsync(0, nonExistentPath);

        Assert.False(result);
    }

    [Fact]
    public async Task WaitForUploadCompletedAsync_WhenCancelled_Throws()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);
        var autoPostService = new PFlowAutoPostService(ldPlayer, client);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            autoPostService.WaitForUploadCompletedAsync(
                0,
                timeout: TimeSpan.FromMilliseconds(50),
                cancellationToken: cts.Token
            )
        );
    }

    [Fact]
    public async Task LDPlayerService_DelegationToAutoPostService_FunctionsCorrectly()
    {
        using var client = new AtxAgentClient(7912);
        var ldPlayer = new LDPlayerService(client);

        var nonExistentVideo = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".mp4"
        );
        var nonExistentPic = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".jpg"
        );

        // Verifying ILDPlayerService delegates to IPFlowAutoPostService correctly
        Assert.False(await ldPlayer.PostReelToFacebookAsync(0, nonExistentVideo));
        Assert.False(await ldPlayer.PostPictureToFacebookAsync(0, nonExistentPic));
        Assert.False(await ldPlayer.PostVideoToFacebookAsync(0, nonExistentVideo));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ldPlayer.WaitForFacebookUploadCompletedAsync(0, cancellationToken: cts.Token)
        );
    }
}
