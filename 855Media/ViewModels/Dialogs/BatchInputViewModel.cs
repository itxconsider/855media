using _855Media.Framework;
using _855Media.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Dialogs;

public partial class BatchInputViewModel : DialogViewModelBase
{
    public BatchInputViewModel(LocalizationManager localizationManager)
    {
        LocalizationManager = localizationManager;
    }

    public LocalizationManager LocalizationManager { get; }

    [ObservableProperty]
    public partial string? Input { get; set; }
}
