using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>Reads End App's windows and processes from Windows. None of it waits for the windows' programs.</summary>
internal sealed class WindowsEndAppSystem : IEndAppSystem
{
    private const string StandInClass = "Ghost";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    // The dialog's icon, at 100% scale.
    private const int IconSize = 32;

    public uint OwnProcessId { get; } = (uint)Environment.ProcessId;

    public string WindowsFolder { get; } = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    public nint ForegroundWindow => Foreground.Window;

    public IReadOnlyList<EndAppWindow> ListWindows() =>
        TopLevelWindows.List().Select(window =>
        {
            var bounds = TopLevelWindows.Bounds(window.Handle) ?? Rectangle.Empty;
            return new EndAppWindow(
                window.Handle,
                window.ProcessId,
                bounds,
                TopLevelWindows.VisibleBounds(window.Handle) ?? bounds,
                window.IsCloaked,
                TopLevelWindows.LetsClicksThrough(window.Handle),
                TopLevelWindows.MayHaveTaskbarButton(window.Handle),
                TopLevelWindows.ClassName(window.Handle) == StandInClass);
        }).ToList();

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window) is var owner && owner != 0 ? owner : window;

    public string ClassName(nint window) => TopLevelWindows.ClassName(window);

    public Rectangle WindowRect(nint window) => TopLevelWindows.Bounds(window) ?? Rectangle.Empty;

    public uint ProcessIdOf(nint window) => TopLevelWindows.ProcessIdOf(window);

    public bool IsNotResponding(nint window) => TopLevelWindows.IsNotResponding(window);

    public IReadOnlyList<nint> NotRespondingWindowsAt(Rectangle bounds) =>
        TopLevelWindows.ListAll()
            .Where(window => TopLevelWindows.IsNotResponding(window) && TopLevelWindows.Bounds(window) == bounds && TopLevelWindows.ClassName(window) != StandInClass)
            .ToList();

    public nint CoreWindowOf(nint frame) => TopLevelWindows.FindChild(frame, CoreWindowClass);

    public int DpiOf(nint window) => (int)TopLevelWindows.Dpi(window) is > 0 and var dpi ? dpi : 96;

    public Rectangle? WorkAreaOf(nint window) => Displays.Of(window)?.WorkArea;

    public string? ProgramPath(uint processId) => AppIdentity.ProgramPath(processId);

    public bool? IsElevated(uint processId) => Elevation.IsProcessElevated(processId);

    public IEndAppProcess? Open(uint processId, out int errorCode) =>
        HeldProcess.Open(processId, out errorCode) is { } process ? new Process(process) : null;

    public IReadOnlyList<ProcessEntry> ListProcesses() => ProcessList.List();

    public DateTime? StartTime(uint processId) => ProcessList.StartTime(processId);

    public (string Name, AppIcon? Icon) AppNameAndIcon(nint window, uint processId)
    {
        var appId = AppIdentity.OfWindow(window) ?? AppIdentity.OfPackagedProcess(processId);
        var program = AppIdentity.ProgramPath(processId);
        var size = (int)Math.Round(IconSize * Displays.MainDisplayDpi() / 96.0);
        var name = (appId is null ? null : AppDisplay.NameOfAppId(appId)) ?? (program is null ? string.Empty : AppDisplay.NameOfProgram(program));
        var icon = (appId is null ? null : AppDisplay.IconOfAppId(appId, size)) ?? (program is null ? null : AppDisplay.IconOfProgram(program, size));
        return (name, icon);
    }

    private sealed class Process(HeldProcess process) : IEndAppProcess
    {
        public uint Id => process.Id;

        public DateTime? StartTime => process.StartTime;

        public string? ProgramPath => process.ProgramPath;

        public bool? IsCritical => process.IsCritical;

        public bool HasExited => process.HasExited;

        public bool EndProcess(out int errorCode) => process.End(out errorCode);

        public bool WaitForExit(TimeSpan timeout) => process.WaitForExit(timeout);

        public void Dispose() => process.Dispose();
    }
}
