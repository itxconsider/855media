using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

// Runs the actual desktop fetch routing/merge/persistence against temporary stores
// and a synthetic root-denied LD. No real emulator, account store or Page file is used.
internal static class NativeFetchCheck
{
    static void RequirePlainStatus(string status)
    {
        if (
            new[] { "root", "Graph", "native", "UID source" }.Any(term =>
                status.Contains(term, StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new Exception("Technical details leaked into Fetch status: " + status);
    }

    internal static Task RunAsync()
    {
        var work = Task.Run(CheckAsync);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _ = work.ContinueWith(_ => deadline.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(deadline.Token);
        if (!work.IsCompleted)
            throw new TimeoutException("Desktop native Fetch validation exceeded 45 seconds.");
        work.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    static async Task CheckAsync()
    {
        foreach (
            var scenarioName in new[]
            {
                "current",
                "current-vdi",
                "new",
                "new-vdi",
                "selected",
                "pending",
                "single",
                "single-vdi",
                "single-missing",
                "single-denied",
                "single-unbound",
                "single-pending",
                "single-unbound-missing",
                "single-unbound-removed",
                "single-unbound-cancelled",
                "removed",
                "removed-failed",
                "removed-unresolved",
                "removed-cancelled",
                "removed-background",
                "removed-late",
                "removed-binding",
            }
        )
        {
            var scenario = scenarioName.Replace("-vdi", "");
            var selected = scenario == "selected";
            var folder = Path.GetFullPath("artifacts/native-fetch/desktop-" + scenarioName);
            Directory.CreateDirectory(Path.Combine(folder, "vms/config"));
            Directory.CreateDirectory(Path.Combine(folder, "vms/leidian900901"));
            File.WriteAllText(
                Path.Combine(folder, "ldconsole.exe"),
                "fixture only; never executed"
            );
            File.WriteAllText(
                Path.Combine(folder, "vms/config/leidian900901.config"),
                "{\"propertySettings.phoneAndroidId\":\"native-fixture\"}"
            );
            File.WriteAllText(
                Path.Combine(
                    folder,
                    "vms/leidian900901/data." + (scenarioName.EndsWith("-vdi") ? "vdi" : "vmdk")
                ),
                "fixture"
            );
            var ld = DispatchProxy.Create<ILDPlayerService, DeniedDevice>();
            var fake = (DeniedDevice)(object)ld;
            fake.Folder = folder;
            var binding = new PFlowAccountBinding(
                PFlowDeviceRouting.ReadIdentity(folder, 900901),
                900901,
                "LD-900901",
                "com.facebook.katana",
                "100000000000001"
            );
            var store = new PFlowAccountStore(Path.Combine(folder, "accounts.json"));
            await store.UpdateAsync(a =>
            {
                a.Clear();
                if (scenario == "new")
                    return;
                a.Add(
                    new()
                    {
                        Binding = binding,
                        Name = "Personal Owner",
                        SelectedForDiscovery = selected,
                    }
                );
            });
            var native = new NativeDevice();
            if (scenario.StartsWith("removed"))
            {
                await store.RemoveAsync(binding);
                native.Failure = scenario;
            }
            if (scenario == "removed-late")
                native.BeforePersonal = async () =>
                {
                    var other = binding with { OwnerUid = "999999999999999" };
                    await store.UpdateAsync(a => a.Add(new() { Binding = other }));
                    await store.RemoveAsync(other);
                };
            if (scenario == "removed-binding")
                native.BeforePersonal = () =>
                    File.WriteAllTextAsync(
                        Path.Combine(folder, "vms/config/leidian900901.config"),
                        "{\"propertySettings.phoneAndroidId\":\"changed-fixture\"}"
                    );
            var history = new PFlowHistoryStore(Path.Combine(folder, "history.db"));
            if (scenario == "pending")
            {
                var job = new PFlowJob
                {
                    DeviceIndex = 900901,
                    State = PFlowState.Queued,
                    SourcePath = "synthetic",
                    TargetId = "200000000000001",
                    PostType = "Picture",
                    Sha256 = new string('A', 64),
                };
                if (await history.ReserveAsync(job))
                {
                    job.State = PFlowState.Unknown;
                    await history.SaveAsync(job);
                }
            }
            using var vm = new AutoPostViewModel(
                ld,
                null!,
                null!,
                null!,
                null!,
                accountStore: store,
                pagesStoragePath: Path.Combine(folder, "pages.json"),
                historyStore: history,
                nativeFetchDeviceFactory: (_, _, _) => native
            );
            if (scenario != "single-denied")
                typeof(ViewModelBase)
                    .GetProperty("ModuleLicense", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(
                        vm,
                        DispatchProxy.Create<ILicenseService, ScreenTimeNameCheck.TestLicense>()
                    );
            vm.Pages.Clear();
            vm.Instances.Clear();
            vm.LdFolder = folder;
            vm.DelayOpenApp = 0;
            vm.DelayOpenEachLD = 0;
            vm.DelayAfterOpenLD = 0;
            vm.DelayBeforeCloseLD = 0;
            vm.CheckSurfsharkConnection = false;
            vm.ClearCacheFBBeforeOpen = true;
            vm.IsClearRecentApp = false;
            vm.CloseLDAfterAction = false;
            vm.AutoSortLDPlayer = false;
            var old = new FacebookPageTarget
            {
                Type = "Page",
                PageId = "200000000000001",
                PersonaUid = "300000000000001",
                UserUid = binding.OwnerUid,
                PageName = "Page One",
                DeviceName = binding.DeviceName,
                VideoReelPath = "fixture-original-source",
                ImagePath = "fixture-original-photos",
                AccountBinding = binding,
            };
            if (scenario.StartsWith("single-unbound") || scenario == "single-pending")
            {
                old.PageId = "";
                old.PersonaUid = "";
                old.FetchDetailsPending = true;
                old.ManagedBy = "Personal Owner";
                if (scenario != "single-pending")
                {
                    old.UserUid = "";
                    old.AccountBinding = null;
                }
            }
            vm.Pages.Add(
                new(
                    new FacebookPageTarget
                    {
                        Type = "Profile",
                        PageId = binding.OwnerUid,
                        UserUid = binding.OwnerUid,
                        PageName = "Personal Owner",
                        DeviceName = binding.DeviceName,
                        AccountBinding = binding,
                    },
                    1
                )
            );
            vm.Pages.Add(new(old, 2));
            if (scenario == "removed-unresolved")
                vm.Pages.Clear();
            var sibling = new FacebookPageTarget
            {
                Type = "Page",
                PageId = "200000000000002",
                PersonaUid = "300000000000002",
                UserUid = binding.OwnerUid,
                PageName = "Page Two",
                DeviceName = binding.DeviceName,
                AccountBinding = binding,
                LivePageAccessVerified = true,
                Followers = 99,
                VideoReelPath = "sibling-source",
                ImagePath = "sibling-photos",
            };
            if (scenario.StartsWith("single"))
            {
                vm.Pages.Add(new(sibling, 3));
                vm.Pages.Last().IsSelected = true;
                native.Multiple = true;
                native.MissingRequested = scenario.EndsWith("missing");
            }
            var worker = new LDInstanceItemViewModel(fake.Instance(), ld, folder);
            vm.Instances.Add(worker);
            if (scenarioName == "current")
            {
                var apply = typeof(AutoPostViewModel).GetMethod(
                    "ApplyFetchCollectedAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!;
                var revision = (await store.ReadRemovalStateAsync()).Revision;
                PFlowFetchActor[] discovered = [new("Progress Probe", "Page")];
                async Task Apply(PFlowFetchCollected update) =>
                    await (Task)
                        apply.Invoke(vm, [worker, update, revision, CancellationToken.None])!;
                await Apply(new("Personal Owner", null, discovered));
                var pending = vm.Pages.Single(p => p.PageName == "Progress Probe");
                if (
                    pending.TableStatus != "Details pending"
                    || pending.IsSelected
                    || !string.IsNullOrEmpty(pending.PageId)
                )
                    throw new Exception("Bad pending Page state");
                var storedPending = (
                    await PFlowPageStorage.LoadPagesAsync(Path.Combine(folder, "pages.json"))
                ).Single(p => p.PageName == "Progress Probe");
                if (
                    !storedPending.FetchDetailsPending
                    || storedPending.LivePageAccessVerified != false
                )
                    throw new Exception("Pending name was not saved before details");
                await Apply(new("Personal Owner", binding.OwnerUid, discovered));
                if (
                    pending.UserUid != binding.OwnerUid
                    || pending.Model.AccountBinding != binding
                    || !pending.Model.FetchDetailsPending
                    || pending.Model.LivePageAccessVerified == true
                )
                    throw new Exception(
                        "Confirmed owner was not attached to pending Page before Page details."
                    );
                var info = new FacebookPageInfo
                {
                    PageName = "Progress Probe",
                    PageId = "300000000000003",
                    PersonaUid = "300000000000003",
                    LivePageAccessVerified = false,
                };
                await Apply(new("Personal Owner", binding.OwnerUid, discovered, info));
                if (
                    pending.PersonaUid != info.PersonaUid
                    || pending.TableStatus != "Details pending"
                )
                    throw new Exception("Progressive UID did not update pending row");
                info.LivePageAccessVerified = true;
                info.FollowersKnown = true;
                info.Followers = 42;
                await Apply(new("Personal Owner", binding.OwnerUid, discovered, info));
                if (
                    pending.TableStatus != "Verified"
                    || pending.Followers != 42
                    || pending.IsSelected
                )
                    throw new Exception("Details did not complete the same Page row");
                await Dispatcher.UIThread.InvokeAsync(() => vm.Pages.Remove(pending));
                Console.WriteLine(
                    "PASS progressive names/UID/details persist on the same unselected desktop row."
                );
            }
            if (scenario == "removed-background")
            {
                await (Task)
                    typeof(AutoPostViewModel)
                        .GetMethod(
                            "LoadAccountMappingsAsync",
                            BindingFlags.NonPublic | BindingFlags.Instance
                        )!
                        .Invoke(vm, [])!;
                if (
                    vm.Pages.Count != 0
                    || (await store.LoadAsync()).Count != 0
                    || !(await store.RemovedBindingsAsync()).Contains(binding)
                    || native.PersonalReads != 0
                )
                    throw new Exception("Background mapping refresh restored removed rows.");
                Console.WriteLine(
                    "PASS background refresh keeps removed cached account/Page excluded."
                );
                continue;
            }
            if (scenario.StartsWith("single"))
            {
                if (scenario == "single-unbound-removed")
                    native.BeforePage = () => store.RemoveAsync(binding);
                if (scenario == "single-unbound-cancelled")
                    native.BeforePage = () =>
                    {
                        (
                            (CancellationTokenSource)
                                typeof(AutoPostViewModel)
                                    .GetField(
                                        "_fetchCts",
                                        BindingFlags.Instance | BindingFlags.NonPublic
                                    )!
                                    .GetValue(vm)!
                        ).Cancel();
                        return Task.CompletedTask;
                    };
                foreach (var row in vm.Pages)
                    row.SyncToModel();
                var before = JsonSerializer.Serialize(sibling);
                await vm.FetchSinglePageAsync(vm.Pages.Single(p => p.PageId == old.PageId));
                if (scenario == "single-denied")
                {
                    if (
                        fake.Forbidden != 0
                        || native.PageReads != 0
                        || native.PersonalReads != 0
                        || vm.IsBusy
                        || JsonSerializer.Serialize(sibling) != before
                    )
                        throw new Exception("Single-Page Fetch bypassed subscription access.");
                    Console.WriteLine(
                        "PASS single-Page Fetch requires active subscription before navigation."
                    );
                    continue;
                }
                var after = await PFlowPageStorage.LoadPagesAsync(
                    Path.Combine(folder, "pages.json")
                );
                var result = after.Single(p => p.PageId == old.PageId);
                File.WriteAllText(Path.Combine(folder, "sibling-before.json"), before);
                File.WriteAllText(
                    Path.Combine(folder, "sibling-after.json"),
                    JsonSerializer.Serialize(sibling)
                );
                if (
                    vm.IsBusy
                    || fake.Forbidden != 0
                    || JsonSerializer.Serialize(sibling) != before
                    || !vm.Pages.Single(p => p.PageId == sibling.PageId).IsSelected
                    || after.Single(p => p.PageId == sibling.PageId).Followers != 99
                    || result.VideoReelPath != old.VideoReelPath
                    || result.ImagePath != old.ImagePath
                    || native.PersonalReads != 0
                )
                    throw new Exception(
                        "Single-Page refresh touched siblings, sources, root, or personal details: "
                            + worker.Status
                    );
                if (scenario is "single" or "single-unbound" or "single-pending")
                {
                    RequirePlainStatus(worker.Status);
                    if (
                        native.PageReads != 1
                        || result.LivePageAccessVerified != true
                        || result.Followers != 321
                        || result.FetchDetailsPending
                        || result.AccountBinding != binding
                    )
                        throw new Exception("Single-Page refresh failed: " + worker.Status);
                }
                else if (scenario is "single-unbound-removed" or "single-unbound-cancelled")
                {
                    if (
                        native.PageReads != 1
                        || !result.FetchDetailsPending
                        || !string.IsNullOrEmpty(result.UserUid)
                        || result.AccountBinding != null
                        || result.LivePageAccessVerified == true
                        || !(
                            worker.Status.Contains("choices changed")
                            || worker.Status.Contains("cancelled")
                        )
                    )
                        throw new Exception(
                            "Late removal/cancellation committed a pending identity: "
                                + worker.Status
                        );
                }
                else if (
                    native.PageReads != 0
                    || result.LivePageAccessVerified != false
                    || !worker.Status.Contains("could not be verified")
                )
                    throw new Exception("Missing Page was substituted: " + worker.Status);
                Console.WriteLine(
                    $"PASS actual desktop {scenario} native Fetch preserves sibling data/selections and requested sources; private/Graph reads=0."
                );
                continue;
            }
            var method = typeof(AutoPostViewModel).GetMethod(
                "ProcessSingleDeviceFetchAsync",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
            var ok = false;
            try
            {
                ok = await (Task<bool>)method.Invoke(vm, [worker, false, CancellationToken.None])!;
            }
            catch (OperationCanceledException) when (scenario == "removed-cancelled") { }
            if (scenario.StartsWith("removed-") && scenario != "removed")
            {
                if (
                    ok
                    || !(await store.RemovedBindingsAsync()).Contains(binding)
                    || (await store.LoadAsync()).Any(a => a.Binding == binding)
                )
                    throw new Exception(
                        "Unsuccessful Fetch restored a removed binding: " + scenario
                    );
                Console.WriteLine("PASS " + scenario + " preserves the removal exclusion.");
                continue;
            }
            if (scenario == "pending")
            {
                if (ok || native.PageReads != 0 || !worker.Status.Contains("uncertain publication"))
                    throw new Exception("Pending publication did not block native Fetch");
                Console.WriteLine(
                    "PASS actual desktop native Fetch blocks Unknown publication before native navigation."
                );
                continue;
            }
            if (!ok)
                throw new Exception("Desktop native Fetch failed: " + worker.Status);
            RequirePlainStatus(worker.Status);
            if (scenario == "removed")
            {
                var restored = (await store.LoadAsync()).Single();
                if (
                    (await store.RemovedBindingsAsync()).Contains(binding)
                    || restored.SelectedForDiscovery
                    || restored.SelectedForScreenTime
                    || restored.SelectedForPosting
                    || restored.CredentialReference.Length > 0
                )
                    throw new Exception("Removed account did not restore with safe defaults.");
                var refresh = typeof(AutoPostViewModel).GetMethod(
                    "LoadAccountMappingsAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!;
                for (var repeat = 0; repeat < 2; repeat++)
                    await (Task)refresh.Invoke(vm, [])!;
                var persist = typeof(AutoPostViewModel).GetMethod(
                    "PersistPagesSnapshotAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!;
                await (Task)persist.Invoke(vm, [])!;
                if (vm.Pages.Count != 2 || (await store.LoadAsync()).Single().Pages.Count != 1)
                    throw new Exception("Repeated mapping refresh removed fetched rows.");
            }
            var saved = await PFlowPageStorage.LoadPagesAsync(Path.Combine(folder, "pages.json"));
            var page = saved.Single(p => !p.IsPrimaryProfile);
            if (
                page.VideoReelPath != "fixture-original-source"
                || page.ImagePath != "fixture-original-photos"
                || page.Followers != 321
                || page.LivePageAccessVerified != true
            )
                throw new Exception("Desktop fetch lost configured sources or fresh UI data");
            if (
                (await store.LoadAsync()).Single().SelectedForDiscovery
                    != (selected || scenario == "new")
                || fake.Forbidden != 0
                || native.PageReads != 1
            )
                throw new Exception(
                    "Desktop route changed account selections, used root, or duplicated fetch"
                );
            if (scenario == "new")
            {
                var created = (await store.LoadAsync()).Single();
                if (
                    !created.SelectedForDiscovery
                    || !created.SelectedForScreenTime
                    || !created.SelectedForPosting
                )
                    throw new Exception("New fetched account did not enable all three options");
                var row = vm.MappedAccounts.Single();
                if (
                    !row.SelectedForDiscovery
                    || !row.SelectedForScreenTime
                    || !row.SelectedForPosting
                )
                    throw new Exception(
                        "New account defaults were not reflected in the account table"
                    );
            }
            Console.WriteLine(
                $"PASS actual desktop native Fetch: selected={selected}, preserved sources/selections, saved native metrics, private/Graph reads=0."
            );
        }
    }

    public class DeniedDevice : DispatchProxy
    {
        public string Folder = "";
        public int Forbidden;

        public LDInstanceInfo Instance() =>
            new()
            {
                Index = 900901,
                Name = "LD-900901",
                IsAndroidStarted = true,
            };

        string Xml()
        {
            var root = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../../../..")
            );
            var xml = File.ReadAllText(
                    Path.Combine(
                        root,
                        "docs/fixtures/pflow-screentime/ld56-personal-dashboard-captured.xml"
                    )
                )
                .Replace("Fixture Personal", "Personal Owner");
            return xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' content-desc='Personal Owner, see your profile' bounds='[48,108][405,188]'/></hierarchy>"
            );
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "GetInstancesAsync":
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([Instance()]);
                case "ResolveLdConsolePath":
                    return Path.Combine(Folder, "ldconsole.exe");
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml())
                    );
                case "ExecuteAdbAsync":
                    var command = (string)args![1]!;
                    if (command.Contains("pidof"))
                        return Task.FromResult("123");
                    if (command.Contains("default_input_method"))
                        return Task.FromResult("com.android.inputmethod.latin/.LatinIME");
                    if (command.Contains("dumpsys notification"))
                        return Task.FromResult("Notification List:\n");
                    if (command.Contains("dumpsys connectivity"))
                        return Task.FromResult(
                            "Active default network: 134\nCurrent Networks:\n  NetworkAgentInfo{network{134} ni{[state: CONNECTED/CONNECTED]} nc{[Capabilities: INTERNET&VALIDATED]}}\n"
                        );
                    Forbidden++;
                    throw new Exception(
                        "Root-disabled desktop fixture rejected command: " + command
                    );
                case "WaitForFacebookReadyAsync":
                case "WaitForBootCompletedAsync":
                case "EnsureAtxAgentRunningAsync":
                case "LaunchAppAsync":
                    return Task.FromResult(true);
            }
            Forbidden++;
            throw new NotSupportedException(method.Name);
        }
    }

    sealed class NativeDevice : IPFlowNativeFetchDevice
    {
        public int PageReads;
        public int PersonalReads;
        public bool Multiple,
            MissingRequested;
        public string Failure = "";
        public Func<Task>? BeforePersonal;
        public Func<Task>? BeforePage;

        public Task<PFlowFetchActor[]> ScanAsync(CancellationToken ct) =>
            Failure == "removed-failed"
                ? Task.FromException<PFlowFetchActor[]>(
                    new InvalidOperationException("Simulated navigation failure")
                )
            : Failure == "removed-cancelled"
                ? Task.FromCanceled<PFlowFetchActor[]>(new CancellationToken(true))
            : Task.FromResult(
                new PFlowFetchActor[] { new("Personal Owner", "Profile", true) }
                    .Concat(
                        MissingRequested ? [] : new[] { new PFlowFetchActor("Page One", "Page") }
                    )
                    .Concat(Multiple ? new[] { new PFlowFetchActor("Page Two", "Page") } : [])
                    .ToArray()
            );

        public Task<bool> SelectAsync(PFlowFetchActor actor, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<string?> ReadNumericIdAsync(PFlowFetchActor actor, CancellationToken ct) =>
            Task.FromResult<string?>(
                Failure == "removed-unresolved" ? null
                : actor.Type == "Profile" ? "100000000000001"
                : "300000000000001"
            );

        public async Task<FacebookAccountDetails?> PersonalDetailsAsync(
            string id,
            string name,
            CancellationToken ct
        )
        {
            PersonalReads++;
            if (BeforePersonal != null)
                await BeforePersonal();
            return new()
            {
                Uid = id,
                Name = name,
                SwitchedToProfile = true,
                Birthdate = "July 8, 1996",
            };
        }

        public async Task<FacebookPageInfo?> PageDetailsAsync(
            FacebookPageInfo page,
            string ownerId,
            string ownerName,
            CancellationToken ct
        )
        {
            PageReads++;
            if (BeforePage != null)
                await BeforePage();
            return new()
            {
                PageId = page.PageId,
                PersonaUid = page.PersonaUid,
                PageName = page.PageName,
                Followers = 321,
                FollowersKnown = true,
            };
        }
    }
}
