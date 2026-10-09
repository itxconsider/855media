using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookAccountDiscoveryServiceTests
{
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

        var info = FacebookAccountDiscoveryService.ParsePageInfoFromXml(sampleXml, "12345");

        Assert.Equal("885media", info.PageName);
        Assert.Equal(1200, info.Followers);
        Assert.Equal("Page", info.Type);
        Assert.Equal(530, info.ReelsCount);
    }

    [Fact]
    public void ParsePageInfoFromXml_ExtractsFacebookJoinedMonthAndYear()
    {
        var sampleXml =
            @"<hierarchy rotation='0'>
  <node index='0' text='Van Fabricator' class='android.widget.TextView' />
  <node index='1' text='Joined Facebook September 2026' class='android.widget.TextView' />
</hierarchy>";

        var info = FacebookAccountDiscoveryService.ParsePageInfoFromXml(sampleXml, "12345");

        Assert.Equal("September 2026", info.CreationDate);
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

        var result = FacebookAccountDiscoveryService.ParseDeviceAccountAndPagesFromXml(
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

        var result = FacebookAccountDiscoveryService.ParseFacebookAccountDetails(
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
    }

    [Fact]
    public void IsNotificationSubtitle_DetectsValidPatterns()
    {
        Assert.True(FacebookAccountDiscoveryService.IsNotificationSubtitle("5 notifications"));
        Assert.True(FacebookAccountDiscoveryService.IsNotificationSubtitle("1 notification"));
        Assert.True(FacebookAccountDiscoveryService.IsNotificationSubtitle("9+ notifications"));
        Assert.False(FacebookAccountDiscoveryService.IsNotificationSubtitle("My Brand Page"));
    }

    [Fact]
    public void IsIgnoredSwitcherHeaderOrAction_DetectsSystemElements()
    {
        Assert.True(FacebookAccountDiscoveryService.IsIgnoredSwitcherHeaderOrAction("Menu"));
        Assert.True(
            FacebookAccountDiscoveryService.IsIgnoredSwitcherHeaderOrAction("Your shortcuts")
        );
        Assert.True(
            FacebookAccountDiscoveryService.IsIgnoredSwitcherHeaderOrAction("Create Facebook Page")
        );
        Assert.False(FacebookAccountDiscoveryService.IsIgnoredSwitcherHeaderOrAction("John Doe"));
    }

    [Fact]
    public void ParseRootShellAccountOutput_ParsesDumpsysOutput()
    {
        var output =
            "AUTH|61594750165531|William Rhodes|EAAAAUa...|[{\"name\":\"c_user\",\"value\":\"61594750165531\"}]\n"
            + "ACCOUNT|Page|61594246817570|1356708460856148|Caprani Plumbing+|61594750165531||https://fbcdn.net/page.jpg\n"
            + "ACCOUNT|Profile|61594750165531||William Rhodes|61594750165531|william@example.com|https://fbcdn.net/user.jpg\n";

        var accounts = FacebookAccountDiscoveryService.ParseRootShellAccountOutput(output);

        Assert.Equal(2, accounts.Count);
        Assert.Equal("Caprani Plumbing+", accounts[0].Name);
        Assert.Equal("William Rhodes", accounts[1].Name);
    }

    [Fact]
    public void ParseRootShellAccountOutput_ParsesPrefsDbPersonaUids()
    {
        const string userUid = "61586742627455";
        var output =
            $"AUTH|{userUid}|Carey Northcutt|||\n"
            + $"PERSONA|{userUid}|{userUid}\n"
            + $"PERSONA|{userUid}|61585774685337\n"
            + $"PERSONA|{userUid}|61593289073003\n";

        var accounts = FacebookAccountDiscoveryService.ParseRootShellAccountOutput(output);

        Assert.Equal(3, accounts.Count);
        Assert.Contains(accounts, a => a.Type == "Profile" && a.Uid == userUid && a.IsActive);
        Assert.Contains(
            accounts,
            a =>
                a.Type == "Page"
                && a.Uid == "61585774685337"
                && a.UserUid == userUid
                && string.IsNullOrEmpty(a.PageId)
        );
        Assert.Contains(
            accounts,
            a => a.Type == "Page" && a.Uid == "61593289073003" && a.UserUid == userUid
        );
    }

    [Fact]
    public void ParseDirectDumps_ParsesBinaryAndXmlDumpsCorrectly()
    {
        var authDump =
            "underlying_account_uid\n61594750165531\nuser_id\n61594750165531\nusername\nWilliam Rhodes\naccess_token\nEAAAAUa999888777\nsession_cookies_string\n[{\"name\":\"c_user\",\"value\":\"61594750165531\"}]";

        var loggedInDump =
            "===FILE:/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/logged_in_61594246817570===\n"
            + "name\nCaprani Plumbing+\ndelegate_page_id\n1356708460856148\nis_page\n1\npic_url\nhttps://fbcdn.net/page.jpg\nuser_id\n61594750165531\n"
            + "===FILE:/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/logged_in_61594750165531===\n"
            + "name\nWilliam Rhodes\nemail\nwilliam@example.com\npic_url\nhttps://fbcdn.net/user.jpg\nuser_id\n61594750165531\n";

        var sharedPrefsDump =
            "<map>\n"
            + "  <string name=\"/auth/auth_device_based_login_credentials/61594750165531\">{&quot;uid&quot;:&quot;61594750165531&quot;,&quot;full_name&quot;:&quot;William Rhodes&quot;,&quot;pic_url&quot;:&quot;https://fbcdn.net/user.jpg&quot;}</string>\n"
            + "</map>";

        var accounts = FacebookAccountDiscoveryService.ParseDirectDumps(
            authDump,
            loggedInDump,
            sharedPrefsDump,
            null
        );

        Assert.Equal(2, accounts.Count);

        var page = accounts.FirstOrDefault(a => a.Type == "Page");
        Assert.NotNull(page);
        Assert.Equal("1356708460856148", page.PageId);
        Assert.Equal("Caprani Plumbing+", page.Name);
        Assert.Equal("61594750165531", page.UserUid);

        var profile = accounts.FirstOrDefault(a => a.Type == "Profile");
        Assert.NotNull(profile);
        Assert.Equal("61594750165531", profile.Uid);
        Assert.Equal("William Rhodes", profile.Name);
        Assert.Equal("william@example.com", profile.Email);
        Assert.True(profile.IsActive);
        Assert.Equal("EAAAAUa999888777", profile.AccessToken);
    }

    [Fact]
    public void ParseRootShellAccountOutput_FallsBackToDirectDumpWhenPrefixMissing()
    {
        var rawDump =
            "underlying_account_uid\n61594750165531\nusername\nJane Doe\n"
            + "===FILE:/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/logged_in_61594750165531===\n"
            + "name\nJane Doe\nuser_id\n61594750165531\n";

        var accounts = FacebookAccountDiscoveryService.ParseRootShellAccountOutput(rawDump);

        Assert.Single(accounts);
        Assert.Equal("Jane Doe", accounts[0].Name);
        Assert.Equal("61594750165531", accounts[0].Uid);
        Assert.True(accounts[0].IsActive);
    }

    [Fact]
    public void FacebookDiscoveryDelayConfig_Defaults_AreValid()
    {
        var config = new FacebookDiscoveryDelayConfig();

        Assert.Equal(2500, config.DelayOpenAppMs);
        Assert.Equal(3000, config.DelayRelaunchAppMs);
        Assert.Equal(3500, config.DelayPageLoadMs);
        Assert.Equal(2500, config.DelayProfileNavMs);
        Assert.Equal(3000, config.DelayPagesNavMs);
        Assert.Equal(2000, config.DelayMenuNavMs);
        Assert.Equal(3500, config.DelaySwitchPersonaMs);
        Assert.Equal(1500, config.DelayAfterTapMs);
        Assert.Equal(1500, config.DelayAfterSwipeMs);
        Assert.Equal(1000, config.DelayAfterBackMs);
        Assert.Equal(1000, config.DelayAppLifecycleStepMs);
    }

    [Fact]
    public void FacebookDiscoveryDelayConfig_FromAutoPostConfig_MapsSecondsToMilliseconds()
    {
        var autoPostConfig = new AutoPostConfig { DelayOpenApp = 5, DelayBeforeSwitchPage = 8 };

        var delayConfig = FacebookDiscoveryDelayConfig.FromAutoPostConfig(autoPostConfig);

        Assert.Equal(5000, delayConfig.DelayOpenAppMs);
        Assert.Equal(8000, delayConfig.DelaySwitchPersonaMs);
        Assert.Equal(8000, delayConfig.DelayPageLoadMs);
    }

    [Fact]
    public void FacebookDiscoveryDelayConfig_FromLdSettings_MapsSecondsToMilliseconds()
    {
        var settings = new LDSettings { DelayOpenApp = 6, DelayBeforeSwitchPage = 10 };

        var delayConfig = FacebookDiscoveryDelayConfig.FromLdSettings(settings);

        Assert.Equal(6000, delayConfig.DelayOpenAppMs);
        Assert.Equal(10000, delayConfig.DelaySwitchPersonaMs);
        Assert.Equal(10000, delayConfig.DelayPageLoadMs);
    }

    [Fact]
    public void FacebookAccountDiscoveryService_DelayConfig_CanBeInitializedAndUpdated()
    {
        var ldPlayer = new LDPlayerService();
        var customConfig = new FacebookDiscoveryDelayConfig
        {
            DelayOpenAppMs = 4000,
            DelayPageLoadMs = 5000,
        };

        var service = new FacebookAccountDiscoveryService(ldPlayer, delayConfig: customConfig);
        Assert.Equal(4000, service.DelayConfig.DelayOpenAppMs);
        Assert.Equal(5000, service.DelayConfig.DelayPageLoadMs);

        service.ApplyDelayConfig(
            new AutoPostConfig { DelayOpenApp = 12, DelayBeforeSwitchPage = 14 }
        );
        Assert.Equal(12000, service.DelayConfig.DelayOpenAppMs);
        Assert.Equal(14000, service.DelayConfig.DelaySwitchPersonaMs);

        service.ApplyDelayConfig(new LDSettings { DelayOpenApp = 7, DelayBeforeSwitchPage = 9 });
        Assert.Equal(7000, service.DelayConfig.DelayOpenAppMs);
        Assert.Equal(9000, service.DelayConfig.DelaySwitchPersonaMs);
    }
}
