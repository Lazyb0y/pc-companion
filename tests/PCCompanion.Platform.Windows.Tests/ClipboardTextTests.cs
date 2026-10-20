using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// The clipboard itself is shared with the user's apps, so these tests read only buffers they make.
public sealed class ClipboardTextTests
{
    [Fact]
    public void TheTextEndsAtTheFirstNul()
    {
        Assert.Equal(new ClipboardTextResult(ClipboardTextStatus.Text, "copied"), ClipboardText.FromBuffer("copied\0left over"));
    }

    [Fact]
    public void AnEmptyTextIsText()
    {
        Assert.Equal(new ClipboardTextResult(ClipboardTextStatus.Text, string.Empty), ClipboardText.FromBuffer("\0"));
    }

    [Fact]
    public void TheLongestTextIsRead()
    {
        var text = new string('a', ClipboardText.MaxLength);

        Assert.Equal(new ClipboardTextResult(ClipboardTextStatus.Text, text), ClipboardText.FromBuffer(text + "\0"));
    }

    [Fact]
    public void ALongerTextIsTooLong()
    {
        // The reader copies at most one character more than the longest text.
        Assert.Equal(new ClipboardTextResult(ClipboardTextStatus.TooLong), ClipboardText.FromBuffer(new string('a', ClipboardText.MaxLength + 1)));
    }

    [Fact]
    public void AShortBufferWithoutANulHoldsItsWholeText()
    {
        // Some apps leave out the closing NUL when their buffer is exactly the text's size.
        Assert.Equal(new ClipboardTextResult(ClipboardTextStatus.Text, "no end"), ClipboardText.FromBuffer("no end"));
    }
}
