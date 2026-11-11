using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.App.Features.AppWindows;
using PCCompanion.App.Features.HideApps;
using PCCompanion.App.Features.ClipboardActions;
using PCCompanion.App.Features.DisplayProfiles;
using PCCompanion.App.Features.DriveActions;
using PCCompanion.App.Features.DynamicWallpaper;
using PCCompanion.App.Features.DropDownApps;
using PCCompanion.App.Features.EndApp;
using PCCompanion.App.Features.FolderRules;
using PCCompanion.App.Features.HotCorners;
using PCCompanion.App.Features.InputLanguagePerApp;
using PCCompanion.App.Features.LockKeys;
using PCCompanion.App.Features.MiddleClickPaste;
using PCCompanion.App.Features.MinimizeToTray;
using PCCompanion.App.Features.NetworkProfiles;
using PCCompanion.App.Features.ShortcutsAndMouseButtons;
using PCCompanion.App.Features.SystemMeters;
using PCCompanion.App.Features.TitleBarActions;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Clipboard;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;
using PCCompanion.Modules.Desktop;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Modules.Display;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Modules.Files;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Modules.Input;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Modules.Input.LockKeys;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Modules.Network;
using PCCompanion.Modules.Network.NetworkProfiles;
using PCCompanion.Modules.Windows;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Modules.Windows.EndApp;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features;

/// <param name="NameKey">The <c>Strings</c> key of the module's name.</param>
public sealed record CatalogModule(string Id, string NameKey, IReadOnlyList<CatalogFeature> Features);

/// <param name="PageType">The feature's page in the settings app.</param>
/// <param name="FirstSettings">
/// Makes the section a feature starts with when it's turned on and has none yet, such as settings
/// read from Windows; null for its defaults. It runs on the settings app's UI thread as the switch
/// changes, so it does only quick work.
/// </param>
/// <param name="DrawnIcon">
/// The name of the feature's drawing in <c>docs/design/icons</c>, such as <c>hot-corners</c>, whose
/// images <c>scripts/feature-icons.ps1</c> makes in <c>Assets/Icons</c>; null to show its glyph.
/// </param>
/// <param name="Actions">
/// The actions the feature offers to shortcuts and mouse buttons, named in the user's language,
/// from its section, which may be missing; null for a feature that offers none. It may throw
/// <see cref="JsonException"/> for a section that can't be read.
/// </param>
/// <param name="Shortcuts">
/// The shortcuts the feature registers while it's on, each with the action it runs, from its
/// section, which may be missing; null for a feature without shortcuts of its own. It may throw
/// <see cref="JsonException"/> for a section that can't be read.
/// </param>
public sealed record CatalogFeature(
    FeatureInfo Info,
    FeatureSupport Support,
    Type PageType,
    Func<JsonElement>? FirstSettings = null,
    string? DrawnIcon = null,
    Func<JsonElement?, IReadOnlyList<OfferedAction>>? Actions = null,
    Func<JsonElement?, IReadOnlyList<(FeatureAction Action, ShortcutKeys Keys)>>? Shortcuts = null);

/// <summary>
/// Every module and feature, in the order the navigation pane and Home show them.
/// </summary>
public static class FeatureCatalog
{
    public static IReadOnlyList<CatalogModule> Modules { get; } =
    [
        new(InputModule.Id, "ModuleInputName",
        [
            new(HotCornersFeature.Definition, HotCornersFeature.CurrentSupport, typeof(HotCornersPage), DrawnIcon: "hot-corners"),
            new(LockKeysFeature.Definition, LockKeysFeature.CurrentSupport, typeof(LockKeysPage), DrawnIcon: "lock-keys"),
            new(InputLanguagePerAppFeature.Definition, InputLanguagePerAppFeature.CurrentSupport, typeof(InputLanguagePerAppPage), DrawnIcon: "input-language-per-app"),
            new(ShortcutsAndMouseButtonsFeature.Definition, ShortcutsAndMouseButtonsFeature.CurrentSupport, typeof(ShortcutsAndMouseButtonsPage), Actions: _ => ShortcutsAndMouseButtonsFeature.OfferedActions()),
        ]),
        new(WindowsModule.Id, "ModuleWindowsName",
        [
            new(AppWindowsFeature.Definition, AppWindowsFeature.CurrentSupport, typeof(AppWindowsPage), DrawnIcon: "app-windows", Actions: _ => AppWindowsFeature.OfferedActions(), Shortcuts: section => AppWindowsFeature.Shortcuts(AppWindowsSettings.Read(section))),
            new(MinimizeToTrayFeature.Definition, MinimizeToTrayFeature.CurrentSupport, typeof(MinimizeToTrayPage), DrawnIcon: "minimize-to-tray", Actions: _ => MinimizeToTrayFeature.OfferedActions(), Shortcuts: section => MinimizeToTrayFeature.Shortcuts(MinimizeToTraySettings.Read(section))),
            new(HideAppsFeature.Definition, HideAppsFeature.CurrentSupport, typeof(HideAppsPage), DrawnIcon: "hide-apps", Actions: _ => HideAppsFeature.OfferedActions(), Shortcuts: section => HideAppsFeature.Shortcuts(HideAppsSettings.Read(section))),
            new(DropDownAppsFeature.Definition, DropDownAppsFeature.CurrentSupport, typeof(DropDownAppsPage), DrawnIcon: "drop-down-apps", Actions: section => DropDownAppsFeature.OfferedActions(DropDownAppsSettings.Read(section)), Shortcuts: section => DropDownAppsFeature.Shortcuts(DropDownAppsSettings.Read(section))),
            new(TitleBarActionsFeature.Definition, TitleBarActionsFeature.CurrentSupport, typeof(TitleBarActionsPage), DrawnIcon: "title-bar-actions"),
            new(EndAppFeature.Definition, EndAppFeature.CurrentSupport, typeof(EndAppPage), DrawnIcon: "end-app", Actions: _ => EndAppFeature.OfferedActions(), Shortcuts: section => EndAppFeature.Shortcuts(EndAppSettings.Read(section))),
        ]),
        new(DesktopModule.Id, "ModuleDesktopName",
        [
            new(SystemMetersFeature.Definition, SystemMetersFeature.CurrentSupport, typeof(SystemMetersPage), DrawnIcon: "system-meters"),
            new(DynamicWallpaperFeature.Definition, DynamicWallpaperFeature.CurrentSupport, typeof(DynamicWallpaperPage), DynamicWallpaperFirstSettings.Create, DrawnIcon: "dynamic-wallpaper"),
        ]),
        new(ClipboardModule.Id, "ModuleClipboardName",
        [
            new(ClipboardActionsFeature.Definition, ClipboardActionsFeature.CurrentSupport, typeof(ClipboardActionsPage), DrawnIcon: "clipboard-actions", Actions: section => ClipboardActionsFeature.OfferedActions(ClipboardActionsSettings.Read(section)), Shortcuts: section => ClipboardActionsFeature.Shortcuts(ClipboardActionsSettings.Read(section))),
            new(MiddleClickPasteFeature.Definition, MiddleClickPasteFeature.CurrentSupport, typeof(MiddleClickPastePage), DrawnIcon: "middle-click-paste"),
        ]),
        new(NetworkModule.Id, "ModuleNetworkName",
        [
            new(NetworkProfilesFeature.Definition, NetworkProfilesFeature.CurrentSupport, typeof(NetworkProfilesPage), NetworkProfilesFirstSettings.Create, DrawnIcon: "network-profiles", Actions: section => NetworkProfilesFeature.OfferedActions(NetworkProfilesSettings.Read(section))),
        ]),
        new(FilesModule.Id, "ModuleFilesName",
        [
            new(FolderRulesFeature.Definition, FolderRulesFeature.CurrentSupport, typeof(FolderRulesPage), DrawnIcon: "folder-rules"),
            new(DriveActionsFeature.Definition, DriveActionsFeature.CurrentSupport, typeof(DriveActionsPage), DrawnIcon: "drive-actions"),
        ]),
        new(DisplayModule.Id, "ModuleDisplayName",
        [
            new(DisplayProfilesFeature.Definition, DisplayProfilesFeature.CurrentSupport, typeof(DisplayProfilesPage), DisplayProfilesFirstSettings.Create, DrawnIcon: "display-profiles", Actions: section => DisplayProfilesFeature.OfferedActions(DisplayProfilesSettings.Read(section))),
        ]),
    ];

    public static IEnumerable<CatalogFeature> Features => Modules.SelectMany(module => module.Features);

    /// <summary>The feature with the ID <paramref name="featureId"/>, or null.</summary>
    public static CatalogFeature? Find(string featureId) => Features.FirstOrDefault(feature => feature.Info.Id == featureId);
}
