using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// The strip's window over the taskbar, and what tells it the taskbar may have changed: window
/// events from every program, Explorer's own events, Windows' settings and display changes, the
/// taskbar restarting, and a 30-second timer.
/// </summary>
internal sealed class WindowsStripHost : IStripHost
{
    private const string WindowClassName = "PCCompanion.SystemMeters.Strip";
    private const uint SettingChangeMessage = 0x001A; // WM_SETTINGCHANGE
    private const uint DpiChangedMessage = 0x02E0; // WM_DPICHANGED

    // Explorer's events that come with the taskbar rising: capture and menus, and window order and focus.
    private const uint ExplorerFirstSystemEvent = 0x0004;
    private const uint ExplorerLastSystemEvent = 0x0009;
    private const uint ExplorerFocusEvent = 0x8005;

    private static readonly TimeSpan LayoutDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LayoutInterval = TimeSpan.FromSeconds(30);

    private readonly MessageWindow _messages;
    private readonly uint _taskbarCreatedMessage;
    private readonly LayeredWindow _window;
    private readonly TaskbarLayoutReader _reader;
    private readonly WinEventHook _windowEvents;
    private readonly WinEventHook _foreground;
    private readonly CoalescableTimer _interval;
    private WinEventHook? _explorerSystem;
    private WinEventHook? _explorerOrder;
    private uint _explorer;
    private CoalescableTimer? _delay;
    private string _description = string.Empty;

    public WindowsStripHost(MessageWindow messages)
    {
        _messages = messages;
        _taskbarCreatedMessage = MessageWindow.RegisterMessage("TaskbarCreated");
        _window = new LayeredWindow(WindowClassName, Strings.SystemMetersName);
        try
        {
            _reader = new TaskbarLayoutReader();

            // Windows that get taskbar buttons appearing or going away, in other programs.
            _windowEvents = new WinEventHook(WinEventHook.ObjectShowEvent, WinEventHook.ObjectHideEvent, skipOwnProcess: true);
            _windowEvents.Raised += (_, e) =>
            {
                if (e.IsAboutWindow && TopLevelWindows.MayHaveTaskbarButton(e.Window))
                {
                    LayOutSoon();
                }
            };

            _foreground = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent);
            _foreground.Raised += (_, _) => ZOrderMayHaveChanged?.Invoke(this, EventArgs.Empty);
            _interval = new CoalescableTimer(messages, LayoutInterval, TimeSpan.FromSeconds(1));
            _interval.Elapsed += (_, _) => LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            Dispose();
            throw;
        }

        // The taskbar runs without admin rights; Windows would block its message to an elevated window.
        if (Elevation.IsElevated)
        {
            messages.AllowFromLowerIntegrity(_taskbarCreatedMessage);
        }

        _messages.MessageReceived += OnMessageReceived;
        _window.MessageReceived += OnMessageReceived;
        _window.Clicked += (_, e) => Clicked?.Invoke(this, e);
        _window.MenuRequested += (_, e) => MenuRequested?.Invoke(this, e);
        _window.PointerOverChanged += (_, e) => PointerOverChanged?.Invoke(this, e);
    }

    public event EventHandler? LayoutMayHaveChanged;

    public event EventHandler? ZOrderMayHaveChanged;

    public event EventHandler? PointerOverChanged;

    public event EventHandler? Clicked;

    public event EventHandler<TrayMenuEventArgs>? MenuRequested;

    public bool IsShown => _window.IsShown;

    public bool IsPointerOver => _window.IsPointerOver;

    public TaskbarState? ReadTaskbar()
    {
        var taskbar = Taskbar.Read();
        if (taskbar is not null && taskbar.ProcessId != _explorer)
        {
            WatchExplorer(taskbar.ProcessId);
        }

        return taskbar;
    }

    public IReadOnlyList<TaskbarElement>? ReadElements(nint taskbar) => _reader.Read(taskbar);

    public bool IsTaskbarTopmost(nint taskbar) => TopLevelWindows.IsTopmost(taskbar);

    public void Show(uint[] pixels, Rectangle bounds, string description)
    {
        _window.Show(pixels, bounds);
        if (description != _description)
        {
            _description = description;
            _window.SetTitle(description);
            _window.SetToolTip(description);
        }
    }

    public void Hide() => _window.Hide();

    public bool KeepInFrontOf(nint taskbar) => _window.KeepInFrontOf(taskbar);

    public void Dispose()
    {
        _messages.MessageReceived -= OnMessageReceived;
        _delay?.Dispose();
        _interval?.Dispose();
        _explorerSystem?.Dispose();
        _explorerOrder?.Dispose();
        _foreground?.Dispose();
        _windowEvents?.Dispose();
        _reader?.Dispose();
        _window.Dispose();
    }

    // Only Explorer's own events, so other programs cost nothing; a restarted Explorer is a new process.
    private void WatchExplorer(uint processId)
    {
        _explorerSystem?.Dispose();
        _explorerOrder?.Dispose();
        (_explorerSystem, _explorerOrder) = (null, null);
        _explorer = processId;
        _explorerSystem = new WinEventHook(ExplorerFirstSystemEvent, ExplorerLastSystemEvent, processId);
        _explorerSystem.Raised += (_, _) => ZOrderMayHaveChanged?.Invoke(this, EventArgs.Empty);
        _explorerOrder = new WinEventHook(WinEventHook.ObjectReorderEvent, ExplorerFocusEvent, processId);
        _explorerOrder.Raised += (_, _) => ZOrderMayHaveChanged?.Invoke(this, EventArgs.Empty);
    }

    // Waits for a burst of changes to end, such as an app opening several windows.
    private void LayOutSoon()
    {
        _delay?.Dispose();
        _delay = new CoalescableTimer(_messages, LayoutDelay, TimeSpan.FromMilliseconds(100));
        _delay.Elapsed += (_, _) =>
        {
            _delay?.Dispose();
            _delay = null;
            LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreatedMessage || e.Message == SettingChangeMessage || e.Message == DpiChangedMessage || Displays.IsChangeMessage(e.Message))
        {
            LayOutSoon();
        }
    }
}
