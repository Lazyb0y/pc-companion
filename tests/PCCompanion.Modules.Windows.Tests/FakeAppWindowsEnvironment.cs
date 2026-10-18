using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// A desktop of made-up windows, front to back, that moves a window to the front when it's
/// activated and to the back when it's sent there, as Windows does.
/// </summary>
internal sealed class FakeAppWindowsEnvironment : IAppWindowsEnvironment
{
    public List<TopLevelWindow> Windows { get; } = [];

    public Dictionary<nint, nint> Owners { get; } = [];

    public Dictionary<nint, string> WindowIds { get; } = [];

    public Dictionary<uint, string> Packages { get; } = [];

    public Dictionary<uint, string> Paths { get; } = [];

    public List<string> Calls { get; } = [];

    public int ProcessReads { get; private set; }

    public bool RefusesActivation { get; set; }

    public nint ForegroundWindow { get; set; }

    public static TopLevelWindow Window(
        nint handle,
        uint processId,
        bool minimized = false,
        bool cloaked = false,
        bool tool = false,
        bool appWindow = false,
        bool noActivate = false,
        bool topmost = false,
        bool owned = false,
        bool desktop = false) =>
        new(handle, processId, minimized, cloaked, tool, appWindow, noActivate, topmost, owned, desktop);

    public IReadOnlyList<TopLevelWindow> ListWindows() => Windows.ToList();

    public nint RootOwner(nint window) => Owners.TryGetValue(window, out var owner) ? owner : window;

    public string? WindowAppId(nint window) => WindowIds.GetValueOrDefault(window);

    public string? PackageAppId(uint processId)
    {
        ProcessReads++;
        return Packages.GetValueOrDefault(processId);
    }

    public string? ProgramPath(uint processId) => Paths.GetValueOrDefault(processId);

    public bool Activate(nint window)
    {
        Calls.Add($"activate {window}");
        if (RefusesActivation)
        {
            return false;
        }

        var activated = Windows.Single(listed => listed.Handle == window);
        Windows.Remove(activated);
        Windows.Insert(0, activated with { IsMinimized = false });
        ForegroundWindow = window;
        return true;
    }

    public bool SendToBack(nint window)
    {
        Calls.Add($"send {window} to the back");
        var sent = Windows.Single(listed => listed.Handle == window);
        Windows.Remove(sent);
        Windows.Add(sent);
        return true;
    }

    /// <summary>The order of <paramref name="handles"/>, front to back.</summary>
    public nint[] Order(params nint[] handles) => Windows.Select(listed => listed.Handle).Where(handles.Contains).ToArray();
}
