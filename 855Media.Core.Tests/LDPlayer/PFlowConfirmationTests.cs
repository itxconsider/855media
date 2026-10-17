using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowConfirmationTests
{
    [Theory]
    [InlineData("Your reel has been published!", "Reel", true)]
    [InlineData(" Your reel\nwas successfully published. ", "Reel", true)]
    [InlineData("Your reel is now live", "Reel", true)]
    [InlineData("Reel shared successfully", "Video", true)]
    [InlineData("Your post was published", "Picture", true)]
    [InlineData("Your story has been shared", "Story", true)]
    [InlineData("Facebook upload complete", "Reel", false)]
    [InlineData("Your reel is uploading", "Reel", false)]
    [InlineData("Your reel could not be published", "Reel", false)]
    [InlineData("Your reel has been published to another Page", "Reel", false)]
    [InlineData("Reel published", "Story", false)]
    [InlineData("Your reel was published", "Picture", false)]
    public void DirectSuccessRequiresExplicitPublicationAndCorrectType(
        string message,
        string type,
        bool expected
    ) => Assert.Equal(expected, PFlowConfirmation.IsPublicationSuccess(message, type));

    [Theory]
    [InlineData("text", "", "true", "", true)]
    [InlineData("content-desc", "", "true", "", true)]
    [InlineData("text", "android.widget.EditText", "true", "", false)]
    [InlineData("text", "", "false", "", false)]
    [InlineData("text", "", "true", "other.app", false)]
    public void DirectMessagesExcludeInputsHiddenAndForeignNodes(
        string attribute,
        string className,
        string visible,
        string package,
        bool expected
    )
    {
        var node = new System.Xml.Linq.XElement(
            "node",
            new System.Xml.Linq.XAttribute(attribute, "Your reel has been published!"),
            new System.Xml.Linq.XAttribute("class", className),
            new System.Xml.Linq.XAttribute("visible-to-user", visible),
            new System.Xml.Linq.XAttribute("package", package)
        );
        var xml = new System.Xml.Linq.XElement("hierarchy", node).ToString();
        Assert.Equal(
            expected,
            PFlowAutoPostService.NewPublicationEvidence(
                xml,
                new System.Collections.Generic.HashSet<string>(),
                "Reel"
            ) != null
        );
        Assert.Null(
            PFlowAutoPostService.NewPublicationEvidence(
                xml,
                new System.Collections.Generic.HashSet<string> { "Your reel has been\npublished." },
                "Reel"
            )
        );
    }

    [Fact]
    public void DirectWaitIsBoundedForNormalAndShortConfirmationWindows()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(8),
            PFlowConfirmation.DirectConfirmationGrace(TimeSpan.FromMinutes(2))
        );
        Assert.Equal(
            TimeSpan.FromMilliseconds(500),
            PFlowConfirmation.DirectConfirmationGrace(TimeSpan.FromSeconds(2))
        );
    }

    public static string Dump(
        string title = "Facebook upload complete",
        int version = 20,
        string flags = "18",
        string package = "com.facebook.katana",
        string channel = "media_upload_notification_channel_id"
    ) =>
        $"  Notification List:\n    NotificationRecord(pkg={package} Notification(channel={channel} flags=0x{flags}))\n      key=upload-key\n      android.title=String ({title})\n      mCreationTimeMs=10\n      mUpdateTimeMs={version}\n  mArchive=Archive (0 notifications)\n";

    [Fact]
    public void ParsesActiveCompletionAndIgnoresArchive()
    {
        var parsed = PFlowConfirmation.ReadUploadNotifications(Dump());
        Assert.Single(parsed);
        Assert.True(parsed[0].Complete);
        Assert.Equal(20, parsed[0].Version);
        Assert.Empty(
            PFlowConfirmation.ReadUploadNotifications(
                "  Notification List:\n  mArchive=Archive (1 notifications)\n" + Dump()
            )
        );
    }

    [Theory]
    [InlineData("other.package", "media_upload_notification_channel_id")]
    [InlineData("com.facebook.katana", "other_channel")]
    public void RejectsWrongSource(string package, string channel) =>
        Assert.Empty(
            PFlowConfirmation.ReadUploadNotifications(Dump(package: package, channel: channel))
        );

    [Fact]
    public void RequiresFreshUnambiguousCompletionAndClosedComposer()
    {
        var complete = PFlowConfirmation.ReadUploadNotifications(Dump());
        Assert.NotNull(PFlowConfirmation.NewUploadCompletion([], complete, true));
        Assert.Null(PFlowConfirmation.NewUploadCompletion([], complete, false));
        Assert.Null(PFlowConfirmation.NewUploadCompletion(complete, complete, true));
        Assert.NotNull(
            PFlowConfirmation.NewUploadCompletion(
                PFlowConfirmation.ReadUploadNotifications(Dump(version: 19)),
                complete,
                true
            )
        );
        Assert.Null(
            PFlowConfirmation.NewUploadCompletion(
                PFlowConfirmation.ReadUploadNotifications(Dump(title: "Uploading", flags: "6a")),
                complete,
                true
            )
        );
        Assert.Null(
            PFlowConfirmation.NewUploadCompletion(
                [],
                PFlowConfirmation.ReadUploadNotifications(Dump(flags: "6a")),
                true
            )
        );
        Assert.Null(
            PFlowConfirmation.NewUploadCompletion(
                [],
                PFlowConfirmation.ReadUploadNotifications(Dump(title: "Uploading")),
                true
            )
        );
        Assert.Null(
            PFlowConfirmation.NewUploadCompletion(
                [],
                [complete[0], complete[0] with { Key = "second" }],
                true
            )
        );
    }

    private sealed class ToastHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("getLastToast", body.RootElement.GetProperty("method").GetString());
            if (Calls == 1)
            {
                Assert.Equal(0, body.RootElement.GetProperty("params").GetArrayLength());
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"error\":{\"code\":-32602}}"),
                };
            }
            Assert.Equal(10000, body.RootElement.GetProperty("params")[0].GetInt32());
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"Reel published\"}"),
            };
        }
    }

    [Fact]
    public async Task ReadsLegacyToastSignature()
    {
        var handler = new ToastHandler();
        using var http = new HttpClient(handler);
        using var client = new AtxAgentClient(httpClient: http);
        var result = await client.ReadLastToastAsync(1);
        Assert.True(result.Supported);
        Assert.Equal("Reel published", result.Message);
        Assert.Equal(2, handler.Calls);
    }
}
