using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>Dynamic Wallpaper's Windows: the hidden window, the timer and the wallpaper function.</summary>
internal sealed class WindowsDynamicWallpaperSystem(DataFolder dataFolder) : IDynamicWallpaperSystem
{
    private const string WindowClassName = "PCCompanion.DynamicWallpaper";

    // The serial number hash of each device path read so far: a display's EDID is read once.
    private readonly Lock _serialsGate = new();
    private readonly Dictionary<string, string> _serials = new(StringComparer.OrdinalIgnoreCase);

    public IPictureFiles Files { get; } = new WindowsPictureFiles();

    public WallpaperStateStore Store { get; } = new(Path.Combine(dataFolder.Path, WallpaperStateStore.FileName));

    public IWallpaperWindow CreateWindow(WallpaperWindowEvents events) => new WallpaperWindow(events);

    public IMomentTimer CreateTimer(Action elapsed) => new MomentTimer(new AbsoluteTimer(elapsed));

    public IWallpaperWorker StartWorker(Action slow) => new WallpaperThread(DynamicWallpaperFeature.CallLimit, slow);

    public TimeZoneInfo TimeZone(bool readAgain)
    {
        if (readAgain)
        {
            TimeZoneInfo.ClearCachedData();
        }

        return TimeZoneInfo.Local;
    }

    public Coordinates? TimeZoneCity(TimeZoneInfo zone) => TimeZonePlaces.Of(zone, WindowsRegion.CountryOrRegion());

    public bool IsWindowsModeDark() => WindowsTheme.IsDark();

    public IWindowsWallpaper Connect() => WindowsWallpaper.Connect();

    public bool PoliciesSet() => WallpaperPolicies.AreSet();

    public WallpaperDisplays ReadDisplays()
    {
        DisplaySetup setup;
        try
        {
            setup = DisplayConfiguration.Read();
        }
        catch (Win32Exception)
        {
            return WallpaperDisplays.None;
        }

        var serials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in setup.Displays)
        {
            serials[display.DevicePath] = SerialOf(display.DevicePath);
        }

        return new WallpaperDisplays(setup.Displays, serials);
    }

    public int NewSeed() => Random.Shared.Next();

    // A display whose EDID Windows can't give now has no serial number this time, and its EDID is
    // read again the next time.
    private string SerialOf(string devicePath)
    {
        lock (_serialsGate)
        {
            if (_serials.TryGetValue(devicePath, out var known))
            {
                return known;
            }
        }

        string serial;
        try
        {
            serial = DisplayRecognition.HashSerial(DisplayEdid.ReadSerialNumberAsync(devicePath).GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            return string.Empty;
        }

        lock (_serialsGate)
        {
            _serials[devicePath] = serial;
        }

        return serial;
    }

    private sealed class MomentTimer(AbsoluteTimer timer) : IMomentTimer
    {
        public void CallAt(DateTimeOffset at) => timer.Set(at);

        public void Cancel() => timer.Cancel();

        public void Dispose() => timer.Dispose();
    }

    private sealed class WallpaperWindow : IWallpaperWindow
    {
        private readonly MessageWindow _window;
        private readonly DeviceNotifications? _devices;
        private readonly IDisposable? _displays;
        private readonly SessionMonitor? _session;
        private readonly uint _taskbarCreated;

        public WallpaperWindow(WallpaperWindowEvents events)
        {
            _window = new MessageWindow(WindowClassName);
            try
            {
                _taskbarCreated = MessageWindow.RegisterMessage("TaskbarCreated");
                _window.MessageReceived += (_, e) =>
                {
                    if (AbsoluteTimer.IsClockChangeMessage(e.Message))
                    {
                        events.ClockChanged();
                    }
                    else if (WindowsTheme.IsChangeMessage(e.Message))
                    {
                        events.SettingChanged();
                    }
                    else if (Displays.IsChangeMessage(e.Message))
                    {
                        events.DisplaysChanged();
                    }
                    else if (e.Message == _taskbarCreated && _taskbarCreated != 0)
                    {
                        events.TaskbarCreated();
                    }
                };
                _devices = new DeviceNotifications(_window);
                _devices.Resumed += (_, _) => events.Resumed();
                _displays = _devices.WatchDisplays(events.DisplaysChanged);
                _session = new SessionMonitor(_window);
                _session.Unlocked += (_, _) => events.Unlocked();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Post(Action action) => _window.BeginInvoke(action);

        public void Dispose()
        {
            _session?.Dispose();
            _displays?.Dispose();
            _devices?.Dispose();
            _window.Dispose();
        }
    }
}
