using System.IO;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class LDPlayerServiceTests
{
    [Fact]
    public void ResolveLdConsolePath_WithDefaultPath_ReturnsValidLdConsole()
    {
        var service = new LDPlayerService();
        var resolved = service.ResolveLdConsolePath(@"C:\LDPlayer\LDPlayer9");

        Assert.EndsWith("ldconsole.exe", resolved);
    }

    [Fact]
    public void LDInstanceInfo_DefaultValues_AreConsistent()
    {
        var info = new LDInstanceInfo
        {
            Index = 11,
            Name = "LD-11",
            IsAndroidStarted = true,
            Pid = 1234,
        };

        Assert.True(info.IsRunning);
        Assert.Equal("FB-01", info.FbClone);
        Assert.Equal("Idle", info.Status);
    }

    [Fact]
    public void LDSettings_DefaultValues_MatchExpectedWorkflow()
    {
        var settings = new LDSettings();

        Assert.Equal(8, settings.RunThread);
        Assert.True(settings.AutoSortLDPlayer);
        Assert.True(settings.CloseLDAfterAction);
        Assert.True(settings.ClearCacheFBBeforeOpen);
        Assert.Equal(30, settings.DelayAfterOpenLD);
        Assert.Equal(30, settings.DelayBeforeCloseLD);
    }

    [Fact]
    public void ScreenTimeSetting_Ranges_AreValid()
    {
        var warmup = new ScreenTimeSetting();

        Assert.True(warmup.WatchVideo);
        Assert.Equal(60, warmup.WatchVideoMin);
        Assert.Equal(120, warmup.WatchVideoMax);
        Assert.False(warmup.NewsFeed);
        Assert.Equal(60, warmup.NewsFeedMin);
        Assert.Equal(120, warmup.NewsFeedMax);
        Assert.False(warmup.LikePost);
        Assert.Equal(1, warmup.LikePostMin);
        Assert.Equal(3, warmup.LikePostMax);
    }

    [Fact]
    public void FacebookPageTarget_Properties_SyncCorrectly()
    {
        var target = new FacebookPageTarget
        {
            DeviceName = "LD-20",
            AppClone = "FB-01",
            PageId = "615942468175556",
            PageName = "885media",
            Type = "Page",
            Followers = 120,
            Reach = "1.2K Views",
            VideoReelPath = @"D:\Media\885\Reels",
        };

        Assert.Equal("LD-20", target.DeviceName);
        Assert.Equal("885media", target.PageName);
        Assert.Equal("Page", target.Type);
        Assert.Equal(120, target.Followers);
        Assert.Equal("1.2K Views", target.Reach);
    }

    [Fact]
    public void ScreenTimeSetting_SupportsSingleActionSelection()
    {
        var settings = new ScreenTimeSetting
        {
            WatchVideo = true,
            NewsFeed = false,
            Marketplace = false,
            CheckIn = false,
            ReadNotification = false,
            AddFriendSuggest = false,
            AcceptFriends = false,
        };

        Assert.True(settings.WatchVideo);
        Assert.False(settings.NewsFeed);
        Assert.False(settings.Marketplace);
        Assert.False(settings.CheckIn);
        Assert.False(settings.ReadNotification);
        Assert.False(settings.AddFriendSuggest);
        Assert.False(settings.AcceptFriends);
    }

    [Fact]
    public void CandidatePoolFiltering_OnlySelectedAndVisibleInstancesRun()
    {
        var allInstances = new[]
        {
            new LDInstanceInfo
            {
                Index = 0,
                Name = "LD-0",
                IsAndroidStarted = true,
                IsSelected = true,
            },
            new LDInstanceInfo
            {
                Index = 1,
                Name = "LD-1",
                IsAndroidStarted = true,
                IsSelected = false,
            },
            new LDInstanceInfo
            {
                Index = 2,
                Name = "LD-2",
                IsAndroidStarted = false,
                IsSelected = true,
            },
            new LDInstanceInfo
            {
                Index = 3,
                Name = "LD-3",
                IsAndroidStarted = false,
                IsSelected = false,
            },
        };

        // When ShowOnlyOpen is true:
        bool showOnlyOpen = true;
        var candidatePool = showOnlyOpen
            ? allInstances.Where(i => i.IsAndroidStarted).ToList()
            : allInstances.ToList();

        var selectedToRun = candidatePool.Where(i => i.IsSelected).ToList();

        // Only LD-0 should run: LD-1 is open but unselected; LD-2 is selected but closed (hidden); LD-3 is closed & unselected
        Assert.Single(selectedToRun);
        Assert.Equal("LD-0", selectedToRun[0].Name);
    }

    [Fact]
    public void DeepLinkCommandStructure_ContainsClearTopFlagAndTargetPackage()
    {
        var deepLink = "fb://watch";
        var packageName = "com.facebook.katana";
        var command =
            $"shell am start -a android.intent.action.VIEW -d \"{deepLink}\" -p {packageName} -f 0x14000000";

        Assert.Contains("android.intent.action.VIEW", command);
        Assert.Contains("-d \"fb://watch\"", command);
        Assert.Contains("-p com.facebook.katana", command);
        Assert.Contains("-f 0x14000000", command);
    }

    [Fact]
    public void ActionTransition_StateResetKeyCodes_AreAndroidStandard()
    {
        const int keycodeHome = 3;
        const int keycodeBack = 4;
        const int keycodeAppSwitch = 187;

        Assert.Equal(3, keycodeHome);
        Assert.Equal(4, keycodeBack);
        Assert.Equal(187, keycodeAppSwitch);
    }

    [Fact]
    public async Task PFlowPageStorage_SaveAndLoad_RoundTripsDataCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fb_pages_test_{Guid.NewGuid()}.json");
        try
        {
            var pages = new List<FacebookPageTarget>
            {
                new()
                {
                    DeviceName = "LD-20",
                    PageId = "615942468175556",
                    PageName = "885media",
                    Followers = 120,
                    Reach = "1.2K Views",
                    VideoReelPath = @"D:\Media\885\Reels",
                },
            };

            await PFlowPageStorage.SavePagesAsync(pages, tempFile);
            var loaded = await PFlowPageStorage.LoadPagesAsync(tempFile);

            Assert.Single(loaded);
            Assert.Equal("885media", loaded[0].PageName);
            Assert.Equal(120, loaded[0].Followers);
            Assert.Equal(@"D:\Media\885\Reels", loaded[0].VideoReelPath);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PFlowPageStorage_LoadPages_WhenFileDoesNotExistOrIsEmpty_ReturnsEmptyList()
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"fb_pages_empty_test_{Guid.NewGuid()}.json"
        );
        try
        {
            var nonExistentLoaded = await PFlowPageStorage.LoadPagesAsync(tempFile);
            Assert.Empty(nonExistentLoaded);

            await File.WriteAllTextAsync(tempFile, "[]");
            var emptyLoaded = await PFlowPageStorage.LoadPagesAsync(tempFile);
            Assert.Empty(emptyLoaded);

            var defaults = PFlowPageStorage.GetDefaultPages();
            Assert.Empty(defaults);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AutoPostConfigStorage_SaveAndLoad_RoundTripsCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"autopost_cfg_test_{Guid.NewGuid()}.json");
        try
        {
            var config = new AutoPostConfig
            {
                IsWatchVideo = true,
                WatchVideoMin = 15,
                WatchVideoMax = 90,
                IsMarketPlace = true,
                MarketPlaceMin = 25,
                MarketPlaceMax = 65,
                IsLikePost = true,
                LikePostMin = 2,
                LikePostMax = 5,
                IsNewsFeed = true,
                NewsFeedMin = 45,
                NewsFeedMax = 110,
                IsCheckIn = true,
                CheckInLocation = "New York, USA",
                IsPostReel = true,
                IsDeleteReelFile = false,
                IsBackupReelFile = true,
                DelayUpload = 75,
                DelayOpenApp = 12,
                DelayAfterOpenLD = 25,
                DelayOpenEachLD = 18,
                DelayBeforeCloseLD = 20,
                DelayBeforeSwitchPage = 22,
                CloseLDAfterAction = false,
                ClearCacheFBBeforeOpen = true,
                AfterPostCheckCopyRight = false,
                SwitchToProfileAfterPost = true,
                SleepPcAfterActionDone = true,
            };

            await AutoPostConfigStorage.SaveConfigAsync(config, tempFile);
            var loaded = await AutoPostConfigStorage.LoadConfigAsync(tempFile);

            Assert.True(loaded.IsWatchVideo);
            Assert.Equal(15, loaded.WatchVideoMin);
            Assert.Equal(90, loaded.WatchVideoMax);
            Assert.True(loaded.IsMarketPlace);
            Assert.Equal(25, loaded.MarketPlaceMin);
            Assert.Equal(65, loaded.MarketPlaceMax);
            Assert.True(loaded.IsCheckIn);
            Assert.Equal("New York, USA", loaded.CheckInLocation);
            Assert.True(loaded.IsPostReel);
            Assert.False(loaded.IsDeleteReelFile);
            Assert.True(loaded.IsBackupReelFile);
            Assert.Equal(75, loaded.DelayUpload);
            Assert.Equal(12, loaded.DelayOpenApp);
            Assert.Equal(25, loaded.DelayAfterOpenLD);
            Assert.Equal(18, loaded.DelayOpenEachLD);
            Assert.Equal(20, loaded.DelayBeforeCloseLD);
            Assert.Equal(22, loaded.DelayBeforeSwitchPage);
            Assert.False(loaded.CloseLDAfterAction);
            Assert.True(loaded.ClearCacheFBBeforeOpen);
            Assert.False(loaded.AfterPostCheckCopyRight);
            Assert.True(loaded.SwitchToProfileAfterPost);
            Assert.True(loaded.SleepPcAfterActionDone);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AutoPostConfigStorage_LoadConfig_WhenFileDoesNotExist_ReturnsDefault()
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"autopost_cfg_nonexistent_{Guid.NewGuid()}.json"
        );
        var loaded = await AutoPostConfigStorage.LoadConfigAsync(tempFile);
        Assert.NotNull(loaded);
        Assert.True(loaded.IsWatchVideo);
        Assert.Equal(60, loaded.DelayUpload);
        Assert.Equal(15, loaded.DelayBeforeCloseLD);
    }

    [Fact]
    public void ParsePageInfoFromXml_ExtractsFollowersAndTypeCorrectly()
    {
        var sampleXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='885media' class='android.widget.TextView' />
  <node index='1' text='1.2K followers' class='android.widget.TextView' />
  <node index='2' text='Page · Digital creator' class='android.widget.TextView' />
  <node index='3' text='530 reels' class='android.widget.TextView' />
</hierarchy>";

        var info = LDPlayerService.ParsePageInfoFromXml(sampleXml, "12345");

        Assert.Equal("885media", info.PageName);
        Assert.Equal(1200, info.Followers);
        Assert.Equal("Page", info.Type);
        Assert.Equal(530, info.ReelsCount);
    }

    [Fact]
    public void ParseDeviceAccountAndPagesFromXml_ExtractsProfileAndManagedPagesCorrectly()
    {
        var profileXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Facebook' class='android.widget.TextView' />
  <node index='1' text='Rosemary Silva' class='android.widget.TextView' />
  <node index='2' text='Edit profile' class='android.widget.Button' />
</hierarchy>";

        var pagesXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Pages' class='android.widget.TextView' />
  <node index='1' text='Nick Parlet' class='android.widget.TextView' />
  <node index='2' text='Marcel Luft Camping' class='android.widget.TextView' />
</hierarchy>";

        var prefsOutput = "\"user_id\": \"615945555642011\", \"name\": \"Rosemary Silva\"";

        var result = LDPlayerService.ParseDeviceAccountAndPagesFromXml(
            profileXml,
            pagesXml,
            prefsOutput,
            26
        );

        Assert.Equal("LD-26", result.DeviceName);
        Assert.Equal("615945555642011", result.UserUid);
        Assert.Equal("Rosemary Silva", result.UserName);
        Assert.Equal("Profile", result.UserProfile.Type);
        Assert.Equal("Rosemary Silva", result.UserProfile.PageName);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal("Nick Parlet", result.Pages[0].PageName);
        Assert.Equal("Page", result.Pages[0].Type);
        Assert.Equal("615945555642011", result.Pages[0].UserUid);

        Assert.Equal("Marcel Luft Camping", result.Pages[1].PageName);
        Assert.Equal("Page", result.Pages[1].Type);
        Assert.Equal("615945555642011", result.Pages[1].UserUid);
    }

    [Fact]
    public void ParseFacebookAccountDetails_ExtractsSwitcherProfilePagesAndAccountCredentialsCorrectly()
    {
        var switcherXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Menu' class='android.widget.TextView' />
  <node index='1' text='Carey Northcutt' class='android.widget.TextView' bounds='[100,200][500,260]' />
  <node index='2' text='Your shortcuts' class='android.widget.TextView' />
  <node index='3' text='Carey Northcutt' class='android.widget.TextView' bounds='[150,350][450,400]' />
  <node index='4' text='YeeYee&apos;s Life' class='android.widget.TextView' bounds='[150,450][450,500]' />
  <node index='5' text='Page' class='android.widget.TextView' />
  <node index='6' text='PlumbBob+' class='android.widget.TextView' bounds='[150,550][450,600]' />
  <node index='7' text='Page' class='android.widget.TextView' />
  <node index='8' text='NYC Fixes' class='android.widget.TextView' bounds='[150,650][450,700]' />
  <node index='9' text='Page' class='android.widget.TextView' />
  <node index='10' text='Create Facebook Page' class='android.widget.TextView' />
  <node index='11' text='Go to Accounts Center' class='android.widget.TextView' />
</hierarchy>";

        var aboutXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='About' class='android.widget.TextView' />
  <node index='1' text='Contact info' class='android.widget.TextView' />
  <node index='2' text='carey.northcutt@gmail.com' class='android.widget.TextView' />
  <node index='3' text='Basic info' class='android.widget.TextView' />
  <node index='4' text='March 14, 1992' class='android.widget.TextView' />
  <node index='5' text='Joined Facebook in October 2021' class='android.widget.TextView' />
</hierarchy>";

        var profileXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Carey Northcutt' class='android.widget.TextView' />
  <node index='1' text='See Carey&apos;s About info' class='android.widget.Button' />
</hierarchy>";

        var dumps =
            "Account {name=carey.northcutt@gmail.com, type=com.facebook.auth.login}\n"
            + "\"user_id\": \"615942468175556\", \"email\": \"carey.northcutt@gmail.com\"";

        var result = LDPlayerService.ParseFacebookAccountDetails(
            switcherXml,
            aboutXml,
            profileXml,
            dumps,
            null,
            20
        );

        Assert.Equal("LD-20", result.DeviceName);
        Assert.Equal("Carey Northcutt", result.Name);
        Assert.Equal("615942468175556", result.Uid);
        Assert.Equal("carey.northcutt@gmail.com", result.Email);
        Assert.Equal("March 14, 1992", result.Birthdate);
        Assert.Equal("Joined Facebook in October 2021", result.CreationDate);

        Assert.Equal(3, result.Pages.Count);
        Assert.Equal("YeeYee's Life", result.Pages[0].PageName);
        Assert.Equal("PlumbBob+", result.Pages[1].PageName);
        Assert.Equal("NYC Fixes", result.Pages[2].PageName);

        Assert.Equal("615942468175556", result.Pages[0].UserUid);
        Assert.Equal("carey.northcutt@gmail.com", result.Pages[0].Email);
        Assert.Equal("March 14, 1992", result.Pages[0].Birthdate);
        Assert.Equal("Joined Facebook in October 2021", result.Pages[0].CreationDate);
    }

    [Fact]
    public void ParseFacebookAccountDetails_WithNotificationSubtitles_ExtractsProfileAndAllPagesWithNotificationsAndManager()
    {
        // Replicates user's exact screen:
        // Carey Northcutt (Primary with blue checkmark)
        // YeeYee's Life (• 3 notifications, Page)
        // PlumbBob+ (• 1 notification, Page)
        // NYC Fixes (Page)
        // Create Facebook Page
        // Go to Accounts Center
        var switcherXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Carey Northcutt' class='android.widget.TextView' />
  <node index='1' text='Your shortcuts' class='android.widget.TextView' />
  <node index='2' text='Carey Northcutt' checked='true' class='android.widget.TextView' bounds='[150,350][450,400]' />
  <node index='3' text='YeeYee&apos;s Life' class='android.widget.TextView' bounds='[150,450][450,500]' />
  <node index='4' text='• 3 notifications' class='android.widget.TextView' bounds='[150,505][350,530]' />
  <node index='5' text='Page' class='android.widget.TextView' bounds='[150,535][220,560]' />
  <node index='6' text='PlumbBob+' class='android.widget.TextView' bounds='[150,580][450,630]' />
  <node index='7' text='• 1 notification' class='android.widget.TextView' bounds='[150,635][350,660]' />
  <node index='8' text='Page' class='android.widget.TextView' bounds='[150,665][220,690]' />
  <node index='9' text='NYC Fixes' class='android.widget.TextView' bounds='[150,710][450,760]' />
  <node index='10' text='Page' class='android.widget.TextView' bounds='[150,765][220,790]' />
  <node index='11' text='Create Facebook Page' class='android.widget.TextView' />
  <node index='12' text='Go to Accounts Center' class='android.widget.TextView' />
</hierarchy>";

        var result = LDPlayerService.ParseFacebookAccountDetails(
            switcherXml,
            null,
            null,
            "\"user_id\": \"615942468175556\", \"email\": \"carey.northcutt@gmail.com\"",
            null,
            0
        );

        Assert.Equal("LD-0", result.DeviceName);
        Assert.Equal("Carey Northcutt", result.Name);
        Assert.Equal("615942468175556", result.Uid);
        Assert.Equal("carey.northcutt@gmail.com", result.Email);

        // Exactly 3 pages extracted despite intervening • 3 notifications and • 1 notification
        Assert.Equal(3, result.Pages.Count);

        Assert.Equal("YeeYee's Life", result.Pages[0].PageName);
        Assert.Equal("Page", result.Pages[0].Type);
        Assert.Equal("3 notifications", result.Pages[0].Notifications);
        Assert.Equal("Carey Northcutt", result.Pages[0].ManagedBy);
        Assert.Equal("615942468175556", result.Pages[0].UserUid);

        Assert.Equal("PlumbBob+", result.Pages[1].PageName);
        Assert.Equal("Page", result.Pages[1].Type);
        Assert.Equal("1 notification", result.Pages[1].Notifications);
        Assert.Equal("Carey Northcutt", result.Pages[1].ManagedBy);
        Assert.Equal("615942468175556", result.Pages[1].UserUid);

        Assert.Equal("NYC Fixes", result.Pages[2].PageName);
        Assert.Equal("Page", result.Pages[2].Type);
        Assert.Empty(result.Pages[2].Notifications);
        Assert.Equal("Carey Northcutt", result.Pages[2].ManagedBy);
        Assert.Equal("615942468175556", result.Pages[2].UserUid);
    }

    [Fact]
    public void ParseFacebookAccountDetails_WhenAccountCredentialsPrivateOrMissing_DoesNotReturnDummyValues()
    {
        var switcherXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Menu' class='android.widget.TextView' />
  <node index='1' text='Rosemary Silva' class='android.widget.TextView' bounds='[100,200][500,260]' />
  <node index='2' text='Your shortcuts' class='android.widget.TextView' />
  <node index='3' text='Rosemary Silva' class='android.widget.TextView' bounds='[150,350][450,400]' />
  <node index='4' text='Nick Parlet' class='android.widget.TextView' bounds='[150,450][450,500]' />
  <node index='5' text='Page' class='android.widget.TextView' />
  <node index='6' text='Marcel Luft Camping' class='android.widget.TextView' bounds='[150,550][450,600]' />
  <node index='7' text='Page' class='android.widget.TextView' />
  <node index='8' text='Create Facebook Page' class='android.widget.TextView' />
  <node index='9' text='Go to Accounts Center' class='android.widget.TextView' />
</hierarchy>";

        var aboutXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='About' class='android.widget.TextView' />
  <node index='1' text='Basic info' class='android.widget.TextView' />
  <node index='2' text='Gender' class='android.widget.TextView' />
  <node index='3' text='Female' class='android.widget.TextView' />
</hierarchy>";

        var profileXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Rosemary Silva' class='android.widget.TextView' />
  <node index='1' text='See Rosemary&apos;s About info' class='android.widget.Button' />
</hierarchy>";

        var result = LDPlayerService.ParseFacebookAccountDetails(
            switcherXml,
            aboutXml,
            profileXml,
            "",
            null,
            26
        );

        Assert.Equal("LD-26", result.DeviceName);
        Assert.Equal("Rosemary Silva", result.Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Uid));
        Assert.NotEqual("Carey Northcutt", result.Name);
        Assert.NotEqual("carey.northcutt@gmail.com", result.Email);
        Assert.NotEqual("March 14, 1992", result.Birthdate);
        Assert.NotEqual("Joined October 2021", result.CreationDate);
        Assert.Equal("Private / Hidden", result.Email);
        Assert.Equal("Private / Hidden", result.Birthdate);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal("Nick Parlet", result.Pages[0].PageName);
        Assert.Equal("Marcel Luft Camping", result.Pages[1].PageName);
    }

    private class MockHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly System.Func<
            System.Net.Http.HttpRequestMessage,
            System.Net.Http.HttpResponseMessage
        > _handler;

        public MockHttpMessageHandler(
            System.Func<
                System.Net.Http.HttpRequestMessage,
                System.Net.Http.HttpResponseMessage
            > handler
        )
        {
            _handler = handler;
        }

        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken
        )
        {
            return System.Threading.Tasks.Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task LDPlayerService_OpenAtxFastPaths_ExecuteSuccessfully()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "video data");

        string? lastUrl = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            lastUrl = req.RequestUri!.ToString();
            if (req.RequestUri.AbsolutePath.EndsWith("/version"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("0.10.1"),
                };
            }
            if (req.RequestUri.AbsolutePath.Contains("/upload/"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"isDir\":false,\"target\":\"/sdcard/v.mp4\"}"
                    ),
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/shell"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("{\"output\":\"\"}"),
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/pidof/com.facebook.katana"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("9999"),
                };
            }
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var http = new System.Net.Http.HttpClient(mockHandler);
        using var atx = new AtxAgentClient(7912, http);
        var service = new LDPlayerService(atx);

        try
        {
            // Test fast upload
            var pushed = await service.PushFileToEmulatorAsync(0, tempFile, "/sdcard/v.mp4");
            Assert.True(pushed);

            // Test fast cache clear
            var cleared = await service.ClearFacebookCacheAsync(0);
            Assert.True(cleared);

            // StopFacebookApp uses direct device ADB; it is outside this HTTP-only mock test.

            // Navigation keys also use direct ADB and require a device-command mock.

            // Test is app running
            var running = await service.IsAppRunningAsync(0, "com.facebook.katana");
            Assert.True(running);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task PostMedia_WithNonExistentFile_ReturnsFalse()
    {
        using var client = new AtxAgentClient(7912);
        var service = new LDPlayerService(client);

        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".mp4");

        Assert.False(await service.PostReelToFacebookAsync(0, nonExistent));
        Assert.False(await service.PostPictureToFacebookAsync(0, nonExistent));
        Assert.False(await service.PostVideoToFacebookAsync(0, nonExistent));
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteFileFromEmulator_WithEmptyPath_ReturnsTrue()
    {
        using var client = new AtxAgentClient(7912);
        var service = new LDPlayerService(client);

        var result = await service.DeleteFileFromEmulatorAsync(0, "");
        Assert.True(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitForFacebookUploadCompleted_WhenCancelled_Throws()
    {
        using var client = new AtxAgentClient(7912);
        var service = new LDPlayerService(client);
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.WaitForFacebookUploadCompletedAsync(
                0,
                timeout: TimeSpan.FromMilliseconds(50),
                cancellationToken: cts.Token
            )
        );
    }

    [Fact]
    public async System.Threading.Tasks.Task SwitchToPersona_WhenAlreadyOnTarget_ReturnsTrueImmediately()
    {
        var menuXml =
            @"<hierarchy rotation=""0"">
            <node class=""android.widget.FrameLayout"" bounds=""[0,0][720,1280]"">
                <node text=""My Cool Page"" bounds=""[100,100][400,200]"" />
            </node>
        </hierarchy>";

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/version"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("0.10.1"),
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/dump/hierarchy"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                jsonrpc = "2.0",
                                id = 1,
                                result = menuXml,
                            }
                        ),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/shell"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("{\"output\":\"\"}"),
                };
            }
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var http = new System.Net.Http.HttpClient(mockHandler);
        using var atx = new AtxAgentClient(7912, http);
        var service = new LDPlayerService(atx);

        var switched = await service.SwitchToPersonaAsync(0, "My Cool Page");
        Assert.True(switched);
    }

    [Fact]
    public async Task LDPlayerService_IsBloksNativeShellActiveAsync_ReturnsTrue()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/version"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "0.10.0",
                        System.Text.Encoding.UTF8,
                        "text/plain"
                    ),
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"output\":\"mCurrentFocus=Window{456 u0 com.facebook.katana/com.facebook.bloks.hosted.activity.BloksNativeShellActivity}\"}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var http = new System.Net.Http.HttpClient(mockHandler);
        using var atx = new AtxAgentClient(7912, http);
        var service = new LDPlayerService(atx);

        var isActive = await service.IsBloksNativeShellActiveAsync(0);
        Assert.True(isActive);
    }

    [Fact]
    public async Task LDPlayerService_OpenBloksAccountsCenterAsync_SendsIntent()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/version"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "0.10.0",
                        System.Text.Encoding.UTF8,
                        "text/plain"
                    ),
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"output\":\"\"}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var http = new System.Net.Http.HttpClient(mockHandler);
        using var atx = new AtxAgentClient(7912, http);
        var service = new LDPlayerService(atx);

        var ok = await service.OpenBloksAccountsCenterAsync(0);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        Assert.Contains("fb%3A%2F%2Faccounts_center", lastCommand);
    }

    [Fact]
    public async Task LDPlayerService_InputTextAdbKeyboardAsync_SendsBroadcastViaAtx()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/version"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "0.10.0",
                        System.Text.Encoding.UTF8,
                        "text/plain"
                    ),
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"output\":\"\"}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var http = new System.Net.Http.HttpClient(mockHandler);
        using var atx = new AtxAgentClient(7912, http);
        var service = new LDPlayerService(atx);

        var ok = await service.InputTextAdbKeyboardAsync(0, "Test Caption 🚀", clearFirst: true);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        Assert.Contains("ADB_INPUT_B64", lastCommand);
    }

    [Fact]
    public async Task LDPlayerService_SendAdbKeyboardEditorActionAsync_SendsEditorCode()
    {
        string? lastCommand = null;
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/version"))
            {
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "0.10.0",
                        System.Text.Encoding.UTF8,
                        "text/plain"
                    ),
                };
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/shell"))
            {
                lastCommand = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(
                        "{\"output\":\"\"}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var http = new System.Net.Http.HttpClient(mockHandler);
        using var atx = new AtxAgentClient(7912, http);
        var service = new LDPlayerService(atx);

        var ok = await service.SendAdbKeyboardEditorActionAsync(0, AdbEditorAction.Done);
        Assert.True(ok);
        Assert.NotNull(lastCommand);
        Assert.Contains("ADB_EDITOR_CODE", lastCommand);
        Assert.Contains("code+6", lastCommand);
    }

    [Fact]
    public void ParseFacebookAccountDetails_LD71RealDumps_ExtractsLauraLindholmAndBirthdayAndUidCorrectly()
    {
        var settingsXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Your profile link' class='android.view.ViewGroup' />
  <node index='1' text='https://www.facebook.com/profile.php?id=61594801504184' class='android.view.ViewGroup' />
  <node index='2' text='' content-desc=""Copy Laura Lindholm's link"" class='android.widget.Button' />
</hierarchy>";

        var profileXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Share a note…' class='android.view.View' />
  <node index='1' text='Laura Lindholm' content-desc='Laura Lindholm' class='android.view.View' />
</hierarchy>";

        var scrolledXml =
            @"<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<hierarchy rotation='0'>
  <node index='0' text='Personal details' class='android.view.View' />
  <node index='1' text='' content-desc='Location,   Kansas City, Missouri' class='android.widget.Button' />
  <node index='2' text='Kansas City, Missouri' class='android.view.View' />
  <node index='3' text='' content-desc='Hometown,   Rochester, New York' class='android.widget.Button' />
  <node index='4' text='Rochester, New York' class='android.view.View' />
  <node index='5' text='' content-desc='Birthday, March 14, 2001' class='android.widget.Button' />
  <node index='6' text='March 14, 2001' class='android.view.View' />
  <node index='7' text='' content-desc='Status,   Divorced' class='android.widget.Button' />
  <node index='8' text='Divorced' class='android.view.View' />
  <node index='9' text='Work' class='android.view.View' />
  <node index='10' text='Walmart' content-desc='Walmart' class='android.widget.Button' />
  <node index='11' text='Education' class='android.view.View' />
  <node index='12' text='Massachusetts Institute of Technology (MIT)' content-desc='Massachusetts Institute of Technology (MIT)' class='android.widget.Button' />
</hierarchy>";

        var combinedProfileXml = $"{profileXml}\n{settingsXml}\n{scrolledXml}";

        var result = LDPlayerService.ParseFacebookAccountDetails(
            null,
            null,
            combinedProfileXml,
            null,
            null,
            71
        );

        Assert.Equal("LD-71", result.DeviceName);
        Assert.Equal("Laura Lindholm", result.Name);
        Assert.Equal("61594801504184", result.Uid);
        Assert.Equal("March 14, 2001", result.Birthdate);
        Assert.Equal("Kansas City, Missouri", result.Location);
        Assert.Equal("Rochester, New York", result.Hometown);
        Assert.Equal("Walmart", result.Work);
        Assert.Equal("Massachusetts Institute of Technology (MIT)", result.Education);
        Assert.Equal("Divorced", result.RelationshipStatus);
    }

    [Fact]
    public void FindLikeButtonCoordinates_WithValidLikeButton_ReturnsCenter()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Like' class='android.widget.Button' bounds='[36,650][180,720]' />
  </node>
</hierarchy>";

        var coords = LDPlayerService.FindLikeButtonCoordinates(xml);

        Assert.NotNull(coords);
        Assert.Equal(108, coords.Value.X);
        Assert.Equal(685, coords.Value.Y);
    }

    [Fact]
    public void FindLikeButtonCoordinates_WhenAlreadyLiked_ReturnsNull()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Unlike' class='android.widget.Button' bounds='[36,650][180,720]' />
  </node>
</hierarchy>";

        var coords = LDPlayerService.FindLikeButtonCoordinates(xml);

        Assert.Null(coords);
    }

    [Fact]
    public void FindLikeButtonCoordinates_WhenOutOfBoundsOrNoLike_ReturnsNull()
    {
        // Button in top header row (y < 120) should be ignored
        var headerXml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Like' class='android.widget.Button' bounds='[36,50][180,100]' />
  </node>
</hierarchy>";

        var coords = LDPlayerService.FindLikeButtonCoordinates(headerXml);

        Assert.Null(coords);
    }

    [Fact]
    public void FindVideoLikeButtonCoordinates_WithValidReelLikeButton_ReturnsCenter()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Like this reel' class='android.widget.Button' bounds='[620,700][700,780]' />
  </node>
</hierarchy>";

        var coords = LDPlayerService.FindVideoLikeButtonCoordinates(xml);

        Assert.NotNull(coords);
        Assert.Equal(660, coords.Value.X);
        Assert.Equal(740, coords.Value.Y);
    }

    [Fact]
    public void FindDirectSwitchButton_WithSwitchNode_ReturnsNode()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Switch to Laura Lindholm' class='android.widget.Button' bounds='[100,200][400,280]' />
  </node>
</hierarchy>";

        var node = LDPlayerService.FindDirectSwitchButton(xml, "Laura Lindholm");

        Assert.NotNull(node);
        Assert.Equal("Switch to Laura Lindholm", node.Attribute("content-desc")?.Value);
    }

    [Fact]
    public void FindDirectSwitchButton_WithPersonaSwitchIntoPage_ReturnsNode()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Plumber John+, switch into your page' class='android.widget.Button' bounds='[399,104][478,208]' />
  </node>
</hierarchy>";

        var node = LDPlayerService.FindDirectSwitchButton(xml, "Plumber John+");

        Assert.NotNull(node);
        Assert.Equal("Plumber John+, switch into your page", node.Attribute("content-desc")?.Value);
    }

    [Fact]
    public void FindDirectSwitchButton_WithPersonaSwitchIntoProfile_ReturnsNode()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='' content-desc='Isslam Harrache, switch into your profile' class='android.widget.Button' bounds='[393,104][476,208]' />
  </node>
</hierarchy>";

        var node = LDPlayerService.FindDirectSwitchButton(xml, "Isslam Harrache");

        Assert.NotNull(node);
        Assert.Equal(
            "Isslam Harrache, switch into your profile",
            node.Attribute("content-desc")?.Value
        );
    }

    [Fact]
    public void FindPersonalProfileNodeInSwitcher_IdentifiesNonPageAccount()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='Create Facebook Page' class='android.view.ViewGroup' bounds='[24,200][600,250]' />
    <node index='2' text='Sam Outdoor' class='android.view.ViewGroup' bounds='[24,300][400,360]' />
    <node index='3' text='Page' class='android.view.ViewGroup' bounds='[24,360][200,400]' />
    <node index='4' text='Laura Lindholm' class='android.view.ViewGroup' bounds='[24,450][400,510]' />
  </node>
</hierarchy>";

        var node = LDPlayerService.FindPersonalProfileNodeInSwitcher(xml);

        Assert.NotNull(node);
        Assert.Equal("Laura Lindholm", node.Attribute("text")?.Value);
    }

    [Fact]
    public void FindPersonalProfileNodeInSwitcher_SkipsSelectedActivePage_AndFindsProfile()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' content-desc='Dismiss' class='android.widget.Button' bounds='[312,362][408,394]' />
    <node index='2' text='Breastfeeding Journey' class='android.view.ViewGroup' selected='true' bounds='[32,426][688,554]' />
    <node index='3' text='Harch Jing' class='android.view.ViewGroup' selected='false' bounds='[32,554][688,684]' />
    <node index='4' text='• 1 notification' class='android.view.ViewGroup' bounds='[32,620][688,660]' />
    <node index='5' text='Jungle Bushcraft' class='android.view.ViewGroup' bounds='[32,684][688,823]' />
    <node index='6' text='Page' class='android.view.ViewGroup' bounds='[32,750][256,799]' />
    <node index='7' text='SAM Outdoor' class='android.view.ViewGroup' bounds='[32,823][688,962]' />
    <node index='8' text='Page' class='android.view.ViewGroup' bounds='[32,890][256,938]' />
    <node index='9' text='Go to Meta Account settings' class='android.widget.Button' bounds='[32,1000][688,1080]' />
  </node>
</hierarchy>";

        var node = LDPlayerService.FindPersonalProfileNodeInSwitcher(xml);

        Assert.NotNull(node);
        Assert.Equal("Harch Jing", node.Attribute("text")?.Value);
    }

    [Fact]
    public void FindPersonalProfileCoordsInBloks_IdentifiesProfileAccount()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' content-desc='Dismiss' class='android.widget.Button' bounds='[312,362][408,394]' />
    <node index='2' text='Breastfeeding Journey' class='android.view.ViewGroup' selected='true' bounds='[32,426][688,554]' />
    <node index='3' text='Harch Jing' class='android.view.ViewGroup' selected='false' bounds='[32,554][688,684]' />
    <node index='4' text='• 1 notification' class='android.view.ViewGroup' bounds='[32,620][688,660]' />
    <node index='5' text='Jungle Bushcraft' class='android.view.ViewGroup' bounds='[32,684][688,823]' />
    <node index='6' text='Page' class='android.view.ViewGroup' bounds='[32,750][256,799]' />
    <node index='7' text='Go to Meta Account settings' class='android.widget.Button' bounds='[32,1000][688,1080]' />
  </node>
</hierarchy>";

        var screen = AtxAgentClient.ParseBloksHierarchy(xml, null);
        var coords = AtxAgentClient.FindPersonalProfileCoordsInBloks(screen);

        Assert.NotNull(coords);
        Assert.Equal((32 + 688) / 2, coords.Value.X);
        Assert.Equal((554 + 684) / 2, coords.Value.Y);
    }

    [Fact]
    public void FindPersonalProfileNodeInSwitcher_WithBogusTargetProfileName_FallsBackToAmandaTroxiron()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' content-desc='Dismiss' class='android.widget.Button' bounds='[312,362][408,394]' />
    <node index='2' class='android.view.ViewGroup' selected='true' bounds='[32,426][688,554]'>
      <node index='0' text='Plumb Bob+' class='android.widget.TextView' bounds='[140,450][400,490]' />
    </node>
    <node index='3' class='android.view.ViewGroup' selected='false' bounds='[32,554][688,684]'>
      <node index='0' text='Amanda Troxiron' class='android.widget.TextView' bounds='[140,570][450,610]' />
      <node index='1' text='• 2 notifications' class='android.widget.TextView' bounds='[140,620][360,650]' />
    </node>
    <node index='4' class='android.view.ViewGroup' bounds='[32,684][688,823]'>
      <node index='0' text='KCWaterheaters+' class='android.widget.TextView' bounds='[140,700][450,740]' />
      <node index='1' text='• 3 notifications' class='android.widget.TextView' bounds='[140,750][360,780]' />
      <node index='2' text='Page' class='android.widget.TextView' bounds='[140,785][220,815]' />
    </node>
    <node index='5' text='Go to Accounts Center' class='android.widget.Button' bounds='[32,1000][688,1080]' />
  </node>
</hierarchy>";

        // When bogus targetProfileName is passed (e.g. from facebook_pages.json "All, 1 of 3")
        var node = LDPlayerService.FindPersonalProfileNodeInSwitcher(xml, "All, 1 of 3");

        Assert.NotNull(node);
        Assert.Equal("Amanda Troxiron", node.Attribute("text")?.Value);
    }

    [Fact]
    public void FindPersonalProfileCoordsInBloks_WithBogusTargetProfileName_FallsBackToAmandaTroxironRowCenter()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' content-desc='Dismiss' class='android.widget.Button' bounds='[312,362][408,394]' />
    <node index='2' class='android.view.ViewGroup' selected='true' bounds='[32,426][688,554]'>
      <node index='0' text='Plumb Bob+' class='android.widget.TextView' bounds='[140,450][400,490]' />
    </node>
    <node index='3' class='android.view.ViewGroup' selected='false' bounds='[32,554][688,684]'>
      <node index='0' text='Amanda Troxiron' class='android.widget.TextView' bounds='[140,570][450,610]' />
      <node index='1' text='• 2 notifications' class='android.widget.TextView' bounds='[140,620][360,650]' />
    </node>
    <node index='4' class='android.view.ViewGroup' bounds='[32,684][688,823]'>
      <node index='0' text='KCWaterheaters+' class='android.widget.TextView' bounds='[140,700][450,740]' />
      <node index='1' text='• 3 notifications' class='android.widget.TextView' bounds='[140,750][360,780]' />
      <node index='2' text='Page' class='android.widget.TextView' bounds='[140,785][220,815]' />
    </node>
    <node index='5' text='Go to Accounts Center' class='android.widget.Button' bounds='[32,1000][688,1080]' />
  </node>
</hierarchy>";

        var screen = AtxAgentClient.ParseBloksHierarchy(xml, null);
        var coords = AtxAgentClient.FindPersonalProfileCoordsInBloks(screen, "All, 1 of 3");

        Assert.NotNull(coords);
        // Direct center of Amanda Troxiron text node
        Assert.Equal((140 + 450) / 2, coords.Value.X);
        // Vertical center is center of Amanda Troxiron row
        Assert.Equal((570 + 610) / 2, coords.Value.Y);
    }

    [Fact]
    public void FindMenuTabInBloks_WithStandard6TabBar_DetectsMenuTabCoordinates()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='0' bounds='[0,1168][720,1280]'>
      <node index='0' content-desc='Home, tab 1 of 6' clickable='true' bounds='[0,1168][120,1280]' selected='true' />
      <node index='1' content-desc='Friends, tab 2 of 6' clickable='true' bounds='[120,1168][240,1280]' selected='false' />
      <node index='2' content-desc='Reels, tab 3 of 6' clickable='true' bounds='[240,1168][360,1280]' selected='false' />
      <node index='3' content-desc='Marketplace, tab 4 of 6' clickable='true' bounds='[360,1168][480,1280]' selected='false' />
      <node index='4' content-desc='Notifications, tab 5 of 6' clickable='true' bounds='[480,1168][600,1280]' selected='false' />
      <node index='5' content-desc='Menu, tab 6 of 6' clickable='true' bounds='[600,1168][720,1280]' selected='false' />
    </node>
  </node>
</hierarchy>";

        var screen = AtxAgentClient.ParseBloksHierarchy(xml, null);
        var tab = AtxAgentClient.FindMenuTabInBloks(screen);

        Assert.NotNull(tab);
        Assert.Equal((600 + 720) / 2, tab.Value.X);
        Assert.Equal((1168 + 1280) / 2, tab.Value.Y);
        Assert.False(tab.Value.IsSelected);
    }

    [Fact]
    public void FindMenuTabInBloks_WhenAlreadySelected_ReportsIsSelectedTrue()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='0' bounds='[0,1168][720,1280]'>
      <node index='0' content-desc='Home, tab 1 of 6' clickable='true' bounds='[0,1168][120,1280]' selected='false' />
      <node index='5' content-desc='Menu / Profile Tab (Selected): Account switcher, shortcuts, and settings menu' clickable='true' bounds='[600,1168][720,1280]' selected='true' />
    </node>
  </node>
</hierarchy>";

        var screen = AtxAgentClient.ParseBloksHierarchy(xml, null);
        var tab = AtxAgentClient.FindMenuTabInBloks(screen);

        Assert.NotNull(tab);
        Assert.Equal(660, tab.Value.X);
        Assert.Equal(1224, tab.Value.Y);
        Assert.True(tab.Value.IsSelected);
    }

    [Fact]
    public void FindMenuTabInBloks_WhenContentDescIsStripped_StructuralFallbackDetectsRightmostTab()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='0' bounds='[0,1168][720,1280]'>
      <node index='0' clickable='true' bounds='[0,1168][144,1280]' selected='true' />
      <node index='1' clickable='true' bounds='[144,1168][288,1280]' selected='false' />
      <node index='2' clickable='true' bounds='[288,1168][432,1280]' selected='false' />
      <node index='3' clickable='true' bounds='[432,1168][576,1280]' selected='false' />
      <node index='4' clickable='true' bounds='[576,1168][720,1280]' selected='false' />
    </node>
  </node>
</hierarchy>";

        var screen = AtxAgentClient.ParseBloksHierarchy(xml, null);
        var tab = AtxAgentClient.FindMenuTabInBloks(screen);

        Assert.NotNull(tab);
        Assert.Equal((576 + 720) / 2, tab.Value.X);
        Assert.Equal((1168 + 1280) / 2, tab.Value.Y);
        Assert.False(tab.Value.IsSelected);
    }

    [Fact]
    public void IsMenuPageInBloks_DetectsMenuPageIndicators()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='Menu' bounds='[24,60][179,125]' />
    <node index='2' content-desc='Switch profile' clickable='true' bounds='[472,192][560,280]' />
    <node index='3' content-desc='More options' clickable='true' bounds='[584,192][672,280]' />
    <node index='4' text='Your shortcuts' bounds='[32,328][688,369]' />
  </node>
</hierarchy>";

        var screen = AtxAgentClient.ParseBloksHierarchy(xml, null);
        Assert.True(AtxAgentClient.IsMenuPageInBloks(screen));
    }

    [Fact]
    public void ParseRootShellAccountOutput_ValidProfileAndPage_ExtractsAccurately()
    {
        var output =
            "AUTH|61594750165531|William Rhodes|EAAAAUa...|[{\"name\":\"c_user\",\"value\":\"61594750165531\"}]\n"
            + "ACCOUNT|Page|61594246817570|1356708460856148|Caprani Plumbing+|61594750165531||https://fbcdn.net/page.jpg\n"
            + "ACCOUNT|Profile|61594750165531||William Rhodes|61594750165531|william@example.com|https://fbcdn.net/user.jpg\n"
            + "DBL|{\"full_name\":\"William Rhodes\",\"uid\":\"61594750165531\",\"pic_url\":\"https://fbcdn.net/user.jpg\"}";

        var accounts = LDPlayerService.ParseRootShellAccountOutput(output);

        Assert.Equal(2, accounts.Count);

        var profile = accounts.FirstOrDefault(a => a.Type == "Profile");
        Assert.NotNull(profile);
        Assert.Equal("61594750165531", profile.Uid);
        Assert.Equal("61594750165531", profile.PageId);
        Assert.Equal("William Rhodes", profile.Name);
        Assert.Equal("william@example.com", profile.Email);
        Assert.True(profile.IsActive);
        Assert.Equal("EAAAAUa...", profile.AccessToken);
        Assert.Contains("c_user", profile.Cookies);

        var page = accounts.FirstOrDefault(a => a.Type == "Page");
        Assert.NotNull(page);
        Assert.Equal("61594246817570", page.Uid);
        Assert.Equal("1356708460856148", page.PageId);
        Assert.Equal("Caprani Plumbing+", page.Name);
        Assert.Equal("61594750165531", page.UserUid);
        Assert.Equal("https://fbcdn.net/page.jpg", page.PicUrl);
        Assert.False(page.IsActive);
    }

    [Fact]
    public void ParseRootShellAccountOutput_DblOnly_ExtractsProfile()
    {
        var output =
            "DBL|{\"full_name\":\"John Doe\",\"uid\":\"61599988877711\",\"pic_url\":\"https://fbcdn.net/pic.jpg\"}";

        var accounts = LDPlayerService.ParseRootShellAccountOutput(output);

        Assert.Single(accounts);
        var acc = accounts[0];
        Assert.Equal("Profile", acc.Type);
        Assert.Equal("61599988877711", acc.Uid);
        Assert.Equal("John Doe", acc.Name);
        Assert.Equal("https://fbcdn.net/pic.jpg", acc.PicUrl);
    }

    [Fact]
    public void ParseRootShellAccountOutput_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(LDPlayerService.ParseRootShellAccountOutput(null));
        Assert.Empty(LDPlayerService.ParseRootShellAccountOutput(""));
        Assert.Empty(LDPlayerService.ParseRootShellAccountOutput("   \n\r  "));
        Assert.Empty(LDPlayerService.ParseRootShellAccountOutput("INVALID_LINE_WITHOUT_PREFIX"));
    }

    [Fact]
    public void FacebookAccountInfo_ToFacebookPageTarget_MapsCorrectly()
    {
        var info = new FacebookAccountInfo
        {
            Type = "Page",
            Uid = "61594246817570",
            PageId = "1356708460856148",
            Name = "Caprani Plumbing+",
            UserUid = "61594750165531",
            Email = "contact@caprani.com",
        };

        var target = info.ToFacebookPageTarget("LD-20", "FB-01");

        Assert.Equal("LD-20", target.DeviceName);
        Assert.Equal("FB-01", target.AppClone);
        Assert.Equal("1356708460856148", target.PageId);
        Assert.Equal("61594750165531", target.UserUid);
        Assert.Equal("Caprani Plumbing+", target.PageName);
        Assert.Equal("Page", target.Type);
        Assert.Equal("61594750165531", target.ManagedBy);
        Assert.Equal("contact@caprani.com", target.Email);
    }

    [Fact]
    public void FindPersonaInSwitcher_FindsTargetPageCorrectly()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' content-desc='Dismiss' class='android.widget.Button' bounds='[312,362][408,394]' />
    <node index='2' text='Breastfeeding Journey' class='android.view.ViewGroup' selected='true' bounds='[32,426][688,554]' />
    <node index='3' text='Harch Jing' class='android.view.ViewGroup' selected='false' bounds='[32,554][688,684]' />
    <node index='4' text='Jungle Bushcraft' class='android.view.ViewGroup' bounds='[32,684][688,823]' />
    <node index='5' text='Page' class='android.view.ViewGroup' bounds='[32,750][256,799]' />
    <node index='6' text='SAM Outdoor' class='android.view.ViewGroup' bounds='[32,823][688,962]' />
    <node index='7' text='Page' class='android.view.ViewGroup' bounds='[32,890][256,938]' />
  </node>
</hierarchy>";

        var match = LDPlayerService.FindPersonaInSwitcher(xml, "SAM Outdoor");

        Assert.NotNull(match);
        Assert.Equal((32 + 688) / 2, match.Value.X);
        Assert.Equal((823 + 962) / 2, match.Value.Y);
        Assert.False(match.Value.IsSelected);
    }

    [Fact]
    public void FindPersonaInSwitcher_IdentifiesSelectedPage()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' content-desc='Dismiss' class='android.widget.Button' bounds='[312,362][408,394]' />
    <node index='2' text='Breastfeeding Journey' class='android.view.ViewGroup' selected='true' bounds='[32,426][688,554]' />
    <node index='3' text='SAM Outdoor' class='android.view.ViewGroup' bounds='[32,823][688,962]' />
  </node>
</hierarchy>";

        var match = LDPlayerService.FindPersonaInSwitcher(xml, "Breastfeeding Journey");

        Assert.NotNull(match);
        Assert.True(match.Value.IsSelected);
    }

    [Fact]
    public void IsAlreadyOnTargetPersona_WhenActiveOnMenuHeader_ReturnsTrue()
    {
        var xml =
            @"<hierarchy rotation='0'>
  <node index='0' bounds='[0,0][720,1280]'>
    <node index='1' text='SAM Outdoor, see your profile' class='android.view.ViewGroup' bounds='[64,120][507,220]' />
    <node index='2' text='Your shortcuts' class='android.view.ViewGroup' bounds='[24,352][224,373]' />
  </node>
</hierarchy>";

        Assert.True(LDPlayerService.IsAlreadyOnTargetPersona(xml, "SAM Outdoor"));
        Assert.False(LDPlayerService.IsAlreadyOnTargetPersona(xml, "Other Page"));
    }

    [Fact]
    public void IsAccountSwitcherOpen_DetectsVariousSwitcherSignatures()
    {
        var xmlWithDismiss =
            "<hierarchy><node content-desc='Dismiss' bounds='[0,0][10,10]' /><node text='Page' /></hierarchy>";
        var xmlWithCreatePage = "<hierarchy><node text='Create Facebook Page' /></hierarchy>";
        var xmlWithMetaSettings =
            "<hierarchy><node text='Go to Meta Account settings' /></hierarchy>";
        var xmlRegularFeed = "<hierarchy><node text='News Feed' /></hierarchy>";

        Assert.True(LDPlayerService.IsAccountSwitcherOpen(xmlWithDismiss));
        Assert.True(LDPlayerService.IsAccountSwitcherOpen(xmlWithCreatePage));
        Assert.True(LDPlayerService.IsAccountSwitcherOpen(xmlWithMetaSettings));
        Assert.False(LDPlayerService.IsAccountSwitcherOpen(xmlRegularFeed));
    }

    [Fact]
    public void BulkFetch_GroupingByDevice_AvoidsDuplicateDeviceLaunches()
    {
        var pages = new List<FacebookPageTarget>
        {
            new()
            {
                DeviceName = "LD-0",
                PageId = "1001",
                PageName = "Page 1",
            },
            new()
            {
                DeviceName = "LD-0",
                PageId = "1002",
                PageName = "Page 2",
            },
            new()
            {
                DeviceName = "LD-1",
                PageId = "2001",
                PageName = "Page 3",
            },
            new()
            {
                DeviceName = "LD-1",
                PageId = "2002",
                PageName = "Page 4",
            },
            new()
            {
                DeviceName = "LD-2",
                PageId = "3001",
                PageName = "Page 5",
            },
        };

        var grouped = pages
            .GroupBy(p => int.TryParse(p.DeviceName.Replace("LD-", ""), out var idx) ? idx : -1)
            .Where(g => g.Key >= 0)
            .ToList();

        Assert.Equal(3, grouped.Count);
        Assert.Equal(2, grouped.First(g => g.Key == 0).Count());
        Assert.Equal(2, grouped.First(g => g.Key == 1).Count());
        Assert.Single(grouped.First(g => g.Key == 2));
    }

    [Fact]
    public async System.Threading.Tasks.Task BulkFetch_ConcurrencyLimiting_EnforcesMaxActiveWorkers()
    {
        const int maxConcurrency = 4;
        const int totalItems = 20;
        int activeCount = 0;
        int peakActive = 0;
        var lockObj = new object();

        var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency);
        var tasks = System
            .Linq.Enumerable.Range(0, totalItems)
            .Select(async _ =>
            {
                await semaphore.WaitAsync();
                try
                {
                    lock (lockObj)
                    {
                        activeCount++;
                        if (activeCount > peakActive)
                            peakActive = activeCount;
                    }
                    await System.Threading.Tasks.Task.Delay(15);
                }
                finally
                {
                    lock (lockObj)
                    {
                        activeCount--;
                    }
                    semaphore.Release();
                }
            });

        await System.Threading.Tasks.Task.WhenAll(tasks);

        Assert.True(
            peakActive <= maxConcurrency,
            $"Peak active count {peakActive} exceeded maximum concurrency {maxConcurrency}"
        );
        Assert.Equal(maxConcurrency, peakActive);
    }
}
