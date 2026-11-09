using System.Reflection;
using _855Media.Core.Resolving;
using _855Media.Services;
using _855Media.ViewModels.Components;
using _855Media.Views.Components;
using Avalonia.Controls;
using Avalonia.Threading;

internal static class InstagramImportChecks
{
    public static void Run()
    {
        var photo = new VideoInfo(
            VideoSource.InstagramPhoto,
            "FAST_1",
            "https://www.instagram.com/p/FAST/",
            "Caption",
            "owner",
            null,
            null,
            null,
            []
        );
        CheckResume(photo);
        using var vm = new InstagramDownloaderViewModel();
        var commands = new List<string>();
        vm.BrowserCommand += commands.Add;
        vm.ImportCommand.Execute(null);
        vm.NavigateCommand.Execute(null);
        if (commands.Count != 0)
            throw new Exception("Inactive access can import or navigate");
        try
        {
            vm.CreateImportQueueAsync(photo.Url, InstagramMediaKind.Photos, CancellationToken.None);
            throw new Exception("Inactive access can create queue");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("subscription")) { }
        var view = new InstagramDownloaderView { DataContext = vm };
        var browser = view.FindControl<Control>("InstagramWebBrowser")!;
        ((Panel)browser.Parent!).Children.Remove(browser);
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(InstagramDownloaderView).GetField("_import", flags)!;
        var active = typeof(InstagramDownloaderView).GetMethod("SetActive", flags)!;
        using (var pending = new CancellationTokenSource())
        {
            field.SetValue(view, pending);
            vm.IsImporting = true;
            for (var i = 0; i < 10; i++)
            {
                active.Invoke(view, [false]);
                active.Invoke(view, [true]);
            }
            if (pending.IsCancellationRequested || !vm.IsImporting)
                throw new Exception("Tab switch cancelled import");
            vm.StopPendingWork();
            if (!pending.IsCancellationRequested)
                throw new Exception("Stop did not cancel");
            field.SetValue(view, null);
        }
        using (var pending = new CancellationTokenSource())
        {
            field.SetValue(view, pending);
            view.DataContext = new InstagramDownloaderViewModel();
            if (!pending.IsCancellationRequested)
                throw new Exception("Changed view owner retained import");
            field.SetValue(view, null);
        }
        using (var pending = new CancellationTokenSource())
        {
            field.SetValue(view, pending);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            if (!pending.IsCancellationRequested)
                throw new Exception("Detach retained import");
            field.SetValue(view, null);
        }
        window.Close();
        Console.WriteLine(
            "PASS Instagram actual view: access guards, ten tab switches, Stop, owner change and detach"
        );
    }

    public static void CheckResume(VideoInfo photo)
    { // Real desktop history/file policy plus persisted post membership, in isolated storage.
        var resumeRoot = Path.Combine(
            Path.GetTempPath(),
            "855-instagram-resume-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(resumeRoot);
        try
        {
            var resumeMedia = photo with
            {
                Id = "FAST_1",
                Url = "https://www.instagram.com/p/FAST/",
                Caption = "Saved caption",
            };
            var savedPath = Path.Combine(resumeRoot, "image.jpg");
            File.WriteAllBytes(savedPath, [1, 2, 3, 4]);
            var record = new HistoryRecord
            {
                SourceItemId = resumeMedia.Id,
                Source = "InstagramPhoto",
                Url = resumeMedia.Url,
                FilePath = savedPath,
                FileSizeBytes = 4,
                Status = DownloadStatus.Completed,
            };
            var checkpoint = new InstagramImportProgress(
                resumeRoot,
                "owner/destination/photos/template"
            );
            Task.Run(() =>
                    checkpoint.RememberAsync(resumeMedia.Url, [resumeMedia], CancellationToken.None)
                )
                .GetAwaiter()
                .GetResult();
            checkpoint = new InstagramImportProgress(
                resumeRoot,
                "owner/destination/photos/template"
            );
            bool CanResume() =>
                checkpoint.CanSkip(
                    resumeMedia.Url,
                    (url, item) =>
                        InstagramImportQueue.HasCompletedFile(record, url, item, resumeRoot, true)
                );
            if (CanResume())
                throw new Exception("Missing caption sidecar was treated as completed");
            File.WriteAllText(Path.ChangeExtension(savedPath, ".txt"), "Saved caption");
            if (!CanResume())
                throw new Exception("Completed matching history/file could not resume");
            record.Status = DownloadStatus.Failed;
            if (CanResume())
                throw new Exception("Failed job resumed as completed");
            record.Status = DownloadStatus.Completed;
            record.SourceItemId = null;
            if (CanResume())
                throw new Exception("Legacy history without item identity skipped a whole post");
            record.SourceItemId = resumeMedia.Id;
            record.Source = "FacebookPhoto";
            if (CanResume())
                throw new Exception("Another platform's item authorized resume");
            record.Source = "InstagramPhoto";
            record.Url = "https://www.instagram.com/p/DIFFERENT/";
            if (CanResume())
                throw new Exception("Different post authorized resume");
            record.Url = resumeMedia.Url;
            record.FilePath = Path.Combine(resumeRoot, "subfolder", "image.jpg");
            if (CanResume())
                throw new Exception("Different destination authorized resume");
            record.FilePath = savedPath;
            File.WriteAllBytes(savedPath, []);
            if (CanResume())
                throw new Exception("Empty placeholder authorized resume");
            File.WriteAllBytes(savedPath, [1, 2]);
            if (CanResume())
                throw new Exception("Changed file size authorized resume");
            Console.WriteLine(
                "PASS Instagram restart resume: actual history, item/type/post/destination/file/sidecar guards"
            );
        }
        finally
        {
            Directory.Delete(resumeRoot, true);
        }
    }
}
