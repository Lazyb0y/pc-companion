using System;
using System.Threading.Tasks;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>
/// Asks the host to switch to a profile through its pipe, off the UI thread, so the switch gets
/// the host's question and revert. The host serves the pipe while Display Profiles is on.
/// </summary>
public static class DisplayProfilesHost
{
    // The switch takes well under a second; the host gives up after 10 seconds.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The host's answer, or <see langword="null"/> when it doesn't answer, as while Display
    /// Profiles is off. Call it on the UI thread: it first lets the host bring its question to
    /// the front, which only the app in front may.
    /// </summary>
    public static Task<DisplayProfilesAnswer?> AskAsync(DisplayProfilesRequest request)
    {
        MessageWindow.AllowOwnerToComeToFront(WindowsDisplayProfilesSystem.HostWindowClassName);
        return Task.Run(() => DisplayProfilesPipe.AskAsync(DisplayProfilesPipe.Name, request, Timeout));
    }
}
