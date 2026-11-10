using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// The elements are as the spike found them in Edge, Windows Forms, WPF and the console, and as the
// Sandbox checks found Windows Forms on .NET Framework.
public sealed class TextBoxesTests
{
    private static readonly FocusableElement Box = new(IsEnabled: true, IsPassword: false, IsDocument: false, HasTextPattern: true, ValueIsReadOnly: false, IsWindowsEditBox: false);

    // Windows Forms on .NET Framework: Windows' own Edit box, with a value pattern but no text pattern.
    private static readonly FocusableElement FrameworkFormsBox = Box with { HasTextPattern = false, IsWindowsEditBox = true };

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

    [Fact]
    public void AnElementWithAValueButWithoutTextIsntOne() =>
        Assert.Equal(TextBoxStatus.NotTextBox, TextBoxes.Classify(Box with { HasTextPattern = false }));

    [Fact]
    public void WindowsOwnPlainBoxWithoutTextIsOne() =>
        Assert.Equal(TextBoxStatus.TextBox, TextBoxes.Classify(FrameworkFormsBox));

    [Fact]
    public void WindowsOwnReadOnlyBoxWithoutTextIsntOne() =>
        Assert.Equal(TextBoxStatus.ReadOnly, TextBoxes.Classify(FrameworkFormsBox with { ValueIsReadOnly = true }));

    [Fact]
    public void WindowsOwnPasswordBoxWithoutTextIsntOne() =>
        Assert.Equal(TextBoxStatus.Password, TextBoxes.Classify(FrameworkFormsBox with { IsPassword = true }));

    [Fact]
    public void WindowsOwnDisabledBoxWithoutTextIsntOne() =>
        Assert.Equal(TextBoxStatus.NotTextBox, TextBoxes.Classify(FrameworkFormsBox with { IsEnabled = false }));
}
