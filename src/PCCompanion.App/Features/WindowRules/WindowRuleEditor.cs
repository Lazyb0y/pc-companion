using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.App.Controls;
using PCCompanion.Core;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.WindowRules;

/// <summary>An open window the rule matches, as the editor lists it.</summary>
public sealed record MatchedWindow(string App, string Title)
{
    public override string ToString() => Title.Length == 0 ? App : $"{App}: {Title}";
}

/// <summary>
/// The rule editor's state: the rule's name, what it matches and its actions, each with a switch,
/// the problems that keep it from being saved, and the open windows it matches now. See "Making
/// rules" in docs/features/windows/window-rules.md.
/// </summary>
public sealed partial class WindowRuleEditor : ObservableObject
{
    // The opacities offered, from most to least opaque.
    private static readonly int[] Opacities = [90, 80, 70, 60, 50, 40, 30, 20];
    private static readonly CompositeFormat PercentFormat = CompositeFormat.Parse(Strings.WindowRulesOpacityPercent);
    private static readonly CompositeFormat NotConnectedFormat = CompositeFormat.Parse(Strings.WindowRulesDisplayNotConnected);

    private readonly List<RuleDisplay> _displays = [];
    private readonly IReadOnlyList<WindowRule> _otherRules;
    private readonly string _id;
    private IReadOnlyList<(OpenRuleWindow Window, string AppName)> _open = [];
    private string _name;
    private ListedApp? _app;
    private bool _titleOn;
    private int _titleMatch;
    private string _titleText;
    private bool _classOn;
    private string _className;
    private bool _displayOn;
    private int _display;
    private bool _placeOn;
    private int _place;
    private double _left;
    private double _top;
    private double _width = 50;
    private double _height = 100;
    private bool _openOn;
    private int _openWhich;
    private bool _onTop;
    private bool _opacityOn;
    private int _opacity = 1;
    private bool _hideFromTaskbar;
    private bool _tabOn;
    private ListedApp? _tabOf;
    private bool _applyOnDisplayChange;
    private bool _enabled;
    private string _matches = string.Empty;

    /// <param name="rule">The rule to edit, or a new empty one.</param>
    /// <param name="isNew">Whether the rule is being added.</param>
    /// <param name="otherRules">The list's other rules, to say which also match the windows.</param>
    public WindowRuleEditor(WindowRule rule, bool isNew, IReadOnlyList<WindowRule> otherRules)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(otherRules);

        _otherRules = otherRules;
        Title = isNew ? Strings.WindowRulesAddTitle : Strings.WindowRulesEditTitle;
        _enabled = rule.Enabled;

        // A new rule, or one written by hand, gets a key that keeps what it remembers.
        _id = rule.Id.Length > 0 ? rule.Id : Guid.NewGuid().ToString("N");
        _name = rule.Name;
        _app = rule.App;
        _titleOn = rule.Title is not null;
        _titleMatch = (int)(rule.Title?.KnownMatch ?? TitleMatch.Contains);
        _titleText = rule.Title?.Text ?? string.Empty;
        _classOn = rule.WindowClass is not null;
        _className = rule.WindowClass ?? string.Empty;

        _displays.Add(new RuleDisplay { Which = nameof(DisplayChoice.Main) });
        _displays.Add(new RuleDisplay { Which = nameof(DisplayChoice.UnderPointer) });
        DisplayChoices.Add(Strings.WindowRulesDisplayMain);
        DisplayChoices.Add(Strings.WindowRulesDisplayPointer);
        _displayOn = rule.Display is not null;
        if (rule.Display is { KnownWhich: DisplayChoice.Chosen } chosen)
        {
            _displays.Add(chosen);
            DisplayChoices.Add(string.Format(CultureInfo.CurrentCulture, NotConnectedFormat, DisplayName(chosen)));
            _display = 2;
        }
        else
        {
            _display = rule.Display?.KnownWhich == DisplayChoice.UnderPointer ? 1 : 0;
        }

        _placeOn = rule.Place is not null;
        _place = (int)(rule.Place?.KnownWhich ?? PlaceChoice.RightHalf);
        if (rule.Place is { KnownWhich: PlaceChoice.Custom } custom)
        {
            (_left, _top, _width, _height) = (custom.Left, custom.Top, custom.Width, custom.Height);
        }

        _openOn = rule.KnownOpen is not null;
        _openWhich = (int)(rule.KnownOpen ?? OpenChoice.Maximized);
        _onTop = rule.OnTop;
        _opacityOn = rule.Opacity is not null;
        _opacity = rule.Opacity is { KnownWhich: OpacityChoice.Wheel } ? Opacities.Length
            : rule.Opacity is { } fixedOpacity ? Math.Max(0, Array.IndexOf(Opacities, Math.Clamp((fixedOpacity.Percent + 5) / 10 * 10, RuleOpacity.Lowest, RuleOpacity.Highest)))
            : 1;
        _hideFromTaskbar = rule.HideFromTaskbar;
        _tabOn = rule.TabOf is not null;
        _tabOf = rule.TabOf;
        _applyOnDisplayChange = rule.ApplyOnDisplayChange;
    }

    public string Title { get; }

    public IReadOnlyList<string> TitleMatchChoices { get; } = [Strings.WindowRulesTitleContains, Strings.WindowRulesTitleIs, Strings.WindowRulesTitlePattern];

    public ObservableCollection<string> DisplayChoices { get; } = [];

    /// <summary>The places, in the order of <see cref="PlaceChoice"/>.</summary>
    public IReadOnlyList<string> PlaceChoices { get; } =
    [
        Strings.WindowRulesPlaceCentered, Strings.WindowRulesPlaceLeftHalf, Strings.WindowRulesPlaceRightHalf, Strings.WindowRulesPlaceTopHalf,
        Strings.WindowRulesPlaceBottomHalf, Strings.WindowRulesPlaceTopLeft, Strings.WindowRulesPlaceTopRight, Strings.WindowRulesPlaceBottomLeft,
        Strings.WindowRulesPlaceBottomRight, Strings.WindowRulesPlaceWorkArea, Strings.WindowRulesPlaceRemembered, Strings.WindowRulesPlaceCustom,
    ];

    /// <summary>How the window opens, in the order of <see cref="OpenChoice"/>.</summary>
    public IReadOnlyList<string> OpenChoices { get; } = [Strings.WindowRulesOpenNormal, Strings.WindowRulesOpenMaximized, Strings.WindowRulesOpenMinimized];

    public IReadOnlyList<string> OpacityChoices { get; } =
        [.. Opacities.Select(percent => string.Format(CultureInfo.CurrentCulture, PercentFormat, percent)), Strings.WindowRulesOpacityWheel];

    /// <summary>The open windows the rule matches now, and which other rules also match them.</summary>
    public ObservableCollection<MatchedWindow> Matched { get; } = [];

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string AppName => _app is { } app ? app.Name : Strings.WindowRulesAppNone;

    public bool TitleOn
    {
        get => _titleOn;
        set => Set(ref _titleOn, value);
    }

    public int TitleMatchIndex
    {
        get => _titleMatch;
        set => Set(ref _titleMatch, value);
    }

    public string TitleText
    {
        get => _titleText;
        set => Set(ref _titleText, value);
    }

    public bool ClassOn
    {
        get => _classOn;
        set => Set(ref _classOn, value);
    }

    public string ClassName
    {
        get => _className;
        set => Set(ref _className, value);
    }

    public bool DisplayOn
    {
        get => _displayOn;
        set => Set(ref _displayOn, value);
    }

    public int DisplayIndex
    {
        get => _display;
        set => Set(ref _display, value);
    }

    public bool PlaceOn
    {
        get => _placeOn;
        set => Set(ref _placeOn, value);
    }

    public int PlaceIndex
    {
        get => _place;
        set => Set(ref _place, value);
    }

    public bool IsCustomPlace => _placeOn && _place == (int)PlaceChoice.Custom;

    public double PlaceLeft
    {
        get => _left;
        set => Set(ref _left, value);
    }

    public double PlaceTop
    {
        get => _top;
        set => Set(ref _top, value);
    }

    public double PlaceWidth
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public double PlaceHeight
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public bool OpenOn
    {
        get => _openOn;
        set => Set(ref _openOn, value);
    }

    public int OpenIndex
    {
        get => _openWhich;
        set => Set(ref _openWhich, value);
    }

    public bool OnTop
    {
        get => _onTop;
        set => Set(ref _onTop, value);
    }

    public bool OpacityOn
    {
        get => _opacityOn;
        set => Set(ref _opacityOn, value);
    }

    public int OpacityIndex
    {
        get => _opacity;
        set => Set(ref _opacity, value);
    }

    public bool HideFromTaskbar
    {
        get => _hideFromTaskbar;
        set => Set(ref _hideFromTaskbar, value);
    }

    public bool TabOn
    {
        get => _tabOn;
        set => Set(ref _tabOn, value);
    }

    public string TabOfName => _tabOf is { } app ? app.Name : Strings.WindowRulesAppNone;

    public bool ApplyOnDisplayChange
    {
        get => _applyOnDisplayChange;
        set => Set(ref _applyOnDisplayChange, value);
    }

    public string NameProblem => _name.Trim().Length == 0 ? Strings.WindowRulesNameMissing : string.Empty;

    public bool HasNameProblem => NameProblem.Length > 0;

    public string AppProblem => _app is { IsValid: true } ? string.Empty : Strings.WindowRulesAppMissing;

    public bool HasAppProblem => AppProblem.Length > 0;

    public string TitleProblem => !_titleOn ? string.Empty
        : _titleText.Length == 0 ? Strings.WindowRulesTitleMissing
        : RuleMatcher.Check(new WindowRule { App = new ListedApp { Program = "x" }, OnTop = true, Title = TitleCondition() }) == RuleProblem.BadPattern ? Strings.WindowRulesTitleBadPattern
        : string.Empty;

    public bool HasTitleProblem => TitleProblem.Length > 0;

    public string PlaceProblem => IsCustomPlace && RuleMatcher.Check(new WindowRule { App = new ListedApp { Program = "x" }, Place = PlaceAction() }) == RuleProblem.BadPlace
        ? Strings.WindowRulesPlaceProblem
        : string.Empty;

    public bool HasPlaceProblem => PlaceProblem.Length > 0;

    public string TabProblem => _tabOn && _tabOf is not { IsValid: true } ? Strings.WindowRulesTabMissing : string.Empty;

    public bool HasTabProblem => TabProblem.Length > 0;

    public bool HasNoAction => !ToRule().HasAction;

    /// <summary>How many open windows the rule matches, or that none does.</summary>
    public string Matches
    {
        get => _matches;
        private set => SetProperty(ref _matches, value);
    }

    public bool CanSave => !HasNameProblem && RuleMatcher.Check(ToRule()) == RuleProblem.None;

    /// <summary>The rule as the editor shows it, kept on or off as it was.</summary>
    public WindowRule ToRule() => new()
    {
        Name = _name.Trim(),
        Id = _id,
        Enabled = _enabled,
        App = _app,
        Title = _titleOn ? TitleCondition() : null,
        WindowClass = _classOn && _className.Trim().Length > 0 ? _className.Trim() : null,
        Display = _displayOn ? _displays.ElementAtOrDefault(_display) ?? _displays[0] : null,
        Place = _placeOn ? PlaceAction() : null,
        Open = _openOn ? ((OpenChoice)_openWhich).ToString() : null,
        OnTop = _onTop,
        Opacity = !_opacityOn ? null
            : _opacity >= Opacities.Length ? new RuleOpacity { Which = nameof(OpacityChoice.Wheel) }
            : new RuleOpacity { Percent = Opacities[_opacity] },
        HideFromTaskbar = _hideFromTaskbar,
        TabOf = _tabOn ? _tabOf : null,
        ApplyOnDisplayChange = _applyOnDisplayChange,
    };

    /// <summary>The rule's app, from Running app, Program file or Pick a window.</summary>
    public void SetApp(ListedApp app)
    {
        _app = app;
        if (_name.Trim().Length == 0)
        {
            _name = app.Name.Length > WindowRule.MaximumNameLength ? app.Name[..WindowRule.MaximumNameLength] : app.Name;
            OnPropertyChanged(nameof(Name));
        }

        OnPropertyChanged(nameof(AppName));
        Changed();
    }

    /// <summary>The app whose window the rule's windows open as a tab of.</summary>
    public void SetTabOf(ListedApp app)
    {
        _tabOf = app;
        _tabOn = true;
        OnPropertyChanged(nameof(TabOfName));
        OnPropertyChanged(nameof(TabOn));
        Changed();
    }

    /// <summary>
    /// Fills the rule from a window: its app, its title as contains, and its class, display, place
    /// and state, with the matching and actions it gets switched off, so the user turns on what
    /// they want.
    /// </summary>
    public void FillFrom(OpenRuleWindow window, ListedApp app)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(app);

        SetApp(app);
        _titleText = window.Facts.Title;
        _titleMatch = (int)TitleMatch.Contains;
        _className = window.Facts.ClassName;
        if (window.Frame is { } frame && window.WorkArea is { } workArea)
        {
            var place = OpenRuleWindows.PlaceOf(frame, workArea);
            (_left, _top, _width, _height) = (place.Left, place.Top, place.Width, place.Height);
            _place = (int)PlaceChoice.Custom;
        }

        _openWhich = (int)(window.State == WindowState.Maximized ? OpenChoice.Maximized : OpenChoice.Normal);
        if (_displays.Count > 2 && window.WorkArea is { } area && ConnectedWorkAreas.IndexOf(area) is var index and >= 0)
        {
            _display = index + 2;
        }

        foreach (var name in new[] { nameof(TitleText), nameof(TitleMatchIndex), nameof(ClassName), nameof(PlaceIndex), nameof(PlaceLeft), nameof(PlaceTop), nameof(PlaceWidth), nameof(PlaceHeight), nameof(OpenIndex), nameof(DisplayIndex) })
        {
            OnPropertyChanged(name);
        }

        Changed();
    }

    // The work areas of the connected displays, in the order they follow Main and Under the pointer.
    private List<System.Drawing.Rectangle> ConnectedWorkAreas { get; } = [];

    /// <summary>
    /// Reads the connected displays and the open windows off the UI thread, as the editor opens,
    /// then lists the windows the rule matches. A chosen display that's connected takes its place
    /// among them.
    /// </summary>
    public async Task LoadAsync()
    {
        string[] leftOut = [Environment.ProcessPath ?? string.Empty, HostProcess.ExecutablePath];
        var (displays, windows) = await Task.Run(() =>
        {
            var connected = ConnectedDisplays.Read();
            var open = OpenRuleWindows.Read(leftOut)
                .Select(window => (Window: window, AppName: AppLooks.ReadName(ListedOf(window), window.Program)))
                .ToList();
            return (connected, open);
        }).ConfigureAwait(true);

        var chosen = _displayOn && _display == 2 ? _displays[2] : null;
        if (chosen is not null)
        {
            _displays.RemoveAt(2);
            DisplayChoices.RemoveAt(2);
        }

        foreach (var display in displays.Where(display => display.WorkArea is not null))
        {
            _displays.Add(display.ToRule());
            ConnectedWorkAreas.Add(display.WorkArea!.Value);
            DisplayChoices.Add(DisplayName(display.ToRule()));
        }

        if (chosen is not null)
        {
            var found = ConnectedDisplays.WorkAreaOf(chosen, displays) is { } area ? ConnectedWorkAreas.IndexOf(area) : -1;
            if (found >= 0)
            {
                _display = found + 2;
            }
            else
            {
                _displays.Add(chosen);
                DisplayChoices.Add(string.Format(CultureInfo.CurrentCulture, NotConnectedFormat, DisplayName(chosen)));
                _display = _displays.Count - 1;
            }

            OnPropertyChanged(nameof(DisplayIndex));
        }

        _open = windows;
        Changed();
    }

    /// <summary>The open windows, as Pick a window lists them, with their apps' names.</summary>
    public IReadOnlyList<(OpenRuleWindow Window, string AppName)> OpenWindows => _open;

    /// <summary>The app of a window, as a rule names it, with the name the page shows.</summary>
    public static ListedApp ListedOf(OpenRuleWindow window, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        var app = window.Facts.App;
        return app.AppId is { } appId
            ? new ListedApp { Name = name ?? string.Empty, AppId = appId }
            : new ListedApp { Name = name ?? string.Empty, Program = app.Program };
    }

    private static string DisplayName(RuleDisplay display) =>
        display.BuiltIn ? Strings.WindowRulesDisplayBuiltIn : display.Name.Length > 0 ? display.Name : Strings.WindowRulesDisplayUnnamed;

    private RuleTitle TitleCondition() => new() { Match = ((TitleMatch)_titleMatch).ToString(), Text = _titleText };

    private RulePlace PlaceAction() => (PlaceChoice)_place == PlaceChoice.Custom
        ? new RulePlace { Which = nameof(PlaceChoice.Custom), Left = _left, Top = _top, Width = _width, Height = _height }
        : new RulePlace { Which = ((PlaceChoice)_place).ToString() };

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
        {
            Changed();
        }
    }

    // Every change can change the problems, whether the rule can be saved, and the windows it matches.
    private void Changed()
    {
        foreach (var name in new[]
        {
            nameof(NameProblem), nameof(HasNameProblem), nameof(AppProblem), nameof(HasAppProblem), nameof(TitleProblem), nameof(HasTitleProblem),
            nameof(PlaceProblem), nameof(HasPlaceProblem), nameof(IsCustomPlace), nameof(TabProblem), nameof(HasTabProblem), nameof(HasNoAction), nameof(CanSave),
        })
        {
            OnPropertyChanged(name);
        }

        Rematch();
    }

    private void Rematch()
    {
        Matched.Clear();
        var rule = ToRule() with { Enabled = true };
        var matcher = new RuleMatcher(rule);
        if (rule.App is not { IsValid: true } || matcher.Problem is RuleProblem.NoApp or RuleProblem.NoTitleText or RuleProblem.BadPattern or RuleProblem.UnknownChoice)
        {
            Matches = string.Empty;
            return;
        }

        foreach (var (window, appName) in _open)
        {
            if (matcher.MatchesApp(window.Facts) && matcher.MatchesTitle(window.Facts.Title) == TitleResult.Matches)
            {
                Matched.Add(new MatchedWindow(appName, window.Facts.Title));
            }
        }

        var others = _otherRules.Count(other => other.Enabled && new RuleMatcher(other) is { IsUsable: true } otherMatcher
            && _open.Any(open => matcher.MatchesApp(open.Window.Facts) && otherMatcher.MatchesApp(open.Window.Facts)));
        Matches = PluralStrings.Format("WindowRulesMatches", Matched.Count)
            + (others > 0 ? " " + PluralStrings.Format("WindowRulesAlsoMatched", others) : string.Empty);
    }
}
