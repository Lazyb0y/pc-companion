using System.Globalization;
using Microsoft.UI.Xaml.Markup;
using PCCompanion.Localization;

namespace PCCompanion.App;

/// <summary>
/// A string from <c>Strings.resx</c> in the user interface language, for use in XAML:
/// <c>{app:Localized Key=NavigationHome}</c>.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Localized : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() =>
        Strings.ResourceManager.GetString(Key, CultureInfo.CurrentUICulture) ?? Key;
}
