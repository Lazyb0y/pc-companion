using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Localization;
using PCCompanion.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.ViewModels;

/// <param name="Tag">The BCP-47 tag, or <see langword="null"/> for the Windows display language.</param>
public sealed record LanguageOption(string? Tag, string DisplayName);

public sealed record ThemeOption(AppTheme Theme, string DisplayName);

/// <summary>
/// The General page. Every change is saved right away.
/// </summary>
public sealed partial class GeneralViewModel : ObservableObject, IDisposable
{
    // The language only changes when the app restarts.
    private static readonly CompositeFormat VersionFormat = CompositeFormat.Parse(Strings.GeneralAboutVersion);
    private static readonly CompositeFormat DetailedLoggingOnUntilFormat = CompositeFormat.Parse(Strings.GeneralDetailedLoggingOnUntil);
    private static readonly CompositeFormat UpdateAvailableFormat = CompositeFormat.Parse(Strings.GeneralUpdateAvailable);
    private static readonly CompositeFormat UpdateStatusAvailableFormat = CompositeFormat.Parse(Strings.GeneralUpdateStatusAvailable);
    private static readonly CompositeFormat LastCheckFormat = CompositeFormat.Parse(Strings.GeneralUpdateLastCheck);

    private static readonly ReleaseVersion CurrentVersion =
        ReleaseVersion.FromAssemblyVersion(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly AppDataPaths _paths;
    private readonly DetailedLogging _detailedLogging;
    private readonly TimeProvider _time;
    private readonly ILogger<GeneralViewModel> _logger;
    private readonly RunAsAdministrator _runAsAdministrator;
    private readonly UpdateCheckStore _updateCheckStore;
    private readonly UpdateChecker _updateChecker;
    private readonly SettingsWatcher _updateCheckWatcher;
    private readonly CancellationTokenSource _leavingPage = new();
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private LanguageOption _selectedLanguage;
    private ThemeOption _selectedTheme;
    private bool _startWithWindows;
    private bool _saveFailed;
    private bool _runAsAdministratorOn;
    private bool _changingRunAsAdministrator;
    private bool _runAsAdministratorFailed;
    private bool _checkForUpdates;
    private ReleaseVersion? _newerVersion;
    private DateTimeOffset? _lastCheck;
    private bool _checkingNow;
    private bool _checkedNow;
    private CheckNowResult? _checkNowProblem;
    private bool _disposed;

    public GeneralViewModel(
        AppSettings settings,
        SettingsStore store,
        AppDataPaths paths,
        DetailedLogging detailedLogging,
        TimeProvider time,
        RunAsAdministrator runAsAdministrator,
        UpdateCheckStore updateCheckStore,
        UpdateChecker updateChecker,
        ILogger<GeneralViewModel> logger)
    {
        _settings = settings;
        _store = store;
        _paths = paths;
        _detailedLogging = detailedLogging;
        _time = time;
        _logger = logger;
        _runAsAdministrator = runAsAdministrator;
        _runAsAdministratorOn = runAsAdministrator.IsOn;
        _updateCheckStore = updateCheckStore;
        _updateChecker = updateChecker;

        // Detailed logging also turns off by itself, on a timer thread.
        _detailedLogging.Changed += OnDetailedLoggingChanged;

        // The host saves the update check's result, also while the page is open.
        _checkForUpdates = settings.Updates.CheckAutomatically;
        var updateCheck = updateCheckStore.Load();
        _newerVersion = updateCheck.NewerThan(CurrentVersion);
        _lastCheck = updateCheck.LastCheck;
        _updateCheckWatcher = new SettingsWatcher(paths.UpdateCheckFile);
        _updateCheckWatcher.Changed += OnUpdateCheckChanged;

        // Each language is shown in its own language.
        Languages =
        [
            new(null, Strings.GeneralLanguageWindows),
            .. Localization.Languages.Available.Select(language => new LanguageOption(language.Name, language.NativeName)),
        ];
        _selectedLanguage = Languages.FirstOrDefault(option => string.Equals(option.Tag, settings.General.Language, StringComparison.OrdinalIgnoreCase))
            ?? Languages[0];

        Themes =
        [
            new(AppTheme.System, Strings.GeneralThemeSystem),
            new(AppTheme.Light, Strings.GeneralThemeLight),
            new(AppTheme.Dark, Strings.GeneralThemeDark),
        ];
        _selectedTheme = Themes.FirstOrDefault(option => option.Theme == settings.General.Theme) ?? Themes[0];

        _startWithWindows = settings.General.StartWithWindows;

        LogsRetention = PluralStrings.Format("GeneralLogsRetention", FileLoggingExtensions.RetentionDays);
        Version = string.Format(CultureInfo.CurrentCulture, VersionFormat, App.InformationalVersion.Split('+')[0]);
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public string LogsRetention { get; }

    public string Version { get; }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is not null && SetProperty(ref _selectedLanguage, value))
            {
                _settings.General.Language = value.Tag;
                Save();
            }
        }
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not null && SetProperty(ref _selectedTheme, value))
            {
                _settings.General.Theme = value.Theme;
                Save();
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _settings.General.StartWithWindows = value;
                Save();
            }
        }
    }

    /// <summary>
    /// Always run as administrator. Changing it asks for admin rights; when the user declines or
    /// it fails, the switch goes back.
    /// </summary>
    public bool RunAsAdministratorOn
    {
        get => _runAsAdministratorOn;
        set
        {
            if (value != _runAsAdministratorOn && !_changingRunAsAdministrator)
            {
                _runAsAdministratorOn = value;
                OnPropertyChanged();
                _ = ChangeRunAsAdministratorAsync(value);
            }
        }
    }

    public bool RunAsAdministratorAvailable => _runAsAdministrator.UnavailableReason is null && !_changingRunAsAdministrator;

    /// <summary>What the setting does, or why it isn't available.</summary>
    public string RunAsAdministratorDescription => _runAsAdministrator.UnavailableReason ?? Strings.GeneralRunAsAdministratorDescription;

    /// <summary>True when the last change of Always run as administrator failed.</summary>
    public bool RunAsAdministratorFailed
    {
        get => _runAsAdministratorFailed;
        private set => SetProperty(ref _runAsAdministratorFailed, value);
    }

    /// <summary>
    /// True when Always run as administrator is on but the host runs without admin rights, so
    /// features don't work in front of apps running as administrator.
    /// </summary>
    public bool HostWithoutAdminRights => HostLauncher.RunsWithoutAdminRights(_settings.General);

    /// <summary>The host writes its logs to another folder while it runs as administrator.</summary>
    // The host keeps its logs in the protected folder only when it got admin rights through UAC,
    // as it does when this app runs without them only because of UAC. With UAC off, or while the
    // host runs without admin rights, its logs are in the user's logs folder, which Open logs
    // folder opens.
    public bool HostLogsShown => _runAsAdministrator.IsOn && Elevation.IsUacLimited && !HostWithoutAdminRights;

    public bool DetailedLoggingOn
    {
        get => _detailedLogging.IsOn;
        set
        {
            if (value == _detailedLogging.IsOn)
            {
                return;
            }

            DateTimeOffset? until = value ? _time.GetLocalNow() + DetailedLogging.Duration : null;
            _settings.Diagnostics.DetailedLoggingUntil = until;
            Save();
            _detailedLogging.TurnOnUntil(until);
        }
    }

    /// <summary>When detailed logging is on, when it turns off; otherwise what it does.</summary>
    public string DetailedLoggingDescription => _detailedLogging.Until is { } until
        ? string.Format(CultureInfo.CurrentCulture, DetailedLoggingOnUntilFormat, until.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : PluralStrings.Format("GeneralDetailedLoggingDescription", (int)DetailedLogging.Duration.TotalHours);

    /// <summary>Whether the host asks GitHub once a day whether a newer version is available.</summary>
    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            if (SetProperty(ref _checkForUpdates, value))
            {
                _settings.Updates.CheckAutomatically = value;
                Save();
                OnPropertyChanged(nameof(UpdateAvailable));
            }
        }
    }

    /// <summary>
    /// Whether the banner shows: when a version newer than this one was found, while the check is
    /// on, or by Check now on this page.
    /// </summary>
    public bool UpdateAvailable => (_checkForUpdates || _checkedNow) && _newerVersion is not null;

    /// <summary>The banner's text, with the newer version and this one.</summary>
    public string UpdateMessage => _newerVersion is { } version
        ? string.Format(CultureInfo.CurrentCulture, UpdateAvailableFormat, version, CurrentVersion)
        : string.Empty;

    /// <summary>
    /// Check now is turned off while this app runs as administrator through UAC, so the request
    /// and saving the result never do.
    /// </summary>
    public bool CheckNowAvailable { get; } = !Elevation.IsUacElevated;

    public bool CheckingNow
    {
        get => _checkingNow;
        private set => SetProperty(ref _checkingNow, value);
    }

    /// <summary>What the last check found, or that Check now is under way or failed.</summary>
    public string UpdateStatus => _checkingNow ? Strings.GeneralUpdateStatusChecking
        : _checkNowProblem == CheckNowResult.NoAnswer ? Strings.GeneralUpdateStatusNoAnswer
        : _checkNowProblem == CheckNowResult.Failed ? Strings.GeneralUpdateStatusFailed
        : _newerVersion is { } version ? string.Format(CultureInfo.CurrentCulture, UpdateStatusAvailableFormat, version)
        : _lastCheck is not null ? Strings.GeneralUpdateStatusUpToDate
        : Strings.GeneralUpdateStatusNotChecked;

    /// <summary>Below the status: when the last check was, what to do after Check now failed, or why it's off.</summary>
    public string? UpdateStatusDetail => !CheckNowAvailable ? Strings.GeneralUpdateCheckNowUnavailable
        : _checkNowProblem == CheckNowResult.NoAnswer ? Strings.GeneralUpdateStatusNoAnswerHint
        : _checkNowProblem == CheckNowResult.Failed ? Strings.GeneralUpdateStatusFailedHint
        : _lastCheck is { } last ? string.Format(CultureInfo.CurrentCulture, LastCheckFormat, last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : null;

    /// <summary>True when the last change couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => _saveFailed;
        private set => SetProperty(ref _saveFailed, value);
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_paths.LogsFolder);
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { _paths.LogsFolder } });
    }

    [RelayCommand]
    private void OpenHostLogsFolder()
    {
        var folder = AppDataPaths.ProtectedLogsFolder(Elevation.CurrentUserSid);
        if (!Directory.Exists(folder))
        {
            LogNoHostLogs(_logger);
            return;
        }

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { folder } });
    }

    // The settings app never runs as administrator, so neither does the browser it starts.
    [RelayCommand]
    private void OpenReleasePage()
    {
        if (!ShellActions.OpenWebPage(GitHubReleaseSource.ReleasePageAddress))
        {
            LogCantOpenReleasePage(_logger);
        }
    }

    // Asks GitHub at once, whatever the setting. The settings app runs without admin rights, so
    // the request and saving the result do too. Selecting it again while a check runs does
    // nothing, rather than turning the button off, so the keyboard focus stays on it.
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CheckNowAvailable))]
    private async Task CheckNowAsync()
    {
        if (_checkingNow)
        {
            return;
        }

        LogCheckingNow(_logger);
        var leavingPage = _leavingPage.Token;
        _checkNowProblem = null;
        CheckingNow = true;
        OnUpdateStatusChanged();
        try
        {
            // Off the window's thread: reading and saving the result, and the request.
            var outcome = await Task.Run(() => _updateChecker.CheckNowAsync(CurrentVersion, leavingPage), leavingPage).ConfigureAwait(true);
            if (outcome.Result is CheckNowResult.UpToDate or CheckNowResult.UpdateAvailable)
            {
                _newerVersion = outcome.NewerVersion;
                _lastCheck = _time.GetUtcNow();
                _checkedNow = true;
            }
            else
            {
                if (outcome.Result == CheckNowResult.NoAnswer)
                {
                    LogCheckNowNoAnswer(_logger);
                }

                _checkNowProblem = outcome.Result;
            }
        }
        catch (OperationCanceledException) when (leavingPage.IsCancellationRequested)
        {
            // The page closed.
        }
        catch (Exception exception)
        {
            LogCheckNowFailed(_logger, exception);
            _checkNowProblem = CheckNowResult.Failed;
        }
        finally
        {
            CheckingNow = false;
            OnUpdateStatusChanged();
        }
    }

    // Called when the page unloads, and again when the window closes.
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detailedLogging.Changed -= OnDetailedLoggingChanged;
        _updateCheckWatcher.Changed -= OnUpdateCheckChanged;
        _updateCheckWatcher.Dispose();
        _leavingPage.Cancel();
        _leavingPage.Dispose();
    }

    private async Task ChangeRunAsAdministratorAsync(bool on)
    {
        _changingRunAsAdministrator = true;
        OnPropertyChanged(nameof(RunAsAdministratorAvailable));

        var result = await _runAsAdministrator.SetAsync(on).ConfigureAwait(true);

        _changingRunAsAdministrator = false;
        _runAsAdministratorOn = _runAsAdministrator.IsOn;
        RunAsAdministratorFailed = result == RunAsAdministratorResult.Failed;
        OnPropertyChanged(nameof(RunAsAdministratorOn));
        OnPropertyChanged(nameof(RunAsAdministratorAvailable));
        OnPropertyChanged(nameof(HostLogsShown));
        OnPropertyChanged(nameof(HostWithoutAdminRights));
    }

    private void OnDetailedLoggingChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            OnPropertyChanged(nameof(DetailedLoggingOn));
            OnPropertyChanged(nameof(DetailedLoggingDescription));
        }

        if (_dispatcher is null)
        {
            Update();
        }
        else
        {
            _dispatcher.TryEnqueue(Update);
        }
    }

    // On a thread pool thread, after the host or Check now saved a new result.
    private void OnUpdateCheckChanged(object? sender, EventArgs e)
    {
        var updateCheck = _updateCheckStore.Load();
        void Update()
        {
            _newerVersion = updateCheck.NewerThan(CurrentVersion);
            _lastCheck = updateCheck.LastCheck;
            OnUpdateStatusChanged();
        }

        if (_dispatcher is null)
        {
            Update();
        }
        else
        {
            _dispatcher.TryEnqueue(Update);
        }
    }

    private void OnUpdateStatusChanged()
    {
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateMessage));
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(UpdateStatusDetail));
    }

    private void Save()
    {
        try
        {
            _store.Save(_settings);
            SaveFailed = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, exception);
            SaveFailed = true;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The host hasn't written logs as administrator yet.")]
    private static partial void LogNoHostLogs(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't open the release page.")]
    private static partial void LogCantOpenReleasePage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for updates, as Check now was selected.")]
    private static partial void LogCheckingNow(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Check now got no answer from GitHub.")]
    private static partial void LogCheckNowNoAnswer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Check now failed.")]
    private static partial void LogCheckNowFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't save the settings.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
