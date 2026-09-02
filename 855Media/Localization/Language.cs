using System.ComponentModel.DataAnnotations;

namespace _855Media.Localization;

public enum Language
{
    System,
    English,
    Ukrainian,
    German,
    French,
    Spanish,
    Khmer,

    [Display(Name = "Simplified Chinese")]
    ChineseSimplified,
}
