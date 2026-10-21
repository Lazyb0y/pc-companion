using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card for a choice from a list: its icon, header and description, and a list that
/// screen readers name after the header. Each item shows as its text, <see cref="object.ToString"/>,
/// which <see cref="ViewModels.Choice"/> gives.
/// </summary>
public sealed partial class ChoiceCard : IconCard
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(object), typeof(ChoiceCard), new PropertyMetadata(null, (sender, e) => ((ChoiceCard)sender).OnItemsSourceChanged(e.NewValue)));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ChoiceCard), new PropertyMetadata(null, (sender, e) => ((ChoiceCard)sender).OnSelectedItemChanged(e.NewValue)));

    private readonly ComboBox _list = new() { MinWidth = 200 };

    // True while the card itself changes the list, whose selection changes then don't come from the user.
    private bool _updating;

    public ChoiceCard()
    {
        Content = _list;
        _list.SelectionChanged += (_, _) =>
        {
            if (!_updating)
            {
                SelectedItem = _list.SelectedItem;
            }
        };
        NameAfterHeader(_list);
    }

    /// <summary>The items to choose from.</summary>
    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The chosen item, or null for none.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    // The items and the chosen one can arrive in either order; the chosen one is kept.
    private void OnItemsSourceChanged(object? items) => Update(() =>
    {
        _list.ItemsSource = items;
        _list.SelectedItem = SelectedItem;
    });

    private void OnSelectedItemChanged(object? item) => Update(() => _list.SelectedItem = item);

    private void Update(Action change)
    {
        _updating = true;
        try
        {
            change();
        }
        finally
        {
            _updating = false;
        }
    }
}
