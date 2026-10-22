using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Utils;
using _855Media.ViewModels.Components;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

Environment.SetEnvironmentVariable(
    "MediaTag_SETTINGS_PATH",
    Path.Combine(Path.GetTempPath(), "855-account-table-fixture.json")
);
Assembly.Load("Material.Avalonia");
Assembly.Load("Material.Avalonia.DataGrid");
AppBuilder
    .Configure<_855Media.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseDesktopWebViewSafe()
    .SetupWithoutStarting();
using var vm = new AutoPostViewModel(null!, null!, null!, null!, null!);
foreach (var index in new[] { 10, 2, 1, 23, 3 })
    vm.MappedAccounts.Add(
        new PFlowAccountItemViewModel(
            new()
            {
                Binding = new(
                    "fixture-" + index,
                    index,
                    "LD-" + index,
                    "com.facebook.katana",
                    "1000000000000" + index
                ),
                Name = "Fixture account " + index,
                SelectedForDiscovery = index == 2,
                SelectedForPosting = index == 3,
            },
            index == 23 ? "Assigned LD instance is missing; review mapping." : ""
        )
    );
void Require(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}
Require(
    vm.DisplayedMappedAccounts.Select(a => a.Account.Binding.DeviceIndex)
        .SequenceEqual(new[] { 1, 2, 3, 10, 23 }),
    "Numeric LD ordering"
);
vm.AccountSearchText = "LD-23";
Require(vm.DisplayedMappedAccounts.Single().DeviceName == "LD-23", "LD search");
vm.AccountSearchText = "fixture account 10";
Require(vm.DisplayedMappedAccounts.Single().DeviceName == "LD-10", "Name search");
vm.AccountSearchText = "100000000000023";
Require(vm.DisplayedMappedAccounts.Single().DeviceName == "LD-23", "UID search");
vm.AccountSearchText = "";
vm.AccountFilter = "Needs attention";
Require(vm.DisplayedMappedAccounts.Single().DeviceName == "LD-23", "Attention filter");
vm.AccountFilter = "Fetch";
Require(vm.DisplayedMappedAccounts.Single().DeviceName == "LD-2", "Fetch filter");
vm.SelectAllMappedAccountsCommand.Execute(null);
vm.ApplySelectedAccountOptionCommand.Execute("Fetch:off");
Require(
    !vm.DisplayedMappedAccounts.Any()
        && vm.SelectedBulkAccountCount == 1
        && vm.CanApplyAccountBulkSelection,
    "Filtered-out edits remain selected and saveable"
);
vm.AccountFilter = "Posting";
Require(vm.DisplayedMappedAccounts.Single().DeviceName == "LD-3", "Posting filter");
vm.AccountFilter = "ScreenTime";
Require(!vm.DisplayedMappedAccounts.Any(), "Empty filter");
vm.AccountFilter = "All";
vm.ClearMappedAccountSelectionCommand.Execute(null);
vm.AccountSearchText = "LD-2";
vm.SelectAllMappedAccountsCommand.Execute(null);
vm.BulkFetchEnabled = true;
vm.BulkScreenTimeEnabled = true;
vm.BulkPostingEnabled = true;
Require(
    vm.BulkFetchEnabled && vm.BulkScreenTimeEnabled && vm.BulkPostingEnabled,
    "Bulk switches turn selected options on"
);
vm.BulkFetchEnabled = false;
vm.BulkScreenTimeEnabled = false;
vm.BulkPostingEnabled = false;
Require(
    !vm.BulkFetchEnabled && !vm.BulkScreenTimeEnabled && !vm.BulkPostingEnabled,
    "Bulk switches turn selected options off"
);
Require(
    vm.MappedAccounts.Single(a => a.DeviceName == "LD-3").SelectedForPosting,
    "Switches preserve unselected accounts"
);
vm.AccountSearchText = "";

// Exercise the real preparation method with isolated synthetic media/history.
var snackbarWindow = new Window
{
    Content = new Material.Styles.Controls.SnackbarHost
    {
        HostName = "Fixture",
        Content = new Grid(),
    },
};
snackbarWindow.Show();
Dispatcher.UIThread.RunJobs();
var mediaFixture = Path.GetFullPath("artifacts/multi-type-queue-fixture");
Directory.CreateDirectory(mediaFixture);
var reelFile = Path.Combine(mediaFixture, "reel.mp4");
var photoFile = Path.Combine(mediaFixture, "photo.jpg");
var storyFile = Path.Combine(mediaFixture, "story.mp4");
File.WriteAllText(reelFile, "fixture");
File.WriteAllText(photoFile, "fixture");
File.WriteAllText(storyFile, "fixture");
using (
    var queueVm = new AutoPostViewModel(
        null!,
        null!,
        new _855Media.Framework.SnackbarManager(),
        null!,
        null!
    )
)
{
    typeof(AutoPostViewModel)
        .GetMethod("DisposePostQueuePersistence", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(queueVm, null);
    typeof(AutoPostViewModel)
        .GetField("_pFlowHistory", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(queueVm, new PFlowHistoryStore(Path.Combine(mediaFixture, "history.db")));
    queueVm.Tasks.Clear();
    queueVm.Pages.Clear();
    queueVm.Pages.Add(
        new(
            new FacebookPageTarget
            {
                DeviceName = "LD-991",
                PageId = "100000000000991",
                UserUid = "100000000000990",
                PageName = "Synthetic queue",
                IsSelected = true,
                VideoReelPath = reelFile,
                ImagePath = photoFile,
                StoryPath = storyFile,
            },
            991
        )
    );
    queueVm.AutoMaxPostByMaturity = false;
    queueVm.PostFromCount = 1;
    queueVm.PostToCount = 1;
    queueVm.PrepareReels = true;
    queueVm.PreparePhotos = true;
    queueVm.PrepareStories = true;
    var prepare = typeof(AutoPostViewModel).GetMethod(
        "PrepareSelectedFilesAsync",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;
    void RunPrepare()
    {
        var task = (Task)prepare.Invoke(queueVm, null)!;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Require(task.IsCompleted, "Preparation timeout");
        task.GetAwaiter().GetResult();
    }
    RunPrepare();
    Require(
        queueVm.Tasks.Select(t => t.Type).SequenceEqual(new[] { "Reel", "Picture", "Story" }),
        "One prepare queues all selected types"
    );
    Require(
        queueVm.IsPostReel && queueVm.IsPostPicture && queueVm.IsPostStory,
        "Prepared types enabled for run"
    );
    queueVm.PrepareReels = false;
    queueVm.PreparePhotos = false;
    queueVm.PrepareStories = false;
    RunPrepare();
    Require(queueVm.Tasks.Count == 3, "Empty type selection adds no rows");
    queueVm.PreparePhotos = true;
    RunPrepare();
    Require(queueVm.Tasks.Count == 3, "Repeated preparation does not duplicate files");
    queueVm.Tasks.Clear();
    queueVm.AutoMaxPostByMaturity = true;
    queueVm.Pages[0].CustomMaxDailyPosts = 2;
    queueVm.PrepareReels = true;
    queueVm.PreparePhotos = true;
    queueVm.PrepareStories = true;
    RunPrepare();
    Require(queueVm.Tasks.Count == 2, "Selected types share the daily limit");
}
snackbarWindow.Close();
Console.WriteLine(
    "PASS one prepare for Reel/Photo/Story, empty type selection and repeated preparation without duplicates; synthetic files/history only"
);

// Source edits reset only continuation plans, never publication history or queued jobs.
var partModel = new FacebookPageTarget
{
    VideoReelPath = @"C:\Fixture\Old",
    VideoLongPath = @"C:\Fixture\Extra",
    GroupReelPath = @"C:\Fixture\Group",
    PendingReelPartGroups = [new() { Key = "saved", Files = [@"C:\Fixture\Old\A Part 1.mp4"] }],
};
var partRow = new FacebookPageTargetViewModel(partModel);
Require(
    partModel.PendingReelPartGroups.Count == 1,
    "Loading saved source preserves pending groups"
);
partRow.VideoReelPath = @"c:\fixture\old\";
Require(
    partModel.PendingReelPartGroups.Count == 1,
    "Equivalent source path preserves pending groups"
);
partRow.ImagePath = @"C:\Fixture\Photos";
Require(partModel.PendingReelPartGroups.Count == 1, "Photo edit preserves Reel groups");
partRow.VideoReelPath = @"C:\Fixture\New";
Require(partModel.PendingReelPartGroups.Count == 0, "Changed Reel source resets groups");
partModel.PendingReelPartGroups.Add(new() { Key = "extra", Files = [] });
partRow.VideoLongPath = @"C:\Fixture\ExtraNew";
Require(partModel.PendingReelPartGroups.Count == 0, "Changed additional Reel source resets groups");
partModel.PendingReelPartGroups.Add(new() { Key = "group", Files = [] });
partRow.GroupReelPath = "";
Require(partModel.PendingReelPartGroups.Count == 0, "Cleared group Reel source resets groups");
Console.WriteLine(
    "PASS source-path reset, equivalent-path/load preservation and non-Reel edit isolation"
);
var view = new AutoPostView { DataContext = vm };
var original = view.FindControl<DataGrid>("MappedAccountsList")!;

// Render the actual Accounts tab content independently of other management panels.
var tab = original.GetVisualAncestors().OfType<TabItem>().FirstOrDefault();

// The not-yet-attached view uses logical ancestry.
Control accountPanel = original;
while (accountPanel.Parent is Control parent && parent is not TabItem)
    accountPanel = parent;
var owner = (TabItem)accountPanel.Parent!;
owner.Content = null;
accountPanel.DataContext = vm;
var window = new Window
{
    Content = new UserControl { DataContext = vm, Content = accountPanel },
};
window.Show();
Directory.CreateDirectory("artifacts/accounts-table");
foreach (var width in new[] { 800, 1280, 1800 })
{
    window.Width = width;
    window.Height = 740;
    for (var i = 0; i < 4; i++)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    Require(original.Bounds.Height > 150 && original.Bounds.Width > 400, "Table usable size");
    Require(
        original.Columns.Count == 9 && original.FrozenColumnCount == 2,
        "Columns and frozen LD"
    );
    Require(original.GetVisualDescendants().OfType<DataGridRow>().Any(), "Rows actually rendered");
    using var bitmap = new RenderTargetBitmap(new PixelSize(width, 740));
    bitmap.Render(accountPanel);
    bitmap.Save($"artifacts/accounts-table/accounts-{width}.png");
}
original.SelectedItem = vm.DisplayedMappedAccounts.First();
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();
Require(
    original
        .GetVisualDescendants()
        .OfType<Button>()
        .Any(b => b.Content?.ToString() == "Transfer to LD…"),
    "Selected row actions rendered"
);
Require(
    original
        .GetVisualDescendants()
        .OfType<Button>()
        .Any(b =>
            b.Content?.ToString() == "Transfer to LD…"
            && ReferenceEquals(b.Command, vm.OpenAccountTransferCommand)
        ),
    "Row actions resolve actual parent commands"
);
using (var bitmap = new RenderTargetBitmap(new PixelSize(1800, 740)))
{
    bitmap.Render(accountPanel);
    bitmap.Save("artifacts/accounts-table/accounts-details.png");
}
Console.WriteLine(
    "PASS real VM search/filter/numeric order/hidden-selection safety and actual 800/1280/1800 table rendering/details"
);
window.Close();
