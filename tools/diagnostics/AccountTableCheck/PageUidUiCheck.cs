using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class PageUidUiCheck
{
    internal static void Run()
    {
        var output = Path.GetFullPath("artifacts/page-uid-only");
        var fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        using var vm = new AutoPostViewModel(
            null!,
            null!,
            new SnackbarManager(),
            null!,
            null!,
            accountStore: new PFlowAccountStore(Path.Combine(fixture, "accounts.json")),
            pagesStoragePath: Path.Combine(fixture, "pages.json"),
            historyStore: new PFlowHistoryStore(Path.Combine(fixture, "history.db")),
            dispatchQueueStore: new PFlowDispatchQueueStore(Path.Combine(fixture, "queue.db"))
        );
        typeof(ViewModelBase)
            .GetProperty("ModuleLicense", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, DispatchProxy.Create<ILicenseService, ScreenTimeNameCheck.TestLicense>());
        var row = new FacebookPageTargetViewModel(
            new FacebookPageTarget
            {
                PageName = "UID display fixture",
                DeviceName = "LD-990",
                Type = "Page",
                PageId = "1356708460856148",
                PersonaUid = "61594246817570",
                UserUid = "61594750165531",
            },
            1
        );
        vm.Pages.Clear();
        var unknownUid = new FacebookPageTargetViewModel(
            new FacebookPageTarget
            {
                Type = "Page",
                PageId = "1356708460856148",
                PersonaUid = "",
            },
            2
        );
        unknownUid.SyncToModel();
        if (unknownUid.PersonaUid.Length != 0 || unknownUid.Model.PersonaUid.Length != 0)
            throw new Exception("Canonical key was incorrectly displayed/saved as Page UID.");
        vm.Pages.Add(row);
        vm.IsManagePagesOpen = true;
        vm.EditPageConfig(row);
        var view = new AutoPostView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 1280,
            Height = 820,
        };
        window.Show();
        Check(view);
        foreach (var width in new[] { 1280, 900 })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using var image = new RenderTargetBitmap(new PixelSize(width, 820));
            image.Render(view);
            image.Save(Path.Combine(output, $"editor-{width}.png"));
        }
        window.Close();
        var details = ((IDataTemplate)view.Resources["TargetRowDetails"]!).Build(row)!;
        details.DataContext = row;
        var detailWindow = new Window
        {
            Content = details,
            Width = 1100,
            Height = 500,
        };
        detailWindow.Show();
        Check(details);
        using (var image = new RenderTargetBitmap(new PixelSize(1100, 500)))
        {
            image.Render(details);
            image.Save(Path.Combine(output, "target-details.png"));
        }
        detailWindow.Close();
        vm.CloseUpdatePage();
        if (row.Model.PageId != "1356708460856148" || row.Model.PersonaUid != "61594246817570")
            throw new Exception("UID UI changed persisted identity keys.");
        Console.WriteLine(
            "PASS actual Page UID editor/details bindings, canonical label absence, 1280/900px renders and preserved stored keys."
        );
    }

    private static void Check(Control root)
    {
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
        var labels = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .ToArray();
        if (
            !labels.Contains("Page UID")
            || labels.Any(t => t.StartsWith("Page ID", StringComparison.Ordinal))
        )
            throw new Exception("UID-only labels are incorrect.");
        var values = root.GetVisualDescendants()
            .Select(c =>
                c switch
                {
                    TextBox box => box.Text,
                    SelectableTextBlock text => text.Text,
                    _ => null,
                }
            )
            .ToArray();
        if (!values.Contains("61594246817570") || values.Contains("1356708460856148"))
            throw new Exception("UID-only value binding is incorrect.");
    }
}
