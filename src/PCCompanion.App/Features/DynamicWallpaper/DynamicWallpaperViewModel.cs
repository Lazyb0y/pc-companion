using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.App.ViewModels;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using Windows.Devices.Geolocation;

namespace PCCompanion.App.Features.DynamicWallpaper;

public sealed record ChangeWithOption(ChangeWith ChangeWith, string DisplayName) : Choice(DisplayName);

public sealed record LocationOption(LocationSource Location, string DisplayName) : Choice(DisplayName);

public sealed record FitOption(WallpaperFit Fit, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Dynamic Wallpaper page. Every change is saved right away. It asks the host what only the
/// host knows, such as whether the feature stepped aside, through its pipe. See "Settings" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed partial class DynamicWallpaperViewModel : FeatureViewModel<DynamicWallpaperSettings>
{
    private static readonly CompositeFormat FolderFormat = CompositeFormat.Parse(Strings.DynamicWallpaperFolder);
    private static readonly CompositeFormat TimeRangeFormat = CompositeFormat.Parse(Strings.DynamicWallpaperTimeRange);
    private static readonly CompositeFormat NoPlaceFormat = CompositeFormat.Parse(Strings.DynamicWallpaperNoPlaceInfo);
    private static readonly CompositeFormat NoCoordinatesFormat = CompositeFormat.Parse(Strings.DynamicWallpaperNoCoordinatesInfo);
    private static readonly CompositeFormat UnnamedDisplayFormat = CompositeFormat.Parse(Strings.DynamicWallpaperUnnamedDisplay);
    private static readonly CompositeFormat TwinDisplayFormat = CompositeFormat.Parse(Strings.DynamicWallpaperTwinDisplay);

    // How long Windows may take to find the PC's position, and how old a position it may give.
    private static readonly TimeSpan LocationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LocationMaximumAge = TimeSpan.FromMinutes(10);

    private readonly ILogger<DynamicWallpaperViewModel> _logger;
    private readonly WindowsPictureFiles _files = new();
    private readonly WallpaperDisplayReader _displayReader = new();
    private ChangeWithOption? _selectedChangeWith;
    private LocationOption? _selectedLocation;
    private FitOption? _selectedFit;
    private DynamicWallpaperAnswer? _answer;
    private bool _spotlightNote;
    private string _timeZoneName = string.Empty;
    private string? _noPlaceInfo;
    private string? _todayNote;
    private bool _locating;
    private bool _locationDenied;
    private bool _locationFailed;

    // Whether each of the settings' displays is connected, as the page last read them; null
    // before the page has read them.
    private IReadOnlyList<bool>? _connected;

    // The coordinates while From your time zone is chosen, which only Coordinates keeps in the
    // settings, so choosing Coordinates again on the page brings them back.
    private (double? Latitude, double? Longitude) _keptCoordinates;

    // Counts the page's looks, so an older look doesn't overwrite a newer one.
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
        LocationOptions =
        [
            new(LocationSource.TimeZone, Strings.DynamicWallpaperLocationTimeZone),
            new(LocationSource.Coordinates, Strings.DynamicWallpaperLocationCoordinates),
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
        _selectedLocation = LocationOptions.First(option => option.Location == Settings.Location);
        _selectedFit = FitOptions.First(option => option.Fit == Settings.Fit);
        _keptCoordinates = (Settings.Latitude, Settings.Longitude);
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

    public IReadOnlyList<LocationOption> LocationOptions { get; }

    public IReadOnlyList<FitOption> FitOptions { get; }

    /// <summary>The periods of the schedule in use, in its order.</summary>
    public ObservableCollection<PeriodItem> Periods { get; } = [];

    /// <summary>With a picture for each display, the displays with their own pictures.</summary>
    public ObservableCollection<DisplayGroup> Displays { get; } = [];

    /// <summary>When each period starts and ends today.</summary>
    public ObservableCollection<TodayItem> Today { get; } = [];

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
                OnPropertyChanged(nameof(IsSun));
                OnPropertyChanged(nameof(HasToday));
                _ = LookAsync();
            }
        }
    }

    public LocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set
        {
            if (value is null || !SetProperty(ref _selectedLocation, value))
            {
                return;
            }

            if (value.Location == LocationSource.Coordinates)
            {
                Save(Settings with { Location = LocationSource.Coordinates, Latitude = _keptCoordinates.Latitude, Longitude = _keptCoordinates.Longitude });
            }
            else
            {
                _keptCoordinates = (Settings.Latitude, Settings.Longitude);
                Save(Settings with { Location = LocationSource.TimeZone, Latitude = null, Longitude = null });
            }

            LocationDenied = false;
            LocationFailed = false;
            OnLocationChanged();
            _ = LookAsync();
        }
    }

    public FitOption? SelectedFit
    {
        get => _selectedFit;
        set
        {
            if (value is not null && SetProperty(ref _selectedFit, value))
            {
                var eachDisplay = Settings.PicturesForEachDisplay;
                Save(Settings with { Fit = value.Fit });
                OnPropertyChanged(nameof(CanEachDisplay));
                if (eachDisplay != Settings.PicturesForEachDisplay)
                {
                    BuildPeriods();
                    _ = LookAsync();
                }
            }
        }
    }

    public bool IsSun => Settings.ChangeWith == ChangeWith.Sun;

    public bool IsTimes => Settings.ChangeWith == ChangeWith.Times;

    public bool IsWindowsMode => Settings.ChangeWith == ChangeWith.WindowsMode;

    /// <summary>Whether Today shows: not for Windows mode, which follows no clock.</summary>
    public bool HasToday => !IsWindowsMode;

    public bool IsTimeZoneLocation => Settings.Location == LocationSource.TimeZone;

    public bool IsCoordinates => Settings.Location == LocationSource.Coordinates;

    /// <summary>Windows' name of its time zone, such as "(UTC+06:00) Dhaka".</summary>
    public string TimeZoneName
    {
        get => _timeZoneName;
        private set => SetProperty(ref _timeZoneName, value);
    }

    /// <summary>What Sunrise and sunset does without a place; <see langword="null"/> with one.</summary>
    public string? NoPlaceInfo
    {
        get => _noPlaceInfo;
        private set
        {
            if (SetProperty(ref _noPlaceInfo, value))
            {
                OnPropertyChanged(nameof(HasNoPlace));
            }
        }
    }

    public bool HasNoPlace => IsSun && NoPlaceInfo is not null;

    /// <summary>The latitude in degrees north, for the number box; not a number without one.</summary>
    public double Latitude
    {
        get => Settings.Latitude ?? double.NaN;
        set => ChangeCoordinate(value, 90, Settings.Latitude, latitude => Settings with { Latitude = latitude });
    }

    /// <summary>The longitude in degrees east, for the number box; not a number without one.</summary>
    public double Longitude
    {
        get => Settings.Longitude ?? double.NaN;
        set => ChangeCoordinate(value, 180, Settings.Longitude, longitude => Settings with { Longitude = longitude });
    }

    /// <summary>Whether the page is waiting for Windows to find the PC's location.</summary>
    public bool Locating
    {
        get => _locating;
        private set => SetProperty(ref _locating, value);
    }

    /// <summary>Whether Windows refused to give the location.</summary>
    public bool LocationDenied
    {
        get => _locationDenied;
        private set => SetProperty(ref _locationDenied, value);
    }

    /// <summary>Whether Windows couldn't find the location.</summary>
    public bool LocationFailed
    {
        get => _locationFailed;
        private set => SetProperty(ref _locationFailed, value);
    }

    /// <summary>That the sun doesn't rise or set today; <see langword="null"/> on other days.</summary>
    public string? TodayNote
    {
        get => _todayNote;
        private set
        {
            if (SetProperty(ref _todayNote, value))
            {
                OnPropertyChanged(nameof(HasTodayNote));
            }
        }
    }

    public bool HasTodayNote => TodayNote is not null;

    /// <summary>Whether each display has its own pictures, as the switch shows it: off with Span.</summary>
    public bool EachDisplay
    {
        get => Settings.PicturesForEachDisplay;
        set
        {
            if (CanEachDisplay && Change(Settings.EachDisplay, value, () => Settings with { EachDisplay = value }))
            {
                BuildPeriods();
                _ = LookAsync();
            }
        }
    }

    /// <summary>Whether A picture for each display can be turned on: not with Span.</summary>
    public bool CanEachDisplay => Settings.Fit != WallpaperFit.Span;

    public bool PicturesForEachDisplay => Settings.PicturesForEachDisplay;

    public string PicturesHeader => PicturesForEachDisplay ? Strings.DynamicWallpaperOtherDisplaysHeader : Strings.DynamicWallpaperPicturesHeader;

    public string PicturesDescription => PicturesForEachDisplay ? Strings.DynamicWallpaperOtherDisplaysDescription : Strings.DynamicWallpaperPicturesDescription;

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

    // Every row: the periods' own, then each display's.
    private IEnumerable<PeriodItem> Rows => Periods.Concat(Displays.SelectMany(group => group.Periods));

    /// <summary>
    /// Asks the host for its state, reads the displays, works out Today and looks at the pictures,
    /// as the page opens and after each change.
    /// </summary>
    public async Task LookAsync()
    {
        var look = ++_look;
        await RefreshHostAsync();
        await RefreshDisplaysAsync();
        await RefreshTodayAsync(look);
        await RefreshFilesAsync(look);
    }

    /// <summary>Asks the host for its state, while the feature is on.</summary>
    public async Task RefreshHostAsync()
    {
        _answer = IsOn ? await DynamicWallpaperHost.AskAsync(DynamicWallpaperRequestKind.State) : null;
        OnHostChanged();
    }

    /// <summary>Uses a picture for a period, or for a display in a period.</summary>
    public void ChoosePicture(PeriodItem item, string picture)
    {
        ArgumentNullException.ThrowIfNull(item);
        ChooseSource(item, new WallpaperSource { Picture = picture });
    }

    /// <summary>Uses a folder for a period, or for a display in a period.</summary>
    public void ChooseFolder(PeriodItem item, string folder)
    {
        ArgumentNullException.ThrowIfNull(item);
        ChooseSource(item, new WallpaperSource { Folder = folder });
    }

    /// <summary>Leaves Sunrise or Sunset without a picture.</summary>
    public void ChooseNone(PeriodItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.CanBeNone)
        {
            Change(item.Period, null);
        }
    }

    /// <summary>Gives a display the period's own picture, as other displays have.</summary>
    public void ChooseSame(PeriodItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.CanBeSame)
        {
            ChooseSource(item, null);
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

    /// <summary>Removes a display that's not connected, with its pictures.</summary>
    public void RemoveDisplay(DisplayGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var index = group.Index;
        if (!group.CanRemove || index >= Settings.Displays.Count)
        {
            return;
        }

        Save(Settings with
        {
            Displays = [.. Settings.Displays.Where((_, i) => i != index)],
            Sun = new SunPeriods
            {
                Sunrise = WithoutDisplay(Settings.Sun.Sunrise, index),
                Day = WithoutDisplay(Settings.Sun.Day, index)!,
                Sunset = WithoutDisplay(Settings.Sun.Sunset, index),
                Night = WithoutDisplay(Settings.Sun.Night, index)!,
            },
            Times = [.. Settings.Times.Select(time => WithoutDisplay(time, index)!)],
            WindowsMode = new WindowsModePeriods
            {
                Light = WithoutDisplay(Settings.WindowsMode.Light, index)!,
                Dark = WithoutDisplay(Settings.WindowsMode.Dark, index)!,
            },
        });
        _connected = _connected is null ? null : [.. _connected.Where((_, i) => i != index)];
        BuildPeriods();
        _ = LookAsync();
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

    [RelayCommand]
    private void OpenLocationSettings()
    {
        if (!ShellActions.OpenLocationSettings())
        {
            LogCantOpenLocationSettings(_logger);
        }
    }

    // Asks Windows for the PC's position once, from the page in front, which Windows' question
    // needs, and keeps it rounded as the coordinates. See "Location" in the spec.
    [RelayCommand]
    private async Task UseMyLocationAsync()
    {
        LocationDenied = false;
        LocationFailed = false;
        Locating = true;
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                LocationDenied = true;
                return;
            }

            var position = await new Geolocator().GetGeopositionAsync(LocationMaximumAge, LocationTimeout);
            var point = position.Coordinate.Point.Position;
            var place = DynamicWallpaperCheck.Round(new Coordinates(point.Latitude, point.Longitude));
            if (!DynamicWallpaperCheck.AreCoordinates(place.Latitude, place.Longitude))
            {
                LocationFailed = true;
                return;
            }

            Save(Settings with { Location = LocationSource.Coordinates, Latitude = place.Latitude, Longitude = place.Longitude });
            _keptCoordinates = (place.Latitude, place.Longitude);
            _selectedLocation = LocationOptions.First(option => option.Location == LocationSource.Coordinates);
            OnPropertyChanged(nameof(SelectedLocation));
            OnLocationChanged();
            _ = LookAsync();
        }
        catch (UnauthorizedAccessException)
        {
            LocationDenied = true;
        }
        catch (Exception exception) when (exception is COMException or TimeoutException or OperationCanceledException)
        {
            // The error only: no location is logged.
            LogLocationFailed(_logger, exception.HResult);
            LocationFailed = true;
        }
        finally
        {
            Locating = false;
        }
    }

    private bool IsBlocked(WallpaperBlock block) => IsOn && _answer is { Running: true } answer && answer.Block == block;

    private void OnHostChanged()
    {
        OnPropertyChanged(nameof(SteppedAside));
        OnPropertyChanged(nameof(Managed));
        OnPropertyChanged(nameof(ExplorerNotAnswering));
        OnPropertyChanged(nameof(RemoteSession));
        foreach (var item in Rows)
        {
            item.InUse = _answer is { Running: true } answer && answer.Period == item.Period.ToString() && answer.Block == WallpaperBlock.None;
        }
    }

    private void OnLocationChanged()
    {
        OnPropertyChanged(nameof(IsTimeZoneLocation));
        OnPropertyChanged(nameof(IsCoordinates));
        OnPropertyChanged(nameof(Latitude));
        OnPropertyChanged(nameof(Longitude));
    }

    // Saves a latitude or longitude with one decimal; one that's empty or out of range is none.
    private void ChangeCoordinate(double value, double limit, double? current, Func<double?, DynamicWallpaperSettings> changed, [CallerMemberName] string? propertyName = null)
    {
        double? coordinate = double.IsFinite(value) && Math.Abs(value) <= limit ? Math.Round(value, 1, MidpointRounding.AwayFromZero) : null;
        if (coordinate == current)
        {
            return;
        }

        Save(changed(coordinate));
        _keptCoordinates = (Settings.Latitude, Settings.Longitude);
        OnPropertyChanged(propertyName);
        _ = LookAsync();
    }

    // The settings' period for a row; null for Sunrise or Sunset without a picture.
    private WallpaperPeriod? PeriodOf(PeriodItem item) => item.Period.Kind switch
    {
        ChangeWith.Sun => Settings.Sun.Of((SunPeriod)item.Period.Index),
        ChangeWith.WindowsMode => item.Period.Index == 0 ? Settings.WindowsMode.Light : Settings.WindowsMode.Dark,
        _ => item.Period.Index < Settings.Times.Count ? Settings.Times[item.Period.Index] : null,
    };

    // A row's own picture or folder: the period's, or the display's; null for none, or for a
    // display with the period's own.
    private WallpaperSource? SourceOf(PeriodItem item) => PeriodOf(item) is not { } period ? null
        : item.Display < 0 ? period.Own
        : period.Displays is { } displays && item.Display < displays.Count ? displays[item.Display]
        : null;

    private void ChooseSource(PeriodItem item, WallpaperSource? source)
    {
        var period = PeriodOf(item);
        if (item.Display < 0)
        {
            Change(item.Period, (period ?? new WallpaperPeriod()) with { Picture = source?.Picture, Folder = source?.Folder });
            return;
        }

        if (period is null || item.Display >= Settings.Displays.Count)
        {
            return;
        }

        var displays = new List<WallpaperSource?>(period.Displays ?? []);
        while (displays.Count < Settings.Displays.Count)
        {
            displays.Add(null);
        }

        displays[item.Display] = source;
        Change(item.Period, period with { Displays = displays.Any(display => display is not null) ? displays : null });
    }

    // A period without a display's entry.
    private static WallpaperPeriod? WithoutDisplay(WallpaperPeriod? period, int index)
    {
        if (period?.Displays is not { } displays || index >= displays.Count)
        {
            return period;
        }

        var left = displays.Where((_, i) => i != index).ToList();
        return period with { Displays = left.Any(display => display is not null) ? left : null };
    }

    private void Change(PeriodName name, WallpaperPeriod? period)
    {
        Save(name.Kind switch
        {
            ChangeWith.Sun => Settings with
            {
                Sun = (SunPeriod)name.Index switch
                {
                    SunPeriod.Sunrise => Settings.Sun with { Sunrise = period },
                    SunPeriod.Day => Settings.Sun with { Day = period ?? new() },
                    SunPeriod.Sunset => Settings.Sun with { Sunset = period },
                    _ => Settings.Sun with { Night = period ?? new() },
                },
            },
            ChangeWith.WindowsMode => Settings with
            {
                WindowsMode = name.Index == 0
                    ? Settings.WindowsMode with { Light = period ?? new() }
                    : Settings.WindowsMode with { Dark = period ?? new() },
            },
            _ => Settings with { Times = [.. Settings.Times.Select((time, index) => index == name.Index ? period ?? time : time)] },
        });
        Describe();
        OnPropertyChanged(nameof(SamePicture));
        _ = LookAsync();
    }

    private void OnTimeChanged(PeriodItem item, TimeOnly time)
    {
        if (item.Period.Index < Settings.Times.Count)
        {
            Change(item.Period, Settings.Times[item.Period.Index] with { At = DynamicWallpaperCheck.FormatTime(time) });

            // The displays' rows carry the time in their names.
            BuildDisplays();
            Describe();
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
                    Periods.Add(new PeriodItem(new PeriodName(ChangeWith.Times, index), TimeOf(index), -1, OnTimeChanged));
                }

                break;
            case ChangeWith.WindowsMode:
                Periods.Add(new PeriodItem(PeriodName.Light, null, -1, OnTimeChanged));
                Periods.Add(new PeriodItem(PeriodName.Dark, null, -1, OnTimeChanged));
                break;
            default:
                foreach (var period in Enum.GetValues<SunPeriod>().OrderBy(period => period == SunPeriod.Night))
                {
                    Periods.Add(new PeriodItem(PeriodName.Of(period), null, -1, OnTimeChanged));
                }

                break;
        }

        BuildDisplays();
        Describe();
        OnPropertyChanged(nameof(CanAddTime));
        OnPropertyChanged(nameof(SamePicture));
        OnPropertyChanged(nameof(EachDisplay));
        OnPropertyChanged(nameof(PicturesForEachDisplay));
        OnPropertyChanged(nameof(PicturesHeader));
        OnPropertyChanged(nameof(PicturesDescription));
        OnHostChanged();
    }

    // With a picture for each display, a group for each of the settings' displays, with a row for
    // each period.
    private void BuildDisplays()
    {
        Displays.Clear();
        if (!Settings.PicturesForEachDisplay)
        {
            return;
        }

        var names = DisplayNames();
        for (var index = 0; index < names.Count; index++)
        {
            var group = new DisplayGroup(index, names[index]) { Connected = ConnectedAt(index) };
            foreach (var period in Periods)
            {
                group.Periods.Add(new PeriodItem(period.Period, period.IsTime ? TimeOf(period.Period.Index) : null, index, OnTimeChanged));
            }

            Displays.Add(group);
        }
    }

    private bool? ConnectedAt(int index) => _connected is null ? null : index < _connected.Count && _connected[index];

    private TimeOnly? TimeOf(int index) =>
        index < Settings.Times.Count && DynamicWallpaperCheck.TryParseTime(Settings.Times[index].At, out var at) ? at : null;

    // The displays' names, as Display Profiles names them; identical displays are numbered.
    private List<string> DisplayNames()
    {
        var names = Settings.Displays
            .Take(DynamicWallpaperCheck.MaximumDisplays)
            .Select((display, index) => display.BuiltIn ? Strings.DynamicWallpaperBuiltInDisplay
                : display.Name.Trim().Length > 0 ? display.Name.Trim()
                : string.Format(CultureInfo.CurrentCulture, UnnamedDisplayFormat, (index + 1).ToString(CultureInfo.CurrentCulture)))
            .ToList();
        return
        [
            .. names.Select((name, index) => names.Count(other => other == name) > 1
                ? string.Format(CultureInfo.CurrentCulture, TwinDisplayFormat, name, names.Take(index + 1).Count(other => other == name).ToString(CultureInfo.CurrentCulture))
                : name),
        ];
    }

    // Each row's picture or folder name, and the problems the settings alone show.
    private void Describe()
    {
        var timeProblems = DynamicWallpaperCheck.CheckTimes(Settings.Times);
        foreach (var item in Rows)
        {
            var period = PeriodOf(item);
            item.CanRemove = item.IsTime && Settings.Times.Count > DynamicWallpaperCheck.MinimumTimes;
            item.CanChoose = period is not null || item.Display < 0;
            if (period is null)
            {
                item.Source = (SunPeriod)item.Period.Index == SunPeriod.Sunrise ? Strings.DynamicWallpaperSunriseNone : Strings.DynamicWallpaperSunsetNone;
                item.Problem = null;
                item.Thumbnail = null;
                continue;
            }

            if (SourceOf(item) is not { } source)
            {
                item.Source = Strings.DynamicWallpaperSameAsOthers;
                item.Problem = null;
                item.Thumbnail = null;
                continue;
            }

            item.Source = source.Folder is { Length: > 0 } folder
                ? string.Format(CultureInfo.CurrentCulture, FolderFormat, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)))
                : source.Picture is { Length: > 0 } picture ? Path.GetFileName(picture) : Strings.DynamicWallpaperNoPicture;
            item.Problem = item.ShowsTime && item.Period.Index < timeProblems.Count && timeProblems[item.Period.Index] == TimeProblem.SameTime
                ? Strings.DynamicWallpaperProblemSameTime
                : DynamicWallpaperText.Of(DynamicWallpaperCheck.Check(source));
        }
    }

    // Reads the connected displays off the UI thread, with a picture for each display. Connected
    // displays that the settings don't have yet are added, so every connected display is listed.
    private async Task RefreshDisplaysAsync()
    {
        if (!Settings.PicturesForEachDisplay)
        {
            return;
        }

        var connected = await Task.Run(() => _displayReader.Read());
        if (!Settings.PicturesForEachDisplay)
        {
            return;
        }

        var known = Settings.Displays
            .Take(DynamicWallpaperCheck.MaximumDisplays)
            .Select(display => DynamicWallpaperCheck.IsValid(display) ? display.ToKnown() : new KnownDisplay("", "", "", false))
            .ToList();
        var recognized = DisplayRecognition.Recognize(known, connected.Displays, connected.SerialOf).Displays;
        var added = connected.Displays
            .Where(display => !recognized.Any(found => found is not null && string.Equals(found.DevicePath, display.DevicePath, StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Max(0, DynamicWallpaperCheck.MaximumDisplays - Settings.Displays.Count))
            .Select(display => WallpaperDisplayReader.ToSettings(display, connected))
            .ToList();
        _connected = [.. recognized.Select(display => display is not null), .. added.Select(_ => true)];
        if (added.Count > 0)
        {
            Save(Settings with { Displays = [.. Settings.Displays, .. added] });
            BuildPeriods();
            return;
        }

        foreach (var group in Displays)
        {
            group.Connected = ConnectedAt(group.Index);
        }
    }

    // Works out today's periods off the UI thread: Sunrise and sunset searches the day for the sun's
    // changes.
    private async Task RefreshTodayAsync(int look)
    {
        var settings = Settings;
        var (zone, place, spans, sunDay) = await Task.Run(() =>
        {
            var local = TimeZoneInfo.Local;
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.Now, local).DateTime);
            var found = WallpaperPlanner.PlaceOf(settings, () => TimeZonePlaces.Of(local, WindowsRegion.CountryOrRegion()));
            SunDay? day = settings.ChangeWith == ChangeWith.Sun && found is { } here ? WallpaperPlanner.SunDayOf(here, date, local) : null;
            return (local, found, WallpaperPlanner.Day(settings, date, local, found), day);
        });
        if (look != _look)
        {
            return;
        }

        Today.Clear();
        foreach (var span in spans)
        {
            var range = string.Format(CultureInfo.CurrentCulture, TimeRangeFormat, LocalTime(span.Start, zone), LocalTime(span.End, zone));
            Today.Add(new TodayItem(DynamicWallpaperText.NameOf(span.Period, span.Period.Kind == ChangeWith.Times ? TimeOf(span.Period.Index) : null), range));
        }

        TimeZoneName = zone.DisplayName;
        NoPlaceInfo = settings.ChangeWith != ChangeWith.Sun || place is not null ? null
            : string.Format(
                CultureInfo.CurrentCulture,
                settings.Location == LocationSource.Coordinates ? NoCoordinatesFormat : NoPlaceFormat,
                DynamicWallpaperText.FormatTime(WallpaperPlanner.FixedSunrise),
                DynamicWallpaperText.FormatTime(WallpaperPlanner.FixedSunset));
        OnPropertyChanged(nameof(HasNoPlace));
        TodayNote = sunDay switch
        {
            SunDay.DoesntRise => Strings.DynamicWallpaperSunDoesntRise,
            SunDay.DoesntSet => Strings.DynamicWallpaperSunDoesntSet,
            _ => null,
        };

        static string LocalTime(DateTimeOffset at, TimeZoneInfo clock) =>
            DynamicWallpaperText.FormatTime(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, clock).DateTime));
    }

    // Checks each row's picture or folder off the UI thread, then shows Windows' thumbnails of
    // those that can be used, and the host's problems with the period in use.
    private async Task RefreshFilesAsync(int look)
    {
        var rows = Rows.Select(item => (Item: item, Source: SourceOf(item))).ToList();
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
                ? answer.Issues.FirstOrDefault(issue => Math.Max(issue.Display, -1) == item.Display)
                : null;
            var sameTime = item.ShowsTime && item.Period.Index < timeProblems.Count && timeProblems[item.Period.Index] == TimeProblem.SameTime;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows Settings' Location page couldn't be opened.")]
    private static partial void LogCantOpenLocationSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Windows couldn't give the PC's location: 0x{HResult:X8}.")]
    private static partial void LogLocationFailed(ILogger logger, int hResult);
}
