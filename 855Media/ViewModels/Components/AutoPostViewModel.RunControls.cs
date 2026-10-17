using System.Windows.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    public string AutomationButtonText => IsRunning ? "Stop" : "Start";

    // Keep the existing commands separate so an in-flight Start never disables Stop.
    public ICommand AutomationButtonCommand =>
        IsRunning ? StopAutomationCommand : StartAutomationCommand;

    public bool CanToggleAutomation => IsRunning || !IsBusy;
}
