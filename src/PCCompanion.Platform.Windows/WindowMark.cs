using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A mark this process puts on another program's window, such as one it hid, so that a later
/// process finds the window again: a window property, which Windows keeps with the window until
/// it's removed or the window is destroyed, also after this process ends. Windows refuses it on a
/// window of a program with more rights, such as an app running as administrator while this
/// process doesn't. A mark can hold a small value, such as what the window had before this process
/// changed it. Setting, reading and removing it send the window no message.
/// </summary>
public sealed class WindowMark
{
    // The property is set by the name's atom in the session's global atom table rather than by the
    // name, so that a window destroyed while marked leaves no reference to the name behind. The
    // atom is never deleted, so a later process finds the marks a process left.
    private readonly ushort _atom;

    /// <param name="name">The property's name, unique to the app, such as <c>PCCompanion.MinimizeToTray</c>.</param>
    /// <exception cref="Win32Exception">Windows couldn't add the name to the atom table.</exception>
    public WindowMark(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        _atom = PInvoke.GlobalAddAtom(name);
        if (_atom == 0)
        {
            throw new Win32Exception();
        }
    }

    private unsafe PCWSTR Name => new((char*)_atom);

    /// <summary>Marks <paramref name="window"/>. Returns false when Windows refused.</summary>
    public bool Set(nint window) => Set(window, 1);

    /// <summary>
    /// Marks <paramref name="window"/> with <paramref name="value"/>, which a later process reads
    /// with <see cref="Value"/>; marking it again replaces the value. Returns false when Windows
    /// refused.
    /// </summary>
    /// <param name="window">The window to mark.</param>
    /// <param name="value">Anything but 0, which a window without the mark reads as.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is 0.</exception>
    public bool Set(nint window, nint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);

        return PInvoke.SetProp(new HWND(window), Name, new HANDLE(value));
    }

    /// <summary>The value <paramref name="window"/>'s mark holds, or 0 when it has no mark.</summary>
    public nint Value(nint window) => PInvoke.GetProp(new HWND(window), Name);

    /// <summary>Whether <paramref name="window"/> carries the mark.</summary>
    public bool IsOn(nint window) => Value(window) != 0;

    /// <summary>Removes the mark from <paramref name="window"/>, if it has it.</summary>
    public void Remove(nint window) => PInvoke.RemoveProp(new HWND(window), Name);

    /// <summary>The top-level windows, shown or hidden, that carry the mark.</summary>
    public IReadOnlyList<nint> FindMarked()
    {
        var marked = new List<nint>();
        PInvoke.EnumWindows((window, _) =>
        {
            if (IsOn(window))
            {
                marked.Add(window);
            }

            return true;
        }, 0);

        return marked;
    }
}
