using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

// Actual desktop preparation and ScreenTime callback routing, with synthetic
// devices/activity and a test-only subscription service. Never operates an LD.
internal static class ScreenTimeNameCheck
{
    internal static Task RunAsync()
    {
        var dispatcher = Dispatcher.UIThread;
        var work = Task.Run(CheckAsync);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = work.ContinueWith(_ => deadline.Cancel(), TaskScheduler.Default);
        dispatcher.MainLoop(deadline.Token);
        if (!work.IsCompleted)
            throw new TimeoutException("Synthetic ScreenTime VM check timed out");
        work.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    private static async Task CheckAsync()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var activity = new Activity();
        using var vm = new AutoPostViewModel(
            ld,
            null!,
            null!,
            null!,
            null!,
            screenTimeService: activity
        );
        vm.Pages.Clear();
        vm.LdFolder = Path.GetFullPath("artifacts/screentime-name-session/ld-fixture");
        Directory.CreateDirectory(Path.Combine(vm.LdFolder, "vms/config"));
        Directory.CreateDirectory(Path.Combine(vm.LdFolder, "vms/leidian900756"));
        File.WriteAllText(
            Path.Combine(vm.LdFolder, "vms/config/leidian900756.config"),
            "{\"propertySettings.phoneAndroidId\":\"synthetic-name-check\"}"
        );
        File.WriteAllText(
            Path.Combine(vm.LdFolder, "vms/leidian900756/data.vmdk"),
            "synthetic fixture"
        );
        typeof(ViewModelBase)
            .GetProperty("ModuleLicense", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, DispatchProxy.Create<ILicenseService, TestLicense>());
        var binding = new PFlowAccountBinding(
            PFlowDeviceRouting.ReadIdentity(vm.LdFolder, 900756),
            900756,
            "LD-900756",
            "com.facebook.katana",
            "100000000000001"
        );
        fake.Binding = binding;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var doc = XDocument.Parse(
            File.ReadAllText(
                    Path.Combine(
                        root,
                        "docs/fixtures/pflow-screentime/ld56-personal-dashboard-captured.xml"
                    )
                )
                .Replace("Fixture Personal", "Personal Owner")
        );
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Personal Owner, see your profile"),
                new XAttribute("bounds", "[48,108][405,188]")
            )
        );
        fake.Xml = doc.ToString();
        var account = new PFlowPersonalAccount
        {
            Binding = binding,
            Name = "Personal Owner",
            SelectedForScreenTime = true,
        };
        typeof(AutoPostViewModel)
            .GetField("_pFlowRunAccounts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new[] { account });
        vm.Pages.Add(
            new(
                new FacebookPageTarget
                {
                    Type = "Profile",
                    DeviceName = binding.DeviceName,
                    PageName = account.Name,
                    PageId = binding.OwnerUid,
                    UserUid = binding.OwnerUid,
                    AccountBinding = binding,
                },
                1
            )
        );
        var worker = new LDInstanceItemViewModel(
            new LDInstanceInfo
            {
                Index = binding.DeviceIndex,
                Name = binding.DeviceName,
                IsAndroidStarted = true,
            },
            ld
        );
        vm.Instances.Add(worker);
        var ensure = typeof(AutoPostViewModel).GetMethod(
            "EnsureRunAccountAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Console.WriteLine("Checking synthetic ScreenTime account preparation");
        await (Task)ensure.Invoke(vm, [worker, account, CancellationToken.None, true])!;
        Console.WriteLine("Checking actual ScreenTime VM callbacks");
        var run = typeof(AutoPostViewModel).GetMethod(
            "RunScreenTimeAccountAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)
            run.Invoke(
                vm,
                [worker, new ScreenTimeSetting { NewsFeed = true }, account, CancellationToken.None]
            )!;
        if (activity.Checks != 5 || fake.ForbiddenReads != 0)
            throw new Exception(
                $"Wrong ScreenTime route: checks={activity.Checks}, forbidden reads={fake.ForbiddenReads}"
            );
        Console.WriteLine(
            "PASS actual VM name-policy account preparation + five ScreenTime identity callbacks; Graph/UID/authentication reads=0; no live activity."
        );
    }

    public class TestLicense : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "IsFeatureAllowed"
                ? true
                : throw new NotSupportedException(method.Name);
    }

    private class Activity : IScreenTimeService
    {
        public int Checks;

        public async Task<ScreenTimeResult> RunAsync(
            int index,
            ScreenTimeSetting settings,
            Func<CancellationToken, Task<bool>> verifyPersonalIdentity,
            Func<ScreenTimeProgress, Task> progress,
            string? path = null,
            CancellationToken ct = default,
            bool deviceLeaseAlreadyHeld = false,
            Func<ScreenTimeCommentDraft, CancellationToken, Task<bool>>? reviewComment = null
        )
        {
            for (var i = 0; i < 5; i++)
            {
                if (!await verifyPersonalIdentity(ct))
                    throw new Exception("Personal identity failed");
                Checks++;
            }
            return new(0, 0, 0);
        }
    }

    public class Device : DispatchProxy
    {
        public PFlowAccountBinding Binding = null!;
        public string Xml = "";
        public int ForbiddenReads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetInstancesAsync":
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                        new()
                        {
                            Index = Binding.DeviceIndex,
                            Name = Binding.DeviceName,
                            IsAndroidStarted = true,
                        },
                    ]);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(Binding.AppPackage + "/Main");
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml);
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml)
                    );
                case "ExecuteAdbAsync":
                    var cmd = (string)args![1]!;
                    if (cmd == "shell pidof com.facebook.katana")
                        return Task.FromResult("123");
                    if (cmd == "shell dumpsys connectivity")
                        return Task.FromResult(
                            "Active default network: 134\nCurrent Networks:\n  NetworkAgentInfo{ network{134} ni{[type: WIFI[], state: CONNECTED/CONNECTED]} nc{[ Transports: WIFI Capabilities: INTERNET&VALIDATED]} }"
                        );
                    ForbiddenReads++;
                    throw new Exception("ScreenTime must not read Graph, tokens or live UID");
                case "LaunchAppAsync":
                case "WaitForBootCompletedAsync":
                case "EnsureAtxAgentRunningAsync":
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
