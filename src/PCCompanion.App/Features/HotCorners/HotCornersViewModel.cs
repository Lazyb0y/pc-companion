using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.HotCorners;

namespace PCCompanion.App.Features.HotCorners;

public sealed record ActionOption(HotCornerAction Action, string DisplayName);

public sealed record KeyOption(HotCornerKey Key, string DisplayName);

/// <summary>
/// The Hot Corners page. Every change is saved right away.
/// </summary>
public sealed partial class HotCornersViewModel : ObservableObject
{
    private static readonly CompositeFormat DelayFormat = CompositeFormat.Parse(Strings.HotCornersDelayValue);

    private readonly FeatureSettingsEditor _editor;
    private HotCornersSettings _settings;
    private bool _saveFailed;

    public HotCornersViewModel(FeatureSettingsEditor editor)
    {
        _editor = editor;
        _settings = editor.Read(HotCornersFeature.Definition.Id, HotCornersSettings.Read);

        Actions =
        [
            new(HotCornerAction.None, Strings.HotCornersActionNone),
            new(HotCornerAction.TaskView, Strings.HotCornersActionTaskView),
            new(HotCornerAction.ShowDesktop, Strings.HotCornersActionShowDesktop),
            new(HotCornerAction.Start, Strings.HotCornersActionStart),
            new(HotCornerAction.Search, Strings.HotCornersActionSearch),
            new(HotCornerAction.Notifications, Strings.HotCornersActionNotifications),
            new(HotCornerAction.Lock, Strings.HotCornersActionLock),
            new(HotCornerAction.TurnOffDisplays, Strings.HotCornersActionTurnOffDisplays),
            new(HotCornerAction.ScreenSaver, Strings.HotCornersActionScreenSaver),
        ];

        Keys =
        [
            new(HotCornerKey.None, Strings.HotCornersKeyNone),
            new(HotCornerKey.Control, Strings.HotCornersKeyControl),
            new(HotCornerKey.Alt, Strings.HotCornersKeyAlt),
            new(HotCornerKey.Shift, Strings.HotCornersKeyShift),
            new(HotCornerKey.Windows, Strings.HotCornersKeyWindows),
        ];

        TopLeft = new CornerViewModel(this, ScreenCorner.TopLeft, Strings.HotCornersTopLeft);
        TopRight = new CornerViewModel(this, ScreenCorner.TopRight, Strings.HotCornersTopRight);
        BottomLeft = new CornerViewModel(this, ScreenCorner.BottomLeft, Strings.HotCornersBottomLeft);
        BottomRight = new CornerViewModel(this, ScreenCorner.BottomRight, Strings.HotCornersBottomRight);
    }

    public static string Glyph => HotCornersFeature.Definition.Glyph;

    public IReadOnlyList<ActionOption> Actions { get; }

    public IReadOnlyList<KeyOption> Keys { get; }

    public CornerViewModel TopLeft { get; }

    public CornerViewModel TopRight { get; }

    public CornerViewModel BottomLeft { get; }

    public CornerViewModel BottomRight { get; }

    public bool IsOn
    {
        get => _settings.Enabled;
        set
        {
            if (value != _settings.Enabled)
            {
                Save(_settings with { Enabled = value });
                OnPropertyChanged();
            }
        }
    }

    public double ActivationDelay
    {
        get => _settings.ActivationDelayMilliseconds;
        set
        {
            var milliseconds = (int)Math.Round(value);
            if (milliseconds != _settings.ActivationDelayMilliseconds)
            {
                Save(_settings with { ActivationDelayMilliseconds = milliseconds });
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActivationDelayText));
            }
        }
    }

    public string ActivationDelayText => string.Format(CultureInfo.CurrentCulture, DelayFormat, _settings.ActivationDelayMilliseconds);

    public static double MaximumActivationDelay => HotCornersSettings.MaximumActivationDelayMilliseconds;

    public bool IgnoreFullScreenApps
    {
        get => _settings.IgnoreFullScreenApps;
        set
        {
            if (value != _settings.IgnoreFullScreenApps)
            {
                Save(_settings with { IgnoreFullScreenApps = value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>True when the last change couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => _saveFailed;
        private set => SetProperty(ref _saveFailed, value);
    }

    internal CornerSettings GetCorner(ScreenCorner corner) => _settings.For(corner);

    internal void SetCorner(ScreenCorner corner, CornerSettings value) => Save(corner switch
    {
        ScreenCorner.TopLeft => _settings with { TopLeft = value },
        ScreenCorner.TopRight => _settings with { TopRight = value },
        ScreenCorner.BottomLeft => _settings with { BottomLeft = value },
        _ => _settings with { BottomRight = value },
    });

    private void Save(HotCornersSettings settings)
    {
        _settings = settings;
        SaveFailed = !_editor.Save(HotCornersFeature.Definition.Id, settings.Write());
    }
}

/// <summary>
/// One corner on the Hot Corners page.
/// </summary>
public sealed partial class CornerViewModel : ObservableObject
{
    private static readonly CompositeFormat ActionNameFormat = CompositeFormat.Parse(Strings.HotCornersActionName);
    private static readonly CompositeFormat KeyNameFormat = CompositeFormat.Parse(Strings.HotCornersKeyName);

    private readonly HotCornersViewModel _page;
    private readonly ScreenCorner _corner;
    private ActionOption _selectedAction;
    private KeyOption _selectedKey;

    public CornerViewModel(HotCornersViewModel page, ScreenCorner corner, string name)
    {
        ArgumentNullException.ThrowIfNull(page);

        _page = page;
        _corner = corner;
        Name = name;
        ActionName = string.Format(CultureInfo.CurrentCulture, ActionNameFormat, name);
        KeyName = string.Format(CultureInfo.CurrentCulture, KeyNameFormat, name);

        var settings = page.GetCorner(corner);
        _selectedAction = page.Actions.First(option => option.Action == settings.Action);
        _selectedKey = page.Keys.First(option => option.Key == settings.Key);
    }

    public string Name { get; }

    /// <summary>The screen reader name of the action list.</summary>
    public string ActionName { get; }

    /// <summary>The screen reader name of the key list.</summary>
    public string KeyName { get; }

    public IReadOnlyList<ActionOption> Actions => _page.Actions;

    public IReadOnlyList<KeyOption> Keys => _page.Keys;

    public ActionOption SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (value is not null && SetProperty(ref _selectedAction, value))
            {
                _page.SetCorner(_corner, _page.GetCorner(_corner) with { Action = value.Action });
            }
        }
    }

    public KeyOption SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (value is not null && SetProperty(ref _selectedKey, value))
            {
                _page.SetCorner(_corner, _page.GetCorner(_corner) with { Key = value.Key });
            }
        }
    }
}
