using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.HotCorners;

public sealed record ActionOption(HotCornerAction Action, string DisplayName);

public sealed record KeyOption(HotCornerKey Key, string DisplayName);

public sealed record WheelOption(HotCornerWheelAction Action, string DisplayName);

public sealed record TriggerOption(CornersTrigger Trigger, string DisplayName) : Choice(DisplayName);

public sealed record PushOption(PushStrength Strength, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Hot Corners &amp; Edges page. Every change is saved right away.
/// </summary>
public sealed partial class HotCornersViewModel : FeatureViewModel<HotCornersSettings>
{
    private static readonly CompositeFormat DelayFormat = CompositeFormat.Parse(Strings.HotCornersDelayValue);
    private static readonly TimeSpan ResultsTimeout = TimeSpan.FromMilliseconds(500);

    private readonly AppSettings _appSettings;
    private readonly CornerResultsReading _resultsReading = new();
    private DispatcherQueueTimer? _resultsTimer;
    private bool _readingResults;
    private string _resultsMessage = string.Empty;

    public HotCornersViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, HotCornersFeature.Definition)
    {
        _appSettings = appSettings;

        Actions = [.. HotCornerActionNames.InPageOrder.Select(action => new ActionOption(action, HotCornerActionNames.NameOf(action)))];

        Keys =
        [
            new(HotCornerKey.None, Strings.HotCornersKeyNone),
            new(HotCornerKey.Control, Strings.HotCornersKeyControl),
            new(HotCornerKey.Alt, Strings.HotCornersKeyAlt),
            new(HotCornerKey.Shift, Strings.HotCornersKeyShift),
            new(HotCornerKey.Windows, Strings.HotCornersKeyWindows),
        ];

        Wheels =
        [
            new(HotCornerWheelAction.None, Strings.HotCornersActionNone),
            new(HotCornerWheelAction.Volume, Strings.HotCornersWheelVolume),
            new(HotCornerWheelAction.Desktops, Strings.HotCornersWheelDesktops),
            new(HotCornerWheelAction.Tracks, Strings.HotCornersWheelTracks),
        ];

        Triggers =
        [
            new(CornersTrigger.Delay, Strings.HotCornersTriggerDelay),
            new(CornersTrigger.Push, Strings.HotCornersTriggerPush),
        ];

        PushStrengths =
        [
            new(PushStrength.Light, Strings.HotCornersPushLight),
            new(PushStrength.Medium, Strings.HotCornersPushMedium),
            new(PushStrength.Firm, Strings.HotCornersPushFirm),
        ];

        TopLeft = new CornerViewModel(this, ScreenCorner.TopLeft, CornerResultText.CornerName(ScreenCorner.TopLeft));
        TopRight = new CornerViewModel(this, ScreenCorner.TopRight, CornerResultText.CornerName(ScreenCorner.TopRight));
        BottomLeft = new CornerViewModel(this, ScreenCorner.BottomLeft, CornerResultText.CornerName(ScreenCorner.BottomLeft));
        BottomRight = new CornerViewModel(this, ScreenCorner.BottomRight, CornerResultText.CornerName(ScreenCorner.BottomRight));
        Top = new CornerViewModel(this, ScreenCorner.Top, CornerResultText.CornerName(ScreenCorner.Top));
        Bottom = new CornerViewModel(this, ScreenCorner.Bottom, CornerResultText.CornerName(ScreenCorner.Bottom));
        Left = new CornerViewModel(this, ScreenCorner.Left, CornerResultText.CornerName(ScreenCorner.Left));
        Right = new CornerViewModel(this, ScreenCorner.Right, CornerResultText.CornerName(ScreenCorner.Right));
        ExcludedApps = new AppListViewModel(
            () => Settings.ExcludedApps,
            app => SaveExcludedApps(Settings.WithExcludedApp(app)),
            index => SaveExcludedApps(Settings.WithoutExcludedApp(index)));
    }

    /// <summary>What happens while an app running as administrator is in front, with the host's rights now.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.HotCornersAdministratorInfoOn
        : Strings.HotCornersAdministratorInfo;

    public IReadOnlyList<ActionOption> Actions { get; }

    public IReadOnlyList<KeyOption> Keys { get; }

    public IReadOnlyList<WheelOption> Wheels { get; }

    public IReadOnlyList<TriggerOption> Triggers { get; }

    public IReadOnlyList<PushOption> PushStrengths { get; }

    public CornerViewModel TopLeft { get; }

    public CornerViewModel TopRight { get; }

    public CornerViewModel BottomLeft { get; }

    public CornerViewModel BottomRight { get; }

    public CornerViewModel Top { get; }

    public CornerViewModel Bottom { get; }

    public CornerViewModel Left { get; }

    public CornerViewModel Right { get; }

    public TriggerOption? SelectedTrigger
    {
        get => Triggers.FirstOrDefault(option => option.Trigger == Settings.CornersTrigger);
        set
        {
            if (value is not null)
            {
                Change(Settings.CornersTrigger, value.Trigger, () => Settings with { CornersTrigger = value.Trigger });
            }
        }
    }

    public PushOption? SelectedPushStrength
    {
        get => PushStrengths.FirstOrDefault(option => option.Strength == Settings.PushStrength);
        set
        {
            if (value is not null)
            {
                Change(Settings.PushStrength, value.Strength, () => Settings with { PushStrength = value.Strength });
            }
        }
    }

    /// <summary>Whether the right edge has a wheel action, which takes the wheel from a maximized window's scroll bar there.</summary>
    public bool IsRightWheelNoteShown => Settings.Right.Wheel != HotCornerWheelAction.None;

    /// <summary>Whether the top edge has a wheel action, which takes the wheel from Title Bar Actions there.</summary>
    public bool IsTopWheelNoteShown => Settings.Top.Wheel != HotCornerWheelAction.None;

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

    public string ActivationDelayText => ReadingOrder.Format(DelayFormat, Settings.ActivationDelayMilliseconds);

    public static double MaximumActivationDelay => HotCornersSettings.MaximumActivationDelayMilliseconds;

    public bool IgnoreFullScreenApps
    {
        get => Settings.IgnoreFullScreenApps;
        set => Change(Settings.IgnoreFullScreenApps, value, () => Settings with { IgnoreFullScreenApps = value });
    }

    /// <summary>The apps in front of which corners don't trigger.</summary>
    public AppListViewModel ExcludedApps { get; }

    private bool SaveExcludedApps(HotCornersSettings settings)
    {
        if (settings == Settings)
        {
            return false;
        }

        Save(settings);
        return true;
    }

    /// <summary>The recent results, newest first.</summary>
    public ObservableCollection<CornerResultItem> Results { get; } = [];

    /// <summary>Why there are no results to show, or an empty string.</summary>
    public string ResultsMessage
    {
        get => _resultsMessage;
        private set
        {
            if (SetProperty(ref _resultsMessage, value))
            {
                OnPropertyChanged(nameof(HasResultsMessage));
            }
        }
    }

    public bool HasResultsMessage => ResultsMessage.Length > 0;

    /// <summary>
    /// Shows the recent results as they come, asking the host about once a second, until
    /// <see cref="StopResults"/>. Call it on the page's thread.
    /// </summary>
    public void StartResults()
    {
        if (_resultsTimer is not null)
        {
            return;
        }

        _resultsTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _resultsTimer.Interval = TimeSpan.FromSeconds(1);
        _resultsTimer.Tick += (_, _) => _ = RefreshResultsAsync();
        _resultsTimer.Start();
        _ = RefreshResultsAsync();
    }

    public void StopResults()
    {
        _resultsTimer?.Stop();
        _resultsTimer = null;
    }

    // The pipe is read off the UI thread; a read still going when the next tick comes is let finish.
    private async Task RefreshResultsAsync()
    {
        if (_readingResults)
        {
            return;
        }

        _readingResults = true;
        try
        {
            var isOn = IsOn;
            var snapshot = isOn
                ? await Task.Run(() => SnapshotPipe.ReadAsync(CornerResults.PipeName, ResultsTimeout, CornerResults.MaximumSnapshotLength)).ConfigureAwait(true)
                : null;
            ShowResults(isOn, snapshot is null ? null : CornerResults.FromJson(snapshot));
        }
        finally
        {
            _readingResults = false;
        }
    }

    private void ShowResults(bool isOn, IReadOnlyList<CornerResult>? results)
    {
        if (!_resultsReading.Read(isOn, results))
        {
            return;
        }

        ResultsMessage = _resultsReading.Note switch
        {
            CornerResultsNote.HotCornersOff => Strings.HotCornersResultsUnavailable,
            CornerResultsNote.NoCornerReached => Strings.HotCornersResultsNone,
            CornerResultsNote.NotAvailable => Strings.HotCornersResultsNotAvailable,
            _ => string.Empty,
        };

        Results.Clear();
        foreach (var result in _resultsReading.Results)
        {
            Results.Add(new CornerResultItem(result));
        }
    }

    internal CornerSettings GetCorner(ScreenCorner corner) => Settings.For(corner);

    internal void SetCorner(ScreenCorner corner, CornerSettings value)
    {
        Save(Settings.With(corner, value));
        OnPropertyChanged(nameof(IsRightWheelNoteShown));
        OnPropertyChanged(nameof(IsTopWheelNoteShown));
    }
}

/// <summary>
/// One corner or edge on the Hot Corners &amp; Edges page.
/// </summary>
public sealed partial class CornerViewModel : ObservableObject
{
    private static readonly CompositeFormat ActionNameFormat = CompositeFormat.Parse(Strings.HotCornersActionName);
    private static readonly CompositeFormat KeyNameFormat = CompositeFormat.Parse(Strings.HotCornersKeyName);
    private static readonly CompositeFormat ProgramNameFormat = CompositeFormat.Parse(Strings.HotCornersProgramName);
    private static readonly CompositeFormat ArgumentsNameFormat = CompositeFormat.Parse(Strings.HotCornersArgumentsName);
    private static readonly CompositeFormat BrowseNameFormat = CompositeFormat.Parse(Strings.HotCornersBrowseName);
    private static readonly CompositeFormat WheelNameFormat = CompositeFormat.Parse(Strings.HotCornersWheelName);
    private static readonly CompositeFormat WheelKeyNameFormat = CompositeFormat.Parse(Strings.HotCornersWheelKeyName);

    private readonly HotCornersViewModel _page;
    private readonly ScreenCorner _corner;
    private ActionOption _selectedAction;
    private KeyOption _selectedKey;
    private WheelOption _selectedWheel;
    private KeyOption _selectedWheelKey;
    private string _programProblem = string.Empty;

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
        WheelName = string.Format(CultureInfo.CurrentCulture, WheelNameFormat, name);
        WheelKeyName = string.Format(CultureInfo.CurrentCulture, WheelKeyNameFormat, name);

        var settings = page.GetCorner(corner);
        _selectedAction = page.Actions.First(option => option.Action == settings.Action);
        _selectedKey = page.Keys.First(option => option.Key == settings.Key);
        _selectedWheel = page.Wheels.First(option => option.Action == settings.Wheel);
        _selectedWheelKey = page.Keys.First(option => option.Key == settings.WheelKey);
        _ = CheckProgramAsync();
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

    /// <summary>The screen reader name of the wheel action list.</summary>
    public string WheelName { get; }

    /// <summary>The screen reader name of the wheel's key list.</summary>
    public string WheelKeyName { get; }

    public IReadOnlyList<ActionOption> Actions => _page.Actions;

    public IReadOnlyList<KeyOption> Keys => _page.Keys;

    public IReadOnlyList<WheelOption> Wheels => _page.Wheels;

    public WheelOption SelectedWheel
    {
        get => _selectedWheel;
        set
        {
            if (value is not null && SetProperty(ref _selectedWheel, value))
            {
                _page.SetCorner(_corner, _page.GetCorner(_corner) with { Wheel = value.Action });
                OnPropertyChanged(nameof(IsWheelKeyShown));
            }
        }
    }

    public KeyOption SelectedWheelKey
    {
        get => _selectedWheelKey;
        set
        {
            if (value is not null && SetProperty(ref _selectedWheelKey, value))
            {
                _page.SetCorner(_corner, _page.GetCorner(_corner) with { WheelKey = value.Key });
            }
        }
    }

    /// <summary>Whether the wheel's key list is shown: only while the wheel has an action.</summary>
    public bool IsWheelKeyShown => _selectedWheel.Action != HotCornerWheelAction.None;

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
                _ = CheckProgramAsync();
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

    /// <summary>What keeps the program from starting, or an empty string, also while it's being checked.</summary>
    public string ProgramProblem
    {
        get => _programProblem;
        private set
        {
            if (SetProperty(ref _programProblem, value))
            {
                OnPropertyChanged(nameof(HasProgramProblem));
            }
        }
    }

    public bool HasProgramProblem => ProgramProblem.Length > 0;

    // The path's form is checked at once. Whether the file exists is read off the UI thread, since a
    // program on a network share that's offline can take seconds to find, and the result is dropped
    // when the program changed meanwhile.
    private async Task CheckProgramAsync()
    {
        var program = _page.GetCorner(_corner).Program;
        var problem = Platform.Windows.ProgramFile.Check(program, _ => true);
        if (problem == Platform.Windows.ProgramProblem.None)
        {
            ProgramProblem = string.Empty;
            problem = await Task.Run(() => Platform.Windows.ProgramFile.Check(program, File.Exists)).ConfigureAwait(true);
            if (program != _page.GetCorner(_corner).Program)
            {
                return;
            }
        }

        ProgramProblem = problem switch
        {
            Platform.Windows.ProgramProblem.NotChosen => Strings.HotCornersProgramNotChosen,
            Platform.Windows.ProgramProblem.NotFullPath => Strings.HotCornersProgramNotFullPath,
            Platform.Windows.ProgramProblem.NotExe => Strings.HotCornersProgramNotExe,
            Platform.Windows.ProgramProblem.NotFound => Strings.HotCornersProgramNotFound,
            _ => string.Empty,
        };
    }
}

/// <summary>
/// A recent result on the Hot Corners &amp; Edges page.
/// </summary>
public sealed class CornerResultItem(CornerResult result)
{
    public string Text { get; } = ReadingOrder.Line(CornerResultText.Describe(result));

    /// <summary>When it happened, and how many times in a row.</summary>
    public string When { get; } = ReadingOrder.Line(CornerResultText.When(result));

    /// <summary>A check mark for an action that ran, otherwise an information icon.</summary>
    public string Glyph { get; } = result.Kind == CornerResultKind.Ran ? "" : "";
}
