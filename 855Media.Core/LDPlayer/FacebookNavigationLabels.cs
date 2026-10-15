using System;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

internal static class FacebookNavigationLabels
{
    // Section-expansion labels only. Callers establish Menu/switcher/details context;
    // this is not a global text replacement or permission to tap an arbitrary button.
    internal static bool IsSectionExpansion(string? label)
    {
        return FacebookLayoutRegistry.IsSectionExpansionLabel(label);
    }
}
