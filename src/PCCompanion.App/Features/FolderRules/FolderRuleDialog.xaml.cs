using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;
using WinRT.Interop;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>
/// The rule editor: a dialog with the rule's name, conditions and action, and the files in the
/// folder now that it would act on. Save is available only for a rule that can be used.
/// </summary>
public sealed partial class FolderRuleDialog : ContentDialog
{
    private static readonly CompositeFormat NumberFormat = CompositeFormat.Parse(Strings.FolderRulesNumberName);
    private static readonly CompositeFormat UnitFormat = CompositeFormat.Parse(Strings.FolderRulesUnitName);

    public FolderRuleDialog(FolderRuleEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public FolderRuleEditor Editor { get; }

    /// <summary>The size units, in the order of <see cref="SizeUnit"/>.</summary>
    public IReadOnlyList<string> Units { get; } = [Strings.FolderRulesUnitKB, Strings.FolderRulesUnitMB, Strings.FolderRulesUnitGB];

    /// <summary>The dates the Date condition reads, in the order of <see cref="FolderRuleEditor.DateWhich"/>.</summary>
    public IReadOnlyList<string> DateChoices { get; } = [Strings.FolderRulesDateModified, Strings.FolderRulesDateCreated];

    public string LargerThanNumberName { get; } = Labeled(NumberFormat, Strings.FolderRulesLargerThan);

    public string LargerThanUnitName { get; } = Labeled(UnitFormat, Strings.FolderRulesLargerThan);

    public string SmallerThanNumberName { get; } = Labeled(NumberFormat, Strings.FolderRulesSmallerThan);

    public string SmallerThanUnitName { get; } = Labeled(UnitFormat, Strings.FolderRulesSmallerThan);

    public string MoreThanNumberName { get; } = Labeled(NumberFormat, Strings.FolderRulesMoreThanDays);

    public string LessThanNumberName { get; } = Labeled(NumberFormat, Strings.FolderRulesLessThanDays);

    private static string Labeled(CompositeFormat format, string label) => string.Format(CultureInfo.CurrentCulture, format, label);

    private void OnAddKindClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<FileKind>(tag, out var kind))
        {
            Editor.AddKind(kind);
        }
    }

    private async void OnCreateFolderClick(object sender, RoutedEventArgs e) => await Editor.CreateDestinationAsync();

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (App.Current.MainWindow is { } window && OpenDialog.PickFolder(WindowNative.GetWindowHandle(window), Strings.FolderRulesDestinationTitle) is { } path)
        {
            Editor.Destination = path;
        }
    }
}
