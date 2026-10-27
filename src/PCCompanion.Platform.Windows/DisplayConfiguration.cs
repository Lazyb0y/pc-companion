using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A graphics adapter's output that a display is connected to. Windows gives the adapter a new ID
/// at every restart and when its driver restarts, so it names an output only meanwhile.
/// </summary>
/// <param name="Adapter">The graphics adapter's locally unique ID (LUID).</param>
/// <param name="Id">The output's number on the adapter.</param>
public readonly record struct DisplayTarget(long Adapter, uint Id);

/// <summary>
/// A graphics adapter's picture source: one part of the desktop, shown on one display, or on
/// several that show the same picture. Its adapter ID changes like a <see cref="DisplayTarget"/>'s.
/// </summary>
public readonly record struct DisplaySource(long Adapter, uint Id);

/// <summary>How a display is connected.</summary>
public enum DisplayConnection
{
    Other,
    Vga,
    Dvi,
    Hdmi,

    /// <summary>DisplayPort, also through USB-C or Thunderbolt.</summary>
    DisplayPort,

    /// <summary>A laptop's or tablet's own screen.</summary>
    BuiltIn,

    /// <summary>A USB display adapter or dock with a driver of its own.</summary>
    Usb,

    /// <summary>A Miracast display.</summary>
    Wireless,

    /// <summary>A display that software makes, with no screen of its own.</summary>
    Virtual,
}

/// <summary>A display's orientation, as Windows Settings names it.</summary>
public enum DisplayOrientation
{
    Landscape,
    Portrait,
    LandscapeFlipped,
    PortraitFlipped,
}

/// <summary>Whether a display supports HDR, and whether HDR is on.</summary>
public enum DisplayHdr
{
    NotSupported,
    Off,
    On,
}

/// <summary>A refresh rate as the exact fraction Windows gives, such as 60000/1001 for 59.94 Hz.</summary>
public readonly record struct DisplayRefreshRate(uint Numerator, uint Denominator)
{
    /// <summary>The rate in hertz; 0 when Windows gave none.</summary>
    public double Hertz => Denominator == 0 ? 0 : (double)Numerator / Denominator;
}

/// <summary>A display connected to the PC, on or off.</summary>
/// <param name="Target">The output it's connected to.</param>
/// <param name="DevicePath">
/// Its device path, such as <c>\\?\DISPLAY#DELA0A3#5&amp;1f2e3d4c&amp;0&amp;UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}</c>,
/// which stays the same while it's on the same connection: Windows keeps one for each connection a
/// display has been on.
/// </param>
/// <param name="Model">
/// The maker's code and the product code from its EDID, such as <c>DELA0A3</c>, as in Windows'
/// hardware IDs; empty when it reports no EDID.
/// </param>
/// <param name="Name">Its name from its EDID, such as <c>DELL U2720Q</c>; empty when it has none.</param>
/// <param name="Connection">How it's connected.</param>
/// <param name="Sources">The picture sources Windows can show on it.</param>
public sealed record ConnectedDisplay(
    DisplayTarget Target,
    string DevicePath,
    string Model,
    string Name,
    DisplayConnection Connection,
    IReadOnlyList<DisplaySource> Sources);

/// <summary>A display that's on: the picture source it shows, where, and how.</summary>
/// <param name="Area">
/// The part of the desktop it shows, in pixels: its resolution, turned by its orientation, at its
/// position on the desktop. The main display's is at (0, 0). Displays that show the same picture
/// have the same source and area.
/// </param>
/// <param name="RefreshRate">Its refresh rate.</param>
/// <param name="Orientation">Its orientation.</param>
/// <param name="Hdr">Whether it supports HDR, and whether HDR is on.</param>
public sealed record DisplayPath(
    DisplaySource Source,
    DisplayTarget Target,
    Rectangle Area,
    DisplayRefreshRate RefreshRate,
    DisplayOrientation Orientation,
    DisplayHdr Hdr);

/// <summary>The displays connected to the PC, and the ones that are on, as Windows Settings shows them.</summary>
public sealed record DisplaySetup(IReadOnlyList<ConnectedDisplay> Displays, IReadOnlyList<DisplayPath> Paths);

/// <summary>A resolution and refresh rate a display offers.</summary>
/// <param name="Width">The resolution's width, in the display's landscape orientation.</param>
/// <param name="Height">The resolution's height, in the display's landscape orientation.</param>
/// <param name="RefreshRate">The refresh rate in whole hertz, rounded down as Windows lists it, such as 59 for 59.94 Hz.</param>
public readonly record struct DisplayMode(int Width, int Height, int RefreshRate);

/// <summary>
/// The displays and the display setup, as Windows Settings reads and changes them
/// (<c>QueryDisplayConfig</c> and <c>SetDisplayConfig</c>). Neither needs admin rights. Reading
/// takes about 0.1 ms.
/// </summary>
public static class DisplayConfiguration
{
    /// <summary>
    /// Whether this is a remote session, such as Remote Desktop's or Windows Sandbox's, where
    /// Windows reports the session's own display, not the PC's, and refuses changes. A remote
    /// session can take over this one, and leave it, while a program runs.
    /// </summary>
    public static bool IsRemoteSession => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;

    /// <summary>Reads the connected displays and the setup in use.</summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static DisplaySetup Read()
    {
        var (paths, modes) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ALL_PATHS);

        var displays = new List<ConnectedDisplay>();
        foreach (var group in paths.Where(path => path.targetInfo.targetAvailable).GroupBy(path => TargetOf(path)))
        {
            var name = TargetName(group.Key);
            displays.Add(new ConnectedDisplay(
                group.Key,
                name.monitorDevicePath.ToString(),
                name.flags.edidIdsValid ? Model(name.edidManufactureId, name.edidProductCodeId) : "",
                name.flags.friendlyNameFromEdid ? name.monitorFriendlyDeviceName.ToString() : "",
                Connection(group.First().targetInfo.outputTechnology),
                [.. group.Select(SourceOf).Distinct()]));
        }

        var active = new List<DisplayPath>();
        foreach (var path in paths.Where(path => (path.flags & PInvoke.DISPLAYCONFIG_PATH_ACTIVE) != 0))
        {
            var target = TargetOf(path);
            active.Add(new DisplayPath(
                SourceOf(path),
                target,
                AreaOf(path, modes),
                RefreshRateOf(path, modes),
                Orientation(path.targetInfo.rotation),
                ReadHdr(target)));
        }

        return new DisplaySetup(displays, active);
    }

    /// <summary>
    /// Lists the resolutions and refresh rates offered by the display that a source shows on, as
    /// Windows Settings does (<c>EnumDisplaySettings</c>); none for a source that isn't in use.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static unsafe IReadOnlyList<DisplayMode> ListModes(DisplaySource source)
    {
        var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        name.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        name.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
        name.header.adapterId = Luid(source.Adapter);
        name.header.id = source.Id;
        ThrowIfFailed(PInvoke.DisplayConfigGetDeviceInfo(&name.header));

        // Windows lists the modes in the display's current orientation.
        var deviceName = name.viewGdiDeviceName.ToString();
        var modes = new HashSet<DisplayMode>();
        var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        for (uint i = 0; PInvoke.EnumDisplaySettingsEx(deviceName, (ENUM_DISPLAY_SETTINGS_MODE)i, ref mode, 0); i++)
        {
            var turned = mode.Anonymous1.Anonymous2.dmDisplayOrientation is DEVMODE_DISPLAY_ORIENTATION.DMDO_90 or DEVMODE_DISPLAY_ORIENTATION.DMDO_270;
            modes.Add(turned
                ? new DisplayMode((int)mode.dmPelsHeight, (int)mode.dmPelsWidth, (int)mode.dmDisplayFrequency)
                : new DisplayMode((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency));
        }

        return [.. modes];
    }

    /// <summary>
    /// Asks Windows whether it would apply a setup, without applying it (<c>SDC_VALIDATE</c>). It
    /// refuses a resolution or refresh rate that a display doesn't offer.
    /// </summary>
    /// <param name="paths">
    /// A path for each display to turn on, as <see cref="Apply"/> takes them.
    /// </param>
    /// <exception cref="ArgumentException">There's no path, a path's source can't be shown on its display, or paths that share a source have different areas.</exception>
    /// <exception cref="Win32Exception">Windows refused the setup, or to read the displays.</exception>
    public static void Check(IReadOnlyList<DisplayPath> paths) =>
        Set(paths, SET_DISPLAY_CONFIG_FLAGS.SDC_VALIDATE | SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG);

    /// <summary>
    /// Applies a setup without saving it in Windows' memory of setups, so a restart or a
    /// reconnection brings back the setup Windows saved. Windows applies the setup as it is, or
    /// refuses it, and leaves the signal timing to the driver. It waits while the displays change
    /// mode, and Windows sends <c>WM_DISPLAYCHANGE</c> meanwhile.
    /// </summary>
    /// <param name="paths">
    /// A path for each display to turn on: its source, its display, its area on the desktop, its
    /// refresh rate and orientation. Connected displays without a path are turned off. Paths that
    /// share a source show the same picture, and need the same area. HDR is left as it is; see
    /// <see cref="SetHdr"/>. A display that's on keeps its scan line ordering, interlaced or
    /// progressive, and its scaling for resolutions that don't fill it; one that's turned on gets
    /// progressive and Windows' own scaling.
    /// </param>
    /// <exception cref="ArgumentException">There's no path, a path's source can't be shown on its display, or paths that share a source have different areas.</exception>
    /// <exception cref="Win32Exception">Windows refused the setup, or to read the displays; nothing changed.</exception>
    public static void Apply(IReadOnlyList<DisplayPath> paths) =>
        Set(paths, SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY | SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG);

    /// <summary>
    /// Saves the setup in use in Windows' memory of setups, as Windows Settings does when a change
    /// is kept, so Windows brings it back the next time these displays are connected. The displays
    /// don't change mode again, and no <c>WM_DISPLAYCHANGE</c> comes.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static unsafe void SaveCurrent()
    {
        var (paths, modes) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS);
        ThrowIfFailed(PInvoke.SetDisplayConfig(
            paths,
            modes,
            SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY | SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG | SET_DISPLAY_CONFIG_FLAGS.SDC_SAVE_TO_DATABASE));
    }

    /// <summary>
    /// Brings back the setup Windows saved last for the displays connected now
    /// (<c>SDC_USE_DATABASE_CURRENT</c>): Windows' own way back from a setup that wasn't saved.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static void ApplySaved() =>
        ThrowIfFailed(PInvoke.SetDisplayConfig([], [], SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY | SET_DISPLAY_CONFIG_FLAGS.SDC_USE_DATABASE_CURRENT));

    /// <summary>
    /// Turns HDR on or off on a display that's on and supports it, as Windows Settings' Use HDR does.
    /// From Windows 11 version 24H2, this leaves color management for displays without HDR as it is.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static unsafe void SetHdr(DisplayTarget target, bool on)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
        {
            var state = new DISPLAYCONFIG_SET_HDR_STATE();
            state.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE;
            state.header.size = (uint)sizeof(DISPLAYCONFIG_SET_HDR_STATE);
            state.header.adapterId = Luid(target.Adapter);
            state.header.id = target.Id;
            state.enableHdr = on;
            ThrowIfFailed(PInvoke.DisplayConfigSetDeviceInfo(&state.header));
            return;
        }

        var older = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
        older.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
        older.header.size = (uint)sizeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE);
        older.header.adapterId = Luid(target.Adapter);
        older.header.id = target.Id;
        older.enableAdvancedColor = on;
        ThrowIfFailed(PInvoke.DisplayConfigSetDeviceInfo(&older.header));
    }

    // Builds the setup on the paths Windows offers now, which hold what a path needs besides its
    // source, display and mode, and never lets Windows change it (SDC_ALLOW_CHANGES): with that,
    // Windows accepts any mode and quietly applies another.
    private static void Set(IReadOnlyList<DisplayPath> paths, SET_DISPLAY_CONFIG_FLAGS flags)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one display must be on.", nameof(paths));
        }

        var (all, _) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ALL_PATHS);
        var inUse = all
            .Where(path => (path.flags & PInvoke.DISPLAYCONFIG_PATH_ACTIVE) != 0)
            .ToDictionary(TargetOf, path => (path.targetInfo.scanLineOrdering, path.targetInfo.scaling));

        var setup = new DISPLAYCONFIG_PATH_INFO[paths.Count];
        var modes = new List<DISPLAYCONFIG_MODE_INFO>();
        var sources = new Dictionary<DisplaySource, (uint Index, Rectangle Area)>();
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var offered = Array.FindIndex(all, candidate => SourceOf(candidate) == path.Source && TargetOf(candidate) == path.Target);
            if (offered < 0)
            {
                throw new ArgumentException($"Source {path.Source.Id} can't be shown on display {path.Target.Id}.", nameof(paths));
            }

            if (sources.TryGetValue(path.Source, out var source))
            {
                if (source.Area != path.Area)
                {
                    throw new ArgumentException($"Displays that share source {path.Source.Id} have different areas.", nameof(paths));
                }
            }
            else
            {
                source = ((uint)modes.Count, path.Area);
                sources.Add(path.Source, source);
                modes.Add(SourceMode(path));
            }

            var built = all[offered];
            built.flags = PInvoke.DISPLAYCONFIG_PATH_ACTIVE;
            built.sourceInfo.Anonymous.modeInfoIdx = source.Index;

            // No target mode: the driver chooses the signal timing for the refresh rate.
            built.targetInfo.Anonymous.modeInfoIdx = PInvoke.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            built.targetInfo.rotation = Rotation(path.Orientation);
            built.targetInfo.refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = path.RefreshRate.Numerator, Denominator = path.RefreshRate.Denominator };

            // Windows refuses a refresh rate without its scan line ordering.
            (built.targetInfo.scanLineOrdering, built.targetInfo.scaling) = inUse.TryGetValue(path.Target, out var kept)
                ? kept
                : (DISPLAYCONFIG_SCANLINE_ORDERING.DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE, DISPLAYCONFIG_SCALING.DISPLAYCONFIG_SCALING_PREFERRED);
            setup[i] = built;
        }

        ThrowIfFailed(PInvoke.SetDisplayConfig(setup, [.. modes], flags));
    }

    private static DISPLAYCONFIG_MODE_INFO SourceMode(DisplayPath path)
    {
        var mode = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
            id = path.Source.Id,
            adapterId = Luid(path.Source.Adapter),
        };
        mode.Anonymous.sourceMode = new DISPLAYCONFIG_SOURCE_MODE
        {
            width = (uint)path.Area.Width,
            height = (uint)path.Area.Height,
            pixelFormat = DISPLAYCONFIG_PIXELFORMAT.DISPLAYCONFIG_PIXELFORMAT_32BPP,
            position = new POINTL { x = path.Area.X, y = path.Area.Y },
        };
        return mode;
    }

    private static unsafe (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query(QUERY_DISPLAY_CONFIG_FLAGS flags)
    {
        while (true)
        {
            var error = PInvoke.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (error != WIN32_ERROR.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)error);
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            fixed (DISPLAYCONFIG_PATH_INFO* pathArray = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* modeArray = modes)
            {
                error = PInvoke.QueryDisplayConfig(flags, &pathCount, pathArray, &modeCount, modeArray, null);
            }

            // The setup changed between the two calls, so the buffers may be too small.
            if (error == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                continue;
            }

            if (error != WIN32_ERROR.ERROR_SUCCESS)
            {
                throw new Win32Exception((int)error);
            }

            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    private static unsafe DISPLAYCONFIG_TARGET_DEVICE_NAME TargetName(DisplayTarget target)
    {
        var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        name.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        name.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
        name.header.adapterId = Luid(target.Adapter);
        name.header.id = target.Id;
        ThrowIfFailed(PInvoke.DisplayConfigGetDeviceInfo(&name.header));
        return name;
    }

    // From Windows 11 version 24H2, the older function also reports displays that support only
    // color management, and the newer one tells HDR apart.
    private static unsafe DisplayHdr ReadHdr(DisplayTarget target)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
        {
            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2();
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2;
            info.header.size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2);
            info.header.adapterId = Luid(target.Adapter);
            info.header.id = target.Id;
            ThrowIfFailed(PInvoke.DisplayConfigGetDeviceInfo(&info.header));
            return !info.highDynamicRangeSupported ? DisplayHdr.NotSupported
                : info.highDynamicRangeUserEnabled ? DisplayHdr.On
                : DisplayHdr.Off;
        }

        var older = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        older.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
        older.header.size = (uint)sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO);
        older.header.adapterId = Luid(target.Adapter);
        older.header.id = target.Id;
        ThrowIfFailed(PInvoke.DisplayConfigGetDeviceInfo(&older.header));

        // Wide color enforced: a display without HDR, which shows advanced color through color management.
        return !older.advancedColorSupported || older.wideColorEnforced ? DisplayHdr.NotSupported
            : older.advancedColorEnabled ? DisplayHdr.On
            : DisplayHdr.Off;
    }

    private static Rectangle AreaOf(in DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var index = path.sourceInfo.Anonymous.modeInfoIdx;
        if (index == PInvoke.DISPLAYCONFIG_PATH_MODE_IDX_INVALID || index >= modes.Length)
        {
            return Rectangle.Empty;
        }

        var mode = modes[index].Anonymous.sourceMode;
        return new Rectangle(mode.position.x, mode.position.y, (int)mode.width, (int)mode.height);
    }

    // The path's rate is the one Windows was asked for; the target mode's is the signal's own.
    private static DisplayRefreshRate RefreshRateOf(in DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var rate = path.targetInfo.refreshRate;
        if (rate.Denominator == 0)
        {
            var index = path.targetInfo.Anonymous.modeInfoIdx;
            if (index != PInvoke.DISPLAYCONFIG_PATH_MODE_IDX_INVALID && index < modes.Length)
            {
                rate = modes[index].Anonymous.targetMode.targetVideoSignalInfo.vSyncFreq;
            }
        }

        return new DisplayRefreshRate(rate.Numerator, rate.Denominator);
    }

    private static DisplayTarget TargetOf(DISPLAYCONFIG_PATH_INFO path) => new(Id(path.targetInfo.adapterId), path.targetInfo.id);

    private static DisplaySource SourceOf(DISPLAYCONFIG_PATH_INFO path) => new(Id(path.sourceInfo.adapterId), path.sourceInfo.id);

    private static long Id(LUID luid) => ((long)luid.HighPart << 32) | luid.LowPart;

    private static LUID Luid(long id) => new() { LowPart = (uint)id, HighPart = (int)(id >> 32) };

    // The EDID keeps the maker's code as three letters of five bits each, A being 1, in
    // big-endian order, which Windows passes on as it is.
    private static string Model(ushort manufacturerId, ushort productCode)
    {
        var maker = (ushort)((manufacturerId >> 8) | (manufacturerId << 8));
        return string.Concat(
            (char)('@' + ((maker >> 10) & 31)),
            (char)('@' + ((maker >> 5) & 31)),
            (char)('@' + (maker & 31)),
            productCode.ToString("X4", CultureInfo.InvariantCulture));
    }

    private static DisplayConnection Connection(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY technology) => technology switch
    {
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 => DisplayConnection.Vga,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI => DisplayConnection.Dvi,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI => DisplayConnection.Hdmi,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_USB_TUNNEL => DisplayConnection.DisplayPort,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
            or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED => DisplayConnection.BuiltIn,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED => DisplayConnection.Usb,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST => DisplayConnection.Wireless,
        DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL => DisplayConnection.Virtual,
        _ => DisplayConnection.Other,
    };

    private static DisplayOrientation Orientation(DISPLAYCONFIG_ROTATION rotation) => rotation switch
    {
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE90 => DisplayOrientation.Portrait,
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE180 => DisplayOrientation.LandscapeFlipped,
        DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE270 => DisplayOrientation.PortraitFlipped,
        _ => DisplayOrientation.Landscape,
    };

    private static DISPLAYCONFIG_ROTATION Rotation(DisplayOrientation orientation) => orientation switch
    {
        DisplayOrientation.Portrait => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE90,
        DisplayOrientation.LandscapeFlipped => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE180,
        DisplayOrientation.PortraitFlipped => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_ROTATE270,
        _ => DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY,
    };

    private static void ThrowIfFailed(int result)
    {
        if (result != (int)WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new Win32Exception(result);
        }
    }
}
