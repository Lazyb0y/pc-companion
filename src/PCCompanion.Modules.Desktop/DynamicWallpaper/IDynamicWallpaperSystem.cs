using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>What the hidden window passes on, each on the host's UI thread.</summary>
/// <param name="ClockChanged">The clock or the time zone changed (<c>WM_TIMECHANGE</c>).</param>
/// <param name="SettingChanged">A Windows setting changed (<c>WM_SETTINGCHANGE</c>), such as the Windows mode or a policy.</param>
/// <param name="DisplaysChanged">The displays changed, or one arrived or left.</param>
/// <param name="Resumed">The PC woke from sleep.</param>
/// <param name="TaskbarCreated">Explorer's taskbar started, as after Explorer restarted.</param>
/// <param name="Unlocked">The session was unlocked or connected again.</param>
public sealed record WallpaperWindowEvents(
    Action ClockChanged,
    Action SettingChanged,
    Action DisplaysChanged,
    Action Resumed,
    Action TaskbarCreated,
    Action Unlocked);

/// <summary>Dynamic Wallpaper's hidden window on the host's UI thread.</summary>
public interface IWallpaperWindow : IDisposable
{
    /// <summary>Runs <paramref name="action"/> on the window's thread, from any thread; nothing runs once it's disposed.</summary>
    void Post(Action action);
}

/// <summary>A timer for a moment, as <see cref="AbsoluteTimer"/> keeps it.</summary>
public interface IMomentTimer : IDisposable
{
    /// <summary>Calls back at <paramref name="at"/>, instead of any moment set before.</summary>
    void CallAt(DateTimeOffset at);

    /// <summary>Calls back at no moment until set again.</summary>
    void Cancel();
}

/// <summary>
/// The wallpaper thread: runs jobs one at a time, in order, on a thread of its own, where calls to
/// Explorer can wait. A job that takes longer than <see cref="DynamicWallpaperFeature.CallLimit"/>
/// has its call to Explorer cancelled.
/// </summary>
public interface IWallpaperWorker : IDisposable
{
    /// <summary>Runs <paramref name="job"/> after the jobs posted before it; none run once it's disposed.</summary>
    void Post(Action job);

    /// <summary>Waits until the jobs posted so far have run, as the host exits.</summary>
    /// <returns><see langword="false"/> when they didn't within <paramref name="timeout"/>.</returns>
    bool Finish(TimeSpan timeout);
}

/// <summary>The connected displays, and a hash of each one's serial number by its device path.</summary>
public sealed record WallpaperDisplays(IReadOnlyList<ConnectedDisplay> Displays, IReadOnlyDictionary<string, string> Serials)
{
    public static WallpaperDisplays None { get; } = new([], new Dictionary<string, string>());

    /// <summary>The serial number hash of a display; empty when it has none.</summary>
    public string SerialOf(ConnectedDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return Serials.TryGetValue(display.DevicePath, out var serial) ? serial : string.Empty;
    }
}

/// <summary>What Dynamic Wallpaper uses from Windows in the host; tests use fakes.</summary>
public interface IDynamicWallpaperSystem
{
    /// <summary>Creates the hidden window on the host's UI thread, which calls the events there.</summary>
    IWallpaperWindow CreateWindow(WallpaperWindowEvents events);

    /// <summary>Creates a timer that calls <paramref name="elapsed"/> on the thread pool.</summary>
    IMomentTimer CreateTimer(Action elapsed);

    /// <summary>
    /// Calls <paramref name="changed"/> on the thread pool when something may have changed the
    /// background: Windows writes <c>Control Panel\Desktop</c> or <c>Control Panel\Colors</c> under
    /// the user's key at every change, and at every switch of virtual desktop.
    /// </summary>
    IDisposable WatchBackground(Action changed);

    /// <summary>Starts the wallpaper thread; <paramref name="slow"/> is called on the thread pool when a job's call to Explorer is cancelled.</summary>
    IWallpaperWorker StartWorker(Action slow);

    /// <summary>The time zone of the PC's clock, read again after it changed.</summary>
    TimeZoneInfo TimeZone(bool readAgain);

    /// <summary>The main city of Windows' time zone, in the user's country or region; <see langword="null"/> for none.</summary>
    Coordinates? TimeZoneCity(TimeZoneInfo zone);

    /// <summary>Whether the Windows mode, which Start and the taskbar use, is dark.</summary>
    bool IsWindowsModeDark();

    // On the wallpaper thread.

    /// <inheritdoc cref="WindowsWallpaper.Connect"/>
    IWindowsWallpaper Connect();

    /// <inheritdoc cref="WallpaperPolicies.AreSet()"/>
    bool PoliciesSet();

    /// <inheritdoc cref="ShellRecords.CurrentVirtualDesktop()"/>
    Guid? CurrentVirtualDesktop();

    /// <inheritdoc cref="ShellRecords.IsSpotlightOn()"/>
    bool? IsSpotlightOn();

    /// <summary>The connected displays and their serial number hashes; an EDID is read once for each device path.</summary>
    WallpaperDisplays ReadDisplays();

    /// <summary>Checks pictures and lists folders.</summary>
    IPictureFiles Files { get; }

    /// <summary>The store of <c>dynamic-wallpaper.json</c>.</summary>
    WallpaperStateStore Store { get; }

    /// <summary>A seed for a folder's new order.</summary>
    int NewSeed();
}
