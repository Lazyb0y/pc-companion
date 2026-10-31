using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.InputLanguagePerApp;

/// <summary>An input method of the user's, with Windows' names.</summary>
public sealed record InputMethodOption(InputMethodId Id, string DisplayName) : Choice(DisplayName);

public sealed record OtherAppsOption(OtherApps Behavior, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Input Language per App page. Every change is saved right away. The user's input methods are
/// read off the UI thread when the page opens.
/// </summary>
public sealed partial class InputLanguagePerAppViewModel : FeatureViewModel<InputLanguagePerAppSettings>
{
    private static readonly CompositeFormat LanguageNameFormat = CompositeFormat.Parse(Strings.InputLanguagePerAppLanguageName);
    private static readonly CompositeFormat InputMethodFormat = CompositeFormat.Parse(Strings.InputLanguagePerAppInputMethod);

    private readonly AppSettings _appSettings;
    private readonly ILogger<InputLanguagePerAppViewModel> _logger;
    private OtherAppsOption? _selectedOtherApps;
    private bool _inputMethodsRead;

    public InputLanguagePerAppViewModel(FeatureSettingsEditor editor, AppSettings appSettings, ILogger<InputLanguagePerAppViewModel> logger)
        : base(editor, InputLanguagePerAppFeature.Definition)
    {
        _appSettings = appSettings;
        _logger = logger;

        OtherAppsOptions =
        [
            new(OtherApps.Remember, Strings.InputLanguagePerAppOtherAppsRemember),
            new(OtherApps.Keep, Strings.InputLanguagePerAppOtherAppsKeep),
            new(OtherApps.Use, Strings.InputLanguagePerAppOtherAppsUse),
        ];
        _selectedOtherApps = OtherAppsOptions.FirstOrDefault(option => option.Behavior == Settings.OtherApps);

        Apps = new AppListViewModel(
            () => [.. Settings.Apps.Select(rule => rule.App)],
            AddApp,
            index => SaveApps(Settings.WithoutApp(index)),
            new AppListChoice(
                () => InputMethods,
                index => OptionOf(Settings.Apps[index].InputMethod),
                (index, choice) => choice is InputMethodOption option && SaveApps(Settings.WithInputMethodAt(index, option.Id)),
                LanguageNameFormat,
                () => _inputMethodsRead ? Strings.InputLanguagePerAppNotInList : null));

        IsPerWindow = KeyboardLayout.IsPerWindow();
        _ = ReadInputMethodsAsync();
    }

    /// <summary>The user's input methods, in the order of Windows' input switcher; none until they're read.</summary>
    public IReadOnlyList<InputMethodOption> InputMethods { get; private set; } = [];

    /// <summary>Whether the user has fewer than two input methods, which the page says once they're read.</summary>
    public bool FewInputMethods => _inputMethodsRead && InputMethods.Count < 2;

    /// <summary>The listed apps, each with the input method it switches to.</summary>
    public AppListViewModel Apps { get; }

    public IReadOnlyList<OtherAppsOption> OtherAppsOptions { get; }

    public OtherAppsOption? SelectedOtherApps
    {
        get => _selectedOtherApps;
        set
        {
            if (value is null || !SetProperty(ref _selectedOtherApps, value))
            {
                return;
            }

            // Use one language starts with the first input method, as an added app does.
            var oneLanguage = value.Behavior == OtherApps.Use && !Settings.TryGetOtherAppsInputMethod(out _) && InputMethods.Count > 0
                ? InputMethods[0].Id.ToString()
                : Settings.OtherAppsInputMethod;
            Save(Settings with { OtherApps = value.Behavior, OtherAppsInputMethod = oneLanguage });
            OnPropertyChanged(nameof(UsesOneLanguage));
            OnPropertyChanged(nameof(SelectedOneLanguage));
        }
    }

    /// <summary>Whether other apps use one language, whose list the page then shows.</summary>
    public bool UsesOneLanguage => Settings.OtherApps == OtherApps.Use;

    public InputMethodOption? SelectedOneLanguage
    {
        get => OptionOf(Settings.OtherAppsInputMethod);
        set
        {
            if (value is not null && value != SelectedOneLanguage)
            {
                Save(Settings with { OtherAppsInputMethod = value.Id.ToString() });
                OnPropertyChanged();
            }
        }
    }

    public bool ShowNotice
    {
        get => Settings.ShowNotice;
        set => Change(Settings.ShowNotice, value, () => Settings with { ShowNotice = value });
    }

    /// <summary>Whether Windows' Let me use a different input method for each app window is on, as read when the page opened.</summary>
    public bool IsPerWindow { get; }

    public string WindowsSettingInfo => IsPerWindow ? Strings.InputLanguagePerAppWindowsSettingOn : Strings.InputLanguagePerAppWindowsSettingOff;

    /// <summary>Which apps can't be switched, with the host's rights now.</summary>
    public string AdministratorInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.InputLanguagePerAppAdministratorInfoOn
        : Strings.InputLanguagePerAppAdministratorInfo;

    private static string NameOf(InputMethod inputMethod) => inputMethod.LayoutName is { } layout
        ? string.Format(CultureInfo.CurrentCulture, InputMethodFormat, inputMethod.LanguageName, layout)
        : inputMethod.LanguageName;

    private InputMethodOption? OptionOf(string? identifier) =>
        InputMethodId.TryParse(identifier, out var id) ? InputMethods.FirstOrDefault(option => option.Id == id) : null;

    // An added app starts with the first input method; with none read, it can't be added.
    private bool AddApp(ListedApp app) => InputMethods.Count > 0 && SaveApps(Settings.WithApp(app, InputMethods[0].Id));

    private bool SaveApps(InputLanguagePerAppSettings settings)
    {
        if (settings == Settings)
        {
            return false;
        }

        Save(settings);
        return true;
    }

    private async Task ReadInputMethodsAsync()
    {
        IReadOnlyList<InputMethod> read;
        try
        {
            read = await Task.Run(PCCompanion.Platform.Windows.InputMethods.Read).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is COMException or Win32Exception)
        {
            LogInputMethodsUnread(_logger, exception);
            read = [];
        }

        InputMethods = [.. read.Select(inputMethod => new InputMethodOption(inputMethod.Id, NameOf(inputMethod)))];
        _inputMethodsRead = true;
        OnPropertyChanged(nameof(InputMethods));
        OnPropertyChanged(nameof(FewInputMethods));
        OnPropertyChanged(nameof(SelectedOneLanguage));
        Apps.Refresh();
    }

    [RelayCommand]
    private void OpenLanguageSettings()
    {
        if (!ShellActions.OpenLanguageSettings())
        {
            LogCantOpenSettings(_logger);
        }
    }

    [RelayCommand]
    private void OpenTypingSettings()
    {
        if (!ShellActions.OpenTypingSettings())
        {
            LogCantOpenSettings(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open its Settings page for Input Language per App.")]
    private static partial void LogCantOpenSettings(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The page couldn't read the user's input methods.")]
    private static partial void LogInputMethodsUnread(ILogger logger, Exception exception);
}
