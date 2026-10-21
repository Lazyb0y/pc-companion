using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace PCCompanion.App.Controls;

/// <summary>
/// A settings card for a value in a range: its icon, header and description, and a slider that
/// screen readers name after the header, with the value as text before it.
/// </summary>
public sealed partial class SliderCard : IconCard
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SliderCard), new PropertyMetadata(0.0, (sender, _) => ((SliderCard)sender).OnRangeChanged()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SliderCard), new PropertyMetadata(100.0, (sender, _) => ((SliderCard)sender).OnRangeChanged()));

    public static readonly DependencyProperty StepFrequencyProperty = DependencyProperty.Register(
        nameof(StepFrequency), typeof(double), typeof(SliderCard), new PropertyMetadata(1.0, (sender, e) => ((SliderCard)sender)._slider.StepFrequency = (double)e.NewValue));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderCard), new PropertyMetadata(0.0, (sender, e) => ((SliderCard)sender).Update(slider => slider.Value = (double)e.NewValue)));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(SliderCard), new PropertyMetadata(string.Empty, (sender, e) => ((SliderCard)sender)._valueText.Text = (string)e.NewValue));

    private readonly TextBlock _valueText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _slider = new() { Width = 200, IsThumbToolTipEnabled = false, SnapsTo = SliderSnapsTo.StepValues };

    // True while the card itself changes the slider, whose value changes then don't come from the user.
    private bool _updating;

    public SliderCard()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(_valueText);
        content.Children.Add(_slider);
        Content = content;
        _slider.ValueChanged += (_, e) =>
        {
            if (!_updating)
            {
                Value = e.NewValue;
            }
        };
        NameAfterHeader(_slider);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The steps the slider snaps to.</summary>
    public double StepFrequency
    {
        get => (double)GetValue(StepFrequencyProperty);
        set => SetValue(StepFrequencyProperty, value);
    }

    /// <summary>The value; the slider changes it.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The value as text, such as "150 ms", shown before the slider.</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    // The range and the value can arrive in either order: a value outside the slider's range so far
    // would be cut to fit it, so the value is set again once the range is.
    private void OnRangeChanged() => Update(slider =>
    {
        slider.Minimum = Minimum;
        slider.Maximum = Maximum;
        slider.Value = Value;
    });

    private void Update(Action<Slider> change)
    {
        _updating = true;
        try
        {
            change(_slider);
        }
        finally
        {
            _updating = false;
        }
    }
}
