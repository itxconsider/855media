using System.ComponentModel.DataAnnotations;

namespace _855Media.Core.Upscaling;

public enum SmartTrackingMode
{
    [Display(Name = "Static Center (Classic)")]
    StaticCenter,

    [Display(Name = "Active Motion Centroid")]
    MotionCentroid,

    [Display(Name = "Face & Subject Priority")]
    FacePriority,
}
