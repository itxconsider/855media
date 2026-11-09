using System.Collections.Concurrent;
using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

internal static class PostingUploadWaitCheck
{
    internal static Task RunAsync()
    {
        var work = Task.Run(CheckAsync);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = work.ContinueWith(_ => deadline.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(deadline.Token);
        if (!work.IsCompleted)
            throw new TimeoutException("Posting upload wait desktop check timed out.");
        work.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    static async Task CheckAsync()
    {
        var folder = Path.GetFullPath(
            "artifacts/ld61-previous-upload/desktop-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path.Combine(folder, "vms/config"));
        Directory.CreateDirectory(Path.Combine(folder, "vms/leidian900761"));
        await File.WriteAllTextAsync(
            Path.Combine(folder, "vms/config/leidian900761.config"),
            "{\"propertySettings.phoneAndroidId\":\"upload-wait-fixture\"}"
        );
        await File.WriteAllTextAsync(
            Path.Combine(folder, "vms/leidian900761/data.vmdk"),
            "fixture"
        );
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var binding = new PFlowAccountBinding(
            PFlowDeviceRouting.ReadIdentity(folder, 900761),
            900761,
            "LD-900761",
            "com.facebook.katana",
            "100000000000001"
        );
        var target = new FacebookPageTarget
        {
            AccountBinding = binding,
            DeviceName = binding.DeviceName,
            PageId = "200000000000001",
            PersonaUid = "300000000000001",
            UserUid = binding.OwnerUid,
            PageName = "Fixture Page",
            Type = "Page",
        };
        var history = new PFlowHistoryStore(Path.Combine(folder, "history.db"));
        using var vm = new AutoPostViewModel(
            ld,
            null!,
            null!,
            null!,
            null!,
            accountStore: new PFlowAccountStore(Path.Combine(folder, "accounts.json")),
            pagesStoragePath: Path.Combine(folder, "pages.json"),
            historyStore: history,
            dispatchQueueStore: new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db"))
        );
        vm.Pages.Clear();
        vm.Pages.Add(new(target, 1));
        vm.LdFolder = folder;
        var worker = new LDInstanceItemViewModel(
            new LDInstanceInfo
            {
                Index = 900761,
                Name = binding.DeviceName,
                IsAndroidStarted = true,
            },
            ld,
            folder
        );
        var messages = new ConcurrentQueue<string>();
        worker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Status")
                messages.Enqueue(worker.Status);
        };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        await (Task)
            typeof(AutoPostViewModel)
                .GetMethod("CheckPostProcessAsync", flags)!
                .Invoke(vm, [binding.DeviceIndex, CancellationToken.None])!;
        var method = typeof(AutoPostViewModel).GetMethod("VerifyPostingTargetNameAsync", flags)!;
        var account = new PFlowPersonalAccount { Binding = binding, Name = "Owner" };
        PFlowJob Job() =>
            new()
            {
                JobId = Guid.NewGuid().ToString("N"),
                BatchId = "fixture-batch",
                DeviceIndex = binding.DeviceIndex,
                AppPackage = binding.AppPackage,
                AccountBinding = binding,
                OwnerUid = binding.OwnerUid,
                TargetId = target.PageId,
                PersonaUid = target.PersonaUid,
                TargetName = target.PageName,
                PostType = "Reel",
                State = PFlowState.Reserved,
            };
        var first = Job();
        if (
            !await (Task<bool>)
                method.Invoke(vm, [worker, target, account, first, null, CancellationToken.None])!
        )
            throw new Exception("Initial actor proof failed.");
        first.State = PFlowState.Confirmed;
        fake.PendingUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        var second = Job();
        if (
            !await (Task<bool>)
                method.Invoke(vm, [worker, target, account, second, null, CancellationToken.None])!
        )
            throw new Exception("Next actor proof failed.");
        if (
            !messages.Any(m => m.StartsWith("Waiting for Facebook"))
            || !messages.Any(m => m.StartsWith("Previous upload cleared"))
            || second.State != PFlowState.Reserved
            || second.SubmittedUtc != null
            || second.NativeIdentityScope == null
            || ReferenceEquals(first.NativeIdentityScope, second.NativeIdentityScope)
            || fake.Forbidden != 0
        )
            throw new Exception(
                "Reused session bypassed upload wait/status or attempted device work."
            );
        second.Sha256 = new string('A', 64);
        if (!await history.ReserveAsync(second))
            throw new Exception("Synthetic previous post reservation failed.");
        second.State = PFlowState.Confirmed;
        second.SubmittedUtc = DateTime.UtcNow.AddSeconds(-5);
        second.ConfirmedUtc = DateTime.UtcNow;
        second.PublicationStage = PFlowPublicationStage.Published;
        second.Evidence = "Observed publication states completed";
        await history.SaveAsync(second);
        fake.CopyrightScan = true;
        var third = Job();
        Console.WriteLine(
            $"SCAN preflight: xml={PFlowBackgroundCopyright.IsOnlyBackgroundScan(await ld.DumpUiHierarchyAsync(binding.DeviceIndex, folder) ?? "")}; proof={PFlowBackgroundCopyright.Create(third, second, second.NativeIdentityScope!) != null}; records={string.Join(",", (await history.ReadAsync()).Select(j => $"{j.State}/{j.SubmittedUtc.HasValue}"))}"
        );

        if (
            !await (Task<bool>)
                method.Invoke(vm, [worker, target, account, third, null, CancellationToken.None])!
            || third.BackgroundCopyright == null
            || third.NativeIdentityScope == null
            || third.SubmittedUtc != null
            || fake.Forbidden != 0
        )
            throw new Exception(
                "Confirmed background scan blocked next job or lost its actor proof."
            );
        fake.CopyrightScan = false;
        fake.CopyrightAfterReads = 1;
        var fourth = Job();
        if (
            !await (Task<bool>)
                method.Invoke(vm, [worker, target, account, fourth, null, CancellationToken.None])!
            || fourth.BackgroundCopyright == null
            || fourth.NativeIdentityScope == null
            || fourth.SubmittedUtc != null
            || fake.Forbidden != 0
        )
            throw new Exception("Late-appearing confirmed copyright scan lost continuation proof.");
        fake.CopyrightAfterReads = -1;
        fourth.State = PFlowState.Confirmed;
        fourth.ConfirmedUtc = DateTime.UtcNow;
        fourth.Evidence = "Observed publication states completed";
        fake.Pids = "456 123";
        await (Task)
            typeof(AutoPostViewModel)
                .GetMethod("CheckPostProcessAsync", flags)!
                .Invoke(vm, [binding.DeviceIndex, CancellationToken.None])!;
        fake.CopyrightScan = false;
        fake.PendingUntil = DateTimeOffset.UtcNow.AddSeconds(1);
        var fifth = Job();
        var messageCount = messages.Count;
        if (
            !await (Task<bool>)
                method.Invoke(vm, [worker, target, account, fifth, null, CancellationToken.None])!
            || !ReferenceEquals(fourth.NativeIdentityScope, fifth.NativeIdentityScope)
            || fifth.SubmittedUtc != null
            || !messages.Skip(messageCount).Contains("Checking current Page session…")
            || fake.Forbidden != 0
        )
            throw new Exception(
                "Confirmed same-target session was discarded by a transient upload label."
            );
        fake.Pids = "789";
        await (Task)
            typeof(AutoPostViewModel)
                .GetMethod("CheckPostProcessAsync", flags)!
                .Invoke(vm, [binding.DeviceIndex, CancellationToken.None])!;
        var scopes =
            (ConcurrentDictionary<int, PFlowNativeIdentityScope>)
                typeof(AutoPostViewModel).GetField("_nativeIdentityScopes", flags)!.GetValue(vm)!;
        if (scopes.ContainsKey(binding.DeviceIndex))
            throw new Exception("A changed process retained actor proof.");
        fake.Pids = "error: offline";
        await (Task)
            typeof(AutoPostViewModel)
                .GetMethod("CheckPostProcessAsync", flags)!
                .Invoke(vm, [binding.DeviceIndex, CancellationToken.None])!;
        if (scopes.ContainsKey(binding.DeviceIndex))
            throw new Exception("Invalid process output retained actor proof.");
        await File.WriteAllLinesAsync(Path.Combine(folder, "status.txt"), messages);
        Console.WriteLine(
            "PASS actual desktop confirmed same-target upload wait retains the original scope; background scan and reused-session guards pass; zero taps/restarts/submissions/Graph reads; isolated stores."
        );
    }

    public class Device : DispatchProxy
    {
        public DateTimeOffset PendingUntil;
        public bool CopyrightScan;
        public int CopyrightAfterReads = -1;
        public int Forbidden;
        public string Pids = "123 456";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    if (CopyrightAfterReads >= 0 && CopyrightAfterReads-- == 0)
                        CopyrightScan = true;
                    return Task.FromResult<string?>(
                        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node package='com.facebook.katana' content-desc='Fixture Page, see your profile' bounds='[48,108][405,188]'/>"
                            + (
                                DateTimeOffset.UtcNow < PendingUntil
                                    ? "<node package='com.facebook.katana' content-desc='Finishing reel upload. Keep Facebook open.'/ >".Replace(
                                        "/ >",
                                        "/>"
                                    )
                                    : ""
                            )
                            + (
                                CopyrightScan
                                    ? "<node package='com.facebook.katana' content-desc='Finishing reel upload. Keep Facebook open.'><node package='com.facebook.katana' text='Running copyright scan...'/></node>"
                                    : ""
                            )
                            + "</node></hierarchy>"
                    );
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "GetInstancesAsync":
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                        new()
                        {
                            Index = 900761,
                            Name = "LD-900761",
                            IsAndroidStarted = true,
                        },
                    ]);
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult(Pids);
                    if ((string)args![1]! == "shell dumpsys notification --noredact")
                        return Task.FromResult(
                            "Notification List:\n mArchive=Archive (0 notifications)"
                        );
                    break;
            }
            Forbidden++;
            throw new Exception(
                "No navigation/restart/publication/Graph/private read allowed: " + method.Name
            );
        }
    }
}
