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

    /// <summary>
    /// Returns the string named <paramref name="key"/>, or the key when there is no such string.
    /// </summary>
    public static string GetString(string key) =>
        Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    protected override object ProvideValue() => GetString(Key);
}
