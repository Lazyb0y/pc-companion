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
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.HotCorners;

public sealed record ActionOption(HotCornerAction Action, string DisplayName);

public sealed record KeyOption(HotCornerKey Key, string DisplayName);

/// <summary>
/// The Hot Corners page. Every change is saved right away.
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
        RefreshExcludedApps();
    }

    /// <summary>What happens while an app running as administrator is in front, with the host's rights now.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
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

    /// <summary>The apps in front of which corners don't trigger.</summary>
    public ObservableCollection<ExcludedAppItem> ExcludedApps { get; } = [];

    public bool HasNoExcludedApps => ExcludedApps.Count == 0;

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ExcludedApp app) => Settings.Lists(app);

    /// <summary>Adds <paramref name="app"/> to the list, unless it's there already.</summary>
    public void AddExcludedApp(ExcludedApp app) => SaveExcludedApps(Settings.WithExcludedApp(app));

    public void RemoveExcludedApp(int index)
    {
        if (index >= 0 && index < Settings.ExcludedApps.Count)
        {
            SaveExcludedApps(Settings.WithoutExcludedApp(index));
        }
    }

    private void SaveExcludedApps(HotCornersSettings settings)
    {
        if (settings != Settings)
        {
            Save(settings);
            RefreshExcludedApps();
        }
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

    private void RefreshExcludedApps()
    {
        ExcludedApps.Clear();
        for (var index = 0; index < Settings.ExcludedApps.Count; index++)
        {
            var item = new ExcludedAppItem(index, Settings.ExcludedApps[index]);
            ExcludedApps.Add(item);
            _ = item.LoadIconAsync();
        }

        OnPropertyChanged(nameof(HasNoExcludedApps));
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

        var settings = page.GetCorner(corner);
        _selectedAction = page.Actions.First(option => option.Action == settings.Action);
        _selectedKey = page.Keys.First(option => option.Key == settings.Key);
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
/// A recent result on the Hot Corners page.
/// </summary>
public sealed class CornerResultItem(CornerResult result)
{
    public string Text { get; } = CornerResultText.Describe(result);

    /// <summary>When it happened, and how many times in a row.</summary>
    public string When { get; } = CornerResultText.When(result);

    /// <summary>A check mark for an action that ran, otherwise an information icon.</summary>
    public string Glyph { get; } = result.Kind == CornerResultKind.Ran ? "" : "";
}

/// <summary>
/// A listed app on the Hot Corners page. Its icon shows once it's read.
/// </summary>
public sealed partial class ExcludedAppItem : ObservableObject
{
    private static readonly CompositeFormat RemoveNameFormat = CompositeFormat.Parse(Strings.HotCornersRemoveAppName);

    private IconElement _icon = AppLooks.ToIconElement(null);

    public ExcludedAppItem(int index, ExcludedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Index = index;
        App = app;
        Name = app.Name.Trim() is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(app.Program) ?? app.AppId ?? string.Empty;
        RemoveName = string.Format(CultureInfo.CurrentCulture, RemoveNameFormat, Name);
    }

    /// <summary>The app's place in the list, from 0.</summary>
    public int Index { get; }

    public ExcludedApp App { get; }

    public string Name { get; }

    /// <summary>The program's path, shown in the tooltip, or <see langword="null"/> for an app ID.</summary>
    public string? Program => App.Program;

    /// <summary>The screen reader name of the Remove button.</summary>
    public string RemoveName { get; }

    /// <summary>The card's icon: the plain app icon until the app's own is read.</summary>
    public IconElement Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    /// <summary>Reads the app's icon off the UI thread, and shows it.</summary>
    public async Task LoadIconAsync()
    {
        var icon = await Task.Run(() => AppLooks.ReadIcon(App, null)).ConfigureAwait(true);
        if (await AppLooks.ToImageAsync(icon).ConfigureAwait(true) is { } image)
        {
            Icon = AppLooks.ToIconElement(image);
        }
    }
}
