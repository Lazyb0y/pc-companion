using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.App.Features;
using PCCompanion.Localization;

namespace PCCompanion.App.ViewModels;

public sealed record ModuleGroup(string Name, IReadOnlyList<FeatureCardViewModel> Features);

/// <summary>
/// Home: a card per feature, grouped by module. Every change is saved right away.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private bool _saveFailed;

    public HomeViewModel(FeatureSettingsEditor editor)
    {
        Modules =
        [
            .. FeatureCatalog.Modules.Select(module => new ModuleGroup(
                Localized.GetString(module.NameKey),
                [.. module.Features.Select(feature => new FeatureCardViewModel(feature, editor, saved => SaveFailed = !saved))])),
        ];
    }

    public IReadOnlyList<ModuleGroup> Modules { get; }

    /// <summary>True when the last change couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => _saveFailed;
        private set => SetProperty(ref _saveFailed, value);
    }
}

/// <summary>
/// A feature's card on Home.
/// </summary>
public sealed partial class FeatureCardViewModel : ObservableObject
{
    private readonly FeatureSettingsEditor _editor;
    private readonly CatalogFeature _feature;
    private readonly Action<bool> _saved;
    private bool _isOn;

    /// <param name="saved">Called after every change with whether it was saved.</param>
    public FeatureCardViewModel(CatalogFeature feature, FeatureSettingsEditor editor, Action<bool> saved)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(editor);

        _editor = editor;
        _feature = feature;
        _saved = saved;
        Id = feature.Info.Id;
        Name = Localized.GetString(feature.Info.NameKey);
        Glyph = feature.Info.Glyph;
        IsSupported = feature.Support.IsSupported;
        Description = IsSupported ? Localized.GetString(feature.Info.DescriptionKey) : Strings.FeatureNotAvailable;
        _isOn = IsSupported && editor.IsEnabled(Id);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Glyph { get; }

    public bool IsSupported { get; }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
            {
                _saved(_editor.SetEnabled(Id, value, _feature.FirstSettings));
            }
        }
    }
}
