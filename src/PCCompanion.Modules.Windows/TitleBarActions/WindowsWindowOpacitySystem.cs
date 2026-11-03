using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>Changes other programs' opacity in Windows itself, marking each window it changes.</summary>
public sealed class WindowsWindowOpacitySystem : IWindowOpacitySystem
{
    public const string MarkName = "PCCompanion.TitleBarActions.Opacity";

    private static readonly Lazy<WindowMark> Marks = new(() => new WindowMark(MarkName));

    public bool IsLayered(nint window) => LayeredStyle.IsLayered(window);

    public LayeredAttributes? Attributes(nint window) => LayeredStyle.Read(window);

    public bool SetAttributes(nint window, LayeredAttributes attributes) => LayeredStyle.Set(window, attributes);

    public bool SetLayered(nint window, bool layered) => LayeredStyle.SetLayered(window, layered);

    public bool Mark(nint window, nint value) => Marks.Value.Set(window, value);

    public nint MarkValue(nint window) => Marks.Value.Value(window);

    public void RemoveMark(nint window) => Marks.Value.Remove(window);

    public IReadOnlyList<nint> FindMarked() => Marks.Value.FindMarked();
}
