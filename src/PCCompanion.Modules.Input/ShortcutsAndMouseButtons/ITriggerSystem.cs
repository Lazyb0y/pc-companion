using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.FullScreen;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// What the feature's thread reads from Windows and asks of it, and of the host, so tests can
/// supply it instead. None of it waits for another app.
/// </summary>
public interface ITriggerSystem
{
    /// <summary>The app of the window in front, or <see langword="null"/> when no window is in front.</summary>
    AppOfWindow? FrontApp();

    /// <summary>The window in front, or 0.</summary>
    nint FrontWindow();

    /// <summary>The app of a top-level window.</summary>
    AppOfWindow AppOf(nint window);

    /// <summary>
    /// The top-level windows that may have a taskbar button, with their apps, read on the thread
    /// pool, since reading their apps can take a moment. It never fails: what can't be read is left out.
    /// </summary>
    Task<IReadOnlyList<(nint Window, AppOfWindow App)>> ReadTaskbarAppsAsync();

    /// <summary>Whether <paramref name="window"/> still exists.</summary>
    bool Exists(nint window);

    /// <summary>Whether the window in front is full screen on its display; see <see cref="FullScreenCheck"/>.</summary>
    bool IsFrontFullScreen();

    /// <summary>
    /// Watches the window in front for changes of its place or size, which the feature's thread
    /// hears of, or stops watching. Called again after another window came to the front.
    /// </summary>
    void WatchFront(bool watch);

    /// <summary>Whether a mouse button trigger's button, or any of its keys, is still held.</summary>
    bool IsHeld(PointerTrigger trigger);

    /// <summary>Keeps the claim's buttons from the apps from now on; <see langword="null"/> keeps none.</summary>
    void Claim(PointerClaim? claim);

    /// <summary>What the shared pointer hook did with the click stamped <paramref name="time"/>; see <see cref="PointerHook.Outcome"/>.</summary>
    PointerInputOutcome Outcome(uint time);

    /// <summary>Sets the shared pointer hook again, as after Windows removed it.</summary>
    void SetHookAgain();

    /// <summary>The user's keyboard layouts.</summary>
    IReadOnlyList<nint> InstalledLayouts();

    /// <summary>What the key with <paramref name="scanCode"/> types with AltGr in <paramref name="layout"/>; see <see cref="KeyboardLayout.TypedWithAltGr"/>.</summary>
    string? TypedWithAltGr(uint scanCode, bool shift, nint layout);

    /// <summary>Whether any of a shortcut's keys is still held: its modifiers, or its key in the layout in front.</summary>
    bool IsHeld(KeyPositionShortcut shortcut);

    /// <summary>Whether Alt is held now.</summary>
    bool IsAltHeld();

    /// <summary>Presses a key that does nothing, so that releasing Alt opens no menu bar.</summary>
    void PressMaskKey();

    void Lock();

    void TurnOffDisplays();

    void StartScreenSaver();

    /// <summary>Runs another feature's action through the host; see <see cref="IHostActions.Run"/>.</summary>
    void Run(FeatureAction action);

    /// <summary>Says which of the feature's shortcuts Windows refused, by their names, for the settings app.</summary>
    void ReportRefused(IEnumerable<string> shortcuts);
}

/// <summary>
/// Reads from Windows and asks of it for the feature's thread, whose hidden window is
/// <paramref name="window"/>. The pointer hook starts with the first claim.
/// </summary>
/// <param name="events">What the feature's thread hears of: kept clicks, the front window moving and a failed hook.</param>
public sealed class WindowsTriggerSystem(MessageWindow window, IHostActions actions, ShortcutRefusals refusals, string featureId, ITriggerEvents events) : ITriggerSystem, IDisposable
{
    // VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN and VK_RWIN.
    private const uint Shift = 0x10;
    private const uint Control = 0x11;
    private const uint Alt = 0x12;
    private const uint LeftWindows = 0x5B;
    private const uint RightWindows = 0x5C;

    // VK_MBUTTON, VK_XBUTTON1 and VK_XBUTTON2.
    private const uint MiddleButton = 0x04;
    private const uint BackButton = 0x05;
    private const uint ForwardButton = 0x06;

    // OBJID_WINDOW: a change of the window itself, not of its caret or a part.
    private const int WindowObject = 0;

    private readonly AppOfWindowReader _apps = new(new WindowsAppIdentityReader());
    private readonly WindowsFullScreenEnvironment _fullScreen = new();
    private PointerHook? _hook;
    private WinEventHook? _frontHook;
    private uint _frontProcess;
    private nint _frontWindow;

    public AppOfWindow? FrontApp() => Foreground.Window is var front && front != 0 ? _apps.Of(TopLevelWindows.Describe(front)) : null;

    public nint FrontWindow() => Foreground.Window;

    public AppOfWindow AppOf(nint window) => _apps.Of(TopLevelWindows.Describe(window));

    // A reader of its own, since this runs on the thread pool.
    public Task<IReadOnlyList<(nint Window, AppOfWindow App)>> ReadTaskbarAppsAsync() => Task.Run<IReadOnlyList<(nint Window, AppOfWindow App)>>(() =>
    {
        var reader = new AppOfWindowReader(new WindowsAppIdentityReader());
        return [.. TopLevelWindows.List().Where(listed => TopLevelWindows.MayHaveTaskbarButton(listed.Handle)).Select(listed => (listed.Handle, reader.Of(listed)))];
    });

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool IsFrontFullScreen() => _fullScreen.FrontWindowDisplay is { } display && FullScreenCheck.IsFullScreen(_fullScreen, display);

    // An out-of-context hook for the process of the window in front only, set again when another process comes to the front.
    public void WatchFront(bool watch)
    {
        var front = Foreground.Window;
        var process = watch && front != 0 ? TopLevelWindows.ProcessIdOf(front) : 0;
        _frontWindow = front;
        if (process == _frontProcess && (_frontHook is not null || process == 0))
        {
            return;
        }

        _frontHook?.Dispose();
        _frontHook = null;
        _frontProcess = process;
        if (process != 0)
        {
            _frontHook = new WinEventHook(WinEventHook.LocationChangeEvent, WinEventHook.LocationChangeEvent, process);
            _frontHook.Raised += (_, e) =>
            {
                if (e.Window == _frontWindow && e.ObjectId == WindowObject)
                {
                    events.FrontMoved();
                }
            };
        }
    }

    public bool IsHeld(PointerTrigger trigger)
    {
        var button = trigger.Button switch
        {
            PointerButton.Back => BackButton,
            PointerButton.Forward => ForwardButton,
            _ => MiddleButton,
        };
        return InputState.IsKeyDown(button)
            || (trigger.Keys.HasFlag(PointerKeys.Shift) && InputState.IsKeyDown(Shift))
            || (trigger.Keys.HasFlag(PointerKeys.Control) && InputState.IsKeyDown(Control))
            || (trigger.Keys.HasFlag(PointerKeys.Alt) && InputState.IsKeyDown(Alt))
            || (trigger.Keys.HasFlag(PointerKeys.Windows) && (InputState.IsKeyDown(LeftWindows) || InputState.IsKeyDown(RightWindows)));
    }

    public void Claim(PointerClaim? claim)
    {
        if (claim is not null && _hook is null)
        {
            _hook = new PointerHook(window);
            _hook.Kept += (_, kept) => events.Kept(kept.Trigger);
            _hook.Failed += (_, failure) => events.HookFailed(failure.Exception);
        }

        _hook?.Claim(claim);
    }

    public PointerInputOutcome Outcome(uint time) => PointerHook.Outcome(time);

    public void SetHookAgain() => PointerHook.SetAgain();

    public void Dispose()
    {
        _frontHook?.Dispose();
        _hook?.Dispose();
    }

    public IReadOnlyList<nint> InstalledLayouts() => KeyboardLayout.Installed();

    public string? TypedWithAltGr(uint scanCode, bool shift, nint layout) => KeyboardLayout.TypedWithAltGr(scanCode, shift, layout);

    public bool IsHeld(KeyPositionShortcut shortcut)
    {
        var modifiers = shortcut.Modifiers;
        if ((modifiers.HasFlag(HotKeyModifiers.Shift) && InputState.IsKeyDown(Shift))
            || (modifiers.HasFlag(HotKeyModifiers.Control) && InputState.IsKeyDown(Control))
            || (modifiers.HasFlag(HotKeyModifiers.Alt) && InputState.IsKeyDown(Alt))
            || (modifiers.HasFlag(HotKeyModifiers.Windows) && (InputState.IsKeyDown(LeftWindows) || InputState.IsKeyDown(RightWindows))))
        {
            return true;
        }

        var layout = KeyboardLayout.OfForegroundWindow() ?? KeyboardLayout.OfCurrentThread();
        return KeyboardLayout.VirtualKeyOf(shortcut.ScanCode, layout) is { } key && InputState.IsKeyDown(key);
    }

    public bool IsAltHeld() => InputState.IsKeyDown(Alt);

    public void PressMaskKey() => ShellActions.PressMaskKey();

    public void Lock() => ShellActions.Lock();

    public void TurnOffDisplays() => ShellActions.TurnOffDisplays(window);

    public void StartScreenSaver() => ShellActions.StartScreenSaver(window);

    public void Run(FeatureAction action) => actions.Run(action);

    public void ReportRefused(IEnumerable<string> shortcuts) => refusals.Set(featureId, shortcuts);
}

/// <summary>What <see cref="WindowsTriggerSystem"/> tells the feature's thread, on that thread.</summary>
public interface ITriggerEvents
{
    /// <summary>The pointer hook kept a click for the feature's claim.</summary>
    void Kept(PointerTrigger trigger);

    /// <summary>The window in front changed its place or size.</summary>
    void FrontMoved();

    /// <summary>The pointer hook's thread failed.</summary>
    void HookFailed(Exception exception);
}
