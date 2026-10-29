using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// Sends any app's window to an icon in the notification area instead of the taskbar. See
/// docs/features/windows/minimize-to-tray.md.
/// </summary>
public sealed partial class MinimizeToTrayFeature : IFeature, IHostMenuItems
{
    // How long the host waits as it exits for the windows to go back on the taskbar.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private const int BringAllBackItem = 100_000;

    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger<MinimizeToTrayFeature> _logger;
    private readonly Func<ITrayWindowsSystem> _recoverySystem;
    private MinimizeToTrayThread? _thread;
    private MinimizeToTrayThread? _stopped;
    private IReadOnlyList<TrayWindowInfo> _menuWindows = [];
    private bool _failed;
    private bool _recovered;
    private bool _askedToEndFirst;

    public MinimizeToTrayFeature(IHostCommands commands, TimeProvider time, ILogger<MinimizeToTrayFeature> logger)
        : this(commands, time, logger, () => new WindowsTrayWindowsSystem(null, action => action()))
    {
    }

    /// <param name="recoverySystem">Where the windows left marked are found, as tests supply it.</param>
    public MinimizeToTrayFeature(IHostCommands commands, TimeProvider time, ILogger<MinimizeToTrayFeature> logger, Func<ITrayWindowsSystem> recoverySystem)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(recoverySystem);

        _commands = commands;
        _time = time;
        _logger = logger;
        _recoverySystem = recoverySystem;

        // A crash ends the host without stopping the feature, so the windows go back on the taskbar first.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static FeatureInfo Definition { get; } = new("minimizeToTray", WindowsModule.Id, "MinimizeToTrayName", "MinimizeToTrayDescription", "");

    /// <summary>
    /// Whether this PC can run Minimize to Tray, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public void Apply(JsonElement? settings)
    {
        // Windows left marked by a PC Companion that ended without bringing them back come back on
        // the taskbar, on or off; it reads every window's properties without waiting for any program.
        if (!_recovered)
        {
            _recovered = true;
            var recovered = TrayController.Recover(_recoverySystem());
            if (recovered > 0)
            {
                LogRecovered(_logger, recovered);
            }
        }

        // A failure on the feature's thread keeps the feature off, like a failing Apply.
        if (_thread is { HasFailed: true })
        {
            _failed = true;
            Stop();
        }

        if (_failed)
        {
            return;
        }

        MinimizeToTraySettings read;
        try
        {
            read = MinimizeToTraySettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        // Windows then asks the host before other apps when the session ends, so the windows in
        // the tray are back on the taskbar before their apps are asked to close.
        if (!_askedToEndFirst)
        {
            _askedToEndFirst = true;
            if (!ShutdownOrder.AskFirst())
            {
                LogShutdownOrderRefused(_logger);
            }
        }

        if (_thread is null)
        {
            _thread = new MinimizeToTrayThread(read, _commands, _time, _logger, _stopped);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems()
    {
        var windows = _thread?.Windows ?? [];
        _menuWindows = windows;
        if (windows.Count == 0)
        {
            return [];
        }

        // Parsed each time, so a change of language shows at once.
        var format = CompositeFormat.Parse(Strings.MinimizeToTrayHostMenuItem);
        var items = windows
            .Select((window, at) => new HostMenuItem(at + 1, window.Title.Length > 0 ? string.Format(CultureInfo.CurrentCulture, format, window.AppName, window.Title) : window.AppName))
            .Append(HostMenuItem.Separator)
            .Append(new HostMenuItem(BringAllBackItem, Strings.MinimizeToTrayBringAllBack))
            .ToList();
        return [HostMenuItem.Submenu(Strings.MinimizeToTrayHostMenu, items)];
    }

    public void ChooseMenuItem(int id)
    {
        if (id == BringAllBackItem)
        {
            _thread?.BringAllBack();
        }
        else if (id >= 1 && id <= _menuWindows.Count)
        {
            _thread?.BringBack(_menuWindows[id - 1].Window);
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    // Runs on the crashing thread; nothing here may throw.
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _thread?.ReleaseNow();
        }
        catch (Exception exception)
        {
            LogReleaseFailed(_logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray brought back {Count} windows left in the tray.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Minimize to Tray settings can't be read, so Minimize to Tray is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to ask PC Companion first when the session ends.")]
    private static partial void LogShutdownOrderRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Minimize to Tray didn't stop within {Seconds} s, so windows may be left in the tray until PC Companion starts again.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Minimize to Tray couldn't put its windows back on the taskbar as PC Companion crashed.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);
}
