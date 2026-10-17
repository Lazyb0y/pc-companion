using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
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

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly AppDataPaths _paths;
    private readonly DetailedLogging _detailedLogging;
    private readonly TimeProvider _time;
    private readonly ILogger<GeneralViewModel> _logger;
    private readonly RunAsAdministrator _runAsAdministrator;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private LanguageOption _selectedLanguage;
    private ThemeOption _selectedTheme;
    private bool _startWithWindows;
    private bool _saveFailed;
    private bool _runAsAdministratorOn;
    private bool _changingRunAsAdministrator;
    private bool _runAsAdministratorFailed;

    public GeneralViewModel(
        AppSettings settings,
        SettingsStore store,
        AppDataPaths paths,
        DetailedLogging detailedLogging,
        TimeProvider time,
        RunAsAdministrator runAsAdministrator,
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

        // Detailed logging also turns off by itself, on a timer thread.
        _detailedLogging.Changed += OnDetailedLoggingChanged;

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

    /// <summary>The host writes its logs to another folder while it runs as administrator.</summary>
    public bool HostLogsShown => _runAsAdministrator.IsOn;

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

    public void Dispose() => _detailedLogging.Changed -= OnDetailedLoggingChanged;

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't save the settings.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
