using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows that minimize, hide and show as Windows would make them, and a record of what
/// the controller did to them. The apps' names and window icons come when a test hands them over,
/// unless <see cref="ReadsLooksAtOnce"/> is set.
/// </summary>
internal sealed class FakeTrayWindowsSystem : ITrayWindowsSystem
{
    public const int Size = 16;

    public static readonly uint[] GlyphPixels = Pixels(0xFF000001);

    public Dictionary<nint, FakeWindow> Windows { get; } = [];

    public List<string> Calls { get; } = [];

    public HashSet<uint> Watched { get; } = [];

    public int Warnings { get; private set; }

    public bool ReadsLooksAtOnce { get; set; } = true;

    public List<(AppOfWindow App, Action<TrayLooks> Read)> LooksRequests { get; } = [];

    public List<(nint Window, Action<uint[]?> Answered)> IconRequests { get; } = [];

    public nint ForegroundWindow { get; set; }

    public bool IsShiftHeld { get; set; }

    public bool IsWindowsKeyHeld { get; set; }

    public static uint[] Pixels(uint pixel) => Enumerable.Repeat(pixel, Size * Size).ToArray();

    public FakeWindow Add(nint handle, uint processId = 10, string title = "Title", string? program = @"C:\Apps\App.exe")
    {
        var window = new FakeWindow { Handle = handle, ProcessId = processId, Title = title, Program = program };
        Windows[handle] = window;
        return window;
    }

    public nint RootOwner(nint window)
    {
        while (Windows.TryGetValue(window, out var listed) && listed.Owner != 0)
        {
            window = listed.Owner;
        }

        return window;
    }

    public bool MayHaveTaskbarButton(nint window) => Windows.TryGetValue(window, out var listed) && listed.OnTaskbar && listed.Owner == 0;

    public bool Exists(nint window) => Windows.ContainsKey(window);

    public bool IsVisible(nint window) => Windows.TryGetValue(window, out var listed) && listed.Visible;

    public bool IsMinimized(nint window) => Windows.TryGetValue(window, out var listed) && listed.Minimized;

    public bool IsEnabled(nint window) => Windows.TryGetValue(window, out var listed) && listed.Enabled;

    public string ClassName(nint window) => Windows.TryGetValue(window, out var listed) ? listed.ClassName : string.Empty;

    public string Title(nint window) => Windows.TryGetValue(window, out var listed) ? listed.Title : string.Empty;

    public uint ProcessIdOf(nint window) => Windows.TryGetValue(window, out var listed) ? listed.ProcessId : 0;

    public string? WindowAppId(nint window) => Windows.TryGetValue(window, out var listed) ? listed.AppId : null;

    public string? PackageAppId(uint processId) => null;

    public string? ProgramPath(uint processId) => Windows.Values.FirstOrDefault(window => window.ProcessId == processId)?.Program;

    public bool Mark(nint window)
    {
        Calls.Add($"mark {window}");
        if (!Windows.TryGetValue(window, out var listed) || listed.Refuses)
        {
            return false;
        }

        listed.Marked = true;
        return true;
    }

    public void Unmark(nint window)
    {
        Calls.Add($"unmark {window}");
        if (Windows.TryGetValue(window, out var listed))
        {
            listed.Marked = false;
        }
    }

    public IReadOnlyList<nint> FindMarked() => [.. Windows.Values.Where(window => window.Marked).Select(window => window.Handle)];

    public bool Minimize(nint window) => Change(window, "minimize", listed => listed.Minimized = !listed.Unresponsive);

    public bool Hide(nint window) => Change(window, "hide", listed => listed.Visible = false, listed => listed.RefusesHide);

    public bool ShowMinimized(nint window) => Change(window, "show minimized", listed => (listed.Visible, listed.Minimized) = (true, true));

    public bool Restore(nint window) => Change(window, "restore", listed => (listed.Visible, listed.Minimized) = (!listed.ShowsLate, false));

    public bool Activate(nint window) => Change(window, "activate", _ => { });

    public void PlayWarning() => Warnings++;

    public void Watch(uint processId)
    {
        Calls.Add($"watch {processId}");
        Watched.Add(processId);
    }

    public void Unwatch(uint processId)
    {
        Calls.Add($"unwatch {processId}");
        Watched.Remove(processId);
    }

    public int IconSize() => Size;

    public uint[]? ClassIcon(nint window, int size) => Windows.TryGetValue(window, out var listed) ? listed.ClassIcon : null;

    public bool RequestIcon(nint window, int size, Action<uint[]?> answered)
    {
        IconRequests.Add((window, answered));
        return true;
    }

    public uint[] Glyph(int size) => GlyphPixels;

    public void ReadLooks(AppOfWindow app, int size, Action<TrayLooks> read)
    {
        if (ReadsLooksAtOnce)
        {
            read(LooksOf(app));
        }
        else
        {
            LooksRequests.Add((app, read));
        }
    }

    /// <summary>Hands over the apps' names asked for so far.</summary>
    public void FinishReadingLooks()
    {
        var requests = LooksRequests.ToList();
        LooksRequests.Clear();
        foreach (var (app, read) in requests)
        {
            read(LooksOf(app));
        }
    }

    private static TrayLooks LooksOf(AppOfWindow app) => new(System.IO.Path.GetFileNameWithoutExtension(app.Program) ?? "App", null);

    private bool Change(nint window, string call, Action<FakeWindow> change, Func<FakeWindow, bool>? refuses = null)
    {
        Calls.Add($"{call} {window}");
        if (!Windows.TryGetValue(window, out var listed) || listed.Refuses || (refuses?.Invoke(listed) ?? false))
        {
            return false;
        }

        change(listed);
        return true;
    }
}

internal sealed class FakeWindow
{
    public nint Handle { get; init; }

    public uint ProcessId { get; init; }

    public string Title { get; set; } = string.Empty;

    public string? Program { get; init; }

    public string? AppId { get; init; }

    public string ClassName { get; init; } = "AppWindow";

    public nint Owner { get; init; }

    public bool OnTaskbar { get; init; } = true;

    public bool Visible { get; set; } = true;

    public bool Minimized { get; set; }

    public bool Enabled { get; set; } = true;

    public bool Marked { get; set; }

    /// <summary>Windows refuses everything for it, as for an app running as administrator.</summary>
    public bool Refuses { get; init; }

    /// <summary>Windows marks it but refuses to hide it.</summary>
    public bool RefusesHide { get; init; }

    /// <summary>Its program doesn't handle a minimize yet, as one that stopped responding doesn't.</summary>
    public bool Unresponsive { get; init; }

    /// <summary>It shows only a while after it's restored, as a program that's slow to respond does.</summary>
    public bool ShowsLate { get; init; }

    public uint[]? ClassIcon { get; init; }
}

/// <summary>The notification icons, as the controller last showed them.</summary>
internal sealed class FakeTrayIcons : ITrayIcons
{
    public Dictionary<uint, (uint[] Pixels, string Name, string ToolTip)> Icons { get; } = [];

    public int Shows { get; private set; }

    public bool Show(uint id, uint[] pixels, int size, string name, string toolTip)
    {
        Shows++;

        // Windows keeps the name an icon was added with.
        var keptName = Icons.TryGetValue(id, out var shown) ? shown.Name : name;
        Icons[id] = (pixels, keptName, toolTip);
        return true;
    }

    public void Remove(uint id) => Icons.Remove(id);
}
