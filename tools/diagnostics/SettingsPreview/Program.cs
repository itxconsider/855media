using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.ViewModels.Dialogs;
using _855Media.Views.Dialogs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

// Preview only: isolated temporary settings, no desktop lifetime, queues, or devices.
Environment.SetEnvironmentVariable(
    "MediaTag_SETTINGS_PATH",
    Path.Combine(Path.GetTempPath(), "855Media-settings-preview.json")
);
System.Reflection.Assembly.Load("Material.Avalonia");
System.Reflection.Assembly.Load("Material.Avalonia.DataGrid");
AppBuilder
    .Configure<_855Media.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
Application.Current!.Styles.Add(
    new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Material.Avalonia/"))
    {
        Source = new Uri("avares://Material.Avalonia/Material.Avalonia.Templates.xaml"),
    }
);
var settings = new SettingsService();
var license = new PreviewLicense();
var services = new ServiceCollection();
services.AddSingleton(settings);
services.AddSingleton<ILicenseService>(license);
services.AddSingleton<DialogManager>();
services.AddSingleton<SnackbarManager>();
services.AddSingleton<LocalizationManager>();
services.AddSingleton<ViewModelManager>();
services.AddTransient<LicenseActivationViewModel>();
services.AddTransient<SettingsViewModel>();
using var provider = services.BuildServiceProvider();
using var vm = provider.GetRequiredService<SettingsViewModel>();
vm.SelectedCategory = 2;
var view = new SettingsView { DataContext = vm };
var window = new Window
{
    Width = 860,
    Height = 620,
    Content = view,
};
window.Show();
foreach (var scenario in new[] { "inactive", "monthly", "lifetime" })
{
    license.SetScenario(scenario);
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    var expected =
        scenario == "monthly" ? "30 days remaining"
        : scenario == "lifetime" ? "Lifetime"
        : "Activation required";
    if (vm.SubscriptionTimeRemaining != expected)
        throw new InvalidOperationException(
            "Incorrect subscription duration: " + vm.SubscriptionTimeRemaining
        );
    var output = Path.GetFullPath("artifacts/settings-subscription-" + scenario + ".png");
    using var bitmap = new RenderTargetBitmap(new PixelSize(860, 620));
    bitmap.Render(view);
    bitmap.Save(output);
    Console.WriteLine(output);
}
foreach (var category in new[] { 0, 1, 2, 3, 4, 5, 7 })
{
    vm.SelectedCategory = category;
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    using var bitmap = new RenderTargetBitmap(new PixelSize(860, 620));
    bitmap.Render(view);
    bitmap.Save(Path.GetFullPath("artifacts/settings-category-" + category + ".png"));
}
Console.WriteLine(
    "PASS: all seven settings sections render; inactive/monthly/lifetime labels match."
);
window.Close();

((IDisposable)Application.Current!).Dispose();

// Rendering stub only. No activation or entitlement bypass is linked into the app.
public sealed class PreviewLicense : ILicenseService
{
    public LicenseStatus Status =>
        CurrentLicense == null ? LicenseStatus.Unlicensed : LicenseStatus.Active;
    public LicensePayload? CurrentLicense { get; private set; }
    public string MachineFingerprint => "DEMO-PC";
    public int TrialDaysRemaining => 0;
    public bool IsTrialActive => false;
    public bool IsProActive => CurrentLicense != null;
    public event Action? LicenseChanged;

    public void SetScenario(string scenario)
    {
        CurrentLicense =
            scenario == "inactive"
                ? null
                : new LicensePayload
                {
                    CustomerName = "Example subscription",
                    Type = scenario == "monthly" ? LicenseType.Monthly : LicenseType.Lifetime,
                    ExpiresAt = scenario == "monthly" ? DateTime.UtcNow.AddDays(30) : null,
                    AllowedFeatures = ["All"],
                };
        LicenseChanged?.Invoke();
    }

    public void Initialize(string? savedToken, DateTime? firstRunDate, DateTime? lastRunDate) =>
        throw new NotSupportedException();

    public void Refresh() { }

    public bool IsFeatureAllowed(LicenseFeature feature) => IsProActive;

    public LicenseActivationResult ActivateWithToken(string token) =>
        throw new NotSupportedException();

    public Task<LicenseActivationResult> ActivateWithKeyOnlineAsync(
        string key,
        string? url = null
    ) => throw new NotSupportedException();

    public void Deactivate() => SetScenario("inactive");
}
