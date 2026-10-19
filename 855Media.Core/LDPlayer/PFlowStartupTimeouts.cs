using System;

namespace _855Media.Core.LDPlayer;

public static class PFlowStartupTimeouts
{
    public static readonly TimeSpan AndroidBoot = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan InternetReady = TimeSpan.FromMinutes(3);
}
