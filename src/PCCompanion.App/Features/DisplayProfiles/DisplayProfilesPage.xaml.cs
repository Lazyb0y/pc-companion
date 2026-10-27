using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PCCompanion.Localization;

namespace PCCompanion.App.Features.DisplayProfiles;

public sealed partial class DisplayProfilesPage : Page
{
    // The automation ID of a profile's More options button, which the page finds it by.
    private const string MoreId = "More";

    private static readonly CompositeFormat DeleteTitleFormat = CompositeFormat.Parse(Strings.DisplayProfilesDeleteTitle);
    private static readonly CompositeFormat UpdateTitleFormat = CompositeFormat.Parse(Strings.DisplayProfilesUpdateTitle);

    // Where keyboard focus goes once the profile list is rebuilt, if it was in the list.
    private ProfileButton? _focus;

    public DisplayProfilesPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<DisplayProfilesViewModel>();
        InitializeComponent();

        // In use follows the displays only while the page shows.
        Loaded += (_, _) => ViewModel.Start();
        Unloaded += (_, _) => ViewModel.Stop();

        // Rebuilding the list replaces its buttons, which would send keyboard focus back to the
        // start of the window: it goes to the same button of the same profile instead, or to the
        // one an action chose, such as the profile it moved.
        ViewModel.ProfilesRebuilding += (_, _) => _focus ??= FocusedProfileButton();
        ViewModel.ProfilesRebuilt += (_, _) =>
        {
            if (_focus is { } focus)
            {
                _focus = null;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => FocusProfileButton(focus));
            }
        };
    }

    public DisplayProfilesViewModel ViewModel { get; }

    private static int IndexOf(object sender) => sender is FrameworkElement { Tag: int index } ? index : -1;

    // A profile's button, by its automation ID, if it shows and can be used.
    private static Button? FindButton(DependencyObject parent, string id)
    {
        for (var child = 0; child < VisualTreeHelper.GetChildrenCount(parent); child++)
        {
            var element = VisualTreeHelper.GetChild(parent, child);
            if (element is Button { Visibility: Visibility.Visible, IsEnabled: true } button && AutomationProperties.GetAutomationId(button) == id)
            {
                return button;
            }

            if (FindButton(element, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private ProfileButton? FocusedProfileButton() =>
        XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button { Tag: int index } button
            && AutomationProperties.GetAutomationId(button) is { Length: > 0 } id
            ? new ProfileButton(index, id)
            : null;

    // The profile's button, or its More options when that one is gone, as Use is once the profile
    // is in use; Add from current setup when no profile is left.
    private void FocusProfileButton(ProfileButton focus)
    {
        ProfileList.UpdateLayout();
        var index = Math.Min(focus.Index, ViewModel.Profiles.Count - 1);
        Control? target = AddCurrentButton;
        if (index >= 0 && ProfileList.ContainerFromIndex(index) is DependencyObject container)
        {
            target = FindButton(container, focus.Id) ?? FindButton(container, MoreId);
        }

        target?.Focus(FocusState.Programmatic);
    }

    // Runs an action that rebuilds the list, with focus going to the given button afterwards.
    private void Rebuild(ProfileButton focus, Action action)
    {
        _focus = focus;
        action();
        _focus = null;
    }

    private async void OnAddCurrentClick(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.CurrentSetupAsync() is not { } current)
        {
            return;
        }

        var editor = new DisplayProfileNameEditor(current, isNew: true, ViewModel.NamesExcept(-1));
        if (await ShowAsync(new DisplayProfileNameDialog(editor)) == ContentDialogResult.Primary)
        {
            ViewModel.Add(current with { Name = editor.ToName() });
        }
    }

    private async void OnUseClick(object sender, RoutedEventArgs e) => await ViewModel.UseAsync(IndexOf(sender));

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Profiles.Count)
        {
            return;
        }

        var editor = new DisplayProfileNameEditor(ViewModel.Profiles[index].Profile, isNew: false, ViewModel.NamesExcept(index));
        if (await ShowAsync(new DisplayProfileNameDialog(editor)) == ContentDialogResult.Primary)
        {
            Rebuild(new ProfileButton(index, MoreId), () => ViewModel.Rename(index, editor.ToName()));
        }
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Profiles.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, UpdateTitleFormat, ViewModel.Profiles[index].Name),
            Content = Strings.DisplayProfilesUpdateContent,
            PrimaryButtonText = Strings.DisplayProfilesUpdateButton,
            CloseButtonText = Strings.DisplayProfilesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary && await ViewModel.CurrentSetupAsync() is { } current)
        {
            Rebuild(new ProfileButton(index, MoreId), () => ViewModel.Update(index, current));
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(IndexOf(sender), -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(IndexOf(sender), 1);

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var index = IndexOf(sender);
        if (index < 0 || index >= ViewModel.Profiles.Count)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, DeleteTitleFormat, ViewModel.Profiles[index].Name),
            PrimaryButtonText = Strings.DisplayProfilesDelete,
            CloseButtonText = Strings.DisplayProfilesCancel,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            // Focus goes to the next profile, or the one before the last.
            Rebuild(new ProfileButton(index, MoreId), () => ViewModel.Delete(index));
        }
    }

    // Focus stays on the profile that moves.
    private void Move(int index, int offset) => Rebuild(new ProfileButton(index + offset, MoreId), () => ViewModel.Move(index, offset));

    // A dialog shows in the page's window and follows its language's direction.
    private Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        dialog.FlowDirection = FlowDirection;
        dialog.RequestedTheme = ActualTheme;
        return dialog.ShowAsync().AsTask();
    }

    // A button of the profile at Index in the list, by its automation ID.
    private readonly record struct ProfileButton(int Index, string Id);
}
