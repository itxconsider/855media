using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPageDiscoveryTests
{
    private const string Settings =
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node text='Settings &amp; privacy' package='com.facebook.katana' bounds='[80,80][500,140]'/><node text='Meta Account' package='com.facebook.katana' bounds='[70,230][550,310]'/><node clickable='true' enabled='true' package='com.facebook.katana' bounds='[600,230][680,310]'/></node></hierarchy>";

    [Fact]
    public async Task DelayedSwitcherWaitsWithoutTappingSettingsOrBackground()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DiscoveryDevice>();
        var device = (DiscoveryDevice)(object)ld;
        device.Screens = new Queue<string>(new[] { Settings, "<hierarchy/>", Fixture });
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, _) =>
            {
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        Assert.NotNull(await service.WaitForDiscoverySwitcherAsync(5, null, default));
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task DiscoveryLeavesSettingsViaFeedWithoutBlindCoordinateTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DiscoveryDevice>();
        var device = (DiscoveryDevice)(object)ld;
        device.Screens = new Queue<string>(new[] { Settings, Fixture });
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, _) =>
            {
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        Assert.True(await service.OpenDiscoverySwitcherAsync(5, null, default));
        Assert.Equal(1, device.FeedLinks);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task SettingsSearchFieldDoesNotPreventReturningToFeed()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DiscoveryDevice>();
        var device = (DiscoveryDevice)(object)ld;
        var doc = XDocument.Parse(Settings);
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("class", "android.widget.EditText"),
                new XAttribute("text", "Search settings")
            )
        );
        device.Screens = new Queue<string>(new[] { doc.ToString(), Fixture });
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, _) =>
            {
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        Assert.True(await service.OpenDiscoverySwitcherAsync(5, null, default));
        Assert.Equal(1, device.FeedLinks);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task PasswordOrAnotherAppStopsDiscoveryNavigation()
    {
        foreach (
            var xml in new[]
            {
                Settings.Replace("text='Meta Account'", "password='true' text='Meta Account'"),
                Settings.Replace("com.facebook.katana", "another.app"),
            }
        )
        {
            var ld = DispatchProxy.Create<ILDPlayerService, DiscoveryDevice>();
            var device = (DiscoveryDevice)(object)ld;
            device.Fallback = xml;
            var clock = DateTime.UtcNow;
            var service = new PFlowPersonaSwitcherService(
                ld,
                null,
                (delay, _) =>
                {
                    clock += delay;
                    return Task.CompletedTask;
                },
                () => clock
            );
            Assert.False(await service.OpenDiscoverySwitcherAsync(5, null, default));
            Assert.Equal(0, device.Taps + device.FeedLinks);
        }
    }

    [Fact]
    public async Task RealMenuDropdownWaitsThroughAnimationInsteadOfTappingAgain()
    {
        var menu = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", "ld55-menu.xml")
        );
        var ld = DispatchProxy.Create<ILDPlayerService, DiscoveryDevice>();
        var device = (DiscoveryDevice)(object)ld;
        device.Screens = new Queue<string>(new[] { menu, menu, "<hierarchy/>", Fixture });
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, _) =>
            {
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        Assert.True(await service.OpenDiscoverySwitcherAsync(5, null, default));
        Assert.Equal(1, device.Taps);
        Assert.Equal(0, device.FeedLinks);
    }

    [Fact]
    public async Task UnavailableSwitcherTimesOutWithoutTappingOrReportingCompletion()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DiscoveryDevice>();
        var device = (DiscoveryDevice)(object)ld;
        device.Fallback = Settings;
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, _) =>
            {
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        Assert.Null(await service.WaitForDiscoverySwitcherAsync(5, null, default));
        Assert.Equal(0, device.Taps);
        Assert.True(device.Reads <= 21);
    }

    [Fact]
    public void DiscoveryScrollStaysInsideSwitcherRows()
    {
        var swipe = PFlowPersonaSwitcherService.DiscoverySwipe(Fixture);
        Assert.NotNull(swipe);
        Assert.True(swipe.Value.FromY > swipe.Value.ToY);
        Assert.True(swipe.Value.ToY > 0);
        Assert.InRange(swipe.Value.X, 1, 719);
        var rowBounds = XDocument
            .Parse(Fixture)
            .Descendants("node")
            .Where(n =>
                n.Attribute("content-desc")?.Value is { } name
                && PFlowPersonaSwitcherService
                    .DiscoveryPageNames(Fixture)
                    .Any(page => name.StartsWith(page, StringComparison.Ordinal))
            )
            .Select(n =>
                System.Text.RegularExpressions.Regex.Match(
                    n.Attribute("bounds")?.Value ?? "",
                    @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
                )
            )
            .Where(m => m.Success)
            .ToArray();
        Assert.True(swipe.Value.FromY <= rowBounds.Max(m => int.Parse(m.Groups[4].Value)));
        Assert.Null(PFlowPersonaSwitcherService.DiscoverySwipe(Settings));
    }

    public class DiscoveryDevice : DispatchProxy
    {
        public Queue<string> Screens = new();
        public string Fallback = "<hierarchy/>";
        public int Taps,
            FeedLinks,
            Reads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "DumpBloksScreenAsync")
            {
                Reads++;
                return Task.FromResult<BloksScreenInfo?>(
                    AtxAgentClient.ParseBloksHierarchy(
                        Screens.Count > 0 ? Screens.Dequeue() : Fallback
                    )
                );
            }
            if (method.Name == "OpenDeepLinkAsync")
            {
                Assert.Equal("fb://feed", args![1]);
                FeedLinks++;
                return Task.FromResult(true);
            }
            if (method.Name == "TapAsync")
            {
                Taps++;
                return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }

    private static string Fixture =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld2-switcher-full.xml"
            )
        );

    [Fact]
    public void TypedSwitcherListsOnlyPageRowsAndStripsNotifications()
    {
        var names = PFlowPersonaSwitcherService.DiscoveryPageNames(Fixture);
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.DoesNotContain("notifications", name));
        Assert.DoesNotContain("Create Facebook Page", names);
        Assert.All(
            names,
            name =>
                Assert.NotNull(
                    PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(Fixture, name, "Page")
                )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<hierarchy />")]
    [InlineData("not xml")]
    public void UnknownScreensProvideNoDiscoveryTargets(string? xml) =>
        Assert.Empty(PFlowPersonaSwitcherService.DiscoveryPageNames(xml));

    [Fact]
    public void TruncatedRowsAreNotUsedAsExactIdentityNames()
    {
        var doc = XDocument.Parse(Fixture);
        foreach (
            var row in doc.Descendants("node")
                .Where(n =>
                    n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("content-desc")?.Value?.Contains("notifications") == true
                )
        )
            row.SetAttributeValue("content-desc", "Truncated…");
        Assert.DoesNotContain(
            "Truncated…",
            PFlowPersonaSwitcherService.DiscoveryPageNames(doc.ToString())
        );
    }
}
