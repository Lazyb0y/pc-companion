using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;

namespace PCCompanion.Modules.Desktop.Tests;

/// <summary>
/// Dynamic Wallpaper's Windows for tests: the wallpaper thread's jobs and the window's posts run at
/// once on the test's thread, the timer only records its moment, and the wallpaper function is
/// <see cref="FakeWallpaper"/>.
/// </summary>
internal sealed class FakeDynamicWallpaperSystem : IDynamicWallpaperSystem, IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public FakeDynamicWallpaperSystem()
    {
        Store = new WallpaperStateStore(Path.Combine(_folder.FullPath, WallpaperStateStore.FileName));
    }

    public FakeWallpaper Wallpaper { get; } = new();

    public FakePictureFiles PictureFiles { get; } = new();

    public IPictureFiles Files => PictureFiles;

    public WallpaperStateStore Store { get; }

    public WallpaperWindowEvents? Events { get; private set; }

    public DateTimeOffset? TimerAt { get; private set; }

    public Action? TimerElapsed { get; private set; }

    public bool WindowOpen { get; private set; }

    public bool WorkerRunning { get; private set; }

    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public int ZoneReads { get; private set; }

    public Coordinates? ZoneCity { get; set; } = new(51.5, -0.1);

    public bool Dark { get; set; }

    public bool Policies { get; set; }

    /// <summary>Thrown as Explorer's while it isn't running.</summary>
    public WallpaperException? ConnectFailure { get; set; }

    public int Connections { get; private set; }

    public WallpaperDisplays Displays { get; set; } = WallpaperDisplays.None;

    public int Seed { get; set; } = 1;

    public IWallpaperWindow CreateWindow(WallpaperWindowEvents events)
    {
        Events = events;
        WindowOpen = true;
        return new Window(this);
    }

    public IMomentTimer CreateTimer(Action elapsed)
    {
        TimerElapsed = elapsed;
        return new Timer(this);
    }

    public Action? BackgroundChanged { get; private set; }

    public Guid? Desktop { get; set; }

    public bool? Spotlight { get; set; }

    public int Workers { get; private set; }

    public IDisposable WatchBackground(Action changed)
    {
        BackgroundChanged = changed;
        return new Watch(this);
    }

    public Guid? CurrentVirtualDesktop() => Desktop;

    public bool? IsSpotlightOn() => Spotlight;

    public IWallpaperWorker StartWorker(Action slow)
    {
        Workers++;
        WorkerRunning = true;
        return new Worker(this);
    }

    public TimeZoneInfo TimeZone(bool readAgain)
    {
        if (readAgain)
        {
            ZoneReads++;
        }

        return Zone;
    }

    public Coordinates? TimeZoneCity(TimeZoneInfo zone) => ZoneCity;

    public bool IsWindowsModeDark() => Dark;

    public IWindowsWallpaper Connect()
    {
        if (ConnectFailure is { } failure)
        {
            throw failure;
        }

        Connections++;
        Wallpaper.Connected = true;
        return Wallpaper;
    }

    public bool PoliciesSet() => Policies;

    public WallpaperDisplays ReadDisplays() => Displays;

    public int NewSeed() => Seed;

    /// <summary>Fires the timer, as at its moment.</summary>
    public void FireTimer() => TimerElapsed?.Invoke();

    public void Dispose() => _folder.Dispose();

    private sealed class Watch(FakeDynamicWallpaperSystem system) : IDisposable
    {
        public void Dispose() => system.BackgroundChanged = null;
    }

    private sealed class Window(FakeDynamicWallpaperSystem system) : IWallpaperWindow
    {
        public void Post(Action action)
        {
            if (system.WindowOpen)
            {
                action();
            }
        }

        public void Dispose() => system.WindowOpen = false;
    }

    private sealed class Timer(FakeDynamicWallpaperSystem system) : IMomentTimer
    {
        public void CallAt(DateTimeOffset at) => system.TimerAt = at;

        public void Cancel() => system.TimerAt = null;

        public void Dispose() => system.TimerAt = null;
    }

    private sealed class Worker(FakeDynamicWallpaperSystem system) : IWallpaperWorker
    {
        private bool _disposed;

        public void Post(Action job)
        {
            if (!_disposed)
            {
                job();
            }
        }

        public bool Finish(TimeSpan timeout) => true;

        public void Dispose()
        {
            _disposed = true;
            system.WorkerRunning = false;
        }
    }
}

/// <summary>Windows' wallpaper function for tests, which records what's set.</summary>
internal sealed class FakeWallpaper : IWindowsWallpaper
{
    public const string FirstDisplay = @"\\?\DISPLAY#DELF11B#5&2a1f3e84&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    public const string SecondDisplay = @"\\?\DISPLAY#DELF11B#5&2a1f3e84&0&UID4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private readonly Dictionary<string, string> _pictures = new(StringComparer.OrdinalIgnoreCase);

    public FakeWallpaper()
    {
        Monitors = [new WallpaperMonitor(FirstDisplay, new Rectangle(0, 0, 1920, 1080))];
        All = @"C:\Old.jpg";
    }

    public IReadOnlyList<WallpaperMonitor> Monitors { get; set; }

    public bool Connected { get; set; }

    public List<(string? Display, string Picture)> Set { get; } = [];

    public WallpaperPosition Fit { get; set; } = WallpaperPosition.Fill;

    public uint Color { get; set; }

    public WallpaperStatus State { get; set; } = WallpaperStatus.On;

    /// <summary>Refuses every picture without an error, as under a policy.</summary>
    public bool Refuses { get; set; }

    /// <summary>Shows this picture whatever is set, as the Desktop Wallpaper policy does.</summary>
    public string? Forced { get; set; }

    /// <summary>Pictures Windows refuses as unreadable.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Thrown at the next call.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Every display's picture, when it's the same on all of them.</summary>
    public string? All
    {
        get => Monitors.Select(monitor => PictureOf(monitor.Id)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? PictureOf(Monitors[0].Id) : null;
        set
        {
            foreach (var monitor in Monitors)
            {
                _pictures[monitor.Id] = value ?? "";
            }
        }
    }

    public IReadOnlyList<WallpaperMonitor> Displays()
    {
        Fail();
        return Monitors;
    }

    public string? PictureOf(string? displayId)
    {
        Fail();
        if (Forced is { } forced)
        {
            return forced;
        }

        return displayId is null ? All : _pictures.GetValueOrDefault(displayId, "");
    }

    public bool SetPicture(string? displayId, string picture)
    {
        Fail();
        if (Refuses)
        {
            return false;
        }

        if (Unreadable.Contains(picture))
        {
            throw new WallpaperException("Windows can't read the picture.", unchecked((int)0x80070057));
        }

        Set.Add((displayId, picture));
        if (displayId is null)
        {
            All = picture;
        }
        else
        {
            _pictures[displayId] = picture;
        }

        State = WallpaperStatus.On;
        return true;
    }

    public WallpaperPosition Position()
    {
        Fail();
        return Fit;
    }

    public void SetPosition(WallpaperPosition position)
    {
        Fail();
        Fit = position;
    }

    public uint BackgroundColor()
    {
        Fail();
        return Color;
    }

    public void SetBackgroundColor(uint rgb)
    {
        Fail();
        Color = rgb;
    }

    public WallpaperStatus Status()
    {
        Fail();
        return State;
    }

    public void TurnOff()
    {
        Fail();
        State = WallpaperStatus.None;
    }

    /// <summary>The slideshow, while one runs.</summary>
    public WallpaperSlideshow? Show { get; set; }

    public WallpaperSlideshow? Slideshow() => State.HasFlag(WallpaperStatus.Slideshow) ? Show : null;

    public void SetSlideshow(WallpaperSlideshow slideshow)
    {
        Fail();
        Show = slideshow;
        State = WallpaperStatus.On | WallpaperStatus.Slideshow;
    }

    public void Dispose() => Connected = false;

    private void Fail()
    {
        if (Failure is { } failure)
        {
            Failure = null;
            throw failure;
        }
    }
}

/// <summary>Pictures and folders for tests: every picture is there unless it's listed as otherwise.</summary>
internal sealed class FakePictureFiles : IPictureFiles
{
    public Dictionary<string, PictureState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<string>> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PictureState Check(string picture) => States.GetValueOrDefault(picture, PictureState.Usable);

    public PictureState CheckFolder(string folder) =>
        States.TryGetValue(folder, out var state) ? state : Folders.ContainsKey(folder) ? PictureState.Usable : PictureState.Missing;

    public IReadOnlyList<string> ListFolder(string folder) =>
        CheckFolder(folder) == PictureState.Usable ? Folders.GetValueOrDefault(folder, []) : [];
}
