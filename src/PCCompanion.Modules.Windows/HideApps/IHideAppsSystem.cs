namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// What Hide Apps reads from Windows and changes there, so tests can supply it instead. Use it on
/// the feature's thread. None of it waits for the windows' programs.
/// </summary>
public interface IHideAppsSystem
{
    bool Exists(nint window);

    bool IsMinimized(nint window);

    /// <summary>Whether the window's program hasn't handled its messages for 5 seconds.</summary>
    bool IsNotResponding(nint window);

    /// <summary>Restores a minimized window as it was, without activating it or changing its place in the order of windows.</summary>
    bool RestoreWithoutActivating(nint window);

    /// <summary>Asks Windows to put the window right behind <paramref name="inFront"/>; the window's program does it later.</summary>
    bool PutBehind(nint window, nint inFront);

    /// <summary>The first visible window behind the window, or 0.</summary>
    nint NextVisibleBehind(nint window);

    /// <summary>Starts or stops reporting each change in the order of windows to <see cref="OrderedRestore.OnOrderChanged"/>.</summary>
    void WatchOrder(bool watch);
}
