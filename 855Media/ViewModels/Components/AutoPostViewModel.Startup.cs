using System;
using _855Media.Core.LDPlayer;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private TimeSpan _pFlowRunAppOpenDelay;

    private PFlowSessionPreparation CreateRunSessionPreparation() =>
        new(_ldPlayerService, LdFolder) { AppOpenDelay = _pFlowRunAppOpenDelay };
}
