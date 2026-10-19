using PCCompanion.Core;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Opens the latest release's page for a host running as administrator, which starts the host with
/// <see cref="HostProcess.OpenReleasePageArgument"/> without admin rights, so the browser doesn't
/// run with them either.
/// </summary>
internal static class ReleasePageCommand
{
    public static void Run()
    {
        // Started with admin rights, for example by hand, it does nothing.
        if (Elevation.IsUacElevated)
        {
            return;
        }

        ShellActions.OpenWebPage(GitHubReleaseSource.ReleasePageAddress);
    }
}
