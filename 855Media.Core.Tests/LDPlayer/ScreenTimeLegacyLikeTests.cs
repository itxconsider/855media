using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeLegacyLikeTests
{
    [Theory]
    [InlineData("confirmed")]
    [InlineData("hidden-id")]
    [InlineData("unknown")]
    [InlineData("changed-before-gesture")]
    public async Task LegacyLikesUseDurableTargetHistoryAndNeverEnableOtherActions(string mode)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "legacy-like-" + Guid.NewGuid().ToString("N") + ".db"
        );
        var history = new ScreenTimeHistoryStore(path);
        await history.StartAsync("first", "123", 1);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        var settings = new ScreenTimeSetting
        {
            OwnerUid = "123",
            OwnerName = "Owner",
            LikePost = true,
            Options = new()
            {
                Reaction = "Love",
                Reactions = false,
                ReactionLimit = 0,
            },
        };
        var engine = new ScreenTimeInteractions(
            ld,
            history,
            1,
            settings,
            "first",
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
            Assert.Equal(mode == "confirmed" ? 1 : 0, engine.Likes);
            Assert.Equal(0, engine.Reactions);
            Assert.False(settings.Options.Reactions);
            Assert.Equal("Love", settings.Options.Reaction);
            if (mode == "confirmed")
            {
                await history.StartAsync("second", "123", 1);
                fake.Reacted = false;
                fake.Dumps = 0;
                var restart = new ScreenTimeInteractions(
                    ld,
                    new ScreenTimeHistoryStore(path),
                    1,
                    settings,
                    "second",
                    null,
                    (_, _) => Task.CompletedTask
                );
                Assert.False(await restart.LikeAsync(default));
                Assert.Equal(1, fake.Taps);
                Assert.Single(history.ReadActions());
            }
            if (mode == "changed-before-gesture")
                Assert.Equal("NotSubmitted", history.ReadActions().Single().State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    public class Device : DispatchProxy
    {
        public string Mode = "confirmed";
        public int Taps,
            Dumps;
        public bool Reacted;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "DumpUiHierarchyAsync")
            {
                Dumps++;
                var id =
                    Mode == "hidden-id"
                        ? ""
                        : "<node text='Post ID: "
                            + (Mode == "changed-before-gesture" && Dumps >= 2 ? "999" : "123")
                            + "'/>";
                return Task.FromResult<string?>(
                    "<hierarchy>"
                        + id
                        + "<node text='"
                        + (Reacted && Mode != "unknown" ? "Remove Like reaction" : "Like")
                        + "' clickable='true' bounds='[0,0][10,10]'/></hierarchy>"
                );
            }
            if (method.Name == "TapAsync")
            {
                Taps++;
                Reacted = true;
                return Task.FromResult(true);
            }
            throw new NotSupportedException(
                "No legacy coordinate adapter is allowed: " + method.Name
            );
        }
    }
}
