using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// Decides which input method the app in front gets when it comes to the front, asks the app to
/// switch to it and confirms the switch, and remembers the input method each app had. See
/// docs/features/input/input-language-per-app.md. Use it on the feature's thread.
/// </summary>
public sealed partial class LanguageSwitcher : IDisposable
{
    // How often, and how many times, the input method is read again after a request, to confirm it.
    private static readonly TimeSpan ConfirmCheck = TimeSpan.FromMilliseconds(50);
    private const int ConfirmChecks = 10;

    private readonly IInputLanguageSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;

    // The console host's thread of each console window that's been in front, or null when it wasn't found.
    private readonly Dictionary<nint, uint?> _consoleHosts = [];
    private readonly HashSet<nint> _consolesLookedUp = [];
    private InputLanguagePerAppSettings _settings = new();
    private (ListedApp App, InputMethodId InputMethod)[] _rules = [];
    private InputMethodId? _oneLanguage;
    private IReadOnlyList<InputMethod> _inputMethods = [];

    // The handles the input methods were read, or are being read, for.
    private IReadOnlyList<nint> _readFor = [];
    private bool _reading;
    private bool _readAgain;
    private Front? _front;
    private ITimer? _confirm;
    private InputMethodId? _asked;
    private bool _readsEverySecond;
    private bool _disposed;

    /// <param name="post">
    /// Runs an action later on the feature's thread, inside its error boundary, as timers need.
    /// </param>
    public LanguageSwitcher(IInputLanguageSystem system, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _time = time;
        _post = post;
        _logger = logger;
    }

    /// <summary>The input method each app had last, for Remember each app's language.</summary>
    public RememberedLanguages Remembered { get; } = new();

    /// <summary>
    /// Applies the settings. A rule applies the next time its app comes to the front. Any other
    /// behavior than Remember for other apps forgets every remembered input method.
    /// </summary>
    public void Apply(InputLanguagePerAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;

        var rules = new List<(ListedApp, InputMethodId)>();
        foreach (var rule in settings.Apps)
        {
            if (rule.TryGetInputMethod(out var inputMethod))
            {
                rules.Add((rule.App, inputMethod));
            }
        }

        _rules = [.. rules];
        if (rules.Count < settings.Apps.Count)
        {
            LogRulesLeftOut(_logger, settings.Apps.Count - rules.Count);
        }

        _oneLanguage = null;
        if (settings.OtherApps == OtherApps.Use)
        {
            if (settings.TryGetOtherAppsInputMethod(out var inputMethod))
            {
                _oneLanguage = inputMethod;
            }
            else if (settings.OtherAppsInputMethod is not null)
            {
                LogOneLanguageLeftOut(_logger);
            }
        }

        if (settings.OtherApps != OtherApps.Remember)
        {
            Remembered.Forget();
        }

        UpdateReads();
    }

    /// <summary>Reads the user's input methods, and takes the app in front as the one in use without switching it.</summary>
    public void Start()
    {
        ReadInputMethods(_system.LayoutHandles());
        Change(switching: false);
    }

    /// <summary>A window came to the front.</summary>
    public void OnForegroundChanged() => Change(switching: true);

    /// <summary>Reads the input method of the app in front, so an app that closes while in front keeps the one it had last.</summary>
    public void OnSecond()
    {
        if (_front is { } front)
        {
            Record(front, Read(front));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopConfirming();
        UpdateReads();
    }

    private void Change(bool switching)
    {
        // Windows the taskbar doesn't show, such as the desktop, Start and Alt + Tab, aren't an
        // app: going to one and back is staying in the app.
        var window = _system.ForegroundWindow;
        var root = window == 0 ? 0 : _system.RootOwner(window);
        var processId = root == 0 || !_system.MayHaveTaskbarButton(root) ? 0 : _system.ProcessIdOf(root);
        if (processId == 0 || _system.FocusInFront() is not { } focus)
        {
            return;
        }

        CheckInputMethods();
        var app = new AppOfWindow(_system.WindowAppId(root), _system.PackageAppId(processId), _system.ProgramPath(processId), processId);

        // The app being left still has its own input method as the next window comes to the front.
        if (_front is { } left)
        {
            Record(left, Read(left));
        }

        if (_front is { } current && current.App.IsSameAppAs(app))
        {
            Focus(current, focus);

            // A window of the app that hasn't been in front since the app came to the front, such
            // as a new one, gets the app's input method; any other is staying in the app. With
            // Keep, Windows decides for an app without a rule, also for its new windows.
            if (current.Windows.Add(root)
                && switching
                && (_settings.OtherApps != OtherApps.Keep || HasRule(app))
                && (_asked ?? current.InputMethod) is { } inputMethod)
            {
                Switch(current, inputMethod, SwitchSource.AppWindow);
            }

            UpdateReads();
            return;
        }

        StopConfirming();
        var next = new Front(app);
        next.Windows.Add(root);
        Focus(next, focus);
        next.InputMethod = Read(next);
        _front = next;
        UpdateReads();

        if (switching && TargetFor(app) is { } target)
        {
            Switch(next, target.InputMethod, target.Source);
        }
    }

    private (InputMethodId InputMethod, SwitchSource Source)? TargetFor(AppOfWindow app)
    {
        foreach (var (listed, inputMethod) in _rules)
        {
            if (listed.Matches(app))
            {
                return (inputMethod, SwitchSource.Rule);
            }
        }

        return _settings.OtherApps switch
        {
            OtherApps.Remember when Remembered.Of(app) is { } remembered => (remembered, SwitchSource.Remembered),
            OtherApps.Use when _oneLanguage is { } one => (one, SwitchSource.OneLanguage),
            _ => null,
        };
    }

    private bool HasRule(AppOfWindow app) => _rules.Any(rule => rule.App.Matches(app));

    private void Switch(Front front, InputMethodId inputMethod, SwitchSource source)
    {
        var handle = InputMethods.HandleOf(inputMethod, _inputMethods);
        if (handle == 0)
        {
            LogNotInList(_logger, source);
            return;
        }

        // Only when it differs, so the foreground event coming twice does no harm.
        if (Read(front) == inputMethod)
        {
            return;
        }

        switch (_system.RequestSwitch(front.Focus, handle))
        {
            case LayoutRequest.Posted when front.ReadThread is null:
                LogAskedUnread(_logger, source);
                break;

            case LayoutRequest.Posted:
                Confirm(front, inputMethod, source);
                break;

            case LayoutRequest.Refused:
                LogRefused(_logger, source);
                break;

            default:
                LogFailed(_logger, source);
                break;
        }
    }

    // Reads the input method again until it changed to the one asked for, or for up to 500 ms.
    private void Confirm(Front front, InputMethodId inputMethod, SwitchSource source)
    {
        StopConfirming();
        var started = _time.GetTimestamp();
        var checks = 0;
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || timer != _confirm)
            {
                return;
            }

            checks++;
            var held = Read(front) == inputMethod;
            if (!held && checks < ConfirmChecks)
            {
                return;
            }

            StopConfirming();
            if (!held)
            {
                LogNotTaken(_logger, source);
                return;
            }

            var took = _time.GetElapsedTime(started);
            LogHeld(_logger, source, took.TotalMilliseconds);
            Record(front, inputMethod);
            if (_settings.ShowNotice && _inputMethods.FirstOrDefault(method => method.Id == inputMethod) is { } shown)
            {
                _system.ShowNotice(shown);
            }
        }), null, ConfirmCheck, ConfirmCheck);
        _confirm = timer;
        _asked = inputMethod;
    }

    private void StopConfirming()
    {
        _confirm?.Dispose();
        _confirm = null;
        _asked = null;
    }

    // Keeps the input method the app has, and remembers it for an app without a rule. While a
    // request is being confirmed, the app may not have handled it yet, so reads keep nothing.
    private void Record(Front front, InputMethodId? inputMethod)
    {
        if (inputMethod is not { } read || _confirm is not null)
        {
            return;
        }

        front.InputMethod = read;
        if (_settings.OtherApps == OtherApps.Remember && !HasRule(front.App))
        {
            Remembered.Remember(front.App, read);
        }
    }

    private InputMethodId? Read(Front front) =>
        front.ReadThread is { } thread && _system.LayoutOf(thread) is { } handle ? InputMethods.IdOf(handle, _inputMethods) : null;

    // A console window's input method is on its console host's thread, which is looked up once.
    private void Focus(Front front, InputFocus focus)
    {
        front.Focus = focus.Window;
        if (!_system.IsConsole(focus.Window))
        {
            front.ReadThread = focus.Thread;
        }
        else if (_consoleHosts.TryGetValue(focus.Window, out var host))
        {
            front.ReadThread = host;
        }
        else
        {
            front.ReadThread = null;
            FindConsoleHost(focus.Window, focus.ProcessId);
        }
    }

    private void FindConsoleHost(nint window, uint processId)
    {
        if (!_consolesLookedUp.Add(window))
        {
            return;
        }

        var started = _time.GetTimestamp();
        _system.FindConsoleHost(window, processId, thread =>
        {
            if (_disposed)
            {
                return;
            }

            var took = _time.GetElapsedTime(started);
            LogConsoleHost(_logger, thread is not null, took.TotalMilliseconds);

            // Console windows that closed are forgotten, so only open ones are kept.
            foreach (var closed in _consoleHosts.Keys.Where(known => !_system.Exists(known)).ToList())
            {
                _consoleHosts.Remove(closed);
                _consolesLookedUp.Remove(closed);
            }

            _consoleHosts[window] = thread;
            if (_front is { } front && front.Focus == window && front.ReadThread is null)
            {
                front.ReadThread = thread;
                UpdateReads();
            }
        });
    }

    // The user's list of input methods is checked at each app switch, and read again when it changed.
    private void CheckInputMethods()
    {
        var handles = _system.LayoutHandles();
        if (handles.SequenceEqual(_readFor))
        {
            return;
        }

        // Until they're read again, only the input methods still in the user's list are used.
        _inputMethods = [.. _inputMethods.Where(method => handles.Any(handle => Low32(handle) == Low32(method.Handle)))];
        ReadInputMethods(handles);
    }

    private void ReadInputMethods(IReadOnlyList<nint> handles)
    {
        _readFor = handles;
        if (_reading)
        {
            _readAgain = true;
            return;
        }

        _reading = true;
        _system.ReadInputMethods(methods =>
        {
            if (_disposed)
            {
                return;
            }

            _reading = false;
            _inputMethods = methods;
            LogInputMethodsRead(_logger, methods.Count);
            if (_readAgain)
            {
                _readAgain = false;
                ReadInputMethods(_readFor);
            }
        });
    }

    // The once-a-second read runs only while an app without a rule is in front and other apps are remembered.
    private void UpdateReads()
    {
        var reads = !_disposed
            && _settings.OtherApps == OtherApps.Remember
            && _front is { ReadThread: not null } front
            && !HasRule(front.App);

        if (reads != _readsEverySecond)
        {
            _readsEverySecond = reads;
            _system.ReadEverySecond(reads);
        }
    }

    // Handles are compared on their low 32 bits, as InputMethods compares them.
    private static uint Low32(nint handle) => unchecked((uint)handle);

    /// <summary>The app in front.</summary>
    private sealed class Front(AppOfWindow app)
    {
        public AppOfWindow App { get; } = app;

        /// <summary>The app's windows that have been in front since it came to the front.</summary>
        public HashSet<nint> Windows { get; } = [];

        /// <summary>The window that typing goes to, which gets the request.</summary>
        public nint Focus { get; set; }

        /// <summary>The thread whose input method the window has, or <see langword="null"/> while it can't be read, as for a console whose host isn't found yet.</summary>
        public uint? ReadThread { get; set; }

        /// <summary>The input method the app had when last read.</summary>
        public InputMethodId? InputMethod { get; set; }
    }

    /// <summary>Why the app was switched, for the log.</summary>
    private enum SwitchSource
    {
        Rule,
        Remembered,
        OneLanguage,

        /// <summary>A new window of the app in front, which gets the app's input method.</summary>
        AppWindow,
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: a switch from {Source} held after {Milliseconds:F0} ms.")]
    private static partial void LogHeld(ILogger logger, SwitchSource source, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: the app didn't take a switch from {Source} within 500 ms.")]
    private static partial void LogNotTaken(ILogger logger, SwitchSource source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: Windows refused a switch from {Source}, as for an app running as administrator.")]
    private static partial void LogRefused(ILogger logger, SwitchSource source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: a switch from {Source} couldn't be asked for.")]
    private static partial void LogFailed(ILogger logger, SwitchSource source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: asked a console for a switch from {Source} without reading it, since its console host isn't found yet.")]
    private static partial void LogAskedUnread(ILogger logger, SwitchSource source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: the input method of a switch from {Source} isn't in the user's list.")]
    private static partial void LogNotInList(ILogger logger, SwitchSource source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: console host found: {Found}, after {Milliseconds:F1} ms.")]
    private static partial void LogConsoleHost(ILogger logger, bool found, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: read {Count} input methods.")]
    private static partial void LogInputMethodsRead(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input Language per App: {Count} listed apps have an input method identifier that can't be read, and are left out.")]
    private static partial void LogRulesLeftOut(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input Language per App: the input method identifier for other apps can't be read, so they aren't switched.")]
    private static partial void LogOneLanguageLeftOut(ILogger logger);
}
