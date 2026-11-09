using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// What End App shows while the user chooses an app: PC Companion's invisible window over every
/// display and the frame around the window under the pointer. Use it on the feature's thread.
/// </summary>
public interface IEndAppView
{
    /// <summary>Covers every display and takes the front. Returns whether it's in front.</summary>
    bool ShowCovering();

    void HideCovering();

    bool IsCovering { get; }

    /// <summary>Shows the crosshair over an app that can be ended, and the Unavailable pointer otherwise.</summary>
    void SetPointer(bool canEnd);

    /// <summary>Sets the covering window's name, which screen readers read.</summary>
    void SetTitle(string title);

    /// <summary>Frames <paramref name="bounds"/>, red for an app that can be ended and gray otherwise, or the high contrast theme's colors.</summary>
    void ShowFrame(Rectangle bounds, int thickness, bool canEnd);

    void HideFrame();
}

/// <summary>What End App asks of other threads: the dialogs, Task Manager, notifications and the thread pool.</summary>
public interface IEndAppHost
{
    /// <summary>
    /// Shows a question on a thread of its own; <paramref name="answered"/> runs on that thread
    /// with the index of the button chosen, or -1 when it was canceled or closed.
    /// </summary>
    IEndAppQuestion Ask(QuestionPage page, AppIcon? icon, Rectangle? area, Action<int> answered);

    /// <summary>Opens Task Manager, on the thread pool.</summary>
    void OpenTaskManager();

    /// <summary>Shows a notification from the PC Companion icon, on the host's UI thread.</summary>
    void Notify(string title, string text);

    /// <summary>Runs <paramref name="work"/> on the thread pool; it passes its result back itself.</summary>
    void Background(Action work);
}

/// <summary>An open question, which can change its page or close from any thread.</summary>
public interface IEndAppQuestion
{
    void Change(QuestionPage page);

    void Close();
}
