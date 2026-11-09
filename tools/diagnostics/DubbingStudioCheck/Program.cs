using _855Media.Core.Dubbing;
using _855Media.Framework;
using _855Media.Services;
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
    Path.Combine(Path.GetTempPath(), "855-dubbing-studio-preview.json")
);
System.Reflection.Assembly.Load("Material.Avalonia");
System.Reflection.Assembly.Load("Material.Avalonia.DataGrid");
AppBuilder
    .Configure<_855Media.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseDesktopWebViewSafe()
    .SetupWithoutStarting();
Application.Current!.Styles.Add(
    new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Material.Avalonia/"))
    {
        Source = new Uri("avares://Material.Avalonia/Material.Avalonia.Templates.xaml"),
    }
);
using var dialogs = new DialogManager();
using var model = new DubbingViewModel(dialogs, new SnackbarManager(), new SettingsService());
model.ProjectName = "Movie adaptation · studio review";
model.TranslationBrief =
    "Mira and Dara are siblings. Use respectful speech with their teacher. Keep character names consistent; tense action dialogue should stay concise.";
model.SelectedStudioTab = 4;
var actor = model.Characters.First();
foreach (
    var cue in new[]
    {
        new SubtitleSegment
        {
            Index = 1,
            StartTime = TimeSpan.FromSeconds(1),
            EndTime = TimeSpan.FromSeconds(3),
            OriginalText = "Don't leave me here.",
            KhmerText = "",
            CharacterId = actor.Id,
        },
        new SubtitleSegment
        {
            Index = 2,
            StartTime = TimeSpan.FromSeconds(2),
            EndTime = TimeSpan.FromSeconds(4),
            OriginalText = "We need to go now!",
            KhmerText = "យើងត្រូវទៅឥឡូវនេះ!",
            CharacterId = actor.Id,
            AudioDurationSeconds = 6,
        },
    }
)
    model.Segments.Add(cue);

// Populate synthetic review data directly; do not activate a license or invoke media/network commands.
foreach (var issue in DubbingQualityService.Analyze(model.Segments, model.Characters))
    model.QualityIssues.Add(issue);
model.QualitySummary =
    "2 lines · missing translation, overlapping actor dialogue and rushed take require review.";
var view = new DubbingView { DataContext = model };
var window = new Window
{
    Width = 1280,
    Height = 820,
    Content = view,
};
window.Show();
Dispatcher.UIThread.RunJobs();
window.UpdateLayout();
var tabs = view.GetVisualDescendants().OfType<TabItem>().ToList();
if (!tabs.Any(t => Equals(t.Header, "Studio QC")))
    throw new Exception("Studio QC tab is missing");
var brief = view.GetVisualDescendants()
    .OfType<TextBox>()
    .Single(t => t.Text == model.TranslationBrief);
brief.Text = "Keep Mira unchanged. Use the teacher's formal title.";
Dispatcher.UIThread.RunJobs();
if (model.TranslationBrief != brief.Text)
    throw new Exception("Translation brief two-way binding failed");
var gotoButtons = view.GetVisualDescendants()
    .OfType<Button>()
    .Where(b => Equals(b.Content, "Go to line"))
    .ToList();
if (
    gotoButtons.Count == 0
    || gotoButtons.Any(b => b.Command == null || b.CommandParameter is not DubbingQualityIssue)
)
    throw new Exception("Issue navigation bindings failed");
Directory.CreateDirectory("artifacts/dubbing-studio");
using (var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820)))
{
    bitmap.Render(view);
    bitmap.Save("artifacts/dubbing-studio/studio-qc-1280x820.png");
}
Console.WriteLine(
    $"PASS actual Dubbing view, QC issue/navigation bindings, translation brief edit; {gotoButtons.Count} issue controls rendered."
);
window.Close();
