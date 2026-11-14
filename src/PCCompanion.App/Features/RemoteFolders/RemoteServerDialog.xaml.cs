using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.RemoteFolders;

/// <summary>
/// The server dialog: the server's address and sign-in, Connect, the server's key to check, and
/// the folder on the server. Save is available only once the details signed in and found the folder.
/// </summary>
public sealed partial class RemoteServerDialog : ContentDialog
{
    public RemoteServerDialog(RemoteServerEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public RemoteServerEditor Editor { get; }

    /// <summary>Sign in with, in the order of <see cref="RemoteServerEditor.SignIn"/>.</summary>
    public IReadOnlyList<string> SignInChoices { get; } = [Strings.RemoteFoldersSignInPassword, Strings.RemoteFoldersSignInKey];

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await Editor.ConnectAsync();

    private void OnBrowseKeyFileClick(object sender, RoutedEventArgs e)
    {
        if (App.Current.MainWindow is { } window
            && OpenDialog.PickFile(WindowNative.GetWindowHandle(window), Strings.RemoteFoldersKeyFileDialogTitle, Strings.RemoteFoldersKeyFileDialogFilter, "*.*") is { } path)
        {
            Editor.KeyFile = path;
        }
    }
}
