using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;

// Actual desktop VM routing, synthetic UI/device only. No real LD, login or publication.
internal static class PostingNameCheck
{
    internal static async Task RunAsync()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        using var vm = new AutoPostViewModel(ld, null!, null!, null!, null!);
        vm.Pages.Clear();
        var binding = new PFlowAccountBinding(
            "fixture",
            900755,
            "fixture-LD",
            "com.facebook.katana",
            "100000000000001"
        );
        var account = new PFlowPersonalAccount
        {
            Binding = binding,
            Name = "Personal Owner",
            SelectedForPosting = true,
        };
        typeof(AutoPostViewModel)
            .GetField("_pFlowRunAccounts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new[] { account });
        var worker = new LDInstanceItemViewModel(
            new LDInstanceInfo { Index = binding.DeviceIndex, Name = binding.DeviceName },
            ld
        );
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        fake.Root = root;
        fake.Xml = File.ReadAllText(
                Path.Combine(
                    root,
                    "docs/fixtures/pflow-screentime/ld55-switcher-selected-page-captured.xml"
                )
            )
            .Replace("Bushcraft improvisation", "Z Outdoors+");
        var ownerMethod = typeof(AutoPostViewModel).GetMethod(
            "EnsurePostingAccountNameAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)
            ownerMethod.Invoke(
                vm,
                [
                    worker,
                    account,
                    (Func<string, Task>)(_ => Task.CompletedTask),
                    CancellationToken.None,
                ]
            )!;
        Require(
            fake.UnexpectedReads == 0 && fake.Switches == 0,
            "Account startup preserves active Page and performs no UID/Graph reads"
        );
        var method = typeof(AutoPostViewModel).GetMethod(
            "VerifyLiveTargetUidAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var total = 0;
        foreach (
            var (name, id) in new[]
            {
                ("Z Outdoors+", "100000000000002"),
                ("Other Page", "100000000000003"),
            }
        )
        {
            var target = new FacebookPageTarget
            {
                PageName = name,
                PageId = id,
                UserUid = binding.OwnerUid,
                Type = "Page",
                AccountBinding = binding,
            };
            var row = new FacebookPageTargetViewModel(target, vm.Pages.Count + 1);
            vm.Pages.Add(row);
            for (var i = 0; i < 10; i++)
            {
                var job = new PFlowJob
                {
                    BatchId = "vm-name-check",
                    DeviceIndex = binding.DeviceIndex,
                    AccountBinding = binding,
                    TargetId = id,
                    TargetName = name,
                    OwnerUid = binding.OwnerUid,
                    PostType =
                        i % 3 == 0 ? "Story"
                        : i % 3 == 1 ? "Picture"
                        : "Reel",
                };
                var verified = await (Task<bool>)
                    method.Invoke(
                        vm,
                        [worker, row, target, account, CancellationToken.None, job, fake.Xml]
                    )!;
                Require(
                    verified && job.NativeIdentityScope is { UsesDatabaseName: true },
                    "Desktop posting selects the explicit name policy"
                );
                Require(
                    await job.NativeIdentityScope!.VerifyAsync(ld, null, default),
                    "Publication identity accepts the name proof"
                );
                job.State = PFlowState.Confirmed;
                job.ConfirmedUtc = DateTime.UtcNow;
                job.Evidence = "synthetic confirmation";
                total++;
            }
        }
        Require(
            fake.UnexpectedReads == 0 && fake.Switches == 2,
            "Two target activations and twenty repeats/publication checks use no UID/Graph reads"
        );
        Console.WriteLine(
            $"PASS: actual desktop VM account startup + {total} Reel/Photo/Story identity checks on two Pages; switches={fake.Switches}; Graph/UID/auth reads={fake.UnexpectedReads}; no publication."
        );
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    public class Device : DispatchProxy
    {
        public string Root = "",
            Xml = "";
        public int UnexpectedReads,
            Switches;

        private string Profile(string name) =>
            File.ReadAllText(
                    Path.Combine(
                        Root,
                        "docs/fixtures/pflow-layouts/ld56-page-business-dashboard.xml"
                    )
                )
                .Replace("Z Outdoors+", name);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (args?.LastOrDefault() is CancellationToken ct)
                ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml)
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/ProfileActivity");
                case "PressBackAsync":
                    Xml = Profile("Z Outdoors+");
                    return Task.FromResult(true);
                case "SwitchToPersonaAsync":
                    Switches++;
                    Xml = Profile(((FacebookPageTarget)args![1]!).PageName);
                    return Task.FromResult(true);
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult("123");
                    UnexpectedReads++;
                    throw new Exception(
                        "Posting must not read tokens, Graph identity or UID settings"
                    );
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
