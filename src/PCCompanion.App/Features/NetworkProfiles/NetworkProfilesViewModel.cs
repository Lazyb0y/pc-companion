using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.NetworkProfiles;

/// <summary>A profile in the page's list.</summary>
/// <param name="Index">The profile's place in the list, from 0.</param>
/// <param name="Summary">What the profile does, or why it isn't in the menu.</param>
/// <param name="IsInUse">Whether Windows uses the profile's settings now.</param>
/// <param name="CanUse">Whether Use is available: the feature is on, the profile can be used, and Group Policy doesn't manage the settings.</param>
public sealed record NetworkProfileItem(
    int Index,
    NetworkProfile Profile,
    string Name,
    string Summary,
    bool IsInUse,
    bool IsNotInUse,
    bool CanUse,
    string UseName,
    string EditName,
    string MoreName,
    bool CanMoveUp,
    bool CanMoveDown);

/// <summary>
/// The Network Profiles page. Every change is saved right away; a profile is saved when the profile
/// editor's Save is chosen. While the page shows, it watches Windows' proxy settings, so In use
/// follows changes made elsewhere.
/// </summary>
public sealed partial class NetworkProfilesViewModel : FeatureViewModel<NetworkProfilesSettings>
{
    // Windows signals each change twice, about 1 ms apart, so one read follows both.
    private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(50);

    private static readonly CompositeFormat UseFormat = CompositeFormat.Parse(Strings.NetworkProfilesUseName);
    private static readonly CompositeFormat EditFormat = CompositeFormat.Parse(Strings.NetworkProfilesEditName);
    private static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.NetworkProfilesMoreName);
    private static readonly CompositeFormat UseFailedFormat = CompositeFormat.Parse(Strings.NetworkProfilesUseFailed);

    private readonly WindowsNetworkProfilesSystem _system = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly ILogger<NetworkProfilesViewModel> _logger;
    private IDisposable? _watch;
    private int _readWaiting;
    private ProxySettings? _windows;
    private ProxyRestrictions _restrictions;
    private string _useProblem = string.Empty;

    public NetworkProfilesViewModel(FeatureSettingsEditor editor, ILogger<NetworkProfilesViewModel> logger)
        : base(editor, NetworkProfilesFeature.Definition)
    {
        _logger = logger;

        // Turning the feature on for the first time adds Original settings, and Use needs it on.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsOn))
            {
                RefreshProfiles();
            }
        };
        RefreshProfiles();
    }

    public ObservableCollection<NetworkProfileItem> Profiles { get; } = [];

    public bool HasNoProfiles => Profiles.Count == 0;

    /// <summary>Whether another profile can be added.</summary>
    public bool CanAdd => Settings.Profiles.Count < NetworkProfilesSettings.MaximumProfiles;

    /// <summary>Whether Group Policy manages the proxy settings, so profiles can't be used.</summary>
    public bool IsManaged => _restrictions != ProxyRestrictions.None;

    /// <summary>Why the last Use didn't switch, or an empty string.</summary>
    public string UseProblem
    {
        get => _useProblem;
        private set
        {
            if (SetProperty(ref _useProblem, value))
            {
                OnPropertyChanged(nameof(HasUseProblem));
            }
        }
    }

    public bool HasUseProblem => _useProblem.Length > 0;

    /// <summary>The other profiles' names, which a profile being added or edited can't have.</summary>
    public IReadOnlyList<string> NamesExcept(int index) =>
        [.. Settings.Profiles.Where((_, at) => at != index).Select(profile => profile.Name)];

    /// <summary>Starts watching Windows' settings, as the page shows.</summary>
    public void Start()
    {
        if (_watch is not null)
        {
            return;
        }

        try
        {
            _watch = _system.Watch(RequestRead);
        }
        catch (Win32Exception exception)
        {
            LogCantWatch(_logger, exception);
        }

        RequestRead();
    }

    /// <summary>Stops watching, as the page goes.</summary>
    public void Stop()
    {
        _watch?.Dispose();
        _watch = null;
    }

    /// <summary>A new profile with Windows' current settings, for Add from current settings, or null when Windows refuses.</summary>
    public async Task<NetworkProfile?> CurrentSettingsAsync()
    {
        try
        {
            var windows = await Task.Run(_system.ReadProxy).ConfigureAwait(true);
            return new NetworkProfile { Proxy = ProxyMatching.FromWindows(windows) };
        }
        catch (Win32Exception exception)
        {
            LogCantRead(_logger, exception.NativeErrorCode);
            return null;
        }
    }

    /// <summary>Switches Windows' proxy settings to the profile at <paramref name="index"/>, as its menu item does.</summary>
    public async Task UseAsync(int index)
    {
        if (index < 0 || index >= Profiles.Count || !Profiles[index].CanUse)
        {
            return;
        }

        var item = Profiles[index];
        UseProblem = string.Empty;
        SwitchOutcome outcome;
        try
        {
            outcome = await Task.Run(() => ProxySwitch.Apply(_system, item.Profile.Proxy)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            LogCantRead(_logger, exception.HResult);
            outcome = new SwitchOutcome(SwitchResult.Refused, Error: exception.HResult);
        }

        var number = index + 1;
        switch (outcome.Result)
        {
            case SwitchResult.Switched:
                LogSwitched(_logger, number);
                _windows = outcome.After;
                break;
            case SwitchResult.NotApplied:
                LogNotApplied(_logger, number);
                _windows = outcome.After;
                UseProblem = string.Format(CultureInfo.CurrentCulture, UseFailedFormat, item.Name);
                break;
            case SwitchResult.Refused:
                LogRefused(_logger, number, outcome.Error);
                UseProblem = string.Format(CultureInfo.CurrentCulture, UseFailedFormat, item.Name);
                break;
            default:
                SetRestrictions(outcome.Restrictions);
                break;
        }

        RefreshProfiles();
    }

    public void Add(NetworkProfile profile) => SaveProfiles([.. Settings.Profiles, profile]);

    public void Replace(int index, NetworkProfile profile) =>
        SaveProfiles([.. Settings.Profiles.Select((existing, at) => at == index ? profile : existing)]);

    public void Delete(int index) => SaveProfiles([.. Settings.Profiles.Where((_, at) => at != index)]);

    public void Move(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || target < 0 || index >= Settings.Profiles.Count || target >= Settings.Profiles.Count)
        {
            return;
        }

        var profiles = Settings.Profiles.ToList();
        (profiles[index], profiles[target]) = (profiles[target], profiles[index]);
        SaveProfiles(profiles);
    }

    [RelayCommand]
    private void OpenProxySettings()
    {
        if (!ShellActions.OpenProxySettings())
        {
            LogCantOpenProxySettings(_logger);
        }
    }

    // From any thread: one read after a short wait, however many signals come meanwhile.
    private void RequestRead()
    {
        if (Interlocked.Exchange(ref _readWaiting, 1) == 0)
        {
            _system.RunInBackground(ReadDelay, Read);
        }
    }

    // On the thread pool.
    private void Read()
    {
        Interlocked.Exchange(ref _readWaiting, 0);
        ProxySettings? windows = null;
        var restrictions = ProxyRestrictions.None;
        try
        {
            restrictions = _system.ReadRestrictions();
            windows = _system.ReadProxy();
        }
        catch (Win32Exception exception)
        {
            LogCantRead(_logger, exception.NativeErrorCode);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            LogCantRead(_logger, exception.HResult);
        }

        _dispatcher.TryEnqueue(() =>
        {
            _windows = windows ?? _windows;
            SetRestrictions(restrictions);
            RefreshProfiles();
        });
    }

    private void SetRestrictions(ProxyRestrictions restrictions)
    {
        if (_restrictions != restrictions)
        {
            _restrictions = restrictions;
            OnPropertyChanged(nameof(IsManaged));
        }
    }

    private void SaveProfiles(IReadOnlyList<NetworkProfile> profiles)
    {
        Save(Settings with { Profiles = profiles });
        RefreshProfiles();
    }

    // The same checks and matching as the menu: a profile the menu leaves out isn't used, and the
    // first profile that matches Windows' settings is the one in use.
    private void RefreshProfiles()
    {
        Profiles.Clear();
        var profiles = Settings.Profiles;
        var usableNames = new List<string>();
        var inUseFound = false;
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            var problem = NetworkProfileCheck.Check(profile, usableNames);
            var summary = index >= NetworkProfilesSettings.MaximumProfiles
                ? Strings.NetworkProfilesTooMany
                : NetworkProfilesText.Summary(profile.Proxy, problem);
            var usable = problem == ProfileProblem.None && index < NetworkProfilesSettings.MaximumProfiles;
            if (usable)
            {
                usableNames.Add(profile.Name);
            }

            var inUse = usable && !inUseFound && _windows is { } windows && ProxyMatching.Matches(profile.Proxy, windows);
            inUseFound |= inUse;
            var name = profile.Name.Trim();
            Profiles.Add(new NetworkProfileItem(
                index,
                profile,
                name,
                summary,
                inUse,
                !inUse,
                IsOn && usable && !inUse && !IsManaged,
                string.Format(CultureInfo.CurrentCulture, UseFormat, name),
                string.Format(CultureInfo.CurrentCulture, EditFormat, name),
                string.Format(CultureInfo.CurrentCulture, MoreFormat, name),
                index > 0,
                index < profiles.Count - 1));
        }

        OnPropertyChanged(nameof(HasNoProfiles));
        OnPropertyChanged(nameof(CanAdd));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't watch the proxy settings, so In use shows the settings as they were when the page opened.")]
    private static partial void LogCantWatch(ILogger logger, Win32Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to read the proxy settings (error {Error}).")]
    private static partial void LogCantRead(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switched to network profile {Number} from the page.")]
    private static partial void LogSwitched(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows reported switching to network profile {Number}, but its proxy settings don't match the profile.")]
    private static partial void LogNotApplied(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused network profile {Number} (error {Error}).")]
    private static partial void LogRefused(ILogger logger, int number, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't open the Proxy page of Windows Settings.")]
    private static partial void LogCantOpenProxySettings(ILogger logger);
}
