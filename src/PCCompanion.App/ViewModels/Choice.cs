namespace PCCompanion.App.ViewModels;

/// <summary>
/// An item of a list on a settings page, such as a <see cref="Controls.ChoiceCard"/>'s, which shows
/// as <see cref="DisplayName"/>.
/// </summary>
public abstract record Choice(string DisplayName)
{
    public sealed override string ToString() => DisplayName;
}
