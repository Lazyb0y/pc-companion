using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// The shortcut, the clipboard and the menu, from Windows.
/// </summary>
internal sealed class WindowsClipboardActionsSystem : IClipboardActionsSystem
{
    private const uint VirtualKeyV = 0x56;

    public IShortcut? RegisterShortcut(MessageWindow window) =>
        HotKey.Register(window, HotKeyModifiers.Windows | HotKeyModifiers.Alt, VirtualKeyV) is { } hotKey ? new Shortcut(hotKey) : null;

    public ClipboardTextResult ReadClipboard(MessageWindow window) => ClipboardText.Read(window);

    public Point PointerPosition() => InputState.PointerPosition ?? Point.Empty;

    public ForegroundTextCursor ReadTextCursor() => TextCursor.Read();

    public bool CanAskTextCursor(nint window) => TextCursor.CanAsk(window);

    // The app gets as long to answer the message as the press waits for it.
    public Task<Rectangle?> AskTextCursor(nint window) => TextCursor.AskAsync(window, MenuPlacer.AnswerLimit);

    // The menu follows the Windows mode and the language, as the PC Companion icon's menu does.
    public uint ShowMenu(MessageWindow window, IReadOnlyList<PopupMenuItem> items, Point position, Rectangle? keepClear)
    {
        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        if (MenuTheme.IsSupported)
        {
            MenuTheme.Set(MenuTheme.ForWindowsMode());
        }

        return PopupMenu.ShowForShortcut(window, items, position.X, position.Y, rightToLeft, keepClear);
    }

    public void AllowFront() => Foreground.AllowAnyProcess();

    public bool FileExists(string path) => File.Exists(path);

    public void RunInBackground(Action action) => Task.Run(action);

    private sealed class Shortcut : IShortcut
    {
        private readonly HotKey _hotKey;

        public Shortcut(HotKey hotKey)
        {
            _hotKey = hotKey;
            _hotKey.Pressed += (_, e) => Pressed?.Invoke(this, e);
        }

        public event EventHandler? Pressed;

        public void Dispose() => _hotKey.Dispose();
    }
}
