using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DropDownApps;

public sealed record DropDownShortcutOption(DropDownShortcut Shortcut, string DisplayName) : Choice(DisplayName);

public sealed record DropDownDisplayOption(DropDownDisplay Display, string DisplayName) : Choice(DisplayName);

public sealed record HideWhenInactiveOption(HideWhenInactive Hide, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Drop-down Apps page. Every change is saved right away. While the page is shown, it reads the
/// recent results about once a second.
/// </summary>
public sealed partial class DropDownAppsViewModel : FeatureViewModel<DropDownAppsSettings>
{
    private static readonly CompositeFormat ShortcutNameFormat = CompositeFormat.Parse(Strings.DropDownAppsShortcutName);
    private static readonly CompositeFormat ControlKeyFormat = CompositeFormat.Parse(Strings.DropDownAppsShortcutControlKey);
    private static readonly CompositeFormat AltKeyFormat = CompositeFormat.Parse(Strings.DropDownAppsShortcutAltKey);
    private static readonly CompositeFormat ControlAltKeyFormat = CompositeFormat.Parse(Strings.DropDownAppsShortcutControlAltKey);
    private static readonly TimeSpan ResultsTimeout = TimeSpan.FromMilliseconds(500);

    private readonly AppSettings _appSettings;
    private readonly DropDownResultsReading _resultsReading = new();
    private DropDownDisplayOption? _selectedDisplay;
    private HideWhenInactiveOption? _selectedHide;
    private DispatcherQueueTimer? _resultsTimer;
    private bool _readingResults;
    private string _resultsMessage = string.Empty;

    public DropDownAppsViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, DropDownAppsFeature.Definition)
    {
        _appSettings = appSettings;

        Shortcuts =
        [
            .. DropDownShortcuts.All.Select(shortcut => new DropDownShortcutOption(shortcut, NameOf(shortcut))),
            new(DropDownShortcut.None, Strings.DropDownAppsShortcutNone),
        ];
        Displays =
        [
            new(DropDownDisplay.WithPointer, Strings.DropDownAppsDisplayWithPointer),
            new(DropDownDisplay.Main, Strings.DropDownAppsDisplayMain),
        ];
        HideOptions =
        [
            new(HideWhenInactive.AnyDisplay, Strings.DropDownAppsHideAnyDisplay),
            new(HideWhenInactive.SameDisplay, Strings.DropDownAppsHideSameDisplay),
            new(HideWhenInactive.Never, Strings.DropDownAppsHideNever),
        ];
        _selectedDisplay = Displays.FirstOrDefault(option => option.Display == Settings.Display);
        _selectedHide = HideOptions.FirstOrDefault(option => option.Hide == Settings.HideWhenInactive);

        Apps = new AppListViewModel(
            () => [.. Settings.Apps.Select(app => app.App)],
            app => SaveApps(Settings.WithApp(app)),
            index => SaveApps(Settings.WithoutApp(index)),
            new AppListChoice(
                () => Shortcuts,
                index => Shortcuts.FirstOrDefault(option => option.Shortcut == Settings.Apps[index].Shortcut),
                (index, choice) => choice is DropDownShortcutOption option && ChooseShortcut(index, option.Shortcut),
                ShortcutNameFormat,
                () => null));
    }

    /// <summary>The shortcuts an app can have, None last.</summary>
    public IReadOnlyList<DropDownShortcutOption> Shortcuts { get; }

    /// <summary>The listed apps, each with its shortcut.</summary>
    public AppListViewModel Apps { get; }

    public IReadOnlyList<DropDownDisplayOption> Displays { get; }

    public DropDownDisplayOption? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (value is not null && SetProperty(ref _selectedDisplay, value))
            {
                Save(Settings with { Display = value.Display });
            }
        }
    }

    public static double MinimumWidth => DropDownAppsSettings.MinimumWidth;

    public static double MinimumHeight => DropDownAppsSettings.MinimumHeight;

    public static double MaximumSize => DropDownAppsSettings.MaximumSize;

    public double Width
    {
        get => Settings.Width;
        set
        {
            var percent = (int)Math.Round(value);
            if (Change(Settings.Width, percent, () => Settings with { Width = percent }))
            {
                OnPropertyChanged(nameof(WidthText));
            }
        }
    }

    public string WidthText => Percent(Settings.Width);

    public double Height
    {
        get => Settings.Height;
        set
        {
            var percent = (int)Math.Round(value);
            if (Change(Settings.Height, percent, () => Settings with { Height = percent }))
            {
                OnPropertyChanged(nameof(HeightText));
            }
        }
    }

    public string HeightText => Percent(Settings.Height);

    public bool Slide
    {
        get => Settings.Slide;
        set => Change(Settings.Slide, value, () => Settings with { Slide = value });
    }

    public IReadOnlyList<HideWhenInactiveOption> HideOptions { get; }

    public HideWhenInactiveOption? SelectedHide
    {
        get => _selectedHide;
        set
        {
            if (value is not null && SetProperty(ref _selectedHide, value))
            {
                Save(Settings with { HideWhenInactive = value.Hide });
            }
        }
    }

    public bool KeepOnTop
    {
        get => Settings.KeepOnTop;
        set => Change(Settings.KeepOnTop, value, () => Settings with { KeepOnTop = value });
    }

    /// <summary>Which windows can't drop down, with the host's rights now.</summary>
    public string CantDropInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.DropDownAppsCantDropInfoOn
        : Strings.DropDownAppsCantDropInfo;

    /// <summary>The recent results, newest first.</summary>
    public ObservableCollection<DropDownResultItem> Results { get; } = [];

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

    private static string Percent(int percent) => (percent / 100.0).ToString("P0", CultureInfo.CurrentCulture);

    // The character the key above Tab types in the user's layout, such as ` or ^, or a
    // description when it types none that can be shown; the number keys show their number.
    private static string NameOf(DropDownShortcut shortcut) => shortcut switch
    {
        DropDownShortcut.WindowsControlAboveTab => string.Format(CultureInfo.CurrentCulture, ControlKeyFormat, KeyAboveTab()),
        DropDownShortcut.WindowsAltAboveTab => string.Format(CultureInfo.CurrentCulture, AltKeyFormat, KeyAboveTab()),
        _ => string.Format(CultureInfo.CurrentCulture, ControlAltKeyFormat, (shortcut - DropDownShortcut.WindowsControlAlt1 + 1).ToString(CultureInfo.InvariantCulture)),
    };

    private static string KeyAboveTab() =>
        KeyboardLayout.CharacterOf(ShortcutKeeper.KeyAboveTab, KeyboardLayout.OfCurrentThread()) is { } character
        && !char.IsControl(character)
        && !char.IsWhiteSpace(character)
            ? character.ToString()
            : Strings.DropDownAppsKeyAboveTab;

    // A shortcut taken from another app leaves that app with none, which its row then shows.
    private bool ChooseShortcut(int index, DropDownShortcut shortcut)
    {
        var tookFromAnother = shortcut != DropDownShortcut.None
            && Settings.Apps.Where((app, at) => at != index && app.Shortcut == shortcut).Any();
        if (!SaveApps(Settings.WithShortcutAt(index, shortcut)))
        {
            return false;
        }

        if (tookFromAnother)
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(Apps.Refresh);
        }

        return true;
    }

    private bool SaveApps(DropDownAppsSettings settings)
    {
        if (settings == Settings)
        {
            return false;
        }

        Save(settings);
        return true;
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
                ? await Task.Run(() => SnapshotPipe.ReadAsync(DropDownResults.PipeName, ResultsTimeout, DropDownResults.MaximumSnapshotLength)).ConfigureAwait(true)
                : null;
            ShowResults(isOn, snapshot is null ? null : DropDownResults.FromJson(snapshot));
        }
        finally
        {
            _readingResults = false;
        }
    }

    private void ShowResults(bool isOn, IReadOnlyList<DropDownResult>? results)
    {
        if (!_resultsReading.Read(isOn, results))
        {
            return;
        }

        ResultsMessage = _resultsReading.Note switch
        {
            DropDownResultsNote.FeatureOff => Strings.DropDownAppsResultsUnavailable,
            DropDownResultsNote.NothingPressed => Strings.DropDownAppsResultsNone,
            DropDownResultsNote.NotAvailable => Strings.DropDownAppsResultsNotAvailable,
            _ => string.Empty,
        };

        Results.Clear();
        foreach (var result in _resultsReading.Results)
        {
            Results.Add(new DropDownResultItem(result, Settings.Apps));
        }
    }
}

/// <summary>
/// A recent result on the Drop-down Apps page.
/// </summary>
public sealed class DropDownResultItem(DropDownResult result, IReadOnlyList<DropDownApp> apps)
{
    public string Text { get; } = DropDownResultText.Describe(result, apps);

    /// <summary>When it happened, and how many times in a row.</summary>
    public string When { get; } = DropDownResultText.When(result);

    /// <summary>A check mark for a shortcut that did what it was pressed for, otherwise an information icon.</summary>
    public string Glyph { get; } = DropDownResultText.IsDone(result.Outcome) ? "" : "";
}
