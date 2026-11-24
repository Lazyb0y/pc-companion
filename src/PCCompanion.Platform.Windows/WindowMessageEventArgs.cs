using System;

namespace PCCompanion.Platform.Windows;

public sealed class WindowMessageEventArgs(uint message, nuint wParam, nint lParam) : EventArgs
{
    public uint Message { get; } = message;

    public nuint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;

    /// <summary>Set when a handler has processed the message, so the default processing is skipped.</summary>
    public bool Handled { get; set; }

    /// <summary>What the window returns for a message a handler processed; 0 unless the handler sets it.</summary>
    public nint Result { get; set; }
}
