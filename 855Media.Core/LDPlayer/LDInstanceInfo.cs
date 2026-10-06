using System;

namespace _855Media.Core.LDPlayer;

public class LDInstanceInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TopWindowHandle { get; set; }
    public long BindWindowHandle { get; set; }
    public bool IsAndroidStarted { get; set; }
    public int Pid { get; set; } = -1;
    public int VboxPid { get; set; } = -1;
    public int Width { get; set; } = 720;
    public int Height { get; set; } = 1280;
    public int Dpi { get; set; } = 320;

    public string Status { get; set; } = "Idle";
    public string FbClone { get; set; } = "FB-01";
    public bool IsSelected { get; set; } = true;

    public bool IsRunning => IsAndroidStarted || Pid > 0;
}
