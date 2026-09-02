using System.ComponentModel.DataAnnotations;

namespace _855Media.ViewModels.Dialogs;

public enum DownloadVideoPopularityFilter
{
    [Display(Name = "Default order")]
    Default,

    [Display(Name = "Most viewed")]
    MostViewed,

    [Display(Name = "Most popular")]
    MostPopular,
}
