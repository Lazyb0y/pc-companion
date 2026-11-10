using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.App.ViewModels;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DynamicWallpaper;

public sealed record ChangeWithOption(ChangeWith ChangeWith, string DisplayName) : Choice(DisplayName);

public sealed record FitOption(WallpaperFit Fit, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Dynamic Wallpaper page. Every change is saved right away. It asks the host what only the
/// host knows, such as whether the feature stepped aside, through its pipe. See "Settings" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed partial class DynamicWallpaperViewModel : FeatureViewModel<DynamicWallpaperSettings>
{
    private static readonly CompositeFormat FolderFormat = CompositeFormat.Parse(Strings.DynamicWallpaperFolder);

    private readonly ILogger<DynamicWallpaperViewModel> _logger;
    private readonly WindowsPictureFiles _files = new();
    private ChangeWithOption? _selectedChangeWith;
    private FitOption? _selectedFit;
    private DynamicWallpaperAnswer? _answer;
    private bool _spotlightNote;

    // Counts the page's looks at the files, so an older look doesn't overwrite a newer one.
    private int _look;

    public DynamicWallpaperViewModel(FeatureSettingsEditor editor, ILogger<DynamicWallpaperViewModel> logger)
        : base(editor, DynamicWallpaperFeature.Definition)
    {
        _logger = logger;

        ChangeWithOptions =
        [
            new(ChangeWith.Sun, Strings.DynamicWallpaperChangeWithSun),
            new(ChangeWith.Times, Strings.DynamicWallpaperChangeWithTimes),
            new(ChangeWith.WindowsMode, Strings.DynamicWallpaperChangeWithWindowsMode),
        ];
        FitOptions =
        [
            new(WallpaperFit.Fill, Strings.DynamicWallpaperFitFill),
            new(WallpaperFit.Fit, Strings.DynamicWallpaperFitFit),
            new(WallpaperFit.Stretch, Strings.DynamicWallpaperFitStretch),
            new(WallpaperFit.Tile, Strings.DynamicWallpaperFitTile),
            new(WallpaperFit.Center, Strings.DynamicWallpaperFitCenter),
            new(WallpaperFit.Span, Strings.DynamicWallpaperFitSpan),
        ];
        _selectedChangeWith = ChangeWithOptions.First(option => option.ChangeWith == Settings.ChangeWith);
        _selectedFit = FitOptions.First(option => option.Fit == Settings.Fit);
        BuildPeriods();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsOn))
            {
                // Turned off from the page: Windows spotlight can't be turned on again for the user.
                SpotlightNote = !IsOn && _answer?.Spotlight == true;
                _answer = null;
                OnHostChanged();
            }
        };
    }

    public IReadOnlyList<ChangeWithOption> ChangeWithOptions { get; }

    public IReadOnlyList<FitOption> FitOptions { get; }

    /// <summary>The periods of the schedule in use, in its order.</summary>
    public ObservableCollection<PeriodItem> Periods { get; } = [];

    public ChangeWithOption? SelectedChangeWith
    {
        get => _selectedChangeWith;
        set
        {
            if (value is not null && SetProperty(ref _selectedChangeWith, value))
            {
                Save(Settings with { ChangeWith = value.ChangeWith });
                BuildPeriods();
                OnPropertyChanged(nameof(IsTimes));
                OnPropertyChanged(nameof(IsWindowsMode));
                _ = LookAsync();
            }
        }
    }

    public FitOption? SelectedFit
    {
        get => _selectedFit;
        set
        {
            if (value is not null && SetProperty(ref _selectedFit, value))
            {
                Save(Settings with { Fit = value.Fit });
            }
        }
    }

    public bool IsTimes => Settings.ChangeWith == ChangeWith.Times;

    public bool IsWindowsMode => Settings.ChangeWith == ChangeWith.WindowsMode;

    /// <summary>Whether Times of day can have another time.</summary>
    public bool CanAddTime => Settings.Times.Count < DynamicWallpaperCheck.MaximumTimes;

    /// <summary>Whether every period has the same picture, as Windows 10's first settings do.</summary>
    public bool SamePicture
    {
        get
        {
            var sources = Periods.Select(item => PeriodOf(item)?.Own).ToList();
            return sources.Count > 1
                && sources.All(source => source is { Picture: { } picture, Folder: null } && string.Equals(picture, sources[0]!.Picture, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool SteppedAside => IsBlocked(WallpaperBlock.SteppedAside);

    public bool Managed => IsBlocked(WallpaperBlock.Managed);

    public bool ExplorerNotAnswering => IsBlocked(WallpaperBlock.ExplorerNotAnswering);

    public bool RemoteSession => IsBlocked(WallpaperBlock.RemoteSession);

    /// <summary>Whether to say how to use Windows spotlight again, after turning off from it.</summary>
    public bool SpotlightNote
    {
        get => _spotlightNote;
        private set => SetProperty(ref _spotlightNote, value);
    }

    /// <summary>Looks at the pictures and asks the host for its state, as the page opens and every few seconds.</summary>
    public async Task LookAsync()
    {
        await RefreshHostAsync();
        await RefreshFilesAsync();
    }

    /// <summary>Asks the host for its state, while the feature is on.</summary>
    public async Task RefreshHostAsync()
    {
        _answer = IsOn ? await DynamicWallpaperHost.AskAsync(DynamicWallpaperRequestKind.State) : null;
        OnHostChanged();
    }

    /// <summary>Uses a picture for a period.</summary>
    public void ChoosePicture(PeriodItem item, string picture)
    {
        ArgumentNullException.ThrowIfNull(item);
        Change(item, (PeriodOf(item) ?? new WallpaperPeriod()) with { Picture = picture, Folder = null });
    }

    /// <summary>Uses a folder for a period.</summary>
    public void ChooseFolder(PeriodItem item, string folder)
    {
        ArgumentNullException.ThrowIfNull(item);
        Change(item, (PeriodOf(item) ?? new WallpaperPeriod()) with { Picture = null, Folder = folder });
    }

    /// <summary>Leaves Sunrise or Sunset without a picture.</summary>
    public void ChooseNone(PeriodItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.CanBeNone)
        {
            Change(item, null);
        }
    }

    /// <summary>Adds a time an hour after the last, with its picture, to Times of day.</summary>
    public void AddTime()
    {
        if (!CanAddTime)
        {
            return;
        }

        var times = Settings.Times;
        var last = times.Count > 0 && DynamicWallpaperCheck.TryParseTime(times[^1].At, out var at) ? at : new TimeOnly(11, 0);
        var time = last.AddHours(1);
        while (times.Any(period => period.At == DynamicWallpaperCheck.FormatTime(time)))
        {
            time = time.AddMinutes(1);
        }

        var added = (times.Count > 0 ? times[^1] : new WallpaperPeriod()) with { At = DynamicWallpaperCheck.FormatTime(time) };
        Save(Settings with { Times = [.. times, added] });
        BuildPeriods();
        _ = LookAsync();
    }

    /// <summary>Removes a time of Times of day, which keeps at least two.</summary>
    public void RemoveTime(PeriodItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsTime && Settings.Times.Count > DynamicWallpaperCheck.MinimumTimes && item.Period.Index < Settings.Times.Count)
        {
            Save(Settings with { Times = [.. Settings.Times.Where((_, index) => index != item.Period.Index)] });
            BuildPeriods();
            _ = LookAsync();
        }
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        _answer = await DynamicWallpaperHost.AskAsync(DynamicWallpaperRequestKind.Resume);
        OnHostChanged();
    }

    [RelayCommand]
    private void OpenBackgroundSettings()
    {
        if (!ShellActions.OpenBackgroundSettings())
        {
            LogCantOpenBackgroundSettings(_logger);
        }
    }

    private bool IsBlocked(WallpaperBlock block) => IsOn && _answer is { Running: true } answer && answer.Block == block;

    private void OnHostChanged()
    {
        OnPropertyChanged(nameof(SteppedAside));
        OnPropertyChanged(nameof(Managed));
        OnPropertyChanged(nameof(ExplorerNotAnswering));
        OnPropertyChanged(nameof(RemoteSession));
        foreach (var item in Periods)
        {
            item.InUse = _answer is { Running: true } answer && answer.Period == item.Period.ToString() && answer.Block == WallpaperBlock.None;
        }
    }

    // The settings' period for a row; null for Sunrise or Sunset without a picture.
    private WallpaperPeriod? PeriodOf(PeriodItem item) => item.Period.Kind switch
    {
        ChangeWith.Sun => Settings.Sun.Of((SunPeriod)item.Period.Index),
        ChangeWith.WindowsMode => item.Period.Index == 0 ? Settings.WindowsMode.Light : Settings.WindowsMode.Dark,
        _ => item.Period.Index < Settings.Times.Count ? Settings.Times[item.Period.Index] : null,
    };

    private void Change(PeriodItem item, WallpaperPeriod? period)
    {
        Save(item.Period.Kind switch
        {
            ChangeWith.Sun => Settings with
            {
                Sun = (SunPeriod)item.Period.Index switch
                {
                    SunPeriod.Sunrise => Settings.Sun with { Sunrise = period },
                    SunPeriod.Day => Settings.Sun with { Day = period ?? new() },
                    SunPeriod.Sunset => Settings.Sun with { Sunset = period },
                    _ => Settings.Sun with { Night = period ?? new() },
                },
            },
            ChangeWith.WindowsMode => Settings with
            {
                WindowsMode = item.Period.Index == 0
                    ? Settings.WindowsMode with { Light = period ?? new() }
                    : Settings.WindowsMode with { Dark = period ?? new() },
            },
            _ => Settings with { Times = [.. Settings.Times.Select((time, index) => index == item.Period.Index ? period ?? time : time)] },
        });
        Describe();
        OnPropertyChanged(nameof(SamePicture));
        _ = LookAsync();
    }

    private void OnTimeChanged(PeriodItem item, TimeOnly time)
    {
        if (item.Period.Index < Settings.Times.Count)
        {
            Change(item, Settings.Times[item.Period.Index] with { At = DynamicWallpaperCheck.FormatTime(time) });
        }
    }

    private void BuildPeriods()
    {
        Periods.Clear();
        switch (Settings.ChangeWith)
        {
            case ChangeWith.Times:
                for (var index = 0; index < Settings.Times.Count; index++)
                {
                    TimeOnly? time = DynamicWallpaperCheck.TryParseTime(Settings.Times[index].At, out var at) ? at : null;
                    Periods.Add(new PeriodItem(new PeriodName(ChangeWith.Times, index), time, OnTimeChanged));
                }

                break;
            case ChangeWith.WindowsMode:
                Periods.Add(new PeriodItem(PeriodName.Light, null, OnTimeChanged));
                Periods.Add(new PeriodItem(PeriodName.Dark, null, OnTimeChanged));
                break;
            default:
                foreach (var period in Enum.GetValues<SunPeriod>().OrderBy(period => period == SunPeriod.Night))
                {
                    Periods.Add(new PeriodItem(PeriodName.Of(period), null, OnTimeChanged));
                }

                break;
        }

        Describe();
        OnPropertyChanged(nameof(CanAddTime));
        OnPropertyChanged(nameof(SamePicture));
        OnHostChanged();
    }

    // Each row's picture or folder name, and the problems the settings alone show.
    private void Describe()
    {
        var timeProblems = DynamicWallpaperCheck.CheckTimes(Settings.Times);
        foreach (var item in Periods)
        {
            var period = PeriodOf(item);
            item.CanRemove = item.IsTime && Settings.Times.Count > DynamicWallpaperCheck.MinimumTimes;
            if (period is null)
            {
                item.Source = (SunPeriod)item.Period.Index == SunPeriod.Sunrise ? Strings.DynamicWallpaperSunriseNone : Strings.DynamicWallpaperSunsetNone;
                item.Problem = null;
                item.Thumbnail = null;
                continue;
            }

            item.Source = period.Folder is { Length: > 0 } folder
                ? string.Format(CultureInfo.CurrentCulture, FolderFormat, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)))
                : period.Picture is { Length: > 0 } picture ? Path.GetFileName(picture) : Strings.DynamicWallpaperNoPicture;
            item.Problem = item.IsTime && item.Period.Index < timeProblems.Count && timeProblems[item.Period.Index] == TimeProblem.SameTime
                ? Strings.DynamicWallpaperProblemSameTime
                : DynamicWallpaperText.Of(DynamicWallpaperCheck.Check(period.Own));
        }
    }

    // Checks each row's picture or folder off the UI thread, then shows Windows' thumbnails of
    // those that can be used, and the host's problems with the period in use.
    private async Task RefreshFilesAsync()
    {
        var look = ++_look;
        var rows = Periods.Select(item => (Item: item, Source: PeriodOf(item)?.Own)).ToList();
        var states = await Task.Run(() => rows
            .Select(row => row.Source is not { } source || DynamicWallpaperCheck.Check(source) != SourceProblem.None
                ? (PictureState?)null
                : source.Picture is { } picture ? _files.Check(picture) : _files.CheckFolder(source.Folder!))
            .ToList());
        if (look != _look)
        {
            return;
        }

        var timeProblems = DynamicWallpaperCheck.CheckTimes(Settings.Times);
        for (var i = 0; i < rows.Count; i++)
        {
            var (item, source) = rows[i];
            if (source is null || states[i] is not { } state)
            {
                continue;
            }

            var hostProblem = _answer is { Running: true } answer && answer.Period == item.Period.ToString()
                ? answer.Issues.FirstOrDefault(issue => issue.Display < 0)
                : null;
            var sameTime = item.IsTime && item.Period.Index < timeProblems.Count && timeProblems[item.Period.Index] == TimeProblem.SameTime;
            item.Problem = DynamicWallpaperText.Of(state)
                ?? (hostProblem is not null ? DynamicWallpaperText.Of(hostProblem.Problem) : null)
                ?? (sameTime ? Strings.DynamicWallpaperProblemSameTime : null);
            item.Thumbnail = state == PictureState.Usable
                ? await PictureThumbnails.LoadAsync(source.Picture ?? source.Folder!, folder: source.Picture is null)
                : null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows Settings' Background page couldn't be opened.")]
    private static partial void LogCantOpenBackgroundSettings(ILogger logger);
}
