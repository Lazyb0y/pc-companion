using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows with a layered style, attributes and marks, which change as Windows would change
/// them. A window in <see cref="DrawItself"/> is layered but reports no attributes, and setting them
/// breaks it, as with <c>UpdateLayeredWindow</c>.
/// </summary>
internal sealed class FakeWindowOpacitySystem : IWindowOpacitySystem
{
    public HashSet<nint> Layered { get; } = [];

    public Dictionary<nint, LayeredAttributes> Shown { get; } = [];

    public HashSet<nint> DrawItself { get; } = [];

    public HashSet<nint> Broken { get; } = [];

    public Dictionary<nint, nint> Marks { get; } = [];

    public bool RefusesMarks { get; set; }

    public int StyleChanges { get; private set; }

    /// <summary>The window's opacity as it shows now, in percent: 100 unless it's layered with an alpha.</summary>
    public int OpacityOf(nint window) =>
        Layered.Contains(window) && Shown.TryGetValue(window, out var shown) && shown.Effects.HasFlag(LayeredEffects.Alpha) ? (int)System.Math.Round(shown.Alpha / 2.55) : 100;

    public bool IsLayered(nint window) => Layered.Contains(window);

    public LayeredAttributes? Attributes(nint window) =>
        Layered.Contains(window) && !DrawItself.Contains(window) && Shown.TryGetValue(window, out var shown) ? shown : null;

    public bool SetAttributes(nint window, LayeredAttributes attributes)
    {
        if (DrawItself.Contains(window))
        {
            Broken.Add(window);
        }

        Shown[window] = attributes;
        return true;
    }

    public bool SetLayered(nint window, bool layered)
    {
        StyleChanges++;
        if (layered)
        {
            Layered.Add(window);
        }
        else
        {
            Layered.Remove(window);
            Shown.Remove(window);
        }

        return true;
    }

    public bool Mark(nint window, nint value)
    {
        if (!RefusesMarks)
        {
            Marks[window] = value;
        }

        return !RefusesMarks;
    }

    public nint MarkValue(nint window) => Marks.GetValueOrDefault(window);

    public void RemoveMark(nint window) => Marks.Remove(window);

    public IReadOnlyList<nint> FindMarked() => Marks.Keys.ToList();
}
