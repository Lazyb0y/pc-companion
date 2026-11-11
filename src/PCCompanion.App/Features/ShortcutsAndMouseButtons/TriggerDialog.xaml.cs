using System;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using Windows.System;
using Windows.UI.Core;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

/// <summary>
/// The editor of a shortcut, as a dialog. While its box has the focus, it records the keys
/// pressed, the host pauses PC Companion's shortcuts so that pressing one records it, and Alt + F4
/// is recorded instead of closing the window. Save is available only for a shortcut that can be used.
/// </summary>
public sealed partial class TriggerDialog : ContentDialog
{
    private readonly ShortcutPauseRequests _pause = new();
    private Func<Task>? _instead;
    private AppWindow? _window;
    private bool _focused;

    public TriggerDialog(TriggerEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        Editor = editor;
        InitializeComponent();
        editor.ExcludedApps.ShowDialog = ShowInsteadAsync;

        // The first time, the focus goes to what's to do first; after another dialog, it stays.
        Opened += (_, _) =>
        {
            if (!_focused)
            {
                _focused = true;
                ((Control)(editor.SelectedAction is null ? ActionBox : ShortcutBox)).Focus(FocusState.Programmatic);
            }
        };
        Closed += (_, _) => StopListening();
    }

    public TriggerEditor Editor { get; }

    /// <summary>Shows the dialog until it's saved or cancelled, with the dialogs it shows in its place in between.</summary>
    /// <param name="show">Shows a dialog in the page's window.</param>
    public async Task<ContentDialogResult> ShowUntilDoneAsync(Func<ContentDialog, Task<ContentDialogResult>> show)
    {
        ArgumentNullException.ThrowIfNull(show);

        while (true)
        {
            var result = await show(this).ConfigureAwait(true);
            if (_instead is not { } instead)
            {
                return result;
            }

            _instead = null;
            await instead().ConfigureAwait(true);
        }
    }

    // WinUI shows one dialog at a time, so this one closes for the other and opens again after it.
    private Task<ContentDialogResult> ShowInsteadAsync(ContentDialog other)
    {
        var done = new TaskCompletionSource<ContentDialogResult>();
        _instead = async () => done.SetResult(await other.ShowAsync());
        Hide();
        return done.Task;
    }

    private void OnShortcutBoxKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = Editor.Recorder.KeyDown(KeyOf(e));

    private void OnShortcutBoxKeyUp(object sender, KeyRoutedEventArgs e) => e.Handled = Editor.Recorder.KeyUp(KeyOf(e));

    private void OnShortcutBoxGotFocus(object sender, RoutedEventArgs e)
    {
        Editor.Recorder.Start(HeldModifiers());
        _pause.Start();
        if (_window is null && App.Current.MainWindow is { } window)
        {
            _window = window.AppWindow;
            _window.Closing += OnWindowClosing;
        }
    }

    private void OnShortcutBoxLostFocus(object sender, RoutedEventArgs e) => StopListening();

    private void StopListening()
    {
        _pause.Stop();
        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
            _window = null;
        }
    }

    // Alt + F4 pressed in the box is recorded, and isn't a request to close the window.
    private static void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (IsDown(VirtualKey.F4))
        {
            args.Cancel = true;
        }
    }

    private static RecorderKey KeyOf(KeyRoutedEventArgs e) =>
        new((uint)e.Key, e.KeyStatus.ScanCode | (e.KeyStatus.IsExtendedKey ? 0xE000u : 0u));

    private static HotKeyModifiers HeldModifiers()
    {
        var held = HotKeyModifiers.None;
        held |= IsDown(VirtualKey.Control) ? HotKeyModifiers.Control : HotKeyModifiers.None;
        held |= IsDown(VirtualKey.Menu) ? HotKeyModifiers.Alt : HotKeyModifiers.None;
        held |= IsDown(VirtualKey.Shift) ? HotKeyModifiers.Shift : HotKeyModifiers.None;
        held |= IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows) ? HotKeyModifiers.Windows : HotKeyModifiers.None;
        return held;
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
