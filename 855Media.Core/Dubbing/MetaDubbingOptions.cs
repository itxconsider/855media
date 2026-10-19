namespace _855Media.Core.Dubbing;

public sealed class MetaDubbingOptions
{
    public bool EnableSamAudio { get; set; }
    public bool EnableOmnilingualAsr { get; set; }
    public string PythonPath { get; set; } = string.Empty;

    public MetaDubbingOptions Copy() =>
        new()
        {
            EnableSamAudio = EnableSamAudio,
            EnableOmnilingualAsr = EnableOmnilingualAsr,
            PythonPath = PythonPath,
        };
}
