using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.App.ViewModels;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Controls;

/// <summary>
/// A page's list of apps, which an <see cref="AppList"/> shows. The page's settings keep the
/// apps, and adding or removing one, or choosing what one gets, saves them right away.
/// </summary>
public sealed partial class AppListViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<ListedApp>> _apps;
    private readonly Func<ListedApp, bool> _add;
    private readonly Func<int, bool> _remove;
    private readonly AppListChoice? _choice;

    /// <param name="apps">The apps the page's settings list now.</param>
    /// <param name="add">Saves the settings with an app added; false when nothing changed.</param>
    /// <param name="remove">Saves the settings with the app at an index removed; false when nothing changed.</param>
    /// <param name="choice">What each app gets from a list in its row, or null for no list.</param>
    public AppListViewModel(Func<IReadOnlyList<ListedApp>> apps, Func<ListedApp, bool> add, Func<int, bool> remove, AppListChoice? choice = null)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);

        _apps = apps;
        _add = add;
        _remove = remove;
        _choice = choice;
        Refresh();
    }

    public ObservableCollection<ListedAppItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Whether each app's row has a list to choose what it gets.</summary>
    public bool HasChoices => _choice is not null;

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => _apps().Any(listed => listed.IsSameAppAs(app));

    /// <summary>Adds <paramref name="app"/> at the end of the list, unless it's there already; returns whether it was added.</summary>
    public bool Add(ListedApp app)
    {
        if (!_add(app))
        {
            return false;
        }

        Refresh();
        return true;
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _apps().Count && _remove(index))
        {
            Refresh();
        }
    }

    /// <summary>Shows the list again, as after the page read what its apps can get.</summary>
    public void Refresh()
    {
        Items.Clear();
        var apps = _apps();
        for (var index = 0; index < apps.Count; index++)
        {
            var item = new ListedAppItem(index, apps[index], _choice);
            Items.Add(item);
            _ = item.LoadIconAsync();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// What each app on a page's list gets from a list in its row, such as the input method it switches to.
/// </summary>
/// <param name="Items">What an app can get, read each time the list shows.</param>
/// <param name="Selected">What the app at an index gets, or null when it's none of <paramref name="Items"/>.</param>
/// <param name="Choose">Saves the settings with the app at an index getting an item; false when nothing changed.</param>
/// <param name="NameFormat">The screen reader name of an app's list, with {0} for the app's name.</param>
/// <param name="Missing">What an app's list shows when it gets none of the items, or null for nothing.</param>
public sealed record AppListChoice(
    Func<IReadOnlyList<Choice>> Items,
    Func<int, Choice?> Selected,
    Func<int, Choice, bool> Choose,
    CompositeFormat NameFormat,
    Func<string?> Missing);

/// <summary>
/// An app on a page's list of apps. Its icon shows once it's read.
/// </summary>
public sealed partial class ListedAppItem : ObservableObject
{
    private static readonly CompositeFormat RemoveNameFormat = CompositeFormat.Parse(Strings.AppListRemoveName);

    private readonly AppListChoice? _choice;
    private IconElement _icon = AppLooks.ToIconElement(null);
    private Choice? _selectedChoice;

    public ListedAppItem(int index, ListedApp app, AppListChoice? choice = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        Index = index;
        App = app;
        Name = app.Name.Trim() is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(app.Program) ?? app.AppId ?? string.Empty;
        RemoveName = string.Format(CultureInfo.CurrentCulture, RemoveNameFormat, Name);

        _choice = choice;
        if (choice is not null)
        {
            Choices = choice.Items();
            _selectedChoice = choice.Selected(index);
            ChoiceName = string.Format(CultureInfo.CurrentCulture, choice.NameFormat, Name);
            ChoicePlaceholder = _selectedChoice is null ? choice.Missing() : null;
        }
    }

    /// <summary>What the app can get from the list in its row, or <see langword="null"/> for no list.</summary>
    public IReadOnlyList<Choice>? Choices { get; }

    public bool HasChoices => Choices is not null;

    /// <summary>What the app gets; choosing another saves the settings.</summary>
    public Choice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (value is not null && value != _selectedChoice && _choice is not null && _choice.Choose(Index, value))
            {
                _selectedChoice = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The screen reader name of the list in the row.</summary>
    public string ChoiceName { get; } = string.Empty;

    /// <summary>What the list shows when the app gets none of its items.</summary>
    public string? ChoicePlaceholder { get; }

    /// <summary>The app's place in the list, from 0.</summary>
    public int Index { get; }

    public ListedApp App { get; }

    public string Name { get; }

    /// <summary>The program's path, shown in the tooltip, or <see langword="null"/> for an app ID.</summary>
    public string? Program => App.Program;

    /// <summary>The screen reader name of the Remove button.</summary>
    public string RemoveName { get; }

    /// <summary>The card's icon: the plain app icon until the app's own is read.</summary>
    public IconElement Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    /// <summary>Reads the app's icon off the UI thread, and shows it.</summary>
    public async Task LoadIconAsync()
    {
        var icon = await Task.Run(() => AppLooks.ReadIcon(App, null)).ConfigureAwait(true);
        if (await AppLooks.ToImageAsync(icon).ConfigureAwait(true) is { } image)
        {
            Icon = AppLooks.ToIconElement(image);
        }
    }
}
