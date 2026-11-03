using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// What changing other programs' opacity needs from Windows, so tests can supply it instead. Any
/// thread can use it; only the layered style's changes wait for the window's program.
/// </summary>
public interface IWindowOpacitySystem
{
    bool IsLayered(nint window);

    /// <summary>A layered window's opacity and color key, or <see langword="null"/> when it draws itself.</summary>
    LayeredAttributes? Attributes(nint window);

    /// <summary>Shows a layered window with the attributes. Returns false when Windows refused. It doesn't wait.</summary>
    bool SetAttributes(nint window, LayeredAttributes attributes);

    /// <summary>Gives the window the layered style, or takes it away; it waits for the window's program.</summary>
    bool SetLayered(nint window, bool layered);

    /// <summary>Marks the window with a value that isn't 0. Returns false when Windows refused.</summary>
    bool Mark(nint window, nint value);

    /// <summary>The value the window's mark holds, or 0 when it has none.</summary>
    nint MarkValue(nint window);

    void RemoveMark(nint window);

    /// <summary>The top-level windows that carry the mark.</summary>
    IReadOnlyList<nint> FindMarked();
}
