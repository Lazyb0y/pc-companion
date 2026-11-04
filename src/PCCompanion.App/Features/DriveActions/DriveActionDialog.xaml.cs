using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Files;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>
/// The action editor: a dialog with what the action does and the boxes of its kind. Save is
/// available only for an action that can be used.
/// </summary>
public sealed partial class DriveActionDialog : ContentDialog
{
    public DriveActionDialog(DriveActionEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => ActionBox.Focus(FocusState.Programmatic);
    }

    public DriveActionEditor Editor { get; }

    /// <summary>What an import does when a file with the name is there, in the order of <see cref="DriveActionEditor.IfExistsIndex"/>.</summary>
    public IReadOnlyList<string> IfExistsChoices { get; } = [Strings.DriveActionsIfExistsAddNumber, Strings.DriveActionsIfExistsSkip];

    private static string? Pick(string title) =>
        App.Current.MainWindow is { } window ? OpenDialog.PickFolder(WindowNative.GetWindowHandle(window), title) : null;

    private void OnBrowseFromClick(object sender, RoutedEventArgs e)
    {
        if (Pick(Strings.DriveActionsBrowseFromTitle) is { } path)
        {
            Editor.ChooseFrom(path);
        }
    }

    private void OnBrowseToClick(object sender, RoutedEventArgs e)
    {
        if (Pick(Strings.DriveActionsBrowseToTitle) is { } path)
        {
            Editor.Destination = path;
        }
    }

    private void OnAddKindClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<FileKind>(tag, out var kind))
        {
            Editor.AddKind(kind);
        }
    }

    private void OnAddKnownFolderClick(object sender, RoutedEventArgs e)
    {
        var path = (sender as FrameworkElement)?.Tag switch
        {
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => null,
        };

        if (!string.IsNullOrEmpty(path))
        {
            Editor.AddFolder(path);
        }
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        if (Pick(Strings.DriveActionsBrowseFoldersTitle) is { } path)
        {
            Editor.AddFolder(path);
        }
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            Editor.RemoveFolder(index);
        }
    }

    // A folder on the drive is kept without its letter, so that it's found whatever letter the drive gets.
    private void OnBrowseOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (Pick(Strings.DriveActionsBrowseFolderTitle) is { } path)
        {
            Editor.Folder = Editor.OnDrive(path) ?? path;
        }
    }
}
