using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.FullScreen;
using PCCompanion.Modules.Input.Notice;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// Switches the keyboard language for the app in front: the input method the user chose for the
/// apps they list, and for other apps the one each had last, the language in use or one input
/// method. Everything but file work and slow reads runs on the host's UI thread. See
/// docs/features/input/input-language-per-app.md.
/// </summary>
/// <param name="notice">The notice window Input Language per App shares with Lock Keys.</param>
/// <param name="isUacElevated">Whether the host runs as administrator after a UAC prompt, when the file isn't used.</param>
/// <param name="system">Creates what the feature reads from Windows and changes there; tests supply a fake.</param>
public sealed partial class InputLanguagePerAppFeature(
    ILogger<InputLanguagePerAppFeature> logger,
    TimeProvider time,
    SharedNotice notice,
    DataFolder dataFolder,
    bool isUacElevated,
    Func<InputLanguagePerAppFeature, IInputLanguageSystem> system) : IFeature
{
    private const string WindowClassName = "PCCompanion.InputLanguagePerApp";

    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadTolerance = TimeSpan.FromMilliseconds(500);

    // How long after a change the remembered input methods are written, and how long the host
    // waits for that as it exits.
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(1);

    private readonly RememberedLanguagesStore _store = new(Path.Join(dataFolder.Path, RememberedLanguagesStore.FileName));
    private readonly WindowsFullScreenEnvironment _fullScreen = new();
    private InputLanguagePerAppSettings _settings = new();
    private MessageWindow? _window;
    private WinEventHook? _foreground;
    private SessionMonitor? _sessions;
    private LanguageSwitcher? _switcher;
    private CoalescableTimer? _reads;
    private ITimer? _save;
    private bool _locked;
    private bool _failed;

    // The file's reads, writes and deletes run on the thread pool one after another, in order.
    private Task _fileWork = Task.CompletedTask;

    // Whether the file was deleted since the last write, so it isn't deleted again at each save of the settings.
    private bool _fileDeleted;

    public InputLanguagePerAppFeature(ILogger<InputLanguagePerAppFeature> logger, TimeProvider time, SharedNotice notice, DataFolder dataFolder)
        : this(logger, time, notice, dataFolder, Elevation.IsUacElevated, feature => new WindowsInputLanguageSystem(feature))
    {
    }

    public static FeatureInfo Definition { get; } = new("inputLanguagePerApp", InputModule.Id, "InputLanguagePerAppName", "InputLanguagePerAppDescription", "");

    /// <summary>
    /// Whether this PC can run Input Language per App, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _window is not null;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        InputLanguagePerAppSettings read;
        try
        {
            read = InputLanguagePerAppSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            read = new InputLanguagePerAppSettings();
        }

        // Saving other settings, such as another feature's, leaves a running feature as it is.
        var unchanged = _window is not null && read == _settings;
        _settings = read;
        if (unchanged)
        {
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            DeleteFile();
            return;
        }

        if (read.OtherApps != OtherApps.Remember)
        {
            DeleteFile();
        }

        if (_window is null)
        {
            Start();
        }
        else
        {
            _switcher?.Apply(read);
        }

        LogConfigured(logger, read.Apps.Count, read.OtherApps, read.ShowNotice);
    }

    // The host disposes features twice as it exits, so the file is waited for only once.
    public void Dispose()
    {
        if (_save is not null)
        {
            Save();
        }

        Stop();
        if (!_fileWork.Wait(ExitTimeout))
        {
            LogFileNotDoneInTime(logger, ExitTimeout.TotalSeconds);
        }

        _fileWork = Task.CompletedTask;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the thread pool, and <paramref name="done"/> with its result
    /// on the feature's thread; when Windows can't do the work, <paramref name="done"/> gets
    /// <paramref name="fallback"/>. Any other failure stops the feature, as on its thread.
    /// </summary>
    internal void OffThread<T>(Func<T> work, Action<T> done, T fallback)
    {
        _ = Task.Run(() =>
        {
            T result;
            try
            {
                result = work();
            }
            catch (Exception exception) when (exception is COMException or Win32Exception)
            {
                LogReadFailed(logger, exception);
                result = fallback;
            }
            catch (Exception exception)
            {
                var failure = ExceptionDispatchInfo.Capture(exception);
                Post(failure.Throw);
                return;
            }

            Post(() => done(result));
        });
    }

    /// <summary>Starts or stops reading the input method of the app in front once a second.</summary>
    internal void ReadEverySecond(bool read)
    {
        if (!read)
        {
            _reads?.Dispose();
            _reads = null;
        }
        else if (_reads is null && _window is not null)
        {
            _reads = new CoalescableTimer(_window, ReadInterval, ReadTolerance);
            _reads.Elapsed += (_, _) => Guard(() => _switcher?.OnSecond());
        }
    }

    /// <summary>
    /// Shows the notice with the input method's names, the language's and, for a keyboard layout,
    /// the layout's, unless an app is full screen on the display.
    /// </summary>
    internal void ShowNotice(InputMethod inputMethod)
    {
        if (_window is null || SharedNotice.ChooseDisplay() is not { } display)
        {
            return;
        }

        if (IsFullScreen(display))
        {
            LogNoticeSkipped(logger);
            return;
        }

        NoticeLine[] lines = inputMethod.LayoutName is { } layout
            ? [new(inputMethod.LanguageName, NoticeIcons.Keyboard), new(layout, null)]
            : [new(inputMethod.LanguageName, NoticeIcons.Keyboard)];
        var image = NoticePainter.Paint(lines, SharedNotice.StyleFor(display, badge: false));
        notice.Show(this, image, display, NoticePosition.BottomCenter, string.Join(", ", lines.Select(line => line.Text)), NoticeDuration, fadesOut: true, Post);
    }

    private void Start()
    {
        var window = new MessageWindow(WindowClassName);
        try
        {
            _sessions = new SessionMonitor(window);
            _foreground = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);
            notice.Acquire(this);
        }
        catch
        {
            DisposeParts();
            window.Dispose();
            throw;
        }

        _window = window;
        _window.MessageReceived += OnMessageReceived;
        _sessions.Locked += (_, _) => Guard(() => Lock(true));
        _sessions.Unlocked += (_, _) => Guard(() => Lock(false));
        _foreground.Raised += (_, _) => Guard(OnForegroundChanged);

        _switcher = new LanguageSwitcher(system(this), time, Post, logger);
        _switcher.RememberedChanged += (_, _) => Guard(ScheduleSave);
        _switcher.Apply(_settings);
        if (_settings.OtherApps == OtherApps.Remember && !isUacElevated)
        {
            ReadFile(_switcher);
        }

        _switcher.Start();
        WarmUp();
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _window.MessageReceived -= OnMessageReceived;
        DisposeParts();
        _window.Dispose();
        _window = null;
        _locked = false;
        LogStopped(logger);
    }

    // Leaves every app's input method as it is.
    private void DisposeParts()
    {
        _save?.Dispose();
        _switcher?.Dispose();
        _reads?.Dispose();
        _foreground?.Dispose();
        _sessions?.Dispose();
        notice.Release(this);
        (_save, _switcher, _reads, _foreground, _sessions) = (null, null, null, null, null);
    }

    private void Lock(bool locked)
    {
        _locked = locked;
        LogLocked(logger, locked);
    }

    // The lock screen shows nothing of the apps, so the app in front when it locked is the one in front after.
    private void OnForegroundChanged()
    {
        if (!_locked)
        {
            _switcher?.OnForegroundChanged();
        }
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (Displays.IsChangeMessage(e.Message))
        {
            Guard(() => notice.Hide(this));
        }
    }

    // The first drawing loads the fonts and the text shaping, which took 50 ms in Windows Sandbox,
    // so it's done once on the thread pool, in the user's language, before any notice shows.
    private void WarmUp()
    {
        var style = SharedNotice.StyleFor(new DisplayDetails(Rectangle.Empty, Rectangle.Empty, 96), badge: false);
        NoticeLine[] lines = [new(CultureInfo.CurrentUICulture.NativeName, NoticeIcons.Keyboard), new(CultureInfo.CurrentUICulture.NativeName, null)];
        _ = Task.Run(() =>
        {
            try
            {
                NoticePainter.Paint(lines, style);
            }
            catch (Win32Exception exception)
            {
                LogWarmUpFailed(logger, exception);
            }
        });
    }

    // Exclusive full screen always counts: a window can't show over it, and trying can take a game
    // out of full screen.
    private bool IsFullScreen(DisplayDetails display) =>
        (_fullScreen.FullScreenMode == FullScreenMode.Exclusive && _fullScreen.FrontWindowDisplay == display.Bounds)
        || FullScreenCheck.IsFullScreen(_fullScreen, display.Bounds);

    // Until the file is read, Remember switches nothing.
    private void ReadFile(LanguageSwitcher switcher)
    {
        switcher.Remembered.WaitForLoad();
        OnFileThread(() =>
        {
            var apps = _store.Load(out var readable);
            Post(() =>
            {
                if (_switcher != switcher)
                {
                    return;
                }

                var added = switcher.Remembered.Load(apps);
                if (readable)
                {
                    LogFileRead(logger, added);
                }
                else
                {
                    LogUnreadableFile(logger);
                }
            });
        });
    }

    private void ScheduleSave()
    {
        if (_save is null && !isUacElevated)
        {
            _save = time.CreateTimer(_ => Post(Save), null, SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Save()
    {
        _save?.Dispose();
        _save = null;
        if (_switcher is not { Remembered.IsLoaded: true } switcher || _settings.OtherApps != OtherApps.Remember || isUacElevated)
        {
            return;
        }

        var apps = switcher.Remembered.Saved;
        _fileDeleted = false;
        OnFileThread(() =>
        {
            try
            {
                _store.Save(apps);
                LogFileSaved(logger, apps.Count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFileNotSaved(logger, exception);
            }
        });
    }

    // Forgetting deletes the file, once, unless the host runs as administrator after a UAC
    // prompt, when a program without admin rights could have put a link in its place.
    private void DeleteFile()
    {
        _save?.Dispose();
        _save = null;
        if (_fileDeleted || isUacElevated)
        {
            return;
        }

        _fileDeleted = true;
        OnFileThread(() =>
        {
            try
            {
                _store.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFileNotDeleted(logger, exception);
            }
        });
    }

    private void OnFileThread(Action work) =>
        _fileWork = _fileWork.ContinueWith(
            _ =>
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    LogFileWorkFailed(logger, exception);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

    // Runs an action later on the window's thread, inside the feature's error boundary.
    private void Post(Action action) => _ = _window?.BeginInvoke(() => Guard(action));

    // Runs on the window's thread, where an exception would end the host. Like a failing Apply,
    // a failure stops the feature until the host restarts.
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            _failed = true;
            Stop();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App applied its settings: {Apps} listed apps, other apps {OtherApps}, notice {ShowNotice}.")]
    private static partial void LogConfigured(ILogger logger, int apps, OtherApps otherApps, bool showNotice);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App: the session locked: {Locked}.")]
    private static partial void LogLocked(ILogger logger, bool locked);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App showed no notice, since an app was full screen.")]
    private static partial void LogNoticeSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App read {Count} remembered input methods.")]
    private static partial void LogFileRead(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App saved {Count} remembered input methods.")]
    private static partial void LogFileSaved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input Language per App couldn't draw its notice ahead of time; the first notice draws it.")]
    private static partial void LogWarmUpFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows couldn't give Input Language per App what it read.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Input Language per App settings can't be read, so Input Language per App is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The remembered input methods can't be read, so they start again; the next save replaces the file.")]
    private static partial void LogUnreadableFile(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input Language per App couldn't save the remembered input methods.")]
    private static partial void LogFileNotSaved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input Language per App couldn't delete the remembered input methods.")]
    private static partial void LogFileNotDeleted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Input Language per App didn't finish with its file within {Seconds} s as PC Companion exited.")]
    private static partial void LogFileNotDoneInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Input Language per App failed with its file of remembered input methods.")]
    private static partial void LogFileWorkFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Input Language per App failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
