using System;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>What Display Profiles uses from Windows in the host; tests use fakes.</summary>
public interface IDisplayProfilesSystem : IDisplaySystem
{
    /// <summary>
    /// Whether this is a remote session, where Windows reports the session's own display and
    /// refuses changes.
    /// </summary>
    bool IsRemoteSession { get; }

    /// <summary>
    /// Creates, on the host's UI thread, a hidden window that calls <paramref name="changed"/>
    /// when the displays change, or one is connected or disconnected, and
    /// <paramref name="cantAnswer"/> when the user can't answer a question: the session locks or
    /// switches to another user, or the PC goes to sleep. Both are called on that thread.
    /// </summary>
    IDisplayWindow CreateWindow(Action changed, Action cantAnswer);

    /// <summary>
    /// Runs <paramref name="action"/> on the thread pool after the actions queued before it: one
    /// at a time, in order.
    /// </summary>
    void RunInBackground(Action action);

    /// <summary>
    /// Runs <paramref name="action"/> as <see cref="RunInBackground"/> does, and waits for it.
    /// </summary>
    /// <returns>False when it didn't end within <paramref name="timeout"/>.</returns>
    bool RunInBackgroundAndWait(Action action, TimeSpan timeout);

    /// <summary>
    /// Asks whether to keep the display settings, counting down from <paramref name="time"/>, on a
    /// thread of its own; see <see cref="Platform.Windows.KeepChangesQuestion"/>.
    /// </summary>
    IKeepQuestion Ask(TimeSpan time, Action<bool> answered, Action<Exception> failed);
}

/// <summary>Display Profiles' hidden window on the host's UI thread.</summary>
public interface IDisplayWindow : IDisposable
{
    /// <summary>Runs <paramref name="action"/> on the window's thread, from any thread; nothing runs once it's disposed.</summary>
    void Post(Action action);

    /// <summary>Calls <paramref name="elapsed"/> once on the window's thread after <paramref name="delay"/>, unless disposed first.</summary>
    IDisposable StartTimer(TimeSpan delay, Action elapsed);
}

/// <summary>A question that's open.</summary>
public interface IKeepQuestion
{
    /// <summary>Closes it as if the user reverted, from any thread; nothing happens once it's closed.</summary>
    void Close();
}
