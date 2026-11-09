using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class AtxAgentClient
{
    private sealed class ReaderState
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal DateTimeOffset LastRecovery;
        internal string Failure = "empty-or-malformed";
    }

    private static readonly ConcurrentDictionary<string, ReaderState> Readers = new();

    private ReaderState Reader(int index) => Readers.GetOrAdd(GetDeviceBaseUrl(index), _ => new());

    public async Task<string?> DumpHierarchyAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    )
    {
        var reader = Reader(deviceIndex);
        await reader.Gate.WaitAsync(cancellationToken);
        try
        {
            return await DumpHierarchyCoreAsync(deviceIndex, cancellationToken);
        }
        finally
        {
            reader.Gate.Release();
        }
    }

    internal async Task<string?> RecoverHierarchyAsync(
        int index,
        Func<CancellationToken, Task<bool>> sameFacebook,
        CancellationToken ct
    )
    {
        var reader = Reader(index);
        await reader.Gate.WaitAsync(ct);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(20));
            var token = budget.Token;
            // A failed reader must not cause a restart storm across polling loops/workers.
            if (
                DateTimeOffset.UtcNow - reader.LastRecovery < TimeSpan.FromMinutes(2)
                || !await sameFacebook(token)
            )
                return null;
            reader.LastRecovery = DateTimeOffset.UtcNow;
            await PFlowTrace.SessionAsync(
                index,
                "ui.reader.recovery.begin",
                $"reason={reader.Failure}; restartBudget=1; facebookRestart=false",
                token
            );
            using (
                var stop = await _httpClient.DeleteAsync(
                    GetDeviceBaseUrl(index) + "/uiautomator",
                    token
                )
            )
                if (!stop.IsSuccessStatusCode)
                    return null;
            if (!await sameFacebook(token))
                return null;
            using (
                var start = await _httpClient.PostAsync(
                    GetDeviceBaseUrl(index) + "/uiautomator",
                    null,
                    token
                )
            )
                if (!start.IsSuccessStatusCode)
                    return null;
            var stable = 0;
            for (var poll = 0; poll < 8; poll++)
            {
                token.ThrowIfCancellationRequested();
                if (!await sameFacebook(token))
                    return null;
                var xml = await DumpHierarchyCoreAsync(index, token);
                if (!await sameFacebook(token))
                    return null;
                if (ReadableFacebook(xml))
                    stable++;
                else
                    stable = 0;
                if (stable == 2)
                {
                    await PFlowTrace.SessionAsync(
                        index,
                        "ui.reader.recovery.ready",
                        "freshFrames=2; facebookProcessUnchanged=true; actorMustBeReverified=true",
                        token
                    );
                    return xml;
                }
                await Task.Delay(250, token);
            }
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            reader.Gate.Release();
        }
    }

    private static bool HierarchyHasNodes(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants("node").Any();
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static bool ReadableFacebook(string? xml)
    {
        try
        {
            var nodes = XDocument.Parse(xml ?? "").Descendants("node").ToArray();
            return nodes.Any(n => n.Attribute("package")?.Value == "com.facebook.katana")
                && nodes.All(n =>
                    string.IsNullOrEmpty(n.Attribute("package")?.Value)
                    || n.Attribute("package")?.Value
                        is "com.facebook.katana"
                            or "com.android.systemui"
                );
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }
}
