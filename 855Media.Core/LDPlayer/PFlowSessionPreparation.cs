using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Preparation never submits credentials, changes networks or clears app data.
public sealed class PFlowSessionPreparation(ILDPlayerService ld, string path)
{
    public string? NetworkIdentity { get; private set; }

    public void BindNetworkIdentity(string identity)
    {
        if (
            string.IsNullOrWhiteSpace(identity)
            || (NetworkIdentity != null && NetworkIdentity != identity)
        )
            throw new InvalidOperationException(
                "Login network context changed. Review the assigned LD before retrying."
            );
        NetworkIdentity = identity;
    }

    public async Task RequireConnectionAsync(PFlowAccountBinding binding, CancellationToken ct)
    {
        await RequireWorkerAsync(binding, ct);
        var connectivity = await ld.ExecuteAdbAsync(
            binding.DeviceIndex,
            "shell dumpsys connectivity",
            path,
            ct
        );
        ct.ThrowIfCancellationRequested();
        if (!LDPlayerService.IsAndroidInternetConnected(connectivity))
            throw new InvalidOperationException(
                "Internet is unavailable in this LD. Restore the connection; login was not retried."
            );
        var network = System.Text.RegularExpressions.Regex.Match(
            connectivity,
            @"Active default network:\s*(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (!network.Success)
            throw new InvalidOperationException(
                "Current network could not be verified. Login stopped."
            );
        BindNetworkIdentity(network.Groups[1].Value);
    }

    public async Task<PFlowWorkerIdentity> RequireWorkerAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        binding.Validate();
        var instance =
            (await ld.GetInstancesAsync(path, ct)).SingleOrDefault(i =>
                i.Index == binding.DeviceIndex
            )
            ?? throw new InvalidOperationException(
                "Assigned LD is missing. Review mapping; no fallback was used."
            );
        var worker = new PFlowWorkerIdentity(
            instance.Index,
            instance.Name,
            PFlowDeviceRouting.ReadIdentity(path, instance.Index),
            binding.AppPackage
        );
        binding.RequireWorker(
            worker.DeviceIndex,
            worker.DeviceName,
            worker.InstanceIdentity,
            worker.AppPackage
        );
        return worker;
    }

    public async Task<string> ReadFacebookScreenAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        var dismissed = false;
        for (var attempt = 0; attempt < 7; attempt++)
        {
            await RequireWorkerAsync(binding, ct);
            var activity =
                await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
            if (
                binding.AppPackage == "com.facebook.katana"
                && System.Text.RegularExpressions.Regex.IsMatch(
                    activity,
                    @"(?<![\w.])com\.facebook\.katana/"
                )
            )
                return await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
            if (string.IsNullOrWhiteSpace(activity))
            {
                // Logout changes activities; Android may briefly report no focused window.
                await Task.Delay(500, ct);
                continue;
            }
            if (activity != PFlowAndroidCredentialPrompt.Activity)
                throw new InvalidOperationException(
                    "Assigned Facebook app is not in the foreground."
                );
            var prompt = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct);
            var close = PFlowAndroidCredentialPrompt.CloseControl(prompt);
            if (close != null && !dismissed)
            {
                if (!await ld.TapAsync(binding.DeviceIndex, close.Value.X, close.Value.Y, path, ct))
                    throw new InvalidOperationException(
                        "Android saved-password prompt could not be dismissed; no saved credential was selected."
                    );
                dismissed = true;
            }
            await Task.Delay(500, ct);
        }
        throw new InvalidOperationException(
            "Android saved-password prompt could not be dismissed; no saved credential was selected."
        );
    }

    public async Task EnsureTextInputAsync(
        PFlowAccountBinding binding,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        await RequireWorkerAsync(binding, ct);
        if (!await ld.IsAdbKeyboardInstalledAsync(binding.DeviceIndex, path, ct))
        {
            await progress("Installing Android text input on the assigned LD.");
            if (
                !await ld.InstallAdbKeyboardAsync(
                    binding.DeviceIndex,
                    customPath: path,
                    cancellationToken: ct
                ) || !await ld.IsAdbKeyboardInstalledAsync(binding.DeviceIndex, path, ct)
            )
                throw new InvalidOperationException(
                    "ADBKeyboard installation could not be verified; no login details were entered."
                );
        }
        await RequireWorkerAsync(binding, ct);
        if (
            !await ld.EnableAdbKeyboardAsync(binding.DeviceIndex, path, ct)
            || !await ld.IsAdbKeyboardActiveAsync(binding.DeviceIndex, path, ct)
        )
            throw new InvalidOperationException(
                "Android text input could not be verified; no login details were entered."
            );
    }

    // Caller owns the device lease for the complete operation, including navigation afterward.
    public async Task PrepareAsync(
        PFlowAccountBinding binding,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        await RequireWorkerAsync(binding, ct);
        var instance = (await ld.GetInstancesAsync(path, ct)).Single(i =>
            i.Index == binding.DeviceIndex
        );
        if (!instance.IsAndroidStarted)
        {
            await progress("Starting the assigned LD instance.");
            if (!await ld.LaunchInstanceAsync(binding.DeviceIndex, path, ct))
                throw new InvalidOperationException("Assigned LD did not start.");
        }
        await progress("Waiting for Android boot.");
        if (
            !await ld.WaitForBootCompletedAsync(
                binding.DeviceIndex,
                TimeSpan.FromSeconds(90),
                path,
                ct
            )
        )
            throw new InvalidOperationException("Android boot timed out.");
        await RequireWorkerAsync(binding, ct);
        await progress("Waiting for validated internet inside this LD.");
        await PFlowSession.WaitInternetAsync(ld, binding.DeviceIndex, path, ct);
        await RequireConnectionAsync(binding, ct);
        ct.ThrowIfCancellationRequested();
        await RequireWorkerAsync(binding, ct);
        if (!await ld.EnsureAtxAgentRunningAsync(binding.DeviceIndex, path, ct))
            throw new InvalidOperationException("Device UI connection is unavailable.");
        await progress("Opening the assigned Facebook app; existing sessions are preserved.");
        if (!await ld.LaunchAppAsync(binding.DeviceIndex, binding.AppPackage, path, ct))
            throw new InvalidOperationException("Assigned Facebook app did not open.");
        await RequireWorkerAsync(binding, ct);
    }
}

public static class PFlowAccountSelection
{
    // Legacy callers without an automatic session navigator must still reject multiple logins.
    public static void RequireSupportedSingleLogin(
        System.Collections.Generic.IEnumerable<PFlowPersonalAccount> accounts,
        int index,
        string package,
        System.Collections.Generic.IEnumerable<string> profileUids
    )
    {
        var owners = accounts
            .Where(a => a.Binding.DeviceIndex == index && a.Binding.AppPackage == package)
            .Select(a => a.Binding.OwnerUid)
            .Concat(profileUids)
            .Where(uid => !string.IsNullOrWhiteSpace(uid))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (owners.Length > 1)
            throw new NotSupportedException(
                "This LD has multiple personal account mappings. Saved-account switching and exact live UID verification are required before posting or ScreenTime can run."
            );
    }

    public static PFlowPersonalAccount[] ForScreenTime(
        System.Collections.Generic.IEnumerable<PFlowPersonalAccount> accounts,
        int index,
        string package
    )
    {
        var selected = accounts
            .Where(a =>
                a.SelectedForScreenTime
                && a.Binding.DeviceIndex == index
                && a.Binding.AppPackage == package
            )
            .ToArray();
        foreach (var account in selected)
            account.Binding.Validate();
        if (selected.GroupBy(a => a.Binding.OwnerUid).Any(g => g.Count() != 1))
            throw new InvalidOperationException(
                "Duplicate personal account mapping. Review accounts before ScreenTime."
            );
        return selected.OrderBy(a => a.Binding.OwnerUid, StringComparer.Ordinal).ToArray();
    }
}
