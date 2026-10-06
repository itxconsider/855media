using System;

namespace _855Media.Core.LDPlayer;

public class LDSettings
{
    public string LDPlayerPath { get; set; } = @"C:\LDPlayer\LDPlayer9";
    public int RunThread { get; set; } = 8;
    public bool ShowOnlyOpen { get; set; } = false;
    public bool AutoSortLDPlayer { get; set; } = true;
    public bool CloseLDAfterAction { get; set; } = true;
    public bool ClearCacheFBBeforeOpen { get; set; } = true;

    // Timing & Delays (in seconds)
    public int DelayAfterOpenLD { get; set; } = 30;
    public int DelayOpenEachLD { get; set; } = 20;
    public int DelayBeforeCloseLD { get; set; } = 30;
    public int DelayUpload { get; set; } = 30;
    public int DelayOpenApp { get; set; } = 30;
    public int DelayBeforeSwitchPage { get; set; } = 30;

    // Post Options
    public bool PostReel { get; set; } = true;
    public bool DeleteFileAfterPost { get; set; } = true;
    public bool BackupFileAfterPost { get; set; } = false;
    public bool CheckCopyrightAfterPost { get; set; } = true;

    // Other Options
    public bool SleepPcAfterActionDone { get; set; } = false;
    public bool DeletePageRecommend { get; set; } = true;
}
