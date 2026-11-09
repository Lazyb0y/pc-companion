using System.Text;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class ClipboardShortcutStatusTests
{
    [Fact]
    public void NoShortcutIsRefusedAtFirst()
    {
        Assert.Equal(new ClipboardShortcutRefusals(false, false), new ClipboardShortcutStatus().Refused);
    }

    [Fact]
    public void TheStatusReadsBackAsWritten()
    {
        var status = new ClipboardShortcutStatus { Refused = new ClipboardShortcutRefusals(true, false) };

        Assert.Equal(new ClipboardShortcutRefusals(true, false), ClipboardShortcutStatus.FromJson(status.ToJson()));
    }

    [Fact]
    public void ThePipeGivesOnlyWhichShortcutsWereRefused()
    {
        var status = new ClipboardShortcutStatus { Refused = new ClipboardShortcutRefusals(false, true) };

        Assert.Equal("""{"copiedText":false,"selectedText":true}""", Encoding.UTF8.GetString(status.ToJson()));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("""{"copiedText":"yes"}""")]
    [InlineData("[]")]
    public void AStatusThatCantBeReadIsNone(string json)
    {
        Assert.Null(ClipboardShortcutStatus.FromJson(Encoding.UTF8.GetBytes(json)));
    }
}
