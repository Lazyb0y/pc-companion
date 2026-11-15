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
    /// Whether the string is a line that can start with a key name, such as "Ctrl + middle click",
    /// so that it reads right to left in right-to-left languages (see <see cref="ReadingOrder"/>).
    /// </summary>
    public bool Line { get; set; }

    /// <summary>
    /// Returns the string named <paramref name="key"/>, or the key when there is no such string.
    /// </summary>
    public static string GetString(string key) =>
        Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    protected override object ProvideValue() => Line ? ReadingOrder.Line(GetString(Key)) : GetString(Key);
}
