using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.ClipboardActions;

/// <summary>
/// The rule editor: a dialog with the rule's name, pattern, sample text and action. Save is
/// available only for a rule that can be used.
/// </summary>
public sealed partial class ClipboardRuleDialog : ContentDialog
{
    public ClipboardRuleDialog(ClipboardRuleEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public ClipboardRuleEditor Editor { get; }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (App.Current.MainWindow is { } window
            && OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.ClipboardActionsProgramDialogTitle, Strings.ClipboardActionsProgramFilter, "*.exe") is { } program)
        {
            Editor.Program = program;
        }
    }
}
