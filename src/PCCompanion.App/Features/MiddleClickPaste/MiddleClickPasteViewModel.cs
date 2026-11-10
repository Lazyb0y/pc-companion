using PCCompanion.App.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Core.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;

namespace PCCompanion.App.Features.MiddleClickPaste;

/// <summary>
/// The Middle-Click Paste page. Every change is saved right away.
/// </summary>
public sealed class MiddleClickPasteViewModel : FeatureViewModel<MiddleClickPasteSettings>
{
    private readonly AppSettings _appSettings;

    public MiddleClickPasteViewModel(FeatureSettingsEditor editor, AppSettings appSettings)
        : base(editor, MiddleClickPasteFeature.Definition)
    {
        _appSettings = appSettings;
        ExcludedApps = new AppListViewModel(
            () => Settings.ExcludedApps,
            app => SaveExcludedApps(Settings.WithExcludedApp(app)),
            index => SaveExcludedApps(Settings.WithoutExcludedApp(index)));
    }

    /// <summary>The apps that keep their middle-click.</summary>
    public AppListViewModel ExcludedApps { get; }

    /// <summary>Which apps' middle-clicks are left alone, with the host's rights now.</summary>
    public string AdminInfo => HostLauncher.RunsAsAdministrator(_appSettings.General)
        ? Strings.MiddleClickPasteAdminInfoOn
        : Strings.MiddleClickPasteAdminInfo;

    private bool SaveExcludedApps(MiddleClickPasteSettings settings)
    {
        if (settings == Settings)
        {
            return false;
        }

        Save(settings);
        return true;
    }
}
