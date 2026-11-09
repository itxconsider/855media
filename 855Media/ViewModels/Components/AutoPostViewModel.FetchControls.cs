using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchButtonText))]
    [NotifyPropertyChangedFor(nameof(FetchButtonCommand))]
    private bool _isFetching;

    public string FetchButtonText => IsFetching ? "Stop fetch" : "Fetch accounts";

    // Switching commands keeps Stop available while the asynchronous Fetch command runs.
    public ICommand FetchButtonCommand =>
        IsFetching ? CancelFetchCommand : FetchSelectedPagesInfoCommand;
}
