using System.Net;
using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowMediaIntentTests
{
    [Theory]
    [InlineData("/sdcard/clip.mp4", false)]
    [InlineData("/sdcard/photo.jpg", true)]
    public void EveryJobExplicitlyOpensResolverInsteadOfSavedAlwaysDestination(
        string path,
        bool image
    )
    {
        Assert.StartsWith(
            "am start -n android/com.android.internal.app.ResolverActivity -a android.intent.action.SEND ",
            PFlowMediaIntent.ShareCommand(path, "com.facebook.katana", image)
        );
    }

    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.MOV", "video/quicktime")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("clip.mkv", "video/x-matroska")]
    [InlineData("photo.webp", "image/webp")]
    [InlineData("photo.png", "image/png")]
    public void UsesActualFileType(string path, string mime) =>
        Assert.Equal(mime, PFlowMediaIntent.MimeType(path));

    [Theory]
    [InlineData("")]
    [InlineData("Error: Activity not started, unable to resolve Intent")]
    [InlineData(
        "Starting: Intent { act=android.intent.action.SEND }\nError: Activity class does not exist"
    )]
    [InlineData("Starting: Intent {}\nPermission Denial")]
    public void ShareErrorsNeverReportSuccess(string output) =>
        Assert.False(PFlowMediaIntent.ShareStarted(output));

    [Fact]
    public void StartAndScanNeedTheirOwnAcknowledgement()
    {
        Assert.True(
            PFlowMediaIntent.ShareStarted("Starting: Intent { act=android.intent.action.SEND }")
        );
        Assert.True(PFlowMediaIntent.ScanAcknowledged("Broadcast completed: result=0"));
        Assert.False(PFlowMediaIntent.ScanAcknowledged("Broadcasting: Intent {}"));
        Assert.False(
            PFlowMediaIntent.ScanAcknowledged("Broadcast completed: result=0\nSecurityException")
        );
        Assert.False(PFlowMediaIntent.ShareStarted("Broadcast completed: result=0"));
        Assert.False(PFlowMediaIntent.ScanAcknowledged("Starting: Intent {}"));
    }

    [Fact]
    public void WrongMediaCategoryAndUnknownFileAreRejected()
    {
        Assert.Null(PFlowMediaIntent.ShareCommand("photo.png", "app", false));
        Assert.Null(PFlowMediaIntent.ShareCommand("clip.mp4", "app", true));
        Assert.Null(PFlowMediaIntent.ShareCommand("file.exe", "app", false));
        Assert.Contains(
            "'file:///sdcard/a b.mp4'",
            PFlowMediaIntent.ShareCommand("/sdcard/a b.mp4", "app", false)
        );
    }

    [Theory]
    [InlineData("Starting: Intent {}", true)]
    [InlineData("Starting: Intent {}\nError: unable to resolve Intent", false)]
    [InlineData("", false)]
    public async Task AtxRejectsAmErrorsEvenWithZeroExitCode(string output, bool expected)
    {
        using var http = new HttpClient(new Handler(output));
        using var client = new AtxAgentClient(7912, http);
        Assert.Equal(expected, await client.ShareVideoAsync(1, "/sdcard/clip.webm"));
    }

    sealed class Handler(string output) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { output, exitCode = 0 }),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }
}
