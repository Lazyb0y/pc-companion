using System.ComponentModel;
using System.Text.Json;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.NetworkProfiles;

/// <summary>
/// What Network Profiles starts with when it's first turned on: Original settings, made from
/// Windows' current proxy settings, so the user always has a way back.
/// </summary>
public static class NetworkProfilesFirstSettings
{
    /// <summary>The section, without profiles when Windows refuses to read its settings.</summary>
    public static JsonElement Create()
    {
        NetworkProfile[] profiles;
        try
        {
            profiles = [new NetworkProfile { Name = Strings.NetworkProfilesOriginalSettings, Proxy = ProxyMatching.FromWindows(InternetProxy.Read()) }];
        }
        catch (Win32Exception)
        {
            profiles = [];
        }

        return new NetworkProfilesSettings { Profiles = profiles }.Write();
    }
}
