using System;
using System.Collections.Generic;
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
public sealed record WallpaperReport(string? Period, WallpaperBlock Block, IReadOnlyList<PictureIssue> Issues)
{
    public static WallpaperReport Empty { get; } = new(null, WallpaperBlock.None, []);

    // Compares the issues one by one, so the same report made twice is equal.
    public bool Equals(WallpaperReport? other) =>
        other is not null && Period == other.Period && Block == other.Block && Issues.SequenceEqual(other.Issues);

    public override int GetHashCode() => HashCode.Combine(Period, Block, Issues.Count);
}

/// <summary>
/// Sets the period's pictures through Windows' wallpaper function, on the wallpaper thread: only
/// those that differ from what Windows shows, each display's when it has its own. See "When pictures
/// change" and "Displays" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
internal sealed partial class WallpaperApplier(IDynamicWallpaperSystem system, ILogger logger)
{
    // Windows refused a picture it can't read, or that's gone.
    private const int InvalidArgument = unchecked((int)0x80070057);
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int PathNotFound = unchecked((int)0x80070003);

    private IWindowsWallpaper? _wallpaper;
    private WallpaperState _state = new();
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

        if (plan.Pictures is not { } pictures)
        {
            _report = new WallpaperReport(period, WallpaperBlock.None, []);
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
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            LogStateNotSaved(logger, exception);
        }
    }

    private static WallpaperPosition PositionOf(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => WallpaperPosition.Fit,
        WallpaperFit.Stretch => WallpaperPosition.Stretch,
        WallpaperFit.Tile => WallpaperPosition.Tile,
        WallpaperFit.Center => WallpaperPosition.Center,
        WallpaperFit.Span => WallpaperPosition.Span,
        _ => WallpaperPosition.Fill,
    };

    private static bool Same(string? shown, string picture) => string.Equals(shown, picture, StringComparison.OrdinalIgnoreCase);

    private void SetPictures(DynamicWallpaperSettings settings, WallpaperPlan plan, WallpaperPeriod pictures, IWindowsWallpaper wallpaper)
    {
        var period = plan.Period.ToString();
        var advance = _state.Period != period;
        var position = PositionOf(settings.Fit);
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
        _report = new WallpaperReport(period, WallpaperBlock.None, issues);
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

        _report = new WallpaperReport(period, block, []);
    }

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

    private void Failed(WallpaperException exception, string period)
    {
        if (WindowsWallpaper.IsExplorerGone(exception.HResult) || CallCancellation.IsCancelled(exception.HResult))
        {
            Disconnect();
            Block(period, WallpaperBlock.ExplorerNotAnswering);
        }

        LogFailed(logger, exception.HResult);
    }

    private void Disconnect()
    {
        _wallpaper?.Dispose();
        _wallpaper = null;
    }

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
