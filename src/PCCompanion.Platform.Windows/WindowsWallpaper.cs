using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace PCCompanion.Platform.Windows;

/// <summary>How Windows fits the background's pictures, the same on every display.</summary>
public enum WallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,

    /// <summary>One picture across all the displays.</summary>
    Span = 5,
}

/// <summary>The state of the background.</summary>
[Flags]
public enum WallpaperStatus
{
    /// <summary>No picture: the background color shows.</summary>
    None = 0,

    /// <summary>A picture or a slideshow shows.</summary>
    On = 1,

    /// <summary>A slideshow changes the pictures.</summary>
    Slideshow = 2,

    /// <summary>A remote session, such as Remote Desktop's, shows no picture.</summary>
    OffInRemoteSession = 4,
}

/// <summary>A display as Windows' wallpaper function lists it.</summary>
/// <param name="Id">Its ID: its device path, as <see cref="ConnectedDisplay.DevicePath"/> gives it.</param>
/// <param name="Area">Its part of the desktop; <see langword="null"/> for a display that was disconnected, which Windows lists while it keeps a picture for it.</param>
public sealed record WallpaperMonitor(string Id, Rectangle? Area);

/// <summary>A slideshow: its folder or pictures, whether it shuffles them and how often it changes.</summary>
/// <param name="Items">The full paths of the slideshow's folder, or of its pictures, all in one folder.</param>
public sealed record WallpaperSlideshow(IReadOnlyList<string> Items, bool Shuffle, TimeSpan Interval);

/// <summary>Windows' wallpaper function, which <see cref="WindowsWallpaper"/> is; tests use fakes.</summary>
public interface IWindowsWallpaper : IDisposable
{
    /// <inheritdoc cref="WindowsWallpaper.Displays"/>
    IReadOnlyList<WallpaperMonitor> Displays();

    /// <inheritdoc cref="WindowsWallpaper.PictureOf"/>
    string? PictureOf(string? displayId);

    /// <inheritdoc cref="WindowsWallpaper.SetPicture"/>
    bool SetPicture(string? displayId, string picture);

    /// <inheritdoc cref="WindowsWallpaper.Position"/>
    WallpaperPosition Position();

    /// <inheritdoc cref="WindowsWallpaper.SetPosition"/>
    void SetPosition(WallpaperPosition position);

    /// <inheritdoc cref="WindowsWallpaper.BackgroundColor"/>
    uint BackgroundColor();

    /// <inheritdoc cref="WindowsWallpaper.SetBackgroundColor"/>
    void SetBackgroundColor(uint rgb);

    /// <inheritdoc cref="WindowsWallpaper.Status"/>
    WallpaperStatus Status();

    /// <inheritdoc cref="WindowsWallpaper.TurnOff"/>
    void TurnOff();

    /// <inheritdoc cref="WindowsWallpaper.Slideshow"/>
    WallpaperSlideshow? Slideshow();

    /// <inheritdoc cref="WindowsWallpaper.SetSlideshow"/>
    void SetSlideshow(WallpaperSlideshow slideshow);
}

/// <summary>
/// Windows' wallpaper function (<c>IDesktopWallpaper</c>), the one Windows Settings uses, which
/// Explorer serves. Every call goes to Explorer and waits for it without a time limit, so use it on
/// a thread of its own, which may let another thread cancel a call that takes too long
/// (<see cref="CallCancellation"/>). Create, use and dispose it on that thread, an MTA thread.
/// Each method throws <see cref="WallpaperException"/> when Windows fails; see <see cref="IsExplorerGone"/>.
/// </summary>
public sealed class WindowsWallpaper : IWindowsWallpaper
{
    private const int Ok = 0;
    private const int ServerUnavailable = unchecked((int)0x800706BA); // RPC_S_SERVER_UNAVAILABLE
    private const int CallFailed = unchecked((int)0x800706BE); // RPC_S_CALL_FAILED
    private const int Disconnected = unchecked((int)0x80010108); // RPC_E_DISCONNECTED
    private const int ServerDied = unchecked((int)0x80010007); // RPC_E_SERVER_DIED
    private const int ServerDiedDidNotExecute = unchecked((int)0x80010012); // RPC_E_SERVER_DIED_DNE
    private const int ClassNotRegistered = unchecked((int)0x80040154); // REGDB_E_CLASSNOTREG
    private const int ServerFailedToStart = unchecked((int)0x80080005); // CO_E_SERVER_EXEC_FAILURE

    private readonly IDesktopWallpaper _wallpaper;
    private bool _disposed;

    private WindowsWallpaper(IDesktopWallpaper wallpaper) => _wallpaper = wallpaper;

    /// <summary>
    /// Connects to Windows' wallpaper function. While Explorer isn't running, it fails at once,
    /// with <c>REGDB_E_CLASSNOTREG</c>.
    /// </summary>
    /// <exception cref="WallpaperException">Windows refused.</exception>
    public static WindowsWallpaper Connect()
    {
        try
        {
            return new((IDesktopWallpaper)new DesktopWallpaper());
        }
        catch (COMException exception)
        {
            throw new WallpaperException("Windows' wallpaper function couldn't be reached.", exception);
        }
    }

    /// <summary>
    /// Whether a failure means that Explorer ended, or isn't running, as while it restarts: then
    /// connect again once its taskbar is back (<c>TaskbarCreated</c>).
    /// </summary>
    public static bool IsExplorerGone(int hresult) =>
        hresult is ServerUnavailable or CallFailed or Disconnected or ServerDied or ServerDiedDidNotExecute or ClassNotRegistered or ServerFailedToStart;

    /// <summary>The displays Windows lists, also the disconnected ones it keeps a picture for.</summary>
    public unsafe IReadOnlyList<WallpaperMonitor> Displays()
    {
        Check(_wallpaper.GetMonitorDevicePathCount(out var count));
        var displays = new List<WallpaperMonitor>((int)count);
        for (uint i = 0; i < count; i++)
        {
            PWSTR id;
            Check(_wallpaper.GetMonitorDevicePathAt(i, &id));
            var path = Take(id);
            RECT area;
            fixed (char* idText = path)
            {
                var result = _wallpaper.GetMonitorRECT(idText, &area);
                Check(result);
                displays.Add(new WallpaperMonitor(path, result.Value == Ok ? Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom) : null));
            }
        }

        return displays;
    }

    /// <summary>
    /// The picture a display shows, or every display when <paramref name="displayId"/> is
    /// <see langword="null"/>: its full path, empty for none; <see langword="null"/> when the
    /// displays differ, or a slideshow runs, for all of them.
    /// </summary>
    public unsafe string? PictureOf(string? displayId)
    {
        PWSTR picture;
        HRESULT result;
        fixed (char* id = displayId)
        {
            result = _wallpaper.GetWallpaper(id, &picture);
        }

        Check(result);
        var path = Take(picture);
        return result.Value == Ok ? path : null;
    }

    /// <summary>
    /// Sets the picture of a display, or of every display, also ones connected later, when
    /// <paramref name="displayId"/> is <see langword="null"/>. Windows shows it a moment later.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when Windows refused it without an error (<c>S_FALSE</c>), as while
    /// a policy keeps the background from changing.
    /// </returns>
    /// <exception cref="WallpaperException">Windows refused, as for a picture that's gone or that it can't read.</exception>
    public unsafe bool SetPicture(string? displayId, string picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        HRESULT result;
        fixed (char* id = displayId)
        fixed (char* path = picture)
        {
            result = _wallpaper.SetWallpaper(id, path);
        }

        Check(result);
        return result.Value == Ok;
    }

    /// <summary>How Windows fits the pictures.</summary>
    public unsafe WallpaperPosition Position()
    {
        DESKTOP_WALLPAPER_POSITION position;
        Check(_wallpaper.GetPosition(&position));
        return (WallpaperPosition)position;
    }

    /// <summary>Sets how Windows fits the pictures, on every display.</summary>
    public void SetPosition(WallpaperPosition position) =>
        Check(_wallpaper.SetPosition((DESKTOP_WALLPAPER_POSITION)position));

    /// <summary>The background color, as 0xRRGGBB, which shows without a picture and around a picture that doesn't fill a display.</summary>
    public unsafe uint BackgroundColor()
    {
        COLORREF color;
        Check(_wallpaper.GetBackgroundColor(&color));
        return ToRgb(color.Value);
    }

    /// <summary>Sets the background color, as 0xRRGGBB.</summary>
    public void SetBackgroundColor(uint rgb) => Check(_wallpaper.SetBackgroundColor(new COLORREF(ToRgb(rgb))));

    /// <summary>Whether a picture or a slideshow shows.</summary>
    public unsafe WallpaperStatus Status()
    {
        DESKTOP_SLIDESHOW_STATE state;
        Check(_wallpaper.GetStatus(&state));
        return (WallpaperStatus)state;
    }

    /// <summary>
    /// Turns the picture off, so the background color shows; the next picture or slideshow turns
    /// it on again.
    /// </summary>
    public void TurnOff() => Check(_wallpaper.Enable(false));

    /// <summary>The slideshow that runs; <see langword="null"/> when none does.</summary>
    public unsafe WallpaperSlideshow? Slideshow()
    {
        if (!Status().HasFlag(WallpaperStatus.Slideshow))
        {
            return null;
        }

        var result = _wallpaper.GetSlideshow(out var items);
        Check(result);
        if (result.Value != Ok || items is null)
        {
            return null;
        }

        var paths = new List<string>();
        try
        {
            items.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                items.GetItemAt(i, out var item);
                PWSTR path;
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path);
                paths.Add(Take(path));
            }
        }
        catch (COMException exception)
        {
            throw new WallpaperException("The slideshow's pictures couldn't be read.", exception);
        }

        DESKTOP_SLIDESHOW_OPTIONS options;
        Check(_wallpaper.GetSlideshowOptions(&options, out var tick));
        return new WallpaperSlideshow(paths, options.HasFlag(DESKTOP_SLIDESHOW_OPTIONS.DSO_SHUFFLEIMAGES), TimeSpan.FromMilliseconds(tick));
    }

    /// <summary>Starts a slideshow, as one read with <see cref="Slideshow"/>.</summary>
    /// <exception cref="WallpaperException">Windows refused, as for a folder that's gone.</exception>
    public unsafe void SetSlideshow(WallpaperSlideshow slideshow)
    {
        ArgumentNullException.ThrowIfNull(slideshow);
        ArgumentOutOfRangeException.ThrowIfZero(slideshow.Items.Count);

        IShellItemArray items;
        if (slideshow.Items.Count == 1)
        {
            Check(PInvoke.SHCreateItemFromParsingName(slideshow.Items[0], null, out IShellItem item));
            Check(PInvoke.SHCreateShellItemArrayFromShellItem(item, out items));
        }
        else
        {
            var lists = new ITEMIDLIST*[slideshow.Items.Count];
            try
            {
                for (var i = 0; i < lists.Length; i++)
                {
                    Check(PInvoke.SHParseDisplayName(slideshow.Items[i], null, out lists[i], 0));
                }

                fixed (ITEMIDLIST** first = lists)
                {
                    Check(PInvoke.SHCreateShellItemArrayFromIDLists((uint)lists.Length, first, out items));
                }
            }
            finally
            {
                foreach (var list in lists)
                {
                    PInvoke.ILFree(list);
                }
            }
        }

        Check(_wallpaper.SetSlideshow(items));
        var options = slideshow.Shuffle ? DESKTOP_SLIDESHOW_OPTIONS.DSO_SHUFFLEIMAGES : 0;
        Check(_wallpaper.SetSlideshowOptions(options, (uint)Math.Clamp(slideshow.Interval.TotalMilliseconds, 0, uint.MaxValue)));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Marshal.FinalReleaseComObject(_wallpaper);
    }

    // A COLORREF is 0x00BBGGRR; swapping red and blue goes both ways.
    private static uint ToRgb(uint color) => ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);

    private static void Check(HRESULT result)
    {
        if (result.Failed)
        {
            throw new WallpaperException($"Windows' wallpaper function failed with 0x{result.Value:X8}.", result.Value);
        }
    }

    private static unsafe string Take(PWSTR text)
    {
        try
        {
            return text.Value is null ? string.Empty : text.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)text.Value);
        }
    }
}

/// <summary>Windows' wallpaper function failed; <see cref="Exception.HResult"/> is Windows' error code.</summary>
public sealed class WallpaperException : Exception
{
    public WallpaperException()
    {
    }

    public WallpaperException(string message)
        : base(message)
    {
    }

    public WallpaperException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        HResult = innerException.HResult;
    }

    public WallpaperException(string message, int hresult)
        : base(message)
    {
        HResult = hresult;
    }
}
