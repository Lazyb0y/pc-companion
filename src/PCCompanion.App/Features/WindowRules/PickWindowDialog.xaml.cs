using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.WindowRules;

namespace PCCompanion.App.Features.WindowRules;

/// <summary>An open window in Pick a window, by its title and its app.</summary>
public sealed class PickedWindow(OpenRuleWindow window, string appName)
{
    public OpenRuleWindow Window { get; } = window;

    public string AppName { get; } = appName;

    public string Title => Window.Facts.Title.Length > 0 ? Window.Facts.Title : Strings.WindowRulesPickWindowUntitled;

    // The list's items are named after this for screen readers.
    public override string ToString() => $"{Title}, {AppName}";
}

/// <summary>
/// Lists the open main windows, front to back, so a rule can be filled in from one. See "Making
/// rules" in docs/features/windows/window-rules.md. The windows were read as the editor opened.
/// </summary>
public sealed partial class PickWindowDialog : ContentDialog
{
    public PickWindowDialog(IReadOnlyList<(OpenRuleWindow Window, string AppName)> windows)
    {
        Items = [.. windows.Select(open => new PickedWindow(open.Window, open.AppName))];
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (Items.Count == 0)
            {
                NoWindows.Visibility = Visibility.Visible;
                Windows.Visibility = Visibility.Collapsed;
                return;
            }

            Windows.SelectedIndex = 0;
            Windows.Focus(FocusState.Programmatic);
        };
    }

    public IReadOnlyList<PickedWindow> Items { get; }

    /// <summary>The chosen window, or <see langword="null"/>.</summary>
    public PickedWindow? Chosen => Windows.SelectedItem as PickedWindow;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => IsPrimaryButtonEnabled = Windows.SelectedItem is not null;
}
