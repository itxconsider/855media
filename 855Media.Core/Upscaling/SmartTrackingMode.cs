using System.ComponentModel.DataAnnotations;

namespace _855Media.Core.Upscaling;

public enum SmartTrackingMode
{
    [Display(Name = "Static Center (Classic)")]
    StaticCenter,

    [Display(Name = "Smart Object Focus (Auto Reframe)")]
    ObjectFocus,

    [Display(Name = "Face & Subject Priority")]
    FacePriority,

    [Display(Name = "Active Motion Centroid")]
    MotionCentroid,
}
