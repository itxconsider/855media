using _855Media.ViewModels;
using _855Media.ViewModels.Components;
using _855Media.ViewModels.Dialogs;
using _855Media.Views;
using _855Media.Views.Components;
using _855Media.Views.Dialogs;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace _855Media.Framework;

public partial class ViewManager
{
    private Control? TryCreateView(ViewModelBase viewModel) =>
        viewModel switch
        {
            MainViewModel => new MainView(),
            DashboardViewModel => new DashboardView(),
            YouTubeDownloaderViewModel => new YouTubeDownloaderView(),
            TikTokDownloaderViewModel => new TikTokDownloaderView(),
            FacebookDownloaderViewModel => new FacebookDownloaderView(),
            AuthSetupViewModel => new AuthSetupView(),
            DownloadMultipleSetupViewModel => new DownloadMultipleSetupView(),
            DownloadSingleSetupViewModel => new DownloadSingleSetupView(),
            MessageBoxViewModel => new MessageBoxView(),
            SettingsViewModel => new SettingsView(),
            BatchInputViewModel => new BatchInputView(),
            LicenseActivationViewModel => new LicenseActivationView(),
            VideoUpscalerViewModel => new VideoUpscalerView(),
            _ => null,
        };

    public Control? TryBindView(ViewModelBase viewModel)
    {
        var view = TryCreateView(viewModel);
        if (view is null)
            return null;

        view.DataContext ??= viewModel;
        view.Loaded += async (_, _) => await viewModel.InitializeAsync();

        return view;
    }

    public UserControl<T>? TryBindUserControl<T>(T viewModel)
        where T : ViewModelBase => TryBindView(viewModel) as UserControl<T>;

    public Window<T>? TryBindWindow<T>(T viewModel)
        where T : ViewModelBase => TryBindView(viewModel) as Window<T>;
}

public partial class ViewManager : IDataTemplate
{
    bool IDataTemplate.Match(object? data) => data is ViewModelBase;

    Control? ITemplate<object?, Control?>.Build(object? data) =>
        data is ViewModelBase viewModel ? TryBindView(viewModel) : null;
}
