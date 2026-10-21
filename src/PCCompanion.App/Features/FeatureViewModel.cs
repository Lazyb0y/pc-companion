using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Core.Features;

namespace PCCompanion.App.Features;

/// <summary>
/// A feature page's view model. It reads the feature's settings, saves every change right away
/// and says when a change couldn't be saved.
/// </summary>
public abstract class FeatureViewModel<TSettings> : ObservableObject
    where TSettings : class, IFeatureSettings<TSettings>
{
    private readonly FeatureSettingsEditor _editor;
    private readonly FeatureInfo _feature;
    private bool _saveFailed;

    protected FeatureViewModel(FeatureSettingsEditor editor, FeatureInfo feature)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(feature);

        _editor = editor;
        _feature = feature;
        Settings = editor.Read(feature.Id, section => TSettings.Read(section));
    }

    /// <summary>The feature's icon, a glyph of the icon font.</summary>
    public string Glyph => _feature.Glyph;

    public bool IsOn
    {
        get => Settings.Enabled;
        set => Change(Settings.Enabled, value, () => Settings.WithEnabled(value));
    }

    /// <summary>True when the last change couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => _saveFailed;
        private set => SetProperty(ref _saveFailed, value);
    }

    /// <summary>The settings with every change made on the page, saved or not.</summary>
    protected TSettings Settings { get; private set; }

    /// <summary>
    /// When <paramref name="value"/> differs from <paramref name="current"/>, saves the settings
    /// <paramref name="changed"/> returns and tells the page that the property changed. Returns
    /// whether it did.
    /// </summary>
    protected bool Change<T>(T current, T value, Func<TSettings> changed, [CallerMemberName] string? propertyName = null)
    {
        ArgumentNullException.ThrowIfNull(changed);

        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        Save(changed());
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Saves the settings right away; <see cref="SaveFailed"/> says whether that worked.</summary>
    protected void Save(TSettings settings)
    {
        Settings = settings;
        SaveFailed = !_editor.Save(_feature.Id, settings.Write());
    }
}
