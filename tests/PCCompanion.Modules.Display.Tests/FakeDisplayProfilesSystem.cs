using System;
using System.Collections.Generic;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.Tests;

/// <summary>
/// Display Profiles' Windows parts for the host, kept in memory: the displays of a
/// <see cref="FakeDisplaySystem"/>, a window whose actions run at once on the test's thread, work
/// for the thread pool that waits until the test runs it with <see cref="RunBackground"/>, and a
/// question the test answers.
/// </summary>
internal sealed class FakeDisplayProfilesSystem : IDisplayProfilesSystem
{
    private readonly Queue<Action> _background = new();
    private Action? _changed;
    private Action? _cantAnswer;

    public FakeDisplaySystem Displays { get; } = new();

    public bool IsRemoteSession { get; set; }

    public bool HasWindow { get; private set; }

    public int Reads { get; private set; }

    /// <summary>The questions asked so far; the last one is the one that's open, if any.</summary>
    public List<FakeQuestion> Questions { get; } = [];

    /// <summary>The timer that's running, if any.</summary>
    public (TimeSpan Delay, Action Elapsed)? Timer { get; private set; }

    public CurrentSetup Read()
    {
        Reads++;
        return Displays.Read();
    }

    public void Check(IReadOnlyList<DisplayPath> setup) => Displays.Check(setup);

    public void Apply(IReadOnlyList<DisplayPath> setup) => Displays.Apply(setup);

    public void SaveCurrent() => Displays.SaveCurrent();

    public void ApplySaved() => Displays.ApplySaved();

    public void SetHdr(DisplayTarget target, bool enabled) => Displays.SetHdr(target, enabled);

    public IReadOnlyList<DisplayMode> ListModes(DisplaySource source) => Displays.ListModes(source);

    public IDisplayWindow CreateWindow(Action changed, Action cantAnswer)
    {
        (_changed, _cantAnswer, HasWindow) = (changed, cantAnswer, true);
        return new Window(this);
    }

    public void RunInBackground(Action action) => _background.Enqueue(action);

    public bool RunInBackgroundAndWait(Action action, TimeSpan timeout)
    {
        RunInBackground(action);
        RunBackground();
        return true;
    }

    public IKeepQuestion Ask(TimeSpan time, Action<bool> answered, Action<Exception> failed)
    {
        var question = new FakeQuestion(time, answered, failed);
        Questions.Add(question);
        return question;
    }

    /// <summary>Runs the work waiting for the thread pool, and any it queues.</summary>
    public void RunBackground()
    {
        while (_background.TryDequeue(out var action))
        {
            action();
        }
    }

    /// <summary>Tells the window the displays changed, as Windows does.</summary>
    public void SignalChange()
    {
        if (HasWindow)
        {
            _changed?.Invoke();
        }
    }

    /// <summary>Tells the window the session locked, as Windows does.</summary>
    public void SignalLocked()
    {
        if (HasWindow)
        {
            _cantAnswer?.Invoke();
        }
    }

    /// <summary>Ends the timer that's running, as if its time was up.</summary>
    public void TimeUp()
    {
        var elapsed = Timer!.Value.Elapsed;
        Timer = null;
        elapsed();
    }

    /// <summary>A question that the test answers; closing it answers false, as the dialog does.</summary>
    public sealed class FakeQuestion(TimeSpan time, Action<bool> answered, Action<Exception> failed) : IKeepQuestion
    {
        public TimeSpan Time { get; } = time;

        public bool IsOpen { get; private set; } = true;

        public void Close()
        {
            if (IsOpen)
            {
                Answer(kept: false);
            }
        }

        public void Answer(bool kept)
        {
            IsOpen = false;
            answered(kept);
        }

        public void Fail(Exception exception)
        {
            IsOpen = false;
            failed(exception);
        }
    }

    private sealed class Window(FakeDisplayProfilesSystem system) : IDisplayWindow
    {
        private bool _disposed;

        public void Post(Action action)
        {
            if (!_disposed)
            {
                action();
            }
        }

        public IDisposable StartTimer(TimeSpan delay, Action elapsed)
        {
            system.Timer = (delay, elapsed);
            return new Stop(() =>
            {
                if (system.Timer?.Elapsed == elapsed)
                {
                    system.Timer = null;
                }
            });
        }

        public void Dispose()
        {
            _disposed = true;
            system.HasWindow = false;
        }
    }

    private sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
