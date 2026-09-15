using System.ComponentModel.DataAnnotations;

namespace _855Media.Core.Upscaling;

public enum SmartZoomMode
{
    [Display(Name = "Center Crop (Uniform)")]
    CenterCrop,

    [Display(Name = "Action-Anchored Zoom")]
    ActionAnchored,

    [Display(Name = "Cinematic Push-In (Dynamic Ken Burns)")]
    CinematicPushIn,
}
