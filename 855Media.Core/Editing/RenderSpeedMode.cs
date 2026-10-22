using System.ComponentModel.DataAnnotations;

namespace _855Media.Core.Editing;

/// <summary>
/// Execution speed and resource allocation profile for video editing and encoding.
/// </summary>
public enum RenderSpeedMode
{
    [Display(Name = "Quality (Pristine)")]
    Quality,

    [Display(Name = "Balanced")]
    Balanced,

    [Display(Name = "Turbo (Fastest)")]
    TurboFast,
}
