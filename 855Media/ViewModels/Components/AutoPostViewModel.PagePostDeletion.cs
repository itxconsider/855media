using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [RelayCommand]
    private void ReviewPagePosts()
    {
        if (!CanUseSubscription())
            return;
        _snackbarManager.Notify(
            "Post review and deletion require an authorized Page API connection. Device-token extraction is no longer available."
        );
    }
}
