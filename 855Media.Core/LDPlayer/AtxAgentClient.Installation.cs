using System;
using System.Collections.Concurrent;

namespace _855Media.Core.LDPlayer;

public partial class AtxAgentClient
{
    private readonly ConcurrentDictionary<string, AtxAgentClient> _installations = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<int, int> _installationPorts = new();
    private string? _installationPath;
    private bool _sharedHttp;
    private readonly string? _routingDirectory;

    public AtxAgentClient ForInstallation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return this;
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = PFlowInstallationRouting.Normalize(path);
        if (key == _installationPath)
            return this;
        var child = _installations.GetOrAdd(
            key,
            p =>
            {
                var client = new AtxAgentClient(_basePort, _httpClient, _routingDirectory)
                {
                    _installationPath = p,
                    _sharedHttp = true,
                };
                client.TapObserved += e => TapObserved?.Invoke(e);
                return client;
            }
        );
        child.IsTapTracingEnabled = IsTapTracingEnabled;
        return child;
    }
}
