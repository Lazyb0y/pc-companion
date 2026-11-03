using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Title Bar Actions' claim and actions in Windows itself, with <paramref name="hook"/> keeping the
/// clicks and <paramref name="requests"/> passing windows on to the other features.
/// </summary>
public sealed class WindowsTitleBarActionsSystem(PointerHook hook, WindowRequests requests) : ITitleBarActionsSystem
{
    public PointerKeys HeldKeys =>
        (InputState.IsModifierHeld(ModifierKey.Shift) ? PointerKeys.Shift : PointerKeys.None)
        | (InputState.IsModifierHeld(ModifierKey.Control) ? PointerKeys.Control : PointerKeys.None)
        | (InputState.IsModifierHeld(ModifierKey.Alt) ? PointerKeys.Alt : PointerKeys.None)
        | (InputState.IsModifierHeld(ModifierKey.Windows) ? PointerKeys.Windows : PointerKeys.None);

    public nint ForegroundWindow => Foreground.Window;

    public void Claim(PointerClaim? claim) => hook.Claim(claim);

    public PointerInputOutcome Outcome(uint time) => PointerHook.Outcome(time);

    public void SetHookAgain() => PointerHook.SetAgain();

    public bool HasMinimizeButton(nint window) => TopLevelWindows.HasMinimizeButton(window);

    public bool IsTopmost(nint window) => TopLevelWindows.IsTopmost(window);

    public nint NextWindowBehind(nint window)
    {
        var current = window;
        for (var step = 0; step < 500; step++)
        {
            current = TopLevelWindows.NextVisibleBehind(current);
            if (current == 0)
            {
                return 0;
            }

            if (TopLevelWindows.MayHaveTaskbarButton(current) && !TopLevelWindows.IsCloaked(current) && !TopLevelWindows.IsMinimized(current))
            {
                return current;
            }
        }

        return 0;
    }

    public bool AskToMinimize(nint window) => TopLevelWindows.AskToMinimize(window);

    public bool SendToBack(nint window) => TopLevelWindows.SendToBack(window);

    public bool BringToFront(nint window) => TopLevelWindows.Activate(window);

    public bool SendToTray(nint window) => requests.SendToTray(window);

    public bool HideApp(nint window) => requests.HideApp(window);

    public void PlayWarningSound() => WarningSound.Play();
}
