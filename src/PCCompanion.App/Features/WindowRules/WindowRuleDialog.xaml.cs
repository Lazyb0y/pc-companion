using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.Controls;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.WindowRules;

/// <summary>
/// The rule editor: a dialog with the rule's name, what it matches, the open windows it matches,
/// and its actions. Save is available only for a rule that can be used. Windows shows one dialog
/// at a time, so choosing a running app or a window hides this one, and the page shows it again
/// afterwards, through <see cref="Pending"/>.
/// </summary>
public sealed partial class WindowRuleDialog : ContentDialog
{
    public WindowRuleDialog(WindowRuleEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public WindowRuleEditor Editor { get; }

    /// <summary>A dialog to show while this one is hidden, after which the page shows this one again.</summary>
    public Func<XamlRoot, Task>? Pending { get; private set; }

    // A dialog of the settings app's own, after which this one shows again.
    private void ShowInstead(Func<XamlRoot, Task> show)
    {
        Pending = show;
        Hide();
    }

    private void OnPickWindowClick(object sender, RoutedEventArgs e) => ShowInstead(async root =>
    {
        var dialog = new PickWindowDialog(Editor.OpenWindows) { XamlRoot = root };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Chosen is { } chosen)
        {
            Editor.FillFrom(chosen.Window, WindowRuleEditor.ListedOf(chosen.Window, chosen.AppName));
        }
    });

    private void OnRunningAppClick(object sender, RoutedEventArgs e) => ShowInstead(async root =>
    {
        if (await RunningApp(root) is { } app)
        {
            Editor.SetApp(app);
        }
    });

    private async void OnProgramFileClick(object sender, RoutedEventArgs e)
    {
        if (await ProgramFileAsync() is { } app)
        {
            Editor.SetApp(app);
        }
    }

    private void OnTabSameAppClick(object sender, RoutedEventArgs e)
    {
        if (Editor.ToRule().App is { } app)
        {
            Editor.SetTabOf(app);
        }
    }

    private void OnTabRunningAppClick(object sender, RoutedEventArgs e) => ShowInstead(async root =>
    {
        if (await RunningApp(root) is { } app)
        {
            Editor.SetTabOf(app);
        }
    });

    private async void OnTabProgramFileClick(object sender, RoutedEventArgs e)
    {
        if (await ProgramFileAsync() is { } app)
        {
            Editor.SetTabOf(app);
        }
    }

    private static async Task<ListedApp?> RunningApp(XamlRoot root)
    {
        var dialog = new RunningAppDialog(_ => false) { XamlRoot = root };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Chosen : null;
    }

    // Only the full path of an .exe file can be an app's program. Whether it exists, and its name,
    // are read off the UI thread.
    private static async Task<ListedApp?> ProgramFileAsync()
    {
        if (App.Current.MainWindow is not { } window
            || OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.ProgramDialogTitle, Strings.ProgramDialogFilter, "*.exe") is not { } program)
        {
            return null;
        }

        var name = await Task.Run(() => ProgramFile.Check(program, File.Exists) == ProgramProblem.None ? AppDisplay.NameOfProgram(program) : null).ConfigureAwait(true);
        return name is null ? null : new ListedApp { Name = name, Program = program };
    }
}
