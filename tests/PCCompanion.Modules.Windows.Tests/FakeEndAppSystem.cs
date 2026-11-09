using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.EndApp;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows, front to back, and processes, with a record of what End App opened and ended.
/// Nothing here touches a real window or process.
/// </summary>
internal sealed class FakeEndAppSystem : IEndAppSystem
{
    public const uint Host = 4;

    public static readonly DateTime Started = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>The visible windows, front to back.</summary>
    public List<EndAppWindow> Windows { get; } = [];

    public Dictionary<nint, string> Classes { get; } = [];

    public Dictionary<nint, nint> Owners { get; } = [];

    public HashSet<nint> NotResponding { get; } = [];

    /// <summary>Windows that are hidden but exist, such as one a stand-in stands for.</summary>
    public List<(nint Handle, uint ProcessId, Rectangle Bounds)> HiddenWindows { get; } = [];

    public Dictionary<nint, nint> CoreWindows { get; } = [];

    public Dictionary<uint, FakeProcess> Processes { get; } = [];

    /// <summary>Processes Windows doesn't let End App open.</summary>
    public HashSet<uint> Refused { get; } = [];

    public HashSet<uint> Elevated { get; } = [];

    public List<uint> Opened { get; } = [];

    public List<uint> Ended { get; } = [];

    public uint OwnProcessId => Host;

    public string WindowsFolder => @"C:\Windows";

    public nint ForegroundWindow { get; set; }

    public int Dpi { get; set; } = 96;

    /// <summary>Adds a window behind the others, of a process that's added with it when it's new.</summary>
    public EndAppWindow AddWindow(nint handle, uint processId, Rectangle bounds, string program = @"C:\Apps\App.exe", bool taskbar = true, string className = "AppWindow")
    {
        var window = new EndAppWindow(handle, processId, bounds, bounds, IsCloaked: false, IsClickThrough: false, taskbar, className == "Ghost");
        Windows.Add(window);
        Classes[handle] = className;
        AddProcess(processId, program);
        return window;
    }

    public FakeProcess AddProcess(uint processId, string program, uint parent = 1, DateTime? start = null)
    {
        if (!Processes.TryGetValue(processId, out var process))
        {
            process = new FakeProcess(this, processId, parent, program, start ?? Started);
            Processes[processId] = process;
        }

        return process;
    }

    public IReadOnlyList<EndAppWindow> ListWindows() => Windows.ToList();

    public nint RootOwner(nint window) => Owners.TryGetValue(window, out var owner) ? RootOwner(owner) : window;

    public string ClassName(nint window) => Classes.TryGetValue(window, out var name) ? name : string.Empty;

    public Rectangle WindowRect(nint window) =>
        Windows.FirstOrDefault(w => w.Handle == window)?.Bounds ?? HiddenWindows.FirstOrDefault(w => w.Handle == window).Bounds;

    public uint ProcessIdOf(nint window) =>
        Windows.FirstOrDefault(w => w.Handle == window)?.ProcessId ?? HiddenWindows.FirstOrDefault(w => w.Handle == window).ProcessId;

    public bool IsNotResponding(nint window) => NotResponding.Contains(window);

    public IReadOnlyList<nint> NotRespondingWindowsAt(Rectangle bounds) =>
        Windows.Where(w => !w.IsStandIn && w.Bounds == bounds).Select(w => w.Handle)
            .Concat(HiddenWindows.Where(w => w.Bounds == bounds).Select(w => w.Handle))
            .Where(NotResponding.Contains)
            .ToList();

    public nint CoreWindowOf(nint frame) => CoreWindows.TryGetValue(frame, out var core) ? core : 0;

    public int DpiOf(nint window) => Dpi;

    public Rectangle? WorkAreaOf(nint window) => new Rectangle(0, 0, 1920, 1040);

    public string? ProgramPath(uint processId) => Processes.TryGetValue(processId, out var process) && !process.HasExited ? process.ProgramPath : null;

    public bool? IsElevated(uint processId) => Processes.ContainsKey(processId) ? Elevated.Contains(processId) : null;

    public IEndAppProcess? Open(uint processId, out int errorCode)
    {
        Opened.Add(processId);
        if (Refused.Contains(processId) || !Processes.TryGetValue(processId, out var process) || process.HasExited)
        {
            errorCode = 5;
            return null;
        }

        errorCode = 0;
        return process;
    }

    public IReadOnlyList<ProcessEntry> ListProcesses() =>
        Processes.Values.Where(p => !p.HasExited).Select(p => new ProcessEntry(p.Id, p.ParentId)).ToList();

    public DateTime? StartTime(uint processId) => Processes.TryGetValue(processId, out var process) && !process.HasExited ? process.StartTime : null;

    public (string Name, AppIcon? Icon) AppNameAndIcon(nint window, uint processId) =>
        (System.IO.Path.GetFileNameWithoutExtension(ProgramPath(processId)) ?? "App", null);

    internal sealed class FakeProcess(FakeEndAppSystem system, uint id, uint parentId, string programPath, DateTime startTime) : IEndAppProcess
    {
        public uint Id { get; } = id;

        public uint ParentId { get; } = parentId;

        public DateTime? StartTime { get; } = startTime;

        public string? ProgramPath { get; } = programPath;

        public bool? IsCritical { get; set; } = false;

        public bool HasExited { get; set; }

        /// <summary>Whether ending it leaves it running, as for a process Windows can't end in time.</summary>
        public bool Lingers { get; set; }

        public int Disposals { get; private set; }

        public bool EndProcess(out int errorCode)
        {
            system.Ended.Add(Id);
            errorCode = 0;
            HasExited = !Lingers;
            return true;
        }

        public bool WaitForExit(TimeSpan timeout) => HasExited;

        public void Dispose() => Disposals++;
    }
}
