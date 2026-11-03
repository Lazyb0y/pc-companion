using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Changes other programs' windows' opacity a step at a time and puts them back, as "The wheel" and
/// "Never leaving a window translucent" in docs/features/windows/title-bar-actions.md say. Each
/// window is marked before it changes, with what it goes back to. Changes run on
/// <paramref name="background"/>, since giving a window the layered style waits for its program,
/// one at a time for each window, and steps that come meanwhile add up. Use it on one thread, the
/// one <paramref name="post"/> runs on; <see cref="PutBackNow"/> runs on any.
/// </summary>
/// <param name="background">Runs work that can wait for another program.</param>
/// <param name="post">Runs work back on this object's thread.</param>
/// <param name="failed">Hears of a failure on <paramref name="background"/>, on that thread.</param>
public sealed class WindowOpacity(IWindowOpacitySystem system, Action<Action> background, Action<Action> post, Action<Exception> failed)
{
    /// <summary>The lowest opacity, in percent, the wheel takes a window to.</summary>
    public const int Lowest = 20;

    /// <summary>How much, in percent, a notch of the wheel changes the opacity.</summary>
    public const int StepSize = 10;

    private static readonly LayeredAttributes Opaque = new(0, 255, LayeredEffects.Alpha);

    private readonly Dictionary<nint, Changed> _windows = [];

    // What each changed window goes back to, null to lose the layered style, for PutBackNow.
    private readonly ConcurrentDictionary<nint, LayeredAttributes?> _changed = new();
    private int _generation;

    /// <summary>The window's opacity in percent, while this object has changed it.</summary>
    public int? Level(nint window) => _windows.TryGetValue(window, out var changed) ? changed.Level : null;

    /// <summary>
    /// Changes the window's opacity by <paramref name="notches"/> steps: more opaque for positive
    /// ones, between <see cref="Lowest"/> and the opacity it had before. Back there, the window is
    /// put back as it was and its mark removed.
    /// </summary>
    public OpacityStep Step(nint window, int notches)
    {
        var known = _windows.TryGetValue(window, out var changed);
        if (!known && Begin(window) is { } begun)
        {
            changed = begun;
        }

        if (changed is null)
        {
            return OpacityStep.DrawsItself;
        }

        var level = Math.Clamp(changed.Level + (notches * StepSize), Math.Min(Lowest, changed.Highest), changed.Highest);
        if (level == changed.Level)
        {
            return OpacityStep.AtLimit;
        }

        if (!known)
        {
            if (!system.Mark(window, Encode(changed.Own)))
            {
                return OpacityStep.Refused;
            }

            _windows[window] = changed;
            _changed[window] = changed.Own;
        }

        changed.Level = level;
        Apply(window, changed);
        return OpacityStep.Changed;
    }

    /// <summary>
    /// Puts back every window this object changed, as PC Companion stops: each shows as before at
    /// once, and loses the layered style it was given on <paramref name="background"/>. Returns how many.
    /// </summary>
    public int PutBackAll()
    {
        Interlocked.Increment(ref _generation);
        var count = _windows.Count;
        foreach (var (window, changed) in _windows)
        {
            PutBack(system, window, changed.Own, background, failed);
        }

        _windows.Clear();
        _changed.Clear();
        return count;
    }

    /// <summary>
    /// Shows every window this object changed as before, without taking away the layered style it
    /// was given, which waits for the window's program, as when PC Companion crashes; their marks
    /// stay, so the next start finishes. Any thread can call it. Returns how many.
    /// </summary>
    public int PutBackNow()
    {
        var count = 0;
        foreach (var (window, own) in _changed)
        {
            system.SetAttributes(window, own ?? Opaque);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Puts back every window left marked by a PC Companion that ended without putting it back.
    /// Returns how many.
    /// </summary>
    public static int Recover(IWindowOpacitySystem system, Action<Action> background, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(system);

        var marked = system.FindMarked();
        foreach (var window in marked)
        {
            PutBack(system, window, Decode(system.MarkValue(window)), background, failed);
        }

        return marked.Count;
    }

    // A window seen for the first time: one already marked goes on from its opacity now, as after a
    // crash; one that draws itself can't change.
    private Changed? Begin(nint window)
    {
        var mark = system.MarkValue(window);
        if (mark != 0)
        {
            var own = Decode(mark);
            var highest = Highest(own);
            return new Changed(own, highest, system.Attributes(window) is { } now && now.Effects.HasFlag(LayeredEffects.Alpha) ? Percent(now.Alpha) : highest);
        }

        if (!system.IsLayered(window))
        {
            return new Changed(null, 100, 100);
        }

        return system.Attributes(window) is { } attributes ? new Changed(attributes, Highest(attributes), Highest(attributes)) : null;
    }

    private void Apply(nint window, Changed changed)
    {
        if (changed.Busy)
        {
            return;
        }

        changed.Busy = true;
        var level = changed.Level;
        var generation = Volatile.Read(ref _generation);
        background(() =>
        {
            try
            {
                Change(window, level, changed.Own, changed.Highest, generation);
            }
            catch (Exception exception)
            {
                failed(exception);
            }

            post(() => Applied(window, changed, level));
        });
    }

    // Back on this object's thread: a step that came meanwhile is applied next.
    private void Applied(nint window, Changed changed, int level)
    {
        changed.Busy = false;
        if (!_windows.TryGetValue(window, out var current) || current != changed)
        {
            return;
        }

        if (changed.Level != level)
        {
            Apply(window, changed);
        }
        else if (level >= changed.Highest)
        {
            _windows.Remove(window);
            _changed.TryRemove(window, out _);
        }
    }

    // On the background thread. A change asked for before the windows were put back is dropped.
    private void Change(nint window, int level, LayeredAttributes? own, int highest, int generation)
    {
        if (generation != Volatile.Read(ref _generation))
        {
            return;
        }

        if (level >= highest)
        {
            if (own is { } attributes)
            {
                system.SetAttributes(window, attributes);
            }
            else
            {
                system.SetAttributes(window, Opaque);
                system.SetLayered(window, false);
            }

            system.RemoveMark(window);
            return;
        }

        if (own is null && !system.IsLayered(window))
        {
            system.SetLayered(window, true);
        }

        var effects = (own?.Effects ?? LayeredEffects.None) | LayeredEffects.Alpha;
        system.SetAttributes(window, new LayeredAttributes(own?.ColorKey ?? 0, (byte)Math.Round(level * 2.55), effects));
    }

    // Shows the window as before at once, without waiting for its program. The layered style it
    // was given goes on the background thread, and its mark with it, so a mark left behind means
    // the next start finishes.
    private static void PutBack(IWindowOpacitySystem system, nint window, LayeredAttributes? own, Action<Action> background, Action<Exception> failed)
    {
        if (own is { } attributes)
        {
            system.SetAttributes(window, attributes);
            system.RemoveMark(window);
            return;
        }

        system.SetAttributes(window, Opaque);
        background(() =>
        {
            try
            {
                system.SetLayered(window, false);
                system.RemoveMark(window);
            }
            catch (Exception exception)
            {
                failed(exception);
            }
        });
    }

    private static int Highest(LayeredAttributes? own) => own is { } attributes && attributes.Effects.HasFlag(LayeredEffects.Alpha) ? Percent(attributes.Alpha) : 100;

    private static int Percent(byte alpha) => (int)Math.Round(alpha / 2.55);

    // The mark's value: 1 when PC Companion gave the window the layered style, otherwise the
    // window's own attributes, with bit 8 set so the value is never 1.
    private static nint Encode(LayeredAttributes? own) =>
        own is { } attributes ? (nint)(0x100L | attributes.Alpha | ((long)attributes.Effects << 16) | ((long)attributes.ColorKey << 32)) : 1;

    private static LayeredAttributes? Decode(nint value) =>
        ((long)value & 0x100) != 0
            ? new LayeredAttributes((uint)(((long)value >> 32) & 0xFFFFFF), (byte)((long)value & 0xFF), (LayeredEffects)(((long)value >> 16) & 0xFF))
            : null;

    // A window this object changes: what it goes back to, the opacity it had, and the one it has now.
    private sealed class Changed(LayeredAttributes? own, int highest, int level)
    {
        public LayeredAttributes? Own { get; } = own;

        public int Highest { get; } = highest;

        public int Level { get; set; } = level;

        public bool Busy { get; set; }
    }
}

/// <summary>What a step of the wheel did to a window's opacity.</summary>
public enum OpacityStep
{
    Changed,

    /// <summary>The window is already at the lowest opacity, or at the one it had before.</summary>
    AtLimit,

    /// <summary>The window draws itself with <c>UpdateLayeredWindow</c>, so its opacity can't change.</summary>
    DrawsItself,

    /// <summary>Windows refused the mark, as for an app running as administrator.</summary>
    Refused,
}
