using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Localization;
using PCCompanion.Logging;

namespace PCCompanion.App.ViewModels;

/// <param name="Tag">The BCP-47 tag, or <see langword="null"/> for the Windows display language.</param>
public sealed record LanguageOption(string? Tag, string DisplayName);

public sealed record ThemeOption(AppTheme Theme, string DisplayName);

/// <summary>
/// The General page. Every change is saved right away.
/// </summary>
public sealed partial class GeneralViewModel : ObservableObject
{
    // The language only changes when the app restarts.
    private static readonly CompositeFormat VersionFormat = CompositeFormat.Parse(Strings.GeneralAboutVersion);

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly AppDataPaths _paths;
    private readonly ILogger<GeneralViewModel> _logger;
    private LanguageOption _selectedLanguage;
    private ThemeOption _selectedTheme;
    private bool _startWithWindows;
    private bool _saveFailed;

    public GeneralViewModel(AppSettings settings, SettingsStore store, AppDataPaths paths, ILogger<GeneralViewModel> logger)
    {
        _settings = settings;
        _store = store;
        _paths = paths;
        _logger = logger;

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't save the settings.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);
}
