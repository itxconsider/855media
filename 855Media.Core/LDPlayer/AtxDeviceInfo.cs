namespace _855Media.Core.LDPlayer;

public class AtxDeviceInfo
{
    public string Serial { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int Sdk { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
    public int DisplayWidth { get; set; }
    public int DisplayHeight { get; set; }
    public int BatteryLevel { get; set; } = 100;
    public string MemoryAround { get; set; } = string.Empty;
}
