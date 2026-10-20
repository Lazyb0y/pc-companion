using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.App.Features.AppWindows;
using PCCompanion.App.Features.ClipboardActions;
using PCCompanion.App.Features.HotCorners;
using PCCompanion.App.Features.SystemMeters;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Clipboard;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Modules.Desktop;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Modules.Input;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Modules.Windows;
using PCCompanion.Modules.Windows.AppWindows;

namespace PCCompanion.App.Features;

/// <param name="NameKey">The <c>Strings</c> key of the module's name.</param>
public sealed record CatalogModule(string Id, string NameKey, IReadOnlyList<CatalogFeature> Features);

/// <param name="PageType">The feature's page in the settings app.</param>
public sealed record CatalogFeature(FeatureInfo Info, FeatureSupport Support, Type PageType);

/// <summary>
/// Every module and feature, in the order the navigation pane and Home show them.
/// </summary>
public static class FeatureCatalog
{
    public static IReadOnlyList<CatalogModule> Modules { get; } =
    [
        new(InputModule.Id, "ModuleInputName",
        [
            new(HotCornersFeature.Definition, HotCornersFeature.CurrentSupport, typeof(HotCornersPage)),
        ]),
        new(WindowsModule.Id, "ModuleWindowsName",
        [
            new(AppWindowsFeature.Definition, AppWindowsFeature.CurrentSupport, typeof(AppWindowsPage)),
        ]),
        new(DesktopModule.Id, "ModuleDesktopName",
        [
            new(SystemMetersFeature.Definition, SystemMetersFeature.CurrentSupport, typeof(SystemMetersPage)),
        ]),
        new(ClipboardModule.Id, "ModuleClipboardName",
        [
            new(ClipboardActionsFeature.Definition, ClipboardActionsFeature.CurrentSupport, typeof(ClipboardActionsPage)),
        ]),
    ];

    public static IEnumerable<CatalogFeature> Features => Modules.SelectMany(module => module.Features);
}
