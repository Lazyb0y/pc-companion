using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.HotCorners;

namespace PCCompanion.App.Features.HotCorners;

public sealed record ActionOption(HotCornerAction Action, string DisplayName);

public sealed record KeyOption(HotCornerKey Key, string DisplayName);

/// <summary>
/// The Hot Corners page. Every change is saved right away.
/// </summary>
public sealed partial class HotCornersViewModel : FeatureViewModel<HotCornersSettings>
{
    private static readonly CompositeFormat DelayFormat = CompositeFormat.Parse(Strings.HotCornersDelayValue);

    private readonly AppSettings _appSettings;

    public HotCornersViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, HotCornersFeature.Definition)
    {
        _appSettings = appSettings;

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
            new(HotCornerAction.KeepDisplaysOn, Strings.HotCornersActionKeepDisplaysOn),
            new(HotCornerAction.RunProgram, Strings.HotCornersActionRunProgram),
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

    /// <summary>What happens while an app running as administrator is in front.</summary>
    public string AdministratorInfo => _appSettings.General.AlwaysRunAsAdministrator
        ? Strings.HotCornersAdministratorInfoOn
        : Strings.HotCornersAdministratorInfo;

    public IReadOnlyList<ActionOption> Actions { get; }

    public IReadOnlyList<KeyOption> Keys { get; }

    public CornerViewModel TopLeft { get; }

    public CornerViewModel TopRight { get; }

    public CornerViewModel BottomLeft { get; }

    public CornerViewModel BottomRight { get; }

    public double ActivationDelay
    {
        get => Settings.ActivationDelayMilliseconds;
        set
        {
            var milliseconds = (int)Math.Round(value);
            if (Change(Settings.ActivationDelayMilliseconds, milliseconds, () => Settings with { ActivationDelayMilliseconds = milliseconds }))
            {
                OnPropertyChanged(nameof(ActivationDelayText));
            }
        }
    }

    public string ActivationDelayText => string.Format(CultureInfo.CurrentCulture, DelayFormat, Settings.ActivationDelayMilliseconds);

    public static double MaximumActivationDelay => HotCornersSettings.MaximumActivationDelayMilliseconds;

    public bool IgnoreFullScreenApps
    {
        get => Settings.IgnoreFullScreenApps;
        set => Change(Settings.IgnoreFullScreenApps, value, () => Settings with { IgnoreFullScreenApps = value });
    }

    internal CornerSettings GetCorner(ScreenCorner corner) => Settings.For(corner);

    internal void SetCorner(ScreenCorner corner, CornerSettings value) => Save(corner switch
    {
        ScreenCorner.TopLeft => Settings with { TopLeft = value },
        ScreenCorner.TopRight => Settings with { TopRight = value },
        ScreenCorner.BottomLeft => Settings with { BottomLeft = value },
        _ => Settings with { BottomRight = value },
    });
}

/// <summary>
/// One corner on the Hot Corners page.
/// </summary>
public sealed partial class CornerViewModel : ObservableObject
{
    private static readonly CompositeFormat ActionNameFormat = CompositeFormat.Parse(Strings.HotCornersActionName);
    private static readonly CompositeFormat KeyNameFormat = CompositeFormat.Parse(Strings.HotCornersKeyName);
    private static readonly CompositeFormat ProgramNameFormat = CompositeFormat.Parse(Strings.HotCornersProgramName);
    private static readonly CompositeFormat ArgumentsNameFormat = CompositeFormat.Parse(Strings.HotCornersArgumentsName);
    private static readonly CompositeFormat BrowseNameFormat = CompositeFormat.Parse(Strings.HotCornersBrowseName);

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
        ProgramName = string.Format(CultureInfo.CurrentCulture, ProgramNameFormat, name);
        ArgumentsName = string.Format(CultureInfo.CurrentCulture, ArgumentsNameFormat, name);
        BrowseName = string.Format(CultureInfo.CurrentCulture, BrowseNameFormat, name);

        var settings = page.GetCorner(corner);
        _selectedAction = page.Actions.First(option => option.Action == settings.Action);
        _selectedKey = page.Keys.First(option => option.Key == settings.Key);
    }

    public string Name { get; }

    /// <summary>The screen reader name of the action list.</summary>
    public string ActionName { get; }

    /// <summary>The screen reader name of the key list.</summary>
    public string KeyName { get; }

    /// <summary>The screen reader name of the program box.</summary>
    public string ProgramName { get; }

    /// <summary>The screen reader name of the arguments box.</summary>
    public string ArgumentsName { get; }

    /// <summary>The screen reader name of the Browse button.</summary>
    public string BrowseName { get; }

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
                OnPropertyChanged(nameof(IsProgramShown));
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

    /// <summary>Whether the program and its arguments are shown, for Run a program.</summary>
    public bool IsProgramShown => _selectedAction.Action == HotCornerAction.RunProgram;

    /// <summary>
    /// The full path of the program. Quotes around it are removed, as File Explorer's
    /// Copy as path adds them.
    /// </summary>
    public string Program
    {
        get => _page.GetCorner(_corner).Program ?? string.Empty;
        set
        {
            var program = value?.Trim().Trim('"').Trim();
            program = string.IsNullOrEmpty(program) ? null : program;
            if (program != _page.GetCorner(_corner).Program)
            {
                _page.SetCorner(_corner, _page.GetCorner(_corner) with { Program = program });
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgramProblem));
                OnPropertyChanged(nameof(HasProgramProblem));
            }
        }
    }

    public string Arguments
    {
        get => _page.GetCorner(_corner).Arguments ?? string.Empty;
        set
        {
            var arguments = string.IsNullOrWhiteSpace(value) ? null : value;
            if (arguments != _page.GetCorner(_corner).Arguments)
            {
                _page.SetCorner(_corner, _page.GetCorner(_corner) with { Arguments = arguments });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>What keeps the program from starting, or an empty string.</summary>
    public string ProgramProblem => Platform.Windows.ProgramFile.Check(_page.GetCorner(_corner).Program, File.Exists) switch
    {
        Platform.Windows.ProgramProblem.NotChosen => Strings.HotCornersProgramNotChosen,
        Platform.Windows.ProgramProblem.NotFullPath => Strings.HotCornersProgramNotFullPath,
        Platform.Windows.ProgramProblem.NotExe => Strings.HotCornersProgramNotExe,
        Platform.Windows.ProgramProblem.NotFound => Strings.HotCornersProgramNotFound,
        _ => string.Empty,
    };

    public bool HasProgramProblem => ProgramProblem.Length > 0;
}
