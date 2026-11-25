using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// The actions that hold while a window is open: always on top, opacity and taskbar hiding, as
/// "Always on top", "Opacity", "Taskbar and Alt + Tab" and "Never losing a window" in
/// docs/features/windows/window-rules.md say. Before it changes a window, it marks it with what
/// it changed and what the window had, so a later PC Companion puts it back after a crash. Use it
/// on the feature's thread.
/// </summary>
internal sealed class HeldActions(IRulesSystem system)
{
    // The mark's bits: the mark itself, so its value is never 0; put on top; taskbar button
    // removed; opacity changed, with the layered style added or the window's own kept.
    private const long Marked = 1;
    private const long OnTopBit = 2;
    private const long TaskbarBit = 4;
    private const long AddedLayeredBit = 8;
    private const long OwnLayeredBit = 16;

    private readonly Dictionary<nint, Held> _held = [];

    /// <summary>The windows something holds, for tests.</summary>
    public IReadOnlyCollection<nint> Windows => _held.Keys;

    /// <summary>
    /// Gives <paramref name="window"/> what its rules hold now, and takes away what they don't any
    /// more. <paramref name="opacity"/> is in percent, or <see langword="null"/> for none.
    /// </summary>
    public void Apply(nint window, bool onTop, int? opacity, bool offTaskbar)
    {
        var held = _held.GetValueOrDefault(window) ?? new Held();
        if (onTop && !held.OnTop && !system.IsTopmost(window))
        {
            held.OnTop = true;
            Mark(window, held);
            system.SetTopmost(window, true);
        }
        else if (!onTop && held.OnTop)
        {
            held.OnTop = false;
            system.SetTopmost(window, false);
        }

        if (offTaskbar && !held.OffTaskbar)
        {
            held.OffTaskbar = true;
            Mark(window, held);
            if (!system.IsMinimized(window))
            {
                system.RemoveTaskbarButton(window);
            }
        }
        else if (!offTaskbar && held.OffTaskbar)
        {
            held.OffTaskbar = false;
            system.AddTaskbarButton(window);
        }

        ApplyOpacity(window, held, opacity);
        Keep(window, held);
    }

    /// <summary>A window came to the front: one whose app took it off the top is put back, once each time.</summary>
    public void OnForeground(nint window)
    {
        if (_held.TryGetValue(window, out var held) && held.OnTop && !system.IsTopmost(window))
        {
            system.SetTopmost(window, true);
        }
    }

    /// <summary>A window starts to minimize: one off the taskbar gets its button back, so it can always be reached.</summary>
    public void OnMinimizeStart(nint window)
    {
        if (_held.TryGetValue(window, out var held) && held.OffTaskbar)
        {
            system.AddTaskbarButton(window);
        }
    }

    /// <summary>A window was restored: one off the taskbar loses its button again.</summary>
    public void OnMinimizeEnd(nint window)
    {
        if (_held.TryGetValue(window, out var held) && held.OffTaskbar)
        {
            system.RemoveTaskbarButton(window);
        }
    }

    /// <summary>Explorer restarted and added every button again: those held off the taskbar go again.</summary>
    public void OnTaskbarCreated()
    {
        foreach (var (window, held) in _held)
        {
            if (held.OffTaskbar && !system.IsMinimized(window))
            {
                system.RemoveTaskbarButton(window);
            }
        }
    }

    /// <summary>A window closed: nothing is put back.</summary>
    public void Forget(nint window) => _held.Remove(window);

    /// <summary>Puts back every window: off the top, its own opacity and its taskbar button. Returns how many.</summary>
    public int ReleaseAll()
    {
        var count = _held.Count;
        foreach (var window in _held.Keys.ToList())
        {
            Apply(window, onTop: false, opacity: null, offTaskbar: false);
        }

        _held.Clear();
        return count;
    }

    /// <summary>
    /// Puts back every window that carries the mark, as left by a PC Companion that ended without
    /// putting them back. Returns how many.
    /// </summary>
    public static int Recover(IRulesSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var windows = system.Marked();
        foreach (var window in windows)
        {
            var value = (long)system.MarkOf(window);
            if ((value & OnTopBit) != 0)
            {
                system.SetTopmost(window, false);
            }

            if ((value & TaskbarBit) != 0)
            {
                system.AddTaskbarButton(window);
            }

            if ((value & (AddedLayeredBit | OwnLayeredBit)) != 0)
            {
                system.PutBackOpacity(window, removeLayered: (value & AddedLayeredBit) != 0, own: (value & OwnLayeredBit) != 0 ? OwnOf(value) : null);
            }

            system.Unmark(window);
        }

        return windows.Count;
    }

    private void ApplyOpacity(nint window, Held held, int? opacity)
    {
        if (opacity is not { } percent)
        {
            if (held.Alpha is not null)
            {
                system.PutBackOpacity(window, held.AddedLayered, held.Own);
                held.Alpha = null;
                held.AddedLayered = false;
                held.Own = null;
            }

            return;
        }

        var first = held.Alpha is null;
        if (first)
        {
            // A window that draws itself with UpdateLayeredWindow reports no attributes, and
            // setting its opacity would break its drawing for good, so it's left alone.
            var layered = system.IsLayered(window);
            var own = layered ? system.LayeredAttributes(window) : null;
            if (layered && own is null)
            {
                return;
            }

            held.AddedLayered = !layered;
            held.Own = own;
        }

        var alpha = (byte)Math.Round(Math.Clamp(percent, RuleOpacity.Lowest, 100) * 255 / 100.0);
        if (held.Alpha == alpha)
        {
            return;
        }

        held.Alpha = alpha;
        Mark(window, held);
        var effects = (held.Own?.Effects ?? LayeredEffects.None) | LayeredEffects.Alpha;
        system.SetOpacity(window, new LayeredAttributes(held.Own?.ColorKey ?? 0, alpha, effects), addLayered: first && held.AddedLayered);
    }

    // Kept while something holds; otherwise forgotten, with its mark removed.
    private void Keep(nint window, Held held)
    {
        if (held.OnTop || held.OffTaskbar || held.Alpha is not null)
        {
            _held[window] = held;
            Mark(window, held);
            return;
        }

        if (_held.Remove(window))
        {
            system.Unmark(window);
        }
    }

    private void Mark(nint window, Held held)
    {
        var value = Marked | (held.OnTop ? OnTopBit : 0) | (held.OffTaskbar ? TaskbarBit : 0);
        if (held.Alpha is not null)
        {
            value |= held.AddedLayered ? AddedLayeredBit : 0;
            if (held.Own is { } own)
            {
                value |= OwnLayeredBit | ((long)own.Alpha << 8) | ((long)own.Effects << 16) | ((long)own.ColorKey << 32);
            }
        }

        system.Mark(window, (nint)value);
    }

    private static LayeredAttributes OwnOf(long value) =>
        new((uint)(value >> 32) & 0xFFFFFF, (byte)(value >> 8), (LayeredEffects)((value >> 16) & 0x3));

    private sealed class Held
    {
        public bool OnTop { get; set; }

        public bool OffTaskbar { get; set; }

        public byte? Alpha { get; set; }

        public bool AddedLayered { get; set; }

        public LayeredAttributes? Own { get; set; }
    }
}
