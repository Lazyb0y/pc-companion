using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// What Title Bar Actions does in Windows besides finding title bars, so tests can supply it
/// instead: the pointer hook's claim, and the actions. Use it on the host's UI thread. None of it
/// waits for the windows' programs.
/// </summary>
public interface ITitleBarActionsSystem
{
    /// <summary>The keys held now.</summary>
    PointerKeys HeldKeys { get; }

    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>Keeps the claim's clicks from the apps from now on; <see langword="null"/> keeps nothing more.</summary>
    void Claim(PointerClaim? claim);

    /// <summary>What the pointer hook did with the click stamped <paramref name="time"/>.</summary>
    PointerInputOutcome Outcome(uint time);

    /// <summary>Sets the pointer hook again, after Windows removed it.</summary>
    void SetHookAgain();

    bool HasMinimizeButton(nint window);

    bool IsTopmost(nint window);

    /// <summary>The next window that the taskbar shows behind <paramref name="window"/>, or 0.</summary>
    nint NextWindowBehind(nint window);

    /// <summary>Asks the window's program to minimize it, as its Minimize button does. Returns false when the request couldn't be posted.</summary>
    bool AskToMinimize(nint window);

    /// <summary>Puts the window behind every other window, without activating it. Returns false when Windows refused.</summary>
    bool SendToBack(nint window);

    /// <summary>Brings the window to the front. Returns false when Windows refused.</summary>
    bool BringToFront(nint window);

    /// <summary>Plays Windows' warning sound.</summary>
    void PlayWarningSound();
}
