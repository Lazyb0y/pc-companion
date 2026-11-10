namespace PCCompanion.Platform.Windows;

/// <summary>What the element under a point is, for pasting into it; logged by name.</summary>
public enum TextBoxStatus
{
    /// <summary>A text box the user can type in: text is pasted there.</summary>
    TextBox,

    /// <summary>Anything else, such as page text, a link or a document without a value pattern.</summary>
    NotTextBox,

    /// <summary>A text box that can't be changed.</summary>
    ReadOnly,

    /// <summary>A password box, which is never pasted into.</summary>
    Password,

    /// <summary>The app answered with an error.</summary>
    NotShared,

    /// <summary>The app didn't answer within UI Automation's time limits.</summary>
    TimedOut,
}

/// <param name="Status">What the element is.</param>
/// <param name="Window">
/// The text box's own window when it has one, as Windows' own text boxes do, so it can be sent
/// messages; otherwise 0.
/// </param>
/// <param name="ErrorCode">The HRESULT of the app's error, for the log, or 0.</param>
public sealed record TextBoxAnswer(TextBoxStatus Status, nint Window = 0, int ErrorCode = 0);

/// <summary>What UI Automation says about an element that takes the keyboard focus.</summary>
/// <param name="IsEnabled">The element is enabled.</param>
/// <param name="IsPassword">The element is a password box.</param>
/// <param name="IsDocument">The element is a document, such as a web page or the console's text.</param>
/// <param name="HasTextPattern">The element gives its text.</param>
/// <param name="ValueIsReadOnly">
/// Whether the element's value pattern says it's read-only, or <see langword="null"/> when it has
/// no value pattern.
/// </param>
/// <param name="IsWindowsEditBox">
/// The element is a window of Windows' own plain text box class, <c>Edit</c>, which gets text by
/// messages. Windows Forms on .NET Framework gives such boxes no text pattern.
/// </param>
public readonly record struct FocusableElement(bool IsEnabled, bool IsPassword, bool IsDocument, bool HasTextPattern, bool? ValueIsReadOnly, bool IsWindowsEditBox);

/// <summary>
/// Tells text boxes from everything else, as "Recognizing a text box" in
/// docs/features/clipboard/middle-click-paste.md says. The element that counts is the one under the
/// point, or the nearest element above it that takes the keyboard focus, up to
/// <see cref="Levels"/> levels up: in a browser, the text under the pointer is part of the box.
/// </summary>
public static class TextBoxes
{
    /// <summary>How many levels up from the element under the point the focusable element is looked for.</summary>
    public const int Levels = 10;

    /// <summary>
    /// Whether <paramref name="element"/>, the focusable element at or above the point, is a text
    /// box: enabled, not a password box, giving its text or one of Windows' own plain text boxes, and
    /// either with a value pattern that isn't read-only, or without a value pattern and not a
    /// document, as the editable parts of a web page.
    /// </summary>
    public static TextBoxStatus Classify(FocusableElement element) =>
        !element.IsEnabled ? TextBoxStatus.NotTextBox
        : element.IsPassword ? TextBoxStatus.Password
        : !element.HasTextPattern && !element.IsWindowsEditBox ? TextBoxStatus.NotTextBox
        : element.ValueIsReadOnly == true ? TextBoxStatus.ReadOnly
        : element.ValueIsReadOnly == false ? TextBoxStatus.TextBox
        : element.IsDocument ? TextBoxStatus.NotTextBox
        : TextBoxStatus.TextBox;
}
