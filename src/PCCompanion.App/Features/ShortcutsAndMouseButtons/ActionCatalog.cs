using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

/// <summary>An action a feature offers, with the feature's name and its own, such as "Hide Apps" and "Hide others".</summary>
public sealed record CatalogAction(CatalogFeature Feature, FeatureAction Action, string FeatureName, string Name)
{
    private static readonly CompositeFormat FullNameFormat = CompositeFormat.Parse(Strings.ShortcutsAndMouseButtonsActionName);

    /// <summary>The name the page shows, such as "Hide Apps: Hide others".</summary>
    public string FullName => string.Format(CultureInfo.CurrentCulture, FullNameFormat, FeatureName, Name);
}

/// <summary>
/// A shortcut another feature has on its own page, with the action it runs, and its key by its
/// position in the settings app's keyboard layout.
/// </summary>
/// <param name="Key">The key's <c>KeyboardEvent.code</c> name, as <see cref="KeyCodes"/> names it.</param>
public sealed record OtherShortcut(CatalogAction Action, HotKeyModifiers Modifiers, string Key)
{
    /// <summary>The shortcut as a trigger's is compared.</summary>
    public TriggerInput Input => new(Modifiers, Key, null);
}

/// <summary>
/// The actions that the features supported on this PC offer, and the shortcuts that the other
/// features that are on have on their own pages, as the settings are when it's read.
/// </summary>
public sealed class ActionCatalog
{
    private ActionCatalog(IReadOnlyList<CatalogAction> actions, IReadOnlyList<OtherShortcut> otherShortcuts)
    {
        Actions = actions;
        OtherShortcuts = otherShortcuts;
    }

    /// <summary>The actions, by feature in the catalog's order.</summary>
    public IReadOnlyList<CatalogAction> Actions { get; }

    /// <summary>The other features' shortcuts, by feature in the catalog's order.</summary>
    public IReadOnlyList<OtherShortcut> OtherShortcuts { get; }

    /// <summary>Reads the actions and shortcuts from the settings, with the keys as <paramref name="layout"/> has them.</summary>
    public static ActionCatalog Read(FeatureSettingsEditor editor, nint layout)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var actions = new List<CatalogAction>();
        var otherShortcuts = new List<OtherShortcut>();
        foreach (var feature in FeatureCatalog.Features)
        {
            if (!feature.Support.IsSupported || feature.Actions is not { } offer)
            {
                continue;
            }

            var featureName = Localized.GetString(feature.Info.NameKey);
            var offered = editor.Read(feature.Info.Id, section => offer(section))
                .Select(action => new CatalogAction(feature, action.Action, featureName, action.Name))
                .ToList();
            actions.AddRange(offered);

            if (feature.Shortcuts is not { } shortcuts || !editor.IsEnabled(feature.Info.Id))
            {
                continue;
            }

            foreach (var (action, keys) in editor.Read(feature.Info.Id, section => shortcuts(section)))
            {
                if (offered.Find(named => named.Action == action) is { } named
                    && keys.InLayout(layout) is { } position
                    && KeyCodes.NameOf(position.ScanCode) is { } key)
                {
                    otherShortcuts.Add(new OtherShortcut(named, position.Modifiers, key));
                }
            }
        }

        return new ActionCatalog(actions, otherShortcuts);
    }

    /// <summary>The action <paramref name="action"/>, or <see langword="null"/> when no feature supported on this PC offers it now.</summary>
    public CatalogAction? Find(FeatureAction action) => Actions.FirstOrDefault(named => named.Action == action);
}
