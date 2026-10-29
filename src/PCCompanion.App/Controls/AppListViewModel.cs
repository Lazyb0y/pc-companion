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
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Controls;

/// <summary>
/// A page's list of apps, which an <see cref="AppList"/> shows. The page's settings keep the
/// apps, and adding or removing one saves them right away.
/// </summary>
public sealed partial class AppListViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<ListedApp>> _apps;
    private readonly Func<ListedApp, bool> _add;
    private readonly Func<int, bool> _remove;

    /// <param name="apps">The apps the page's settings list now.</param>
    /// <param name="add">Saves the settings with an app added; false when nothing changed.</param>
    /// <param name="remove">Saves the settings with the app at an index removed; false when nothing changed.</param>
    public AppListViewModel(Func<IReadOnlyList<ListedApp>> apps, Func<ListedApp, bool> add, Func<int, bool> remove)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);

        _apps = apps;
        _add = add;
        _remove = remove;
        Refresh();
    }

    public ObservableCollection<ListedAppItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => _apps().Any(listed => listed.IsSameAppAs(app));

    /// <summary>Adds <paramref name="app"/> to the list, unless it's there already.</summary>
    public void Add(ListedApp app)
    {
        if (_add(app))
        {
            Refresh();
        }
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _apps().Count && _remove(index))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        Items.Clear();
        var apps = _apps();
        for (var index = 0; index < apps.Count; index++)
        {
            var item = new ListedAppItem(index, apps[index]);
            Items.Add(item);
            _ = item.LoadIconAsync();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// An app on a page's list of apps. Its icon shows once it's read.
/// </summary>
public sealed partial class ListedAppItem : ObservableObject
{
    private static readonly CompositeFormat RemoveNameFormat = CompositeFormat.Parse(Strings.AppListRemoveName);

    private IconElement _icon = AppLooks.ToIconElement(null);

    public ListedAppItem(int index, ListedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Index = index;
        App = app;
        Name = app.Name.Trim() is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(app.Program) ?? app.AppId ?? string.Empty;
        RemoveName = string.Format(CultureInfo.CurrentCulture, RemoveNameFormat, Name);
    }

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
