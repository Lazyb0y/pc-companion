using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>Why the background isn't as Dynamic Wallpaper would have it.</summary>
public enum WallpaperBlock
{
    None,

    /// <summary>A policy manages the background.</summary>
    Managed,

    /// <summary>Explorer isn't running, or didn't answer.</summary>
    ExplorerNotAnswering,

    /// <summary>A remote session shows no picture.</summary>
    RemoteSession,

    /// <summary>Someone else changed the background, so Dynamic Wallpaper stepped aside until it's resumed.</summary>
    SteppedAside,
}

/// <summary>Why a picture or folder can't be used now.</summary>
public enum PictureProblem
{
    /// <summary>The settings' picture or folder isn't valid.</summary>
    Invalid,

    Missing,
    OnNetwork,
    OnlineOnly,

    /// <summary>A folder without pictures that can be used.</summary>
    NoPictures,

    /// <summary>Windows refused the picture, as one it can't read.</summary>
    Refused,
}

/// <summary>A picture or folder of the period in use that can't be used.</summary>
/// <param name="Display">The place of the display in the settings' displays; -1 for the period's own, for every display or Other displays.</param>
public sealed record PictureIssue(int Display, PictureProblem Problem);

/// <summary>What the page shows about the host's work: the period in use, and what's in the way.</summary>
/// <param name="Spotlight">Whether Windows spotlight changed the background before Dynamic Wallpaper did.</param>
public sealed record WallpaperReport(string? Period, WallpaperBlock Block, IReadOnlyList<PictureIssue> Issues, bool Spotlight = false)
{
    public static WallpaperReport Empty { get; } = new(null, WallpaperBlock.None, []);

    // Compares the issues one by one, so the same report made twice is equal.
    public bool Equals(WallpaperReport? other) =>
        other is not null && Period == other.Period && Block == other.Block && Issues.SequenceEqual(other.Issues) && Spotlight == other.Spotlight;

    public override int GetHashCode() => HashCode.Combine(Period, Block, Issues.Count, Spotlight);
}

/// <summary>
/// Sets the period's pictures through Windows' wallpaper function, on the wallpaper thread: only
/// those that differ from what Windows shows, each display's when it has its own. It remembers the
/// background before its first change and puts it back when turned off, and steps aside when someone
/// else changes it. See "When pictures change", "Displays" and "Working alongside Windows" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
internal sealed partial class WallpaperApplier(IDynamicWallpaperSystem system, ILogger logger)
{
    // Windows refused a picture it can't read, or that's gone.
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);

    private IWindowsWallpaper? _wallpaper;
    private WallpaperState _state = new();

    // The virtual desktop in use when the pictures were set last, where Windows records it.
    private Guid? _desktop;
    private bool _loaded;
    private volatile WallpaperReport _report = WallpaperReport.Empty;

    /// <summary>The report after the last job, for the page; read from any thread.</summary>
    public WallpaperReport Report => _report;

    /// <summary>The state, for the jobs that follow on the wallpaper thread.</summary>
    public WallpaperState State
    {
        get
        {
            Load();
            return _state;
        }
    }

    /// <summary>Sets the plan's pictures and the fit, where Windows shows others.</summary>
    public void Apply(DynamicWallpaperSettings settings, WallpaperPlan plan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);

        Load();
        var period = plan.Period.ToString();
        if (system.PoliciesSet())
        {
            Block(period, WallpaperBlock.Managed);
            return;
        }

        if (_state.SteppedAside)
        {
            Block(period, WallpaperBlock.SteppedAside);
            return;
        }

        if (plan.Pictures is not { } pictures)
        {
            _report = Made(period, WallpaperBlock.None, []);
            return;
        }

        if (Connection() is not { } wallpaper)
        {
            Block(period, WallpaperBlock.ExplorerNotAnswering);
            return;
        }

        try
        {
            if (wallpaper.Status().HasFlag(WallpaperStatus.OffInRemoteSession))
            {
                Block(period, WallpaperBlock.RemoteSession);
                return;
            }

            SetPictures(settings, plan, pictures, wallpaper);
        }
        catch (WallpaperException exception)
        {
            Failed(exception, period);
        }
    }

    /// <summary>Drops the connection to Explorer, so the next job connects again, as after Explorer restarted.</summary>
    public void Reconnect() => Disconnect();

    /// <summary>
    /// Puts back the background Windows had before the first change, unless the feature stepped
    /// aside, then forgets it. While Explorer doesn't answer, it keeps it, to put it back the next
    /// time the host starts with the feature off.
    /// </summary>
    public void TurnOff()
    {
        Load();
        if (_state.Remembered is not { } remembered || _state.SteppedAside)
        {
            Forget();
            return;
        }

        if (Connection() is not { } wallpaper)
        {
            return;
        }

        try
        {
            PutBack(wallpaper, remembered);
            LogPutBack(logger, remembered.Kind, remembered.Spotlight);
        }
        catch (WallpaperException exception)
        {
            Failed(exception, null);
            return;
        }

        Forget();
    }

    /// <summary>
    /// Looks at a change of the background: Dynamic Wallpaper's own, a switch of virtual desktop,
    /// which gets the period's pictures, or someone else's, which makes it step aside.
    /// </summary>
    public void CheckOthers(DynamicWallpaperSettings settings, WallpaperPlan plan)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);

        Load();
        if (_state.Remembered is null || _state.SteppedAside || _state.SetLast.Count == 0 || Connection() is not { } wallpaper)
        {
            return;
        }

        var period = plan.Period.ToString();
        try
        {
            var status = wallpaper.Status();
            var picture = status.HasFlag(WallpaperStatus.On) && !status.HasFlag(WallpaperStatus.Slideshow);
            if (picture && _state.SetLast.All(item => Same(wallpaper.PictureOf(item.Display), item.Picture)))
            {
                return;
            }

            // A switch of virtual desktop shows that desktop's picture, which may be an old one.
            var desktop = system.CurrentVirtualDesktop();
            if (desktop is not null && desktop != _desktop)
            {
                LogDesktopSwitched(logger);
                Apply(settings, plan);
                return;
            }

            // Where the desktop in use isn't known, a picture of the settings' is the feature's own, as on another desktop.
            if (picture && _state.SetLast.All(item => IsOwn(settings, wallpaper.PictureOf(item.Display))))
            {
                Apply(settings, plan);
                return;
            }

            Save(_state with { SteppedAside = true, Remembered = Read(wallpaper), SetLast = [] });
            LogSteppedAside(logger);
            Block(period, WallpaperBlock.SteppedAside);
        }
        catch (WallpaperException exception)
        {
            Failed(exception, period);
        }
    }

    /// <summary>
    /// Changes the background again after stepping aside: it remembers the background Windows has
    /// now, as when turned on, and sets the period's pictures.
    /// </summary>
    public void Resume(DynamicWallpaperSettings settings, WallpaperPlan plan)
    {
        Load();
        if (!_state.SteppedAside)
        {
            return;
        }

        Save(_state with { SteppedAside = false, Remembered = null, SetLast = [] });
        LogResumed(logger);
        Apply(settings, plan);
    }

    /// <summary>Saves the state, when it changed.</summary>
    public void Save(WallpaperState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state == _state)
        {
            return;
        }

        _state = state;
        try
        {
            system.Store.Save(state);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogStateNotSaved(logger, exception);
        }
    }

    private static bool Same(string? shown, string picture) => string.Equals(shown, picture, StringComparison.OrdinalIgnoreCase);

    // Whether a picture is one of the settings' pictures, or in one of their folders.
    private static bool IsOwn(DynamicWallpaperSettings settings, string? shown)
    {
        if (string.IsNullOrEmpty(shown))
        {
            return false;
        }

        WallpaperPeriod?[] periods = [settings.Sun.Sunrise, settings.Sun.Day, settings.Sun.Sunset, settings.Sun.Night, settings.WindowsMode.Light, settings.WindowsMode.Dark, .. settings.Times];
        var folder = Path.GetDirectoryName(shown);
        return periods
            .OfType<WallpaperPeriod>()
            .SelectMany(period => (IEnumerable<WallpaperSource?>)[period.Own, .. period.Displays ?? []])
            .OfType<WallpaperSource>()
            .Any(source => Same(shown, source.Picture ?? "") || (source.Folder is { } own && string.Equals(folder, Path.TrimEndingDirectorySeparator(own), StringComparison.OrdinalIgnoreCase)));
    }

    // Puts a remembered background back. A picture that's gone, or a slideshow whose folder is,
    // gives the background color.
    private static void PutBack(IWindowsWallpaper wallpaper, RememberedBackground remembered)
    {
        wallpaper.SetBackgroundColor(remembered.Color & 0xFFFFFF);
        if (Enum.IsDefined(remembered.Fit))
        {
            wallpaper.SetPosition(remembered.Fit);
        }

        switch (remembered.Kind)
        {
            case BackgroundKind.Slideshow when remembered.Slideshow.Count > 0 && remembered.Slideshow.All(item => DynamicWallpaperCheck.CheckPath(item) == SourceProblem.None):
                try
                {
                    wallpaper.SetSlideshow(new WallpaperSlideshow(remembered.Slideshow, remembered.SlideshowShuffle, TimeSpan.FromMilliseconds(Math.Max(remembered.SlideshowInterval, 0))));
                }
                catch (WallpaperException exception) when (exception.HResult is InvalidArgument or FileNotFound or PathNotFound)
                {
                    wallpaper.TurnOff();
                }

                break;
            case BackgroundKind.Picture:
                var connected = wallpaper.Displays().Where(display => display.Area is not null).Select(display => display.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var putBack = false;
                foreach (var picture in remembered.Pictures.Where(picture =>
                    DynamicWallpaperCheck.CheckPath(picture.Picture) == SourceProblem.None && (picture.Display is null || connected.Contains(picture.Display))))
                {
                    try
                    {
                        putBack |= wallpaper.SetPicture(picture.Display, picture.Picture);
                    }
                    catch (WallpaperException exception) when (exception.HResult is InvalidArgument or FileNotFound or PathNotFound)
                    {
                        // Gone: the background color shows for it.
                    }
                }

                if (!putBack)
                {
                    wallpaper.TurnOff();
                }

                break;
            default:
                wallpaper.TurnOff();
                break;
        }
    }

    // The background Windows has now, to put back.
    private RememberedBackground Read(IWindowsWallpaper wallpaper)
    {
        var status = wallpaper.Status();
        var background = new RememberedBackground { Fit = wallpaper.Position(), Color = wallpaper.BackgroundColor() };
        if (status.HasFlag(WallpaperStatus.Slideshow) && wallpaper.Slideshow() is { } slideshow)
        {
            return background with
            {
                Kind = BackgroundKind.Slideshow,
                Slideshow = slideshow.Items,
                SlideshowShuffle = slideshow.Shuffle,
                SlideshowInterval = (long)slideshow.Interval.TotalMilliseconds,
            };
        }

        if (!status.HasFlag(WallpaperStatus.On))
        {
            return background with { Kind = BackgroundKind.SolidColor };
        }

        IReadOnlyList<DisplayPicture> pictures = wallpaper.PictureOf(null) is { } all
            ? [new DisplayPicture(null, all)]
            : [.. wallpaper.Displays().Where(display => display.Area is not null).Select(display => new DisplayPicture(display.Id, wallpaper.PictureOf(display.Id) ?? ""))];
        return background with { Kind = BackgroundKind.Picture, Pictures = pictures, Spotlight = system.IsSpotlightOn() == true };
    }

    private void Forget()
    {
        _state = new WallpaperState();
        try
        {
            system.Store.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogStateNotSaved(logger, exception);
        }
    }

    private void SetPictures(DynamicWallpaperSettings settings, WallpaperPlan plan, WallpaperPeriod pictures, IWindowsWallpaper wallpaper)
    {
        var period = plan.Period.ToString();
        if (_state.Remembered is null)
        {
            Save(_state with { Remembered = Read(wallpaper) });
            LogRemembered(logger, _state.Remembered!.Kind, _state.Remembered.Spotlight);
        }

        var advance = _state.Period != period;
        var position = WallpaperFits.PositionOf(settings.Fit);
        if (wallpaper.Position() != position)
        {
            wallpaper.SetPosition(position);
        }

        var issues = new List<PictureIssue>();
        var folders = _state.Folders.ToList();
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var set = new List<DisplayPicture>();
        var managed = false;
        foreach (var (display, index, source) in Targets(settings, pictures, wallpaper))
        {
            if (Resolve(source, advance, folders, chosen) is not { } picture)
            {
                issues.Add(new PictureIssue(index, Problem(source)));
                continue;
            }

            try
            {
                if (!Same(wallpaper.PictureOf(display), picture))
                {
                    if (!wallpaper.SetPicture(display, picture))
                    {
                        managed = true;
                        break;
                    }

                    LogPictureSet(logger, period, index);
                }

                set.Add(new DisplayPicture(display, picture));
            }
            catch (WallpaperException exception) when (exception.HResult is InvalidArgument or FileNotFound or PathNotFound)
            {
                LogPictureRefused(logger, period, index, exception.HResult);
                issues.Add(new PictureIssue(index, PictureProblem.Refused));
            }
        }

        // A policy can show its own picture instead, which Windows then reports.
        managed |= set.Any(item => !Same(wallpaper.PictureOf(item.Display), item.Picture));
        if (managed)
        {
            Block(period, WallpaperBlock.Managed);
            return;
        }

        Save(_state with { Period = period, SetLast = set, Folders = folders });
        _desktop = system.CurrentVirtualDesktop();
        _report = Made(period, WallpaperBlock.None, issues);
    }

    // Each display to set and its picture or folder: every display at once, or each connected one.
    private IEnumerable<(string? Display, int Index, WallpaperSource Source)> Targets(DynamicWallpaperSettings settings, WallpaperPeriod pictures, IWindowsWallpaper wallpaper)
    {
        if (!settings.PicturesForEachDisplay)
        {
            yield return (null, -1, pictures.Own);
            yield break;
        }

        var displays = system.ReadDisplays();
        var known = settings.Displays
            .Take(DynamicWallpaperCheck.MaximumDisplays)
            .Select(display => DynamicWallpaperCheck.IsValid(display) ? display.ToKnown() : new KnownDisplay("", "", "", false))
            .ToList();
        var recognized = DisplayRecognition.Recognize(known, displays.Displays, displays.SerialOf).Displays;
        foreach (var monitor in wallpaper.Displays().Where(monitor => monitor.Area is not null))
        {
            var index = recognized.ToList().FindIndex(display => display is not null && string.Equals(display.DevicePath, monitor.Id, StringComparison.OrdinalIgnoreCase));
            yield return (monitor.Id, index, index < 0 ? pictures.Own : pictures.For(index));
        }
    }

    // The picture to show for a picture or folder; a folder shows its next picture when the period
    // starts, and the same one when it's looked at again within the period.
    private string? Resolve(WallpaperSource source, bool advance, List<FolderPlace> folders, Dictionary<string, string> chosen)
    {
        if (DynamicWallpaperCheck.Check(source) != SourceProblem.None)
        {
            return null;
        }

        if (source.Picture is { } picture)
        {
            return system.Files.Check(picture) == PictureState.Usable ? picture : null;
        }

        var folder = source.Folder!;
        if (chosen.TryGetValue(folder, out var already))
        {
            return already;
        }

        var listed = system.Files.ListFolder(folder);
        if (listed.Count == 0)
        {
            return null;
        }

        var place = folders.FirstOrDefault(entry => string.Equals(entry.Folder, folder, StringComparison.Ordinal));
        string next;
        if (!advance && FolderOrder.Current(folder, listed, place) is { } current)
        {
            next = current;
        }
        else
        {
            (next, var moved) = FolderOrder.Next(folder, listed, place, system.NewSeed);
            folders.RemoveAll(entry => string.Equals(entry.Folder, folder, StringComparison.Ordinal));
            folders.Add(moved);
        }

        chosen[folder] = next;
        return next;
    }

    private PictureProblem Problem(WallpaperSource source)
    {
        if (DynamicWallpaperCheck.Check(source) != SourceProblem.None)
        {
            return PictureProblem.Invalid;
        }

        var state = source.Picture is { } picture ? system.Files.Check(picture) : system.Files.CheckFolder(source.Folder!);
        return state switch
        {
            PictureState.Missing => PictureProblem.Missing,
            PictureState.OnNetwork => PictureProblem.OnNetwork,
            PictureState.OnlineOnly => PictureProblem.OnlineOnly,
            _ => PictureProblem.NoPictures,
        };
    }

    private void Block(string period, WallpaperBlock block)
    {
        if (_report.Block != block)
        {
            LogBlocked(logger, block);
        }

        _report = Made(period, block, []);
    }

    // A report, with whether the background to put back is Windows spotlight's.
    private WallpaperReport Made(string? period, WallpaperBlock block, IReadOnlyList<PictureIssue> issues) =>
        new(period, block, issues, _state.Remembered?.Spotlight == true);

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _state = system.Store.Load(out var readable);
        if (!readable)
        {
            LogStateUnreadable(logger);
        }
    }

    private IWindowsWallpaper? Connection()
    {
        if (_wallpaper is not null)
        {
            return _wallpaper;
        }

        try
        {
            return _wallpaper = system.Connect();
        }
        catch (WallpaperException exception)
        {
            LogNotConnected(logger, exception.HResult);
            return null;
        }
    }

    private void Failed(WallpaperException exception, string? period)
    {
        if (WindowsWallpaper.IsExplorerGone(exception.HResult) || CallCancellation.IsCancelled(exception.HResult))
        {
            Disconnect();
            Block(period ?? _report.Period ?? "", WallpaperBlock.ExplorerNotAnswering);
        }

        LogFailed(logger, exception.HResult);
    }

    private void Disconnect()
    {
        _wallpaper?.Dispose();
        _wallpaper = null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper remembered the background, a {Kind} (Windows spotlight: {Spotlight}).")]
    private static partial void LogRemembered(ILogger logger, BackgroundKind kind, bool spotlight);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper put back the background, a {Kind} (Windows spotlight: {Spotlight}).")]
    private static partial void LogPutBack(ILogger logger, BackgroundKind kind, bool spotlight);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Someone else changed the background, so Dynamic Wallpaper stepped aside.")]
    private static partial void LogSteppedAside(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper resumed.")]
    private static partial void LogResumed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The virtual desktop in use changed, so Dynamic Wallpaper sets its pictures there.")]
    private static partial void LogDesktopSwitched(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper set {Period}'s picture for display {Display}.")]
    private static partial void LogPictureSet(ILogger logger, string period, int display);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused {Period}'s picture for display {Display} with 0x{Code:X8}.")]
    private static partial void LogPictureRefused(ILogger logger, string period, int display, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dynamic Wallpaper can't change the background: {Block}.")]
    private static partial void LogBlocked(ILogger logger, WallpaperBlock block);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows' wallpaper function couldn't be reached: 0x{Code:X8}.")]
    private static partial void LogNotConnected(ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows' wallpaper function failed with 0x{Code:X8}.")]
    private static partial void LogFailed(ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "dynamic-wallpaper.json can't be read, so Dynamic Wallpaper starts it again.")]
    private static partial void LogStateUnreadable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "dynamic-wallpaper.json couldn't be saved.")]
    private static partial void LogStateNotSaved(ILogger logger, Exception exception);
}
