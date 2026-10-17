using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.App.Features.HotCorners;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input;
using PCCompanion.Modules.Input.HotCorners;

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
    ];

    public static IEnumerable<CatalogFeature> Features => Modules.SelectMany(module => module.Features);
}
