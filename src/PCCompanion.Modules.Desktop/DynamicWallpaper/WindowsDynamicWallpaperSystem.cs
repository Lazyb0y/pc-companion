using System;
using System.IO;
using System.Threading.Tasks;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>Dynamic Wallpaper's Windows: the hidden window, the timer and the wallpaper function.</summary>
internal sealed class WindowsDynamicWallpaperSystem(DataFolder dataFolder) : IDynamicWallpaperSystem
{
    private const string WindowClassName = "PCCompanion.DynamicWallpaper";

    private readonly WallpaperDisplayReader _displays = new();

    public IPictureFiles Files { get; } = new WindowsPictureFiles();

    public WallpaperStateStore Store { get; } = new(Path.Combine(dataFolder.Path, WallpaperStateStore.FileName));

    public IWallpaperWindow CreateWindow(WallpaperWindowEvents events) => new WallpaperWindow(events);

    public IMomentTimer CreateTimer(Action elapsed) => new MomentTimer(new AbsoluteTimer(elapsed));

    public IDisposable WatchBackground(Action changed) => new BackgroundWatch(changed);

    public IDisposable ServePipe(Func<byte[], Task<byte[]>> answer, Action<Exception> failed) =>
        RequestPipe.Serve(DynamicWallpaperPipe.Name, DynamicWallpaperPipe.MaximumRequestLength, answer, failed);

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

    public Guid? CurrentVirtualDesktop() => ShellRecords.CurrentVirtualDesktop();

    public bool? IsSpotlightOn() => ShellRecords.IsSpotlightOn();

    public WallpaperDisplays ReadDisplays() => _displays.Read();

    public int NewSeed() => Random.Shared.Next();

    private sealed class MomentTimer(AbsoluteTimer timer) : IMomentTimer
    {
        public void CallAt(DateTimeOffset at) => timer.Set(at);

        public void Cancel() => timer.Cancel();

        public void Dispose() => timer.Dispose();
    }

    private sealed class BackgroundWatch : IDisposable
    {
        private readonly RegistryWatch _desktop;
        private readonly RegistryWatch _colors;

        public BackgroundWatch(Action changed)
        {
            _desktop = new RegistryWatch(Microsoft.Win32.Registry.CurrentUser, @"Control Panel\Desktop", changed);
            try
            {
                _colors = new RegistryWatch(Microsoft.Win32.Registry.CurrentUser, @"Control Panel\Colors", changed);
            }
            catch
            {
                _desktop.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _desktop.Dispose();
            _colors.Dispose();
        }
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
