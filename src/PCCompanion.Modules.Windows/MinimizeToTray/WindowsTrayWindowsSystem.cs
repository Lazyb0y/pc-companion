using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// Minimize to Tray's view of Windows. Without a window, as when the feature only brings back
/// windows left marked, it can't watch processes or read apps' looks.
/// </summary>
public sealed class WindowsTrayWindowsSystem : ITrayWindowsSystem, IDisposable
{
    /// <summary>The mark's name, which PC Companion keeps from one version to the next.</summary>
    public const string MarkName = "PCCompanion.MinimizeToTray";

    // Segoe Fluent Icons' AppIconDefault, which Segoe MDL2 Assets has too.
    private const string AppGlyph = "";
    private const int Windows11Build = 22000;
    private const uint White = 0xFFFFFF;
    private const uint Black = 0x000000;

    private static readonly Lazy<WindowMark> Marks = new(() => new WindowMark(MarkName));

    private readonly MessageWindow? _window;
    private readonly Action<Action> _guard;
    private readonly WindowsAppIdentityReader _identity = new();
    private readonly Dictionary<uint, (WinEventHook Shows, WinEventHook Titles)> _watched = [];

    /// <param name="window">The feature thread's window, where reports arrive.</param>
    /// <param name="guard">Runs a report inside the feature thread's error boundary.</param>
    public WindowsTrayWindowsSystem(MessageWindow? window, Action<Action> guard)
    {
        ArgumentNullException.ThrowIfNull(guard);

        _window = window;
        _guard = guard;
    }

    public event EventHandler<nint>? WindowShown;

    public event EventHandler<nint>? WindowDestroyed;

    public event EventHandler<nint>? TitleChanged;

    public nint ForegroundWindow => Foreground.Window;

    public bool IsShiftHeld => InputState.IsModifierHeld(ModifierKey.Shift);

    public bool IsWindowsKeyHeld => InputState.IsModifierHeld(ModifierKey.Windows);

    public string? WindowAppId(nint window) => _identity.WindowAppId(window);

    public string? PackageAppId(uint processId) => _identity.PackageAppId(processId);

    public string? ProgramPath(uint processId) => _identity.ProgramPath(processId);

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window);

    public bool MayHaveTaskbarButton(nint window) => TopLevelWindows.MayHaveTaskbarButton(window);

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool IsVisible(nint window) => TopLevelWindows.IsVisible(window);

    public bool IsMinimized(nint window) => TopLevelWindows.IsMinimized(window);

    public bool IsEnabled(nint window) => TopLevelWindows.IsEnabled(window);

    public string ClassName(nint window) => TopLevelWindows.ClassName(window);

    public string Title(nint window) => TopLevelWindows.Title(window);

    public uint ProcessIdOf(nint window) => TopLevelWindows.ProcessIdOf(window);

    public bool Mark(nint window) => Marks.Value.Set(window);

    public void Unmark(nint window) => Marks.Value.Remove(window);

    public IReadOnlyList<nint> FindMarked() => Marks.Value.FindMarked();

    public bool Minimize(nint window) => TopLevelWindows.Minimize(window);

    public bool Hide(nint window) => TopLevelWindows.Hide(window);

    public bool ShowMinimized(nint window) => TopLevelWindows.ShowMinimized(window);

    public bool Restore(nint window) => TopLevelWindows.Restore(window);

    public bool Activate(nint window) => TopLevelWindows.Activate(window);

    public void PlayWarning() => WarningSound.Play();

    public void Watch(uint processId)
    {
        if (_window is null)
        {
            throw new InvalidOperationException("Watching needs the feature thread's window.");
        }

        if (_watched.ContainsKey(processId))
        {
            return;
        }

        // Out-of-context hooks for that process only, which report on this thread.
        var shows = new WinEventHook(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectShowEvent, processId);
        WinEventHook titles;
        try
        {
            titles = new WinEventHook(WinEventHook.ObjectNameChangeEvent, WinEventHook.ObjectNameChangeEvent, processId);
        }
        catch
        {
            shows.Dispose();
            throw;
        }

        shows.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                (e.EventType == WinEventHook.ObjectShowEvent ? WindowShown : WindowDestroyed)?.Invoke(this, e.Window);
            }
        });
        titles.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                TitleChanged?.Invoke(this, e.Window);
            }
        });
        _watched[processId] = (shows, titles);
    }

    public void Unwatch(uint processId)
    {
        if (_watched.Remove(processId, out var hooks))
        {
            hooks.Shows.Dispose();
            hooks.Titles.Dispose();
        }
    }

    public int IconSize() => Displays.SmallIconSize(Displays.MainDisplayDpi());

    public uint[]? ClassIcon(nint window, int size)
    {
        var large = size > 16;
        var icon = WindowIcon.OfClass(window, large);
        if (icon == 0)
        {
            icon = WindowIcon.OfClass(window, !large);
        }

        return icon == 0 ? null : WindowIcon.Pixels(icon, size);
    }

    // The large icon when the small one would be scaled up; the program can destroy the icon at any
    // time, so it's drawn as soon as it arrives.
    public bool RequestIcon(nint window, int size, Action<uint[]?> answered)
    {
        ArgumentNullException.ThrowIfNull(answered);

        return WindowIcon.Request(window, size > 16, icon => _guard(() => answered(icon == 0 ? null : WindowIcon.Pixels(icon, size))));
    }

    public uint[] Glyph(int size)
    {
        var color = WindowsTheme.IsHighContrast() ? WindowsTheme.TextColor() : WindowsTheme.IsDark() ? White : Black;
        var font = Environment.OSVersion.Version.Build >= Windows11Build ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        var mask = TextMask.Render(AppGlyph, font, 400, size);

        var pixels = new uint[size * size];
        int left = (size - mask.Width) / 2, top = (size - mask.Height) / 2;
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                int targetX = left + x, targetY = top + y;
                if (targetX >= 0 && targetY >= 0 && targetX < size && targetY < size)
                {
                    pixels[(targetY * size) + targetX] = Premultiply(color, mask.Coverage[(y * mask.Width) + x]);
                }
            }
        }

        return pixels;
    }

    public void ReadLooks(AppOfWindow app, int size, Action<TrayLooks> read)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(read);

        var window = _window ?? throw new InvalidOperationException("Reading looks needs the feature thread's window.");

        // The shell and the program file can take a while, so they're read on the thread pool.
        _ = Task.Run(() =>
        {
            var looks = Looks(app, size);
            window.BeginInvoke(() => _guard(() => read(looks)));
        });
    }

    public void Dispose()
    {
        foreach (var processId in new List<uint>(_watched.Keys))
        {
            Unwatch(processId);
        }
    }

    private static TrayLooks Looks(AppOfWindow app, int size)
    {
        string? name = null;
        AppIcon? icon = null;
        try
        {
            name = (app.AppId is { } appId ? AppDisplay.NameOfAppId(appId) : null) ?? (app.Program is { } program ? AppDisplay.NameOfProgram(program) : null);
            icon = (app.Program is { } path ? AppDisplay.IconOfProgram(path, size) : null) ?? (app.AppId is { } id ? AppDisplay.IconOfAppId(id, size) : null);
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
        {
            // The app's name falls back to what is known without reading.
        }

        name ??= Path.GetFileNameWithoutExtension(app.Program) ?? app.AppId ?? Strings.MinimizeToTrayName;
        var pixels = icon is { } found && found.Width == size && found.Height == size
            ? MemoryMarshal.Cast<byte, uint>(found.Pixels.Span).ToArray()
            : null;
        return new TrayLooks(name, pixels);
    }

    private static uint Premultiply(uint color, byte coverage)
    {
        uint red = (color >> 16) & 0xFF, green = (color >> 8) & 0xFF, blue = color & 0xFF;
        return ((uint)coverage << 24) | ((red * coverage / 255) << 16) | ((green * coverage / 255) << 8) | (blue * coverage / 255);
    }
}
