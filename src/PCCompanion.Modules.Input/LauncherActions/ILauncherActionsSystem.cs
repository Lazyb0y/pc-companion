using System;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>
/// What Launcher Actions asks of Windows, so tests can supply it instead. Created and disposed on
/// the host's UI thread while the feature is on. None of it waits for another app.
/// </summary>
public interface ILauncherActionsSystem : IDisposable
{
    /// <summary>
    /// Serves the pipe the command asks through, answering each request with what
    /// <paramref name="answer"/> returns, until disposed. Both run on the thread pool, and so does
    /// <paramref name="failed"/> when the pipe stops working.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows couldn't create the pipe.</exception>
    IDisposable Serve(Func<byte[], Task<byte[]>> answer, Action<Exception> failed);

    /// <summary>
    /// Runs <paramref name="action"/> on the host's UI thread, from any thread. It mustn't throw.
    /// Returns false, and it doesn't run, once the system is disposed.
    /// </summary>
    bool Post(Action action);

    /// <summary>Locks the PC.</summary>
    void Lock();

    /// <summary>Turns off the displays, as their power button would.</summary>
    void TurnOffDisplays();

    /// <summary>Starts the screen saver the user chose.</summary>
    void StartScreenSaver();

    /// <summary>Whether a key or mouse button is held now.</summary>
    bool IsInputHeld();
}

/// <summary>Launcher Actions' requests of Windows, through a hidden window on the host's UI thread.</summary>
public sealed class WindowsLauncherActionsSystem : ILauncherActionsSystem
{
    private const string WindowClassName = "PCCompanion.LauncherActions";

    private readonly MessageWindow _window = new(WindowClassName);

    public IDisposable Serve(Func<byte[], Task<byte[]>> answer, Action<Exception> failed) =>
        RequestPipe.Serve(LauncherPipe.Name, LauncherPipe.MaximumRequestLength, answer, failed);

    public bool Post(Action action) => _window.BeginInvoke(action);

    public void Lock() => ShellActions.Lock();

    public void TurnOffDisplays() => ShellActions.TurnOffDisplays(_window);

    public void StartScreenSaver() => ShellActions.StartScreenSaver(_window);

    public bool IsInputHeld() => InputState.IsAnyKeyOrButtonDown();

    public void Dispose() => _window.Dispose();
}
