using System.ComponentModel.DataAnnotations;

namespace _855Media.ViewModels.Dialogs;

public enum DownloadVideoTypeFilter
{
    [Display(Name = "All")]
    All,

    [Display(Name = "Shorts only")]
    ShortsOnly,

    [Display(Name = "Long videos only")]
    LongVideosOnly,
}
