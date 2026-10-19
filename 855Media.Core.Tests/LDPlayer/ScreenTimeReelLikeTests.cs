using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeReelLikeTests
{
    internal static string Screen(bool selected)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld68-reel-reaction-selected")
        );
        var icon = doc.Descendants("node")
            .First(n =>
                n.Attribute("selected")?.Value == "true"
                && (n.Attribute("content-desc")?.Value ?? "").EndsWith(" reactions")
            );
        icon.SetAttributeValue("selected", selected ? "true" : "false");
        return doc.ToString();
    }

    [Fact]
    public void RecognizesIconAndAppliedStateWithoutPostId()
    {
        var off = ScreenTimeReelReactionTarget.Read(Screen(false));
        var on = ScreenTimeReelReactionTarget.Read(Screen(true));
        Assert.NotNull(off);
        Assert.NotNull(on);
        Assert.False(off.Applied);
        Assert.True(on.Applied);
        Assert.Equal(off.Key, on.Key);
        Assert.Equal("[624,574][720,654]", off.Control.Attribute("bounds")?.Value);
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("already-reacted")]
    [InlineData("unknown")]
    [InlineData("changed")]
    public async Task ReelLikesTapOnceAndRequireSelectedState(string mode)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "reel-like-" + Guid.NewGuid().ToString("N") + ".db"
        );
        var history = new ScreenTimeHistoryStore(path);
        await history.StartAsync("test", "123", 1);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        var engine = new ScreenTimeInteractions(
            ld,
            history,
            1,
            new() { OwnerUid = "123", OwnerName = "Owner" },
            "test",
            null,
            (_, _) => Task.CompletedTask
        );
        try
        {
            if (mode == "unknown")
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    engine.LikeAsync(default)
                );
                Assert.True(history.NeedsReview(1));
                Assert.Equal("Unknown", history.ReadActions().Single().State);
            }
            else
                Assert.Equal(mode == "confirmed", await engine.LikeAsync(default));
            Assert.Equal(mode is "confirmed" or "unknown" ? 1 : 0, fake.Taps);
            if (mode == "confirmed")
            {
                Assert.Equal("Confirmed", history.ReadActions().Single().State);
                Assert.False(await engine.LikeAsync(default));
                Assert.Equal(1, fake.Taps);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CountAloneHiddenAndForeignControlsAreRejected()
    {
        foreach (var mode in new[] { "count-only", "hidden", "foreign", "ambiguous" })
        {
            var doc = XDocument.Parse(Screen(false));
            var n = doc.Descendants("node")
                .First(n => n.Attribute("bounds")?.Value == "[624,574][720,654]");
            if (mode == "count-only")
                n.Remove();
            if (mode == "hidden")
                n.SetAttributeValue("visible-to-user", "false");
            if (mode == "foreign")
                n.SetAttributeValue("package", "other.app");
            if (mode == "ambiguous")
                n.Parent!.Add(new XElement(n));
            Assert.Null(ScreenTimeReelReactionTarget.Read(doc.ToString()));
        }
    }

    public class Device : DispatchProxy
    {
        public string Mode = "confirmed";
        public int Taps,
            Reads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "DumpUiHierarchyAsync")
            {
                Reads++;
                var xml = Screen(Mode == "already-reacted" || Taps > 0 && Mode != "unknown");
                if (Mode == "changed" && Reads > 1)
                    xml = xml.Replace("King Harris", "Different Author");
                return Task.FromResult<string?>(xml);
            }
            if (method.Name == "TapAsync")
            {
                Taps++;
                return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
