using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using PCCompanion.Localization;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>
/// What Display Profiles starts with when it's first turned on: Original setup, made from the
/// current setup, so the user always has a way back.
/// </summary>
public static class DisplayProfilesFirstSettings
{
    /// <summary>
    /// The section, without profiles in a remote session or when Windows refuses to read the
    /// displays. The setup is read on the thread pool, about 100 ms the first time, mostly for the
    /// first EDID, while the calling thread waits.
    /// </summary>
    public static JsonElement Create()
    {
        DisplayProfile[] profiles = [];
        if (!DisplayConfiguration.IsRemoteSession)
        {
            try
            {
                var system = new WindowsDisplayProfilesSystem(DisplayProfilesViewModel.WindowClassName);
                var current = Task.Run(system.Read).GetAwaiter().GetResult();
                profiles = [current.ToProfile(Strings.DisplayProfilesOriginalSetup)];
            }
            catch (Win32Exception)
            {
                profiles = [];
            }
        }

        return new DisplayProfilesSettings { Profiles = profiles }.Write();
    }
}
