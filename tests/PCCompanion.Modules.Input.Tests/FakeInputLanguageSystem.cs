using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.Tests;

/// <summary>
/// Made-up windows and threads with input methods, which switch as Windows and apps would switch
/// them, and a record of what Input Language per App asked. Unless <see cref="PerWindow"/>, a
/// window brought to the front with <see cref="BringToFront"/> gets the language in use, as with
/// Windows' setting off.
/// </summary>
internal sealed class FakeInputLanguageSystem : IInputLanguageSystem
{
    public static readonly InputMethod UnitedStates = new(new InputMethodId(0x0409, 0x00000409), 0x04090409, "English (United States)", "US");
    public static readonly InputMethod German = new(new InputMethodId(0x0407, 0x00000407), 0x04070407, "German (Germany)", "German");
    public static readonly InputMethod Japanese = new(new InputMethodId(0x0411, null), 0x04110411, "Japanese", null);

    private readonly List<Action<IReadOnlyList<InputMethod>>> _reads = [];
    private readonly List<(nint Window, uint ProcessId, Action<uint?> Found)> _lookups = [];
    private (uint Thread, nint Layout)? _late;

    public List<FakeLanguageWindow> Windows { get; } = [];

    /// <summary>The handle of each thread's input method.</summary>
    public Dictionary<uint, nint> Layouts { get; } = [];

    /// <summary>The user's input methods, in the order of Windows' input switcher.</summary>
    public List<InputMethod> InputMethods { get; } = [UnitedStates, German, Japanese];

    /// <summary>Each request: the window and the handle asked for.</summary>
    public List<(nint Window, nint Layout)> Requests { get; } = [];

    public List<InputMethod> Notices { get; } = [];

    /// <summary>Whether a read of the input methods finishes at once; otherwise the test calls <see cref="FinishReads"/>.</summary>
    public bool ReadsAtOnce { get; set; } = true;

    public int Reads { get; private set; }

    public bool ReadsEverySecond { get; private set; }

    /// <summary>Windows' setting to keep an input method for each app window.</summary>
    public bool PerWindow { get; set; }

    /// <summary>
    /// Whether a window brought to the front gets the language in use only after the foreground
    /// event, as Windows sometimes gives it, but before a request from it is handled.
    /// </summary>
    public bool ChangesLate { get; set; }

    public bool IsPerWindow => PerWindow;

    public nint ForegroundWindow { get; set; }

    public int PendingLookups => _lookups.Count;

    /// <summary>Adds a window of an app named after its program, on a thread of its own with <paramref name="layout"/>.</summary>
    public FakeLanguageWindow Add(nint handle, uint processId, uint thread, string? program, nint layout = 0x04090409)
    {
        var window = new FakeLanguageWindow { Handle = handle, ProcessId = processId, Thread = thread, Program = program };
        Windows.Add(window);
        Layouts[thread] = layout;
        return window;
    }

    /// <summary>
    /// Adds a console window, which reports the console program's process and thread, whose input
    /// method is on the console host's thread.
    /// </summary>
    public FakeLanguageWindow AddConsole(nint handle, uint processId, uint thread, uint hostThread, nint layout = 0x04090409)
    {
        var window = new FakeLanguageWindow { Handle = handle, ProcessId = processId, Thread = thread, Program = @"C:\Windows\System32\cmd.exe", Console = true, HostThread = hostThread };
        Windows.Add(window);
        Layouts[hostThread] = layout;
        return window;
    }

    public FakeLanguageWindow this[nint handle] => Windows.Single(window => window.Handle == handle);

    /// <summary>The handle of the input method <paramref name="window"/> types with.</summary>
    public nint LayoutOfWindow(nint window) => Layouts[ThreadOf(this[window])];

    /// <summary>
    /// Brings the window to the front; unless <see cref="PerWindow"/>, it gets the language in use
    /// first, the one of the window in front before.
    /// </summary>
    public void BringToFront(nint window)
    {
        _late = null;
        if (!PerWindow && TypingWindow(ForegroundWindow) is { } before && TypingWindow(window) is { } after && Layouts.TryGetValue(ThreadOf(before), out var inUse))
        {
            if (ChangesLate)
            {
                _late = (ThreadOf(after), inUse);
            }
            else
            {
                Layouts[ThreadOf(after)] = inUse;
            }
        }

        ForegroundWindow = window;
    }

    /// <summary>Gives the window brought to the front the language in use, with <see cref="ChangesLate"/>.</summary>
    public void ApplyLateChange()
    {
        if (_late is { } late)
        {
            Layouts[late.Thread] = late.Layout;
            _late = null;
        }
    }

    /// <summary>Finishes the reads of the input methods with <see cref="InputMethods"/>.</summary>
    public void FinishReads()
    {
        var reads = _reads.ToList();
        _reads.Clear();
        foreach (var read in reads)
        {
            read([.. InputMethods]);
        }
    }

    /// <summary>Finishes the lookups of console hosts with each console's host thread; a host thread of 0 isn't found.</summary>
    public void FinishLookups()
    {
        var lookups = _lookups.ToList();
        _lookups.Clear();
        foreach (var (window, processId, found) in lookups)
        {
            found(Find(window) is { Console: true, HostThread: not 0 } console && console.ProcessId == processId ? console.HostThread : null);
        }
    }

    public nint RootOwner(nint window)
    {
        while (Find(window) is { Owner: not 0 } listed)
        {
            window = listed.Owner;
        }

        return window;
    }

    public bool MayHaveTaskbarButton(nint window) => Find(window) is { TaskbarButton: true, Owner: 0 };

    public uint ProcessIdOf(nint window) => Find(window)?.ProcessId ?? 0;

    public bool Exists(nint window) => Find(window) is not null;

    public InputFocus? FocusInFront() =>
        TypingWindow(ForegroundWindow) is { } window ? new InputFocus(window.Handle, window.Thread, window.ProcessId) : null;

    public nint? LayoutOf(uint thread) => Layouts.TryGetValue(thread, out var layout) && layout != 0 ? layout : null;

    public IReadOnlyList<nint> LayoutHandles() => [.. InputMethods.Select(method => method.Handle)];

    public LayoutRequest RequestSwitch(nint window, nint layout)
    {
        // Windows' own change comes first, as the spike showed.
        ApplyLateChange();
        Requests.Add((window, layout));
        if (Find(window) is not { } listed)
        {
            return LayoutRequest.Failed;
        }

        if (listed.Answer == LayoutRequest.Posted && listed.TakesRequests)
        {
            Layouts[ThreadOf(listed)] = layout;
        }

        return listed.Answer;
    }

    public bool IsConsole(nint window) => Find(window) is { Console: true };

    public void ReadInputMethods(Action<IReadOnlyList<InputMethod>> read)
    {
        Reads++;
        _reads.Add(read);
        if (ReadsAtOnce)
        {
            FinishReads();
        }
    }

    public void FindConsoleHost(nint window, uint processId, Action<uint?> found) => _lookups.Add((window, processId, found));

    public void ReadEverySecond(bool read) => ReadsEverySecond = read;

    public void ShowNotice(InputMethod inputMethod) => Notices.Add(inputMethod);

    public string? WindowAppId(nint window) => Find(window)?.AppId;

    public string? PackageAppId(uint processId) => null;

    public string? ProgramPath(uint processId) => Windows.FirstOrDefault(window => window.ProcessId == processId)?.Program;

    // A console window's input method is on its console host's thread.
    private static uint ThreadOf(FakeLanguageWindow window) => window.Console ? window.HostThread : window.Thread;

    private FakeLanguageWindow? Find(nint window) => Windows.FirstOrDefault(listed => listed.Handle == window && !listed.Closed);

    private FakeLanguageWindow? TypingWindow(nint window) => Find(window) is { } front ? Find(front.Focus == 0 ? front.Handle : front.Focus) : null;
}

/// <summary>A made-up window and its app, for <see cref="FakeInputLanguageSystem"/>.</summary>
internal sealed class FakeLanguageWindow
{
    public nint Handle { get; init; }

    public uint ProcessId { get; init; }

    /// <summary>The window's thread; for a console, the console program's, which has no input method.</summary>
    public uint Thread { get; init; }

    public string? Program { get; init; }

    public string? AppId { get; init; }

    public nint Owner { get; init; }

    /// <summary>The window that typing goes to while this one is in front, as a Store app's in its frame; 0 for this one.</summary>
    public nint Focus { get; init; }

    public bool TaskbarButton { get; set; } = true;

    /// <summary>A console window, whose input method is on <see cref="HostThread"/>.</summary>
    public bool Console { get; init; }

    public uint HostThread { get; init; }

    public bool Closed { get; set; }

    /// <summary>What a request to the window returns.</summary>
    public LayoutRequest Answer { get; set; } = LayoutRequest.Posted;

    /// <summary>Whether the app switches when asked.</summary>
    public bool TakesRequests { get; set; } = true;
}
