using PCCompanion.Localization;

namespace PCCompanion.App.Controls;

/// <summary>
/// The Permissions &amp; data card at the end of every feature page: the same icon and header, and
/// the page's description of what the feature reads and changes.
/// </summary>
public sealed partial class PermissionsCard : IconCard
{
    public PermissionsCard()
    {
        Glyph = "";
        Header = Strings.FeaturePermissionsHeader;
    }
}
