using System;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

/// <summary>
/// The meters' Windows parts, recorded instead of shown: no icon reaches the taskbar, no menu
/// opens and Task Manager doesn't start. It may be used from the meters thread while a test
/// waits for it. Its events are what the icons and the strip did, such as "show Memory" or
/// "remove Network".
/// </summary>
internal sealed class FakeMetersSystem : RecordingFake<FakeMetersSystem>, IMetersSystem
{
    private int _updates;
    private int _taskManagerOpened;

    public FakeMetersSystem()
    {
        Appearance = new FakeAppearance(this);
    }

    public FakeSystemReadings FakeReadings { get; } = new();

    public ISystemReadings Readings => FakeReadings;

    public IMeterAppearance Appearance { get; }

    /// <summary>The Windows build; the strip runs only on tested ones, and by default this isn't one.</summary>
    public int WindowsBuild { get; set; } = 22000;

    /// <summary>The strip's host the meters created last.</summary>
    public FakeStripHost? StripHost { get; private set; }

    /// <summary>The error boundary the meters gave the strip's host, which its own handlers run their work in.</summary>
    public Action<Action>? StripGuard { get; private set; }

    /// <summary>The command the next menu returns.</summary>
    public MeterMenuCommand MenuChoice { get; set; }

    /// <summary>Thrown by each icon shown, to fail the meters.</summary>
    public Exception? ShowFailure { get; set; }

    /// <summary>The icons the meters created last, and their window.</summary>
    public FakeMeterIcons? Icons { get; private set; }

    public MessageWindow? Window { get; private set; }

    /// <summary>The number of updates, each of which looks at the appearance once.</summary>
    public int Updates => Volatile.Read(ref _updates);

    public int TaskManagerOpened => Volatile.Read(ref _taskManagerOpened);

    public IMeterIcons CreateIcons(MessageWindow window)
    {
        lock (Gate)
        {
            Window = window;
            Icons = new FakeMeterIcons(this);
            Changed();
            return Icons;
        }
    }

    public IStripHost CreateStripHost(MessageWindow window, Action<Action> guard)
    {
        lock (Gate)
        {
            StripGuard = guard;
            StripHost = new FakeStripHost(Record);
            Changed();
            return StripHost;
        }
    }

    /// <summary>What pauses the meters, which tests change; neither the session's lock nor the PC's displays.</summary>
    public FakeMeterPauses? Pauses { get; private set; }

    public IMeterPauses WatchPauses(MessageWindow window)
    {
        lock (Gate)
        {
            Pauses = new FakeMeterPauses(window);
            Changed();
            return Pauses;
        }
    }

    public bool OpenTaskManager()
    {
        Interlocked.Increment(ref _taskManagerOpened);
        Changed();
        return true;
    }

    public MeterMenuCommand ShowMenu(MessageWindow window, int x, int y) => MenuChoice;

    /// <summary>Runs <paramref name="action"/> on the meters thread, as Windows' messages would.</summary>
    public void OnMetersThread(Action<FakeMeterIcons> action)
    {
        Assert.True(WaitUntil(system => system.Window is not null));
        Window!.BeginInvoke(() => action(Icons!));
    }

    private sealed class FakeAppearance(FakeMetersSystem system) : IMeterAppearance
    {
        public int IconSize()
        {
            Interlocked.Increment(ref system._updates);
            system.Changed();
            return 16;
        }

        public uint Color() => 0xFFFFFF;

        public bool IsHighContrast() => false;
    }

    /// <summary>
    /// The session's lock and the displays' state, as a test sets them: unlocked and on until it
    /// changes them, whatever the PC running the tests does. Each change arrives on the meters thread.
    /// </summary>
    internal sealed class FakeMeterPauses(MessageWindow window) : IMeterPauses
    {
        public event EventHandler? Locked;

        public event EventHandler? Unlocked;

        public event EventHandler? DisplaysOff;

        public event EventHandler? DisplaysOn;

        public void Lock() => window.BeginInvoke(() => Locked?.Invoke(this, EventArgs.Empty));

        public void Unlock() => window.BeginInvoke(() => Unlocked?.Invoke(this, EventArgs.Empty));

        public void TurnDisplaysOff() => window.BeginInvoke(() => DisplaysOff?.Invoke(this, EventArgs.Empty));

        public void TurnDisplaysOn() => window.BeginInvoke(() => DisplaysOn?.Invoke(this, EventArgs.Empty));

        public void Dispose()
        {
        }
    }

    /// <summary>Records what the meters show; tests raise its events on the meters thread.</summary>
    internal sealed class FakeMeterIcons(FakeMetersSystem system) : IMeterIcons
    {
        public event EventHandler? Selected;

        public event EventHandler<TrayMenuEventArgs>? MenuRequested;

        public event EventHandler<MeterEventArgs>? AddedAgain;

        /// <summary>The last tooltip shown for each meter.</summary>
        public Dictionary<Meter, string> ToolTips { get; } = [];

        /// <summary>Whether Windows refuses the icons.</summary>
        public bool Refuse { get; set; }

        public bool IsDisposed { get; private set; }

        public bool Show(Meter meter, uint[] pixels, int size, string name, string toolTip)
        {
            if (system.ShowFailure is { } failure)
            {
                throw failure;
            }

            if (Refuse)
            {
                system.Record($"refuse {meter}");
                return false;
            }

            ToolTips[meter] = toolTip;
            system.Record($"show {meter}");
            return true;
        }

        public void Remove(Meter meter) => system.Record($"remove {meter}");

        public void Select() => Selected?.Invoke(this, EventArgs.Empty);

        public void OpenMenu() => MenuRequested?.Invoke(this, new TrayMenuEventArgs(10, 20));

        /// <summary>Raises <see cref="AddedAgain"/>, as after the taskbar restarted.</summary>
        public void AddAgain(Meter meter) => AddedAgain?.Invoke(this, new MeterEventArgs(meter));

        public void Dispose()
        {
            IsDisposed = true;
            system.Record("dispose");
        }
    }
}
