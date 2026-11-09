using System.Reflection;
using _855Media.ViewModels.Components;

internal static class AccountInstallationCheck
{
    public static void Run()
    {
        using var vm = new AutoPostViewModel(null!, null!, null!, null!, null!);
        vm.LdFolder = @"C:\LDPlayer\LDPlayer14";
        var field = typeof(AutoPostViewModel).GetField(
            "_accountInstallationIdentities",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        field.SetValue(vm, new HashSet<string> { "fourteen" });
        var nine = new PFlowAccountItemViewModel(
            new()
            {
                Binding = new("nine", 45, "LD-45", "com.facebook.katana", "123"),
                Name = "Old account",
                SelectedForPosting = true,
            },
            "Other installation"
        );
        var fourteen = new PFlowAccountItemViewModel(
            new()
            {
                Binding = new("fourteen", 45, "LD-45", "com.facebook.katana", "456"),
                Name = "New account",
            },
            ""
        );
        vm.MappedAccounts.Add(nine);
        vm.MappedAccounts.Add(fourteen);
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        Check(
            vm.DisplayedMappedAccounts.Single() == fourteen,
            "Default table excludes other installation with same index/name"
        );
        Check(vm.LdAccountsHeading.Contains("LDPlayer14"), "Heading discloses active installation");
        vm.SelectAllMappedAccountsCommand.Execute(null);
        Check(
            fourteen.IsSelectedForBulk && !nine.IsSelectedForBulk,
            "Select visible cannot select LD9"
        );
        vm.AccountFilter = "All saved instances";
        Check(vm.DisplayedMappedAccounts.Count() == 2, "Other mappings remain reviewable");
        Check(
            !nine.IsMappingValid && nine.SelectedForPosting,
            "Other installation is invalid here and saved options preserved"
        );
        vm.AccountFilter = "All";
        vm.AccountSearchText = "Old account";
        Check(!vm.DisplayedMappedAccounts.Any(), "Search does not bypass installation filter");
        vm.AccountSearchText = "";
        vm.LdFolder = @"C:\LDPlayer\LDPlayer9";
        Check(
            !vm.DisplayedMappedAccounts.Any() && !fourteen.IsSelectedForBulk,
            "Folder change clears stale visibility and bulk selection before refresh"
        );
        Check(!vm.MappedAccounts.Any(), "Old validity removed until refresh");
        // Refresh reloads the records from the unchanged saved store.
        vm.MappedAccounts.Add(nine);
        vm.MappedAccounts.Add(fourteen);
        field.SetValue(vm, new HashSet<string> { "nine" });
        Check(
            vm.DisplayedMappedAccounts.Single() == nine,
            "Recomputed scope selects only matching installation"
        );
        Check(vm.MappedAccounts.Count == 2, "Switching scope never deletes saved mappings");
    }
}
