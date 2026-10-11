using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AtxAgentClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public void PortCalculation_MapsDeviceIndexCorrectly()
    {
        using var client = new AtxAgentClient(7912);

        Assert.Equal(7912, client.GetDevicePort(0));
        Assert.Equal(7913, client.GetDevicePort(1));
        Assert.Equal(7927, client.GetDevicePort(15));
        Assert.Equal("http://127.0.0.1:7912", client.GetDeviceBaseUrl(0));
        Assert.Equal("http://127.0.0.1:7913", client.GetDeviceBaseUrl(1));
    }

    [Fact]
    public async Task IsRunningAsync_WhenEndpointReturns200_ReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/version"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("0.10.0", Encoding.UTF8, "text/plain"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var isRunning = await client.IsRunningAsync(0);

        Assert.True(isRunning);
    }

    [Fact]
    public async Task DumpHierarchyAsync_WhenJsonRpcSuccess_ReturnsXml()
    {
        var mockXml = "<hierarchy rotation=\"0\"><node text=\"Facebook\" /></hierarchy>";
        var jsonResponse = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = 1,
                result = mockXml,
            }
        );

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/dump/hierarchy"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var hierarchy = await client.DumpHierarchyAsync(0);

        Assert.NotNull(hierarchy);
        Assert.Contains("Facebook", hierarchy);
    }

    [Fact]
    public async Task TapAsync_SendsShellTapCommandSuccessfully()
    {
        string? capturedCommand = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                capturedCommand = body;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var success = await client.TapAsync(0, 500, 800);

        Assert.True(success);
        Assert.NotNull(capturedCommand);
        Assert.Contains("input+tap+500+800", capturedCommand);
    }

    [Fact]
    public async Task TapAsync_ObserverExceptionsDoNotChangeResultOrSuppressOtherObservers()
    {
        var tapCommands = 0;
        var mockHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/dump/hierarchy"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<hierarchy><node text=\"Birthday\" clickable=\"true\" bounds=\"[0,0][100,100]\" /></hierarchy>"
                    ),
                };
            tapCommands++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"output\":\"\",\"exitCode\":0}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        });
        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient) { IsTapTracingEnabled = true };
        AtxTapEvent? observed = null;
        client.TapObserved += _ => throw new InvalidOperationException("Observer unavailable");
        client.TapObserved += tap => observed = tap;

        Assert.True(await client.TapAsync(0, 50, 50));
        Assert.Equal(1, tapCommands);
        Assert.NotNull(observed);
        Assert.True(observed.Success);
        Assert.Equal("Birthday", observed.Target);
    }

    [Fact]
    public async Task TakeScreenshotAsync_ReturnsRawImageBytes()
    {
        var dummyBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02 };

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/screenshot"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(dummyBytes),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var screenshot = await client.TakeScreenshotAsync(0);

        Assert.NotNull(screenshot);
        Assert.Equal(dummyBytes.Length, screenshot.Length);
        Assert.Equal(dummyBytes[0], screenshot[0]);
    }

    [Fact]
    public async Task ExecuteShellWithOutputAsync_ParsesOutputFieldFromJson()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"SM-S9180\\n\",\"exitCode\":0,\"error\":null}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var (success, output) = await client.ExecuteShellWithOutputAsync(
            0,
            "getprop ro.product.model"
        );

        Assert.True(success);
        Assert.Equal("SM-S9180\n", output);
    }

    [Fact]
    public async Task AppLifecycle_StartStopAndDeepLink_SendCorrectShellCommands()
    {
        string? lastCommand = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        await client.StopAppAsync(0, "com.facebook.katana");
        Assert.Contains("am+force-stop+com.facebook.katana", lastCommand);

        await client.OpenDeepLinkAsync(0, "fb://feed", "com.facebook.katana");
        Assert.Contains("am+start+-a+android.intent.action.VIEW+-d", lastCommand);

        await client.ClearAppCacheAsync(0, "com.facebook.katana");
        Assert.Contains("rm+-rf", lastCommand);
    }

    [Fact]
    public async Task UploadFileAsync_SendsMultipartDataToEndpoint()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "sample video test");

        string? capturedPath = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/upload/"))
            {
                capturedPath = req.RequestUri.AbsolutePath;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"isDir\":false,\"mode\":\"0660\",\"target\":\"/sdcard/test.mp4\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        try
        {
            var success = await client.UploadFileAsync(0, tempFile, "/sdcard/test.mp4");
            Assert.True(success);
            Assert.NotNull(capturedPath);
            Assert.Contains("/upload/sdcard/test.mp4", capturedPath);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadFile_ReturnsBytesAndStringContent()
    {
        var sampleBytes = Encoding.UTF8.GetBytes("sample remote text content");

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/raw/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(sampleBytes),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var bytes = await client.DownloadFileBytesAsync(0, "/sdcard/file.txt");
        var text = await client.DownloadFileTextAsync(0, "/sdcard/file.txt");

        Assert.NotNull(bytes);
        Assert.Equal("sample remote text content", text);
    }

    [Fact]
    public async Task GetDeviceInfoAsync_ParsesPropertiesCorrectly()
    {
        var json =
            @"{
            ""serial"": ""emulator-5554"",
            ""brand"": ""Samsung"",
            ""model"": ""SM-S9180"",
            ""version"": ""9"",
            ""sdk"": 28,
            ""agentVersion"": ""0.10.1"",
            ""display"": { ""width"": 720, ""height"": 1280 },
            ""battery"": { ""level"": 98 },
            ""memory"": { ""around"": ""3 GB"" }
        }";

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var info = await client.GetDeviceInfoAsync(0);

        Assert.NotNull(info);
        Assert.Equal("emulator-5554", info.Serial);
        Assert.Equal("SM-S9180", info.Model);
        Assert.Equal(28, info.Sdk);
        Assert.Equal(720, info.DisplayWidth);
        Assert.Equal(1280, info.DisplayHeight);
        Assert.Equal(98, info.BatteryLevel);
        Assert.Equal("3 GB", info.MemoryAround);
    }

    [Fact]
    public async Task IsAppRunningAsync_WhenPidofReturns200_ReturnsTrue_When410_ReturnsFalse()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/pidof/com.facebook.katana"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("12345", Encoding.UTF8, "text/plain"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.Gone);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var isFbRunning = await client.IsAppRunningAsync(0, "com.facebook.katana");
        var isOtherRunning = await client.IsAppRunningAsync(0, "com.other.app");

        Assert.True(isFbRunning);
        Assert.False(isOtherRunning);
    }

    [Fact]
    public async Task FindElementAndClick_ParsesXmlAndTapsCoordinates()
    {
        var xml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node resource-id=""com.facebook.katana:id/login_button"" text=""Log In"" content-desc=""Log in to account"" bounds=""[100,500][300,600]"" />
            </node>
        </hierarchy>";

        string? capturedCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/dump/hierarchy"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/shell"))
            {
                capturedCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        // Center of [100,500][300,600] is (200, 550)
        var (found, x, y) = await client.FindElementBoundsAsync(0, text: "Log In");
        Assert.True(found);
        Assert.Equal(200, x);
        Assert.Equal(550, y);

        var clicked = await client.ClickElementAsync(0, resourceId: "login_button");
        Assert.True(clicked);
        Assert.NotNull(capturedCommand);
        Assert.Contains("input+tap+200+550", capturedCommand);
    }

    [Fact]
    public async Task NavigationKeys_SendCorrectKeyCodes()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        await client.PressHomeAsync(0);
        Assert.Contains("input+keyevent+3", lastCommand);

        await client.PressBackAsync(0);
        Assert.Contains("input+keyevent+4", lastCommand);

        await client.PressRecentAppsAsync(0);
        Assert.Contains("input+keyevent+187", lastCommand);

        await client.InputTextAsync(0, "hello world");
        Assert.Contains("input+text+hello%25sworld", lastCommand);
    }

    [Fact]
    public async Task ShareImageAsync_SendsCorrectSendIntentCommand()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"Starting: Intent { act=android.intent.action.SEND }\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var result = await client.ShareImageAsync(
            0,
            "/sdcard/DCIM/Camera/photo.jpg",
            "com.facebook.katana"
        );
        Assert.True(result);
        Assert.NotNull(lastCommand);
        Assert.Contains("android.intent.action.SEND", lastCommand);
        Assert.Contains("image%2Fjpeg", lastCommand);
        Assert.Contains("photo.jpg", lastCommand);
    }

    [Fact]
    public void ParseBloksHierarchy_ExtractsElementsAndAttributes_Correctly()
    {
        var xml = """
            <hierarchy rotation="0">
              <node class="android.widget.FrameLayout" bounds="[0,0][720,1280]">
                <node class="com.facebook.bloks.hosted.activity.BloksNativeShellActivity" bounds="[0,0][720,1280]">
                  <node class="android.widget.TextView" text="Accounts Center" bounds="[30,50][300,100]" />
                  <node class="android.view.ViewGroup" bounds="[30,120][690,220]" clickable="true">
                    <node class="android.widget.TextView" text="Profiles" bounds="[40,130][150,160]" />
                    <node class="android.widget.TextView" text="Alice Johnson" bounds="[40,170][250,200]" />
                  </node>
                  <node class="android.view.ViewGroup" bounds="[30,240][690,340]" clickable="true">
                    <node class="android.widget.TextView" text="Contact info" bounds="[40,250][180,280]" />
                    <node class="android.widget.TextView" text="alice.j@example.com" bounds="[40,290][350,320]" />
                  </node>
                  <node class="android.view.ViewGroup" bounds="[30,360][690,460]" clickable="true">
                    <node class="android.widget.TextView" text="Birthday" bounds="[40,370][150,400]" />
                    <node class="android.widget.TextView" text="August 15, 1992" bounds="[40,410][280,440]" />
                  </node>
                </node>
              </node>
            </hierarchy>
            """;

        var screen = AtxAgentClient.ParseBloksHierarchy(
            xml,
            "com.facebook.katana/com.facebook.bloks.hosted.activity.BloksNativeShellActivity"
        );

        Assert.True(screen.IsBloksActive);
        Assert.Contains("Accounts Center", screen.VisibleTexts);
        Assert.Contains("Alice Johnson", screen.VisibleTexts);
        Assert.Contains("alice.j@example.com", screen.VisibleTexts);
        Assert.Contains("August 15, 1992", screen.VisibleTexts);

        var profilesElem = screen.FindElement("Profiles");
        Assert.NotNull(profilesElem);
        Assert.Equal(95, profilesElem.CenterX); // (40 + 150) / 2
        Assert.Equal(145, profilesElem.CenterY); // (130 + 160) / 2

        var emailVal = screen.ExtractFieldValue("Contact info");
        Assert.Equal("alice.j@example.com", emailVal);

        var bdayVal = screen.ExtractFieldValue("Birthday");
        Assert.Equal("August 15, 1992", bdayVal);
    }

    [Fact]
    public async Task GetCurrentFocusedActivityAsync_ParsesDumpsysWindowOutput()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"  mCurrentFocus=Window{e889a74 u0 com.facebook.katana/com.facebook.bloks.hosted.activity.BloksNativeShellActivity}\\n\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var focus = await client.GetCurrentFocusedActivityAsync(0);
        Assert.Equal(
            "com.facebook.katana/com.facebook.bloks.hosted.activity.BloksNativeShellActivity",
            focus
        );

        var isBloks = await client.IsBloksNativeShellActiveAsync(0);
        Assert.True(isBloks);
    }

    [Fact]
    public async Task OpenBloksAccountsCenterAsync_SendsAccountsCenterDeepLink()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var ok = await client.OpenBloksAccountsCenterAsync(0);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        Assert.Contains("am+start", lastCommand);
        Assert.Contains("fb%3A%2F%2Faccounts_center", lastCommand);
        Assert.Contains("com.facebook.katana", lastCommand);
    }

    [Fact]
    public async Task ClickBloksElementAsync_FindsBoundsAndSendsTap()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/dump/hierarchy"))
            {
                var xml = """
                <hierarchy rotation="0">
                  <node class="android.widget.FrameLayout" bounds="[0,0][720,1280]">
                    <node class="android.widget.TextView" text="Personal details" bounds="[100,200][300,300]" />
                  </node>
                </hierarchy>
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "text/xml"),
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var ok = await client.ClickBloksElementAsync(0, "Personal details");
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        // Center of [100,200][300,300] is (200, 250)
        Assert.Contains("input+tap+200+250", lastCommand);
    }

    [Fact]
    public async Task ExtractBloksAccountInfoAsync_ParsesAllVisibleAccountFields()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/dump/hierarchy"))
            {
                var xml = """
                <hierarchy rotation="0">
                  <node class="android.widget.FrameLayout" bounds="[0,0][720,1280]">
                    <node class="com.facebook.bloks.hosted.activity.BloksNativeShellActivity" bounds="[0,0][720,1280]">
                      <node class="android.widget.TextView" text="Accounts Center" bounds="[30,50][300,100]" />
                      <node class="android.widget.TextView" text="Profiles" bounds="[40,130][150,160]" />
                      <node class="android.widget.TextView" text="Sarah Connor" bounds="[40,170][250,200]" />
                      <node class="android.widget.TextView" text="Contact info" bounds="[40,250][180,280]" />
                      <node class="android.widget.TextView" text="sarah.c@sky.net" bounds="[40,290][350,320]" />
                      <node class="android.widget.TextView" text="Birthday" bounds="[40,370][150,400]" />
                      <node class="android.widget.TextView" text="November 20, 1984" bounds="[40,410][280,440]" />
                      <node class="android.widget.TextView" text="100084729184712" bounds="[40,490][300,520]" />
                    </node>
                  </node>
                </hierarchy>
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "text/xml"),
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"mCurrentFocus=Window{123 u0 com.facebook.katana/com.facebook.bloks.hosted.activity.BloksNativeShellActivity}\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var info = await client.ExtractBloksAccountInfoAsync(0);

        Assert.True(info.AccountsCenterLoaded);
        Assert.Equal("sarah.c@sky.net", info.Email);
        Assert.Equal("November 20, 1984", info.Birthday);
        Assert.Equal("Sarah Connor", info.Name);
        Assert.Equal("100084729184712", info.UserId);
    }

    [Fact]
    public async Task InputTextAdbKeyboardAsync_SendsBase64Broadcast()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var text = "សួស្តី Khmer & Emojis 🚀";
        var ok = await client.InputTextAdbKeyboardAsync(0, text);
        Assert.True(ok);
        Assert.NotNull(lastCommand);

        var expectedB64 = Uri.EscapeDataString(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
        );
        Assert.Contains("ADB_INPUT_B64", lastCommand);
        Assert.Contains(expectedB64, lastCommand);
    }

    [Fact]
    public async Task ClearTextAdbKeyboardAsync_SendsClearTextBroadcast()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var ok = await client.ClearTextAdbKeyboardAsync(0);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        Assert.Contains("ADB_CLEAR_TEXT", lastCommand);
    }

    [Fact]
    public async Task SendAdbKeyboardEditorActionAsync_SendsEditorCode()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var ok = await client.SendAdbKeyboardEditorActionAsync(0, AdbEditorAction.Search);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        Assert.Contains("ADB_EDITOR_CODE", lastCommand);
        Assert.Contains("code+3", lastCommand);
    }

    [Fact]
    public async Task InputTextAsync_WithUnicodeText_AutomaticallyRoutesToAdbKeyboard()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"output\":\"\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        using var client = new AtxAgentClient(7912, httpClient);

        var unicodeText = "Viral Reel 🎬🔥";
        var ok = await client.InputTextAsync(0, unicodeText);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        // Should have automatically routed to ADB_INPUT_B64 instead of raw `input text`
        Assert.Contains("ADB_INPUT_B64", lastCommand);
    }
}
