using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowReelNavigation
{
    internal sealed record Entry(string Xml, XElement? Reel, FacebookLayoutControl? Create);

    internal static async Task<Entry> FindFeedEntryAsync(
        ILDPlayerService ld,
        int index,
        string initialXml,
        Func<Task<string>> observe,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= Task.Delay;
        var xml = initialXml;
        var reveals = 0;
        for (var observation = 0; observation < 7; observation++)
        {
            ct.ThrowIfCancellationRequested();
            var feed = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed);
            if (!feed.IsMatch)
                return new(xml, null, null);
            // A card can appear after Feed itself is ready. Always check each
            // fresh observation before scrolling or falling back to the header.
            var reel = PFlowReelLayout.CreateEntry(xml);
            if (reel != null)
                return new(xml, reel, null);
            var create = feed.Control(FacebookControlRole.Create);
            if (create != null)
                return new(xml, null, create);
            if (observation == 6)
                break;
            // Allow the first loading frame to settle before revealing hidden navigation.
            if (observation > 0 && reveals < 3)
            {
                var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                if (width > 0 && height > 0)
                {
                    if (
                        !await ld.SwipeAsync(
                            index,
                            width / 2,
                            height / 3,
                            width / 2,
                            height * 4 / 5,
                            400,
                            path,
                            ct
                        )
                    )
                        return new(xml, null, null);
                    reveals++;
                    await PFlowTrace.SessionAsync(
                        index,
                        "reel.entry.reveal",
                        $"attempt={reveals}; observedFeed=true",
                        ct
                    );
                }
            }
            await delay(TimeSpan.FromMilliseconds(500), ct);
            xml = await observe();
        }
        return new(xml, null, null);
    }
}
