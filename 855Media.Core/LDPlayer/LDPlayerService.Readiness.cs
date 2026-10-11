using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    public async Task<bool> WaitForFacebookReadyAsync(
        int index,
        TimeSpan timeout,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<string, Task>? progress = null
    )
    {
        if (progress != null)
            await progress("Preparing Android automation and checking internet connectivity.");
        await EnsureAtxAgentRunningAsync(index, customPath, cancellationToken);
        var timer = Stopwatch.StartNew();
        var nextUpdate = TimeSpan.Zero;
        while (timer.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectivity = await ExecuteAdbAsync(
                index,
                "shell dumpsys connectivity",
                customPath,
                cancellationToken
            );
            if (IsAndroidInternetConnected(connectivity))
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "internet.connected",
                    "Android reports a connected network with validated internet access.",
                    cancellationToken
                );
                if (progress != null)
                    await progress("Internet connected inside LD; starting account discovery.");
                return true;
            }
            if (timer.Elapsed >= nextUpdate)
            {
                if (progress != null)
                    await progress(
                        $"Waiting for internet inside LD ({(int)timer.Elapsed.TotalSeconds}s/{(int)timeout.TotalSeconds}s)."
                    );
                nextUpdate = timer.Elapsed + TimeSpan.FromSeconds(10);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        await PFlowFetchTrace.WriteAsync(
            index,
            "internet.timeout",
            "Android did not report validated internet access; Facebook fetch was skipped.",
            cancellationToken
        );
        if (progress != null)
            await progress("Internet connection timed out; Facebook fetch stopped.");
        return false;
    }

    public static bool IsAndroidInternetConnected(string? connectivity)
    {
        if (string.IsNullOrWhiteSpace(connectivity))
            return false;
        var normalized = connectivity.Replace("\r", string.Empty);
        var header = Regex.Match(
            normalized,
            @"^[ \t]*Current Networks:[ \t]*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase
        );
        if (!header.Success)
            return false;
        var activeDefault = Regex.Match(
            normalized,
            @"Active default network:\s*(\d+)",
            RegexOptions.IgnoreCase
        );
        if (!activeDefault.Success)
            return false;
        var currentNetworks = normalized[(header.Index + header.Length)..];
        var nextSection = Regex.Match(currentNetworks, @"^\S", RegexOptions.Multiline);
        if (nextSection.Success)
            currentNetworks = currentNetworks[..nextSection.Index];
        foreach (
            Match agent in Regex.Matches(
                currentNetworks,
                @"^[ \t]+NetworkAgentInfo\{",
                RegexOptions.Multiline
            )
        )
        {
            var block = ReadConnectivityAgentRecord(
                currentNetworks,
                agent.Index + agent.Value.IndexOf('{')
            );
            if (
                block == null
                || !Regex.IsMatch(
                    block,
                    @"\bnetwork\{\s*" + Regex.Escape(activeDefault.Groups[1].Value) + @"\s*\}"
                )
            )
                continue;
            var networkInfo = Regex.Match(block, @"\bni\{\[(.*?)\]\}", RegexOptions.Singleline);
            var capabilities = Regex.Match(block, @"\bnc\{\[(.*?)\]\}", RegexOptions.Singleline);
            if (!networkInfo.Success || !capabilities.Success)
                continue;
            var flags = Regex.Match(capabilities.Groups[1].Value, @"\bCapabilities:\s*([A-Z_&|]+)");
            if (
                Regex.IsMatch(networkInfo.Groups[1].Value, @"\bstate:\s*CONNECTED/CONNECTED\b")
                && flags.Success
                && Regex.IsMatch(flags.Groups[1].Value, @"(?:^|[&|])INTERNET(?:[&|]|$)")
                && Regex.IsMatch(flags.Groups[1].Value, @"(?:^|[&|])VALIDATED(?:[&|]|$)")
            )
                return true;
        }
        return false;
    }

    public static bool IsSurfsharkVpnConnected(string? connectivity, int surfsharkUid)
    {
        if (string.IsNullOrWhiteSpace(connectivity) || surfsharkUid <= 0)
            return false;

        // Only the current-network section establishes readiness. Later sections
        // can contain previously connected agents or historical VPN requests.
        var normalized = connectivity.Replace("\r", string.Empty);
        var header = Regex.Match(
            normalized,
            @"^[ \t]*Current Networks:[ \t]*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase
        );
        if (!header.Success)
            return false;

        var currentNetworks = normalized[(header.Index + header.Length)..];
        var nextSection = Regex.Match(currentNetworks, @"^\S", RegexOptions.Multiline);
        if (nextSection.Success)
            currentNetworks = currentNetworks[..nextSection.Index];

        foreach (
            Match agent in Regex.Matches(
                currentNetworks,
                @"^[ \t]+NetworkAgentInfo\{",
                RegexOptions.Multiline
            )
        )
        {
            var block = ReadConnectivityAgentRecord(
                currentNetworks,
                agent.Index + agent.Value.IndexOf('{')
            );
            if (block == null)
                continue;
            var networkInfo = Regex.Match(block, @"\bni\{\[(.*?)\]\}", RegexOptions.Singleline);
            var capabilities = Regex.Match(block, @"\bnc\{\[(.*?)\]\}", RegexOptions.Singleline);
            if (!networkInfo.Success || !capabilities.Success)
                continue;

            var text = capabilities.Groups[1].Value;
            var owner = Regex.Match(text, @"\b(?:EstablishingAppUid|OwnerUid):\s*(\d+)");
            var capabilityFlags = Regex.Match(text, @"\bCapabilities:\s*([A-Z_&|]+)");
            if (
                Regex.IsMatch(networkInfo.Groups[1].Value, @"\bstate:\s*CONNECTED/CONNECTED\b")
                && Regex.IsMatch(text, @"Transports:\s*VPN\b")
                && capabilityFlags.Success
                && Regex.IsMatch(capabilityFlags.Groups[1].Value, @"(?:^|[&|])VALIDATED(?:[&|]|$)")
                && owner.Success
                && int.TryParse(owner.Groups[1].Value, out var ownerUid)
                && ownerUid == surfsharkUid
            )
                return true;
        }
        return false;
    }

    private static string? ReadConnectivityAgentRecord(string text, int openingBrace)
    {
        var depth = 0;
        for (var position = openingBrace; position < text.Length; position++)
        {
            if (text[position] == '{')
                depth++;
            else if (text[position] == '}' && --depth == 0)
                return text[openingBrace..(position + 1)];
        }
        return null;
    }
}
