using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.DynamicWallpaper;

namespace PCCompanion.App.Features.DynamicWallpaper;

/// <summary>
/// A period's row on the page: its name, its picture or folder, and whether it's in use; for a
/// display with its own pictures, the display's picture or folder for the period.
/// </summary>
public sealed partial class PeriodItem : ObservableObject
{
    private static readonly CompositeFormat ChooseFormat = CompositeFormat.Parse(Strings.DynamicWallpaperChooseName);
    private static readonly CompositeFormat RemoveFormat = CompositeFormat.Parse(Strings.DynamicWallpaperRemoveName);
    private static readonly CompositeFormat TimeFormat = CompositeFormat.Parse(Strings.DynamicWallpaperTimeName);
    private static readonly CompositeFormat RowFormat = CompositeFormat.Parse(Strings.DynamicWallpaperRowName);
    private static readonly CompositeFormat RowInUseFormat = CompositeFormat.Parse(Strings.DynamicWallpaperRowNameInUse);
    private static readonly CompositeFormat ProblemFormat = CompositeFormat.Parse(Strings.DynamicWallpaperPictureProblem);

    private readonly Action<PeriodItem, TimeOnly> _timeChanged;
    private string _name;
    private string _source = string.Empty;
    private string? _problem;
    private ImageSource? _thumbnail;
    private bool _inUse;
    private bool _canRemove;
    private bool _canChoose = true;
    private TimeSpan? _time;

    /// <param name="period">The period.</param>
    /// <param name="time">For Times of day, its time.</param>
    /// <param name="display">The display's place in the settings' displays; -1 for the period's own picture.</param>
    /// <param name="timeChanged">Called when the user changes the time.</param>
    public PeriodItem(PeriodName period, TimeOnly? time, int display, Action<PeriodItem, TimeOnly> timeChanged)
    {
        Period = period;
        Display = display;
        _timeChanged = timeChanged;
        _time = time?.ToTimeSpan();
        _name = DynamicWallpaperText.NameOf(period, time);
        CanBeNone = display < 0 && period.Kind == ChangeWith.Sun && (SunPeriod)period.Index is SunPeriod.Sunrise or SunPeriod.Sunset;
    }

    public PeriodName Period { get; }

    /// <summary>The display's place in the settings' displays; -1 for the period's own picture.</summary>
    public int Display { get; }

    /// <summary>Whether it can have no picture: Sunrise and Sunset.</summary>
    public bool CanBeNone { get; }

    /// <summary>Whether it's a display's row, which can have the same picture as other displays.</summary>
    public bool CanBeSame => Display >= 0;

    /// <summary>Whether it's a time of Times of day, which has its time and can be removed.</summary>
    public bool IsTime => Period.Kind == ChangeWith.Times;

    /// <summary>Whether the row shows the time and Remove: a time's own row.</summary>
    public bool ShowsTime => IsTime && Display < 0;

    public string Name
    {
        get => _name;
        private set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(ChooseName));
                OnPropertyChanged(nameof(RemoveName));
                OnPropertyChanged(nameof(TimeName));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    /// <summary>The picture's file name or the folder's, or what having none means.</summary>
    public string Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value))
            {
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    /// <summary>Why the picture or folder can't be used now; <see langword="null"/> when it can.</summary>
    public string? Problem
    {
        get => _problem;
        set
        {
            if (SetProperty(ref _problem, value))
            {
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string Description => Problem is null ? Source : string.Format(CultureInfo.CurrentCulture, ProblemFormat, Source, Problem);

    /// <summary>Windows' thumbnail of the picture or folder; <see langword="null"/> while there's none.</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasNoThumbnail));
            }
        }
    }

    public bool HasNoThumbnail => Thumbnail is null;

    public bool InUse
    {
        get => _inUse;
        set
        {
            if (SetProperty(ref _inUse, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    /// <summary>Whether a time can be removed: Times of day keeps at least two.</summary>
    public bool CanRemove
    {
        get => _canRemove;
        set => SetProperty(ref _canRemove, value);
    }

    /// <summary>Whether a picture can be chosen: not for a display while Sunrise or Sunset has none.</summary>
    public bool CanChoose
    {
        get => _canChoose;
        set => SetProperty(ref _canChoose, value);
    }

    /// <summary>The time, for the time picker.</summary>
    public TimeSpan? Time
    {
        get => _time;
        set
        {
            if (value is { } time && SetProperty(ref _time, value))
            {
                var at = TimeOnly.FromTimeSpan(time);
                Name = DynamicWallpaperText.NameOf(Period, at);
                _timeChanged(this, at);
            }
        }
    }

    public string ChooseName => string.Format(CultureInfo.CurrentCulture, ChooseFormat, Name);

    public string RemoveName => string.Format(CultureInfo.CurrentCulture, RemoveFormat, Name);

    public string TimeName => string.Format(CultureInfo.CurrentCulture, TimeFormat, Name);

    /// <summary>What screen readers read for the row, such as "Night, Stars.jpg, in use".</summary>
    public string AutomationName => string.Format(CultureInfo.CurrentCulture, InUse ? RowInUseFormat : RowFormat, Name, Description);
}

/// <summary>A display with its own pictures, and its row for each period.</summary>
public sealed partial class DisplayGroup(int index, string name) : ObservableObject
{
    private static readonly CompositeFormat RemoveFormat = CompositeFormat.Parse(Strings.DynamicWallpaperRemoveDisplayName);

    private bool? _connected;

    /// <summary>The display's place in the settings' displays.</summary>
    public int Index { get; } = index;

    public string Name { get; } = name;

    public ObservableCollection<PeriodItem> Periods { get; } = [];

    /// <summary>Whether the display is connected; <see langword="null"/> until the page has read the displays.</summary>
    public bool? Connected
    {
        get => _connected;
        set
        {
            if (SetProperty(ref _connected, value))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(CanRemove));
            }
        }
    }

    public string Status => Connected switch
    {
        true => Strings.DynamicWallpaperDisplayConnected,
        false => Strings.DynamicWallpaperDisplayNotConnected,
        null => string.Empty,
    };

    /// <summary>Whether the display can be removed: only while it's not connected, since a connected one is listed again.</summary>
    public bool CanRemove => Connected == false;

    public string RemoveName => string.Format(CultureInfo.CurrentCulture, RemoveFormat, Name);
}

/// <summary>A period's time span today, for Today.</summary>
public sealed record TodayItem(string Name, string Span);
