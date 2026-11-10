using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// The elements are as the spike found them in Edge, Windows Forms, WPF and the console.
public sealed class TextBoxesTests
{
    private static readonly FocusableElement Box = new(IsEnabled: true, IsPassword: false, IsDocument: false, HasTextPattern: true, ValueIsReadOnly: false);

    [Fact]
    public void ABoxWhoseValueCanChangeIsATextBox() =>
        Assert.Equal(TextBoxStatus.TextBox, TextBoxes.Classify(Box));

    [Fact]
    public void ARichEditDocumentWhoseValueCanChangeIsATextBox() =>
        Assert.Equal(TextBoxStatus.TextBox, TextBoxes.Classify(Box with { IsDocument = true }));

    [Fact]
    public void AReadOnlyBoxIsntOne() =>
        Assert.Equal(TextBoxStatus.ReadOnly, TextBoxes.Classify(Box with { ValueIsReadOnly = true }));

    [Fact]
    public void AWebPageIsntOne() =>
        Assert.Equal(TextBoxStatus.ReadOnly, TextBoxes.Classify(Box with { IsDocument = true, ValueIsReadOnly = true }));

    [Fact]
    public void APasswordBoxIsntOne() =>
        Assert.Equal(TextBoxStatus.Password, TextBoxes.Classify(Box with { IsPassword = true }));

    [Fact]
    public void ADisabledBoxIsntOne() =>
        Assert.Equal(TextBoxStatus.NotTextBox, TextBoxes.Classify(Box with { IsEnabled = false }));

    [Fact]
    public void AnEditablePartOfAPageIsOne() =>
        Assert.Equal(TextBoxStatus.TextBox, TextBoxes.Classify(Box with { ValueIsReadOnly = null }));

    [Fact]
    public void ADocumentWithoutAValuePatternIsntOne() =>
        Assert.Equal(TextBoxStatus.NotTextBox, TextBoxes.Classify(Box with { IsDocument = true, ValueIsReadOnly = null }));

    [Fact]
    public void AnElementWithoutTextIsntOne() =>
        Assert.Equal(TextBoxStatus.NotTextBox, TextBoxes.Classify(Box with { HasTextPattern = false, ValueIsReadOnly = true }));
}
