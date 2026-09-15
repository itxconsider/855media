using System.ComponentModel.DataAnnotations;

namespace _855Media.Core.Upscaling;

/// <summary>
/// Execution speed and resource allocation profile for video upscaling and encoding.
/// </summary>
public enum RenderSpeedMode
{
    [Display(Name = "Quality (Pristine Neural - Slower)")]
    Quality,

    [Display(Name = "Balanced (Standard Speed & Quality)")]
    Balanced,

    [Display(Name = "Turbo (Fastest - Ideal for Long Videos)")]
    TurboFast,
}
