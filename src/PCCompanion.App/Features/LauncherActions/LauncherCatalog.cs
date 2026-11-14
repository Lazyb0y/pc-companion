using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.LauncherActions;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

namespace PCCompanion.App.Features.LauncherActions;

/// <summary>
/// The actions that launchers and the command run, as the settings are when it's read. See
/// "Actions" in docs/features/input/launcher-actions.md.
/// </summary>
public static class LauncherCatalog
{
    /// <summary>
    /// The actions available now, by feature in the catalog's order, then Launcher Actions' own:
    /// those of the features that are on and supported on this PC, except the ones on the window in
    /// front, named in the user's language. A feature whose section can't be read offers none.
    /// </summary>
    /// <param name="section">A feature's section of the settings, by its ID, or null when it has none.</param>
    /// <param name="withFeaturesOff">
    /// Whether the actions of features that are off are read too, as for matching a command's IDs,
    /// so that the host can say the feature is off.
    /// </param>
    public static IReadOnlyList<LauncherAction> Read(Func<string, JsonElement?> section, bool withFeaturesOff = false)
    {
        ArgumentNullException.ThrowIfNull(section);

        var actions = new List<LauncherAction>();
        foreach (var feature in FeatureCatalog.Features)
        {
            // Shortcuts & Mouse Buttons' own Lock and the rest run only from its triggers;
            // Launcher Actions has its own.
            if (!feature.Support.IsSupported
                || feature.Actions is not { } offer
                || feature.Info.Id == ShortcutsAndMouseButtonsFeature.Definition.Id
                || section(feature.Info.Id) is not { } featureSection
                || !(withFeaturesOff || FeatureSettings.IsEnabled(featureSection)))
            {
                continue;
            }

            IReadOnlyList<OfferedAction> offered;
            try
            {
                offered = offer(featureSection);
            }
            catch (JsonException)
            {
                continue;
            }

            var featureName = Localized.GetString(feature.Info.NameKey);
            actions.AddRange(offered
                .Where(action => !action.OnWindowInFront)
                .Select(action => new LauncherAction(action.Action, action.Name, featureName)));
        }

        actions.AddRange(LauncherActionsFeature.OwnActions().Select(action => new LauncherAction(action.Action, action.Name, Strings.AppDisplayName)));
        return actions;
    }
}
