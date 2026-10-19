using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace _855Media.Core.LDPlayer;

public enum PFlowAuthenticationState
{
    Cached,
    Verifying,
    Authenticated,
    LoginRequired,
    Checkpoint,
    Unavailable,
    VerificationFailed,
}

public enum PFlowPageAccessState
{
    Cached,
    Verifying,
    Available,
    NotInConfirmedSwitcher,
    SwitchFailed,
    VerificationFailed,
}

// Immutable routing snapshot. Owner UID is never used to find a different emulator.
public sealed record PFlowAccountBinding(
    string InstanceIdentity,
    int DeviceIndex,
    string DeviceName,
    string AppPackage,
    string OwnerUid
)
{
    public string AccountKey =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        new[]
                        {
                            InstanceIdentity,
                            DeviceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            DeviceName,
                            AppPackage,
                            OwnerUid,
                        }
                    )
                )
            )
        );

    public void Validate()
    {
        if (
            DeviceIndex < 0
            || string.IsNullOrWhiteSpace(InstanceIdentity)
            || string.IsNullOrWhiteSpace(DeviceName)
            || string.IsNullOrWhiteSpace(AppPackage)
            || string.IsNullOrWhiteSpace(OwnerUid)
            || !OwnerUid.All(char.IsAsciiDigit)
        )
            throw new InvalidOperationException(
                "An explicit instance, app package and personal UID mapping is required."
            );
    }

    public void RequireWorker(int index, string name, string identity, string package)
    {
        Validate();
        if (
            index != DeviceIndex
            || name != DeviceName
            || identity != InstanceIdentity
            || package != AppPackage
        )
            throw new InvalidOperationException(
                "Account binding mismatch. Review its assigned LD instance and app; no fallback was used."
            );
    }
}

public sealed class PFlowPersonalAccount
{
    public PFlowAccountBinding Binding { get; set; } = new("", -1, "", "", "");
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool InstanceLocked { get; set; }
    public string AccountDate { get; set; } = ""; // Imported metadata (ISO date), not a verified Facebook join date.
    public bool SelectedForDiscovery { get; set; }
    public bool SelectedForScreenTime { get; set; }
    public bool SelectedForPosting { get; set; }
    public PFlowAuthenticationState Authentication { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public string CredentialReference { get; set; } = "";
    public bool AllowAutomaticLogin { get; set; }
    public bool AllowAccountRotation { get; set; }
    public int RotationOrder { get; set; }
    public List<PFlowPageAccess> Pages { get; set; } = [];

    // Only explicit same-index remaps create these entries; used to recover Page rows after restart.
    public List<PFlowAccountBinding> PreviousBindings { get; set; } = [];
    public List<PFlowAccountBinding> TransferredFromBindings { get; set; } = [];
}

public sealed class PFlowPageAccess
{
    public string PageId { get; set; } = "";
    public string PersonaUid { get; set; } = "";
    public string Name { get; set; } = "";
    public PFlowPageAccessState State { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public string VerifiedRunId { get; set; } = "";
}

public static class PFlowDeviceRouting
{
    public static int Resolve(string name, IEnumerable<LDInstanceInfo> instances)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;
        var matches = instances
            .Where(i =>
                i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || i.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) == name
            )
            .ToArray();
        if (matches.Length == 1)
            return matches[0].Index;
        if (matches.Length > 1)
            return -1;
        // Legacy LD-N labels resolve only to an existing instance, never an invented index.
        if (
            name.StartsWith("LD-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[3..], out var index)
        )
            return instances.Count(i => i.Index == index) == 1 ? index : -1;
        return -1;
    }

    public static string ReadIdentity(string ldDirectory, int index)
    {
        if (index < 0)
            throw new InvalidOperationException("Unknown LD instance.");
        var root = Path.GetFullPath(ldDirectory);
        var config = Path.Combine(root, "vms", "config", $"leidian{index}.config");
        var disk = Path.Combine(root, "vms", $"leidian{index}", "data.vmdk");
        using var json = JsonDocument.Parse(File.ReadAllText(config));
        if (
            !json.RootElement.TryGetProperty("propertySettings.phoneAndroidId", out var androidId)
            || string.IsNullOrWhiteSpace(androidId.GetString())
            || !File.Exists(disk)
        )
            throw new InvalidOperationException(
                "LD instance identity is unavailable. Review the instance mapping."
            );
        // LD's vbox UUID is index-derived on this installation. Include configured Android
        // identity and data-disk creation time to detect replacement, without exposing either.
        var identity = JsonSerializer.Serialize(
            new[]
            {
                root.ToUpperInvariant(),
                androidId.GetString(),
                File.GetCreationTimeUtc(disk)
                    .Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }
        );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
