using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
                [.. module.Features.Select(feature => new FeatureCardViewModel(feature, editor, saved => SaveFailed = !saved, id => FeatureOpenRequested?.Invoke(this, id)))])),
        ];
    }

    /// <summary>Raised with a feature's ID when its card asks for the feature's page.</summary>
    public event EventHandler<string>? FeatureOpenRequested;

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
    private readonly Action<string> _open;
    private bool _isOn;

    /// <param name="saved">Called after every change with whether it was saved.</param>
    /// <param name="open">Called with the feature's ID to open its page.</param>
    public FeatureCardViewModel(CatalogFeature feature, FeatureSettingsEditor editor, Action<bool> saved, Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(editor);

        _editor = editor;
        _feature = feature;
        _saved = saved;
        _open = open;
        Id = feature.Info.Id;
        Name = Localized.GetString(feature.Info.NameKey);
        Glyph = feature.Info.Glyph;
        DrawnIcon = feature.DrawnIcon;
        IsSupported = feature.Support.IsSupported;
        Description = IsSupported ? Localized.GetString(feature.Info.DescriptionKey) : Strings.FeatureNotAvailable;
        _isOn = IsSupported && editor.IsEnabled(Id);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Glyph { get; }

    /// <summary>The name of the feature's drawing, which the card shows instead of the glyph, or null.</summary>
    public string? DrawnIcon { get; }

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

    /// <summary>Opens the feature's page, when the card is clicked or a screen reader opens it.</summary>
    [RelayCommand]
    private void Open() => _open(Id);
}
