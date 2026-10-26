using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>
/// Switches the proxy settings between named profiles from the PC Companion icon's menu. See
/// docs/features/network/network-profiles.md.
/// </summary>
public sealed partial class NetworkProfilesFeature(INetworkProfilesSystem system, IHostCommands commands, ILogger<NetworkProfilesFeature> logger)
    : IFeature, IHostMenuItems
{
    // Windows signals each change twice, about 1 ms apart, so one read follows both.
    private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(50);

    private readonly Lock _readGate = new();
    private ReadState _read;

    // Set and cleared on the host's UI thread; the thread pool reads it to post its results.
    private volatile Session? _session;

    // On the host's UI thread.
    private IReadOnlyList<UsableProfile> _profiles = [];

    // The profiles of the menu that opened last, as it numbered them. The settings can change while
    // it's open, which moves or changes the profiles.
    private IReadOnlyList<UsableProfile> _menuProfiles = [];
    private ProxySettings? _windows;
    private ProxyRestrictions _restrictions;
    private int? _inUse;
    private bool _failed;

    public NetworkProfilesFeature(IHostCommands commands, ILogger<NetworkProfilesFeature> logger)
        : this(new WindowsNetworkProfilesSystem(), commands, logger)
    {
    }

    private enum ReadState
    {
        Idle,
        Waiting,
        Running,

        // Running, and asked again since it started.
        RunningAgain,
    }

    public static FeatureInfo Definition { get; } = new("networkProfiles", NetworkModule.Id, "NetworkProfilesName", "NetworkProfilesDescription", "");

    /// <summary>
    /// Whether this PC can run Network Profiles, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _session is not null;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        NetworkProfilesSettings read;
        try
        {
            read = NetworkProfilesSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        _profiles = Usable(read.Profiles);
        if (_session is null)
        {
            Start();
        }

        UpdateInUse();
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems()
    {
        _menuProfiles = _session is null ? [] : _profiles;
        return _session is null ? [] : NetworkProfilesMenu.Build(_menuProfiles, _inUse, _windows is not null, _restrictions != ProxyRestrictions.None);
    }

    public void ChooseMenuItem(int id)
    {
        if (_session is not { } session)
        {
            return;
        }

        if (id == NetworkProfilesMenu.SettingsItem)
        {
            commands.OpenFeatureSettings(Definition.Id);
            return;
        }

        // The profile the menu showed, wherever it is now, unless it was changed or deleted since.
        if (_menuProfiles.FirstOrDefault(profile => profile.Number == id) is not { } shown
            || _profiles.FirstOrDefault(profile => profile.Profile == shown.Profile) is not { } chosen)
        {
            LogNoSuchProfile(logger, id);
            return;
        }

        if (_restrictions != ProxyRestrictions.None)
        {
            LogManaged(logger, chosen.Number);
            return;
        }

        LogChosen(logger, chosen.Number);
        system.RunInBackground(TimeSpan.Zero, () => Switch(chosen, session));
    }

    public void Dispose() => Stop();

    // The profiles the menu shows, up to the most it shows; each other one is logged with its number.
    private List<UsableProfile> Usable(IReadOnlyList<NetworkProfile> profiles)
    {
        var usable = new List<UsableProfile>();
        for (var index = 0; index < profiles.Count; index++)
        {
            var number = index + 1;
            if (number > NetworkProfilesSettings.MaximumProfiles)
            {
                LogTooManyProfiles(logger, profiles.Count, NetworkProfilesSettings.MaximumProfiles);
                break;
            }

            var problem = NetworkProfileCheck.Check(profiles[index], usable.Select(other => other.Profile.Name));
            if (problem != ProfileProblem.None)
            {
                LogUnusableProfile(logger, number, problem);
                continue;
            }

            usable.Add(new UsableProfile(number, profiles[index]));
        }

        return usable;
    }

    private void Start()
    {
        var session = new Session(system.CreateInvoker());
        try
        {
            session.Watch = system.Watch(RequestRead);
        }
        catch
        {
            session.Invoker.Dispose();
            throw;
        }

        _session = session;
        LogStarted(logger);
        RequestRead();
    }

    private void Stop()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        session.Watch?.Dispose();
        session.Invoker.Dispose();
        (_windows, _inUse, _restrictions) = (null, null, ProxyRestrictions.None);
        LogStopped(logger);
    }

    // From any thread: reads the settings after a short wait, or once more after the read that's running.
    private void RequestRead()
    {
        lock (_readGate)
        {
            switch (_read)
            {
                case ReadState.Idle:
                    _read = ReadState.Waiting;
                    break;
                case ReadState.Running:
                    _read = ReadState.RunningAgain;
                    return;
                default:
                    return;
            }
        }

        system.RunInBackground(ReadDelay, Read);
    }

    // On the thread pool.
    private void Read()
    {
        lock (_readGate)
        {
            _read = ReadState.Running;
        }

        if (_session is { } session)
        {
            try
            {
                var restrictions = system.ReadRestrictions();
                ProxySettings? proxy = null;
                int? error = null;
                try
                {
                    proxy = system.ReadProxy();
                }
                catch (Win32Exception exception)
                {
                    error = exception.NativeErrorCode;
                }

                session.Invoker.Post(() => Guard(session, () => Update(proxy, restrictions, error)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // The menu keeps what it shows, and a switch reads the policies again first.
                var error = exception.HResult;
                session.Invoker.Post(() => Guard(session, () => LogCantReadPolicies(logger, error)));
            }
            catch (Exception exception)
            {
                session.Invoker.Post(() => Fail(session, exception));
            }
        }

        bool again;
        lock (_readGate)
        {
            again = _read == ReadState.RunningAgain;
            _read = again ? ReadState.Waiting : ReadState.Idle;
        }

        if (again)
        {
            system.RunInBackground(ReadDelay, Read);
        }
    }

    // On the thread pool. Group Policy can manage the settings since the menu opened, so the switch
    // reads the policies again first.
    private void Switch(UsableProfile chosen, Session session)
    {
        // Turned off since the choice: change nothing.
        if (_session != session)
        {
            return;
        }

        try
        {
            var outcome = ProxySwitch.Apply(system, chosen.Profile.Proxy);
            session.Invoker.Post(() => Guard(session, () => Switched(chosen.Number, outcome)));
        }
        catch (Exception exception)
        {
            session.Invoker.Post(() => Fail(session, exception));
        }
    }

    private void Update(ProxySettings? proxy, ProxyRestrictions restrictions, int? error)
    {
        if (restrictions != _restrictions)
        {
            LogRestrictions(logger, restrictions);
            _restrictions = restrictions;
        }

        if (error is { } code)
        {
            LogCantRead(logger, code);
            return;
        }

        _windows = proxy;
        UpdateInUse();
    }

    private void Switched(int number, SwitchOutcome outcome)
    {
        switch (outcome.Result)
        {
            case SwitchResult.Managed:
                _restrictions = outcome.Restrictions;
                LogManaged(logger, number);
                return;
            case SwitchResult.Refused:
                LogRefused(logger, number, outcome.Error);
                return;
            case SwitchResult.Switched:
                LogSwitched(logger, number);
                break;
            default:
                LogNotApplied(logger, number);
                break;
        }

        _windows = outcome.After;
        UpdateInUse();
    }

    private void UpdateInUse()
    {
        if (_windows is not { } windows)
        {
            return;
        }

        var inUse = _profiles.FirstOrDefault(profile => ProxyMatching.Matches(profile.Profile.Proxy, windows))?.Number;
        if (inUse != _inUse)
        {
            if (inUse is { } number)
            {
                LogInUse(logger, number);
            }
            else
            {
                LogNoneInUse(logger);
            }
        }

        _inUse = inUse;
    }

    // Runs a result on the host's UI thread, where an exception would end the host, and only for
    // the session it belongs to: results that come after the feature stopped are dropped.
    private void Guard(Session session, Action action)
    {
        if (_session != session)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Fail(session, exception);
        }
    }

    // Like a failing Apply, a failure keeps the feature off until the host restarts.
    private void Fail(Session session, Exception exception)
    {
        if (_session != session)
        {
            return;
        }

        LogFailed(logger, exception);
        _failed = true;
        Stop();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Network Profiles settings can't be read, so Network Profiles is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Network profile {Number} can't be used ({Problem}), so it's left out of the menu.")]
    private static partial void LogUnusableProfile(ILogger logger, int number, ProfileProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "There are {Count} network profiles; only the first {Maximum} are used.")]
    private static partial void LogTooManyProfiles(ILogger logger, int count, int maximum);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Network Profiles started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Network Profiles stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows' proxy settings match network profile {Number}.")]
    private static partial void LogInUse(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows' proxy settings match none of the network profiles.")]
    private static partial void LogNoneInUse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Group Policy restrictions on the proxy settings: {Restrictions}.")]
    private static partial void LogRestrictions(ILogger logger, ProxyRestrictions restrictions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to read the proxy settings (error {Error}).")]
    private static partial void LogCantRead(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to read the proxy policies (error {Error}).")]
    private static partial void LogCantReadPolicies(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Network profile {Number} was chosen.")]
    private static partial void LogChosen(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Network profile {Number} was chosen, but there's no such profile any more.")]
    private static partial void LogNoSuchProfile(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Network profile {Number} wasn't applied: Group Policy manages the proxy settings.")]
    private static partial void LogManaged(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switched to network profile {Number}.")]
    private static partial void LogSwitched(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused network profile {Number} (error {Error}).")]
    private static partial void LogRefused(ILogger logger, int number, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows reported switching to network profile {Number}, but its proxy settings don't match the profile.")]
    private static partial void LogNotApplied(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Error, Message = "Network Profiles failed, so it's off until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // What a start of the feature has: results from another start are dropped.
    private sealed class Session(IThreadInvoker invoker)
    {
        public IThreadInvoker Invoker { get; } = invoker;

        public IDisposable? Watch { get; set; }
    }
}
