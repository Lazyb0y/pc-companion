using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// Reads the windows and their apps from Windows, and switches them there.
/// </summary>
internal sealed class WindowsAppWindowsEnvironment : IAppWindowsEnvironment
{
    public nint ForegroundWindow => Foreground.Window;

    public IReadOnlyList<TopLevelWindow> ListWindows() => TopLevelWindows.List();

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window);

    public string? WindowAppId(nint window) => AppIdentity.OfWindow(window);

    public string? PackageAppId(uint processId) => AppIdentity.OfPackagedProcess(processId);

    public string? ProgramPath(uint processId) => AppIdentity.ProgramPath(processId);

    public bool Activate(nint window) => TopLevelWindows.Activate(window);

    public bool SendToBack(nint window) => TopLevelWindows.SendToBack(window);
}
