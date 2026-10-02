namespace _855Media.ViewModels.Dialogs;

public record SkipOption(string DisplayName, int? Value)
{
    public bool IsCustom => Value is null;

    public override string ToString() => DisplayName;
}
