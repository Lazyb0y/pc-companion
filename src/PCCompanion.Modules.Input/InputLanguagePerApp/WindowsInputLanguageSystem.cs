using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// Reads what Input Language per App needs from Windows and asks apps to switch, on the host's UI
/// thread; the feature runs the slower reads off it, the once-a-second read and the notice.
/// </summary>
internal sealed class WindowsInputLanguageSystem(InputLanguagePerAppFeature feature) : IInputLanguageSystem
{
    public nint ForegroundWindow => Foreground.Window;

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window);

    public bool MayHaveTaskbarButton(nint window) => TopLevelWindows.MayHaveTaskbarButton(window);

    public uint ProcessIdOf(nint window) => TopLevelWindows.ProcessIdOf(window);

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public InputFocus? FocusInFront() => KeyboardLayout.FocusInFront();

    public nint? LayoutOf(uint thread) => KeyboardLayout.OfThread(thread);

    public IReadOnlyList<nint> LayoutHandles() => KeyboardLayout.Installed();

    public LayoutRequest RequestSwitch(nint window, nint layout) => KeyboardLayout.RequestSwitch(window, layout);

    public bool IsConsole(nint window) => ConsoleWindows.IsConsole(window);

    public void ReadInputMethods(Action<IReadOnlyList<InputMethod>> read) => feature.OffThread(InputMethods.Read, read, []);

    public void FindConsoleHost(nint window, uint processId, Action<uint?> found) =>
        feature.OffThread(() => ConsoleWindows.HostThreadOf(window, processId), found, null);

    public void ReadEverySecond(bool read) => feature.ReadEverySecond(read);

    public void ShowNotice(InputMethod inputMethod) => feature.ShowNotice(inputMethod);

    public string? WindowAppId(nint window) => AppIdentity.OfWindow(window);

    public string? PackageAppId(uint processId) => AppIdentity.OfPackagedProcess(processId);

    public string? ProgramPath(uint processId) => AppIdentity.ProgramPath(processId);
}
