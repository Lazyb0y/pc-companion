using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Checks only pages the dialog refuses before it shows: a shown dialog would take the focus. The
// Sandbox checks answer shown dialogs.
public sealed class QuestionDialogTests
{
    [Fact]
    public void APageNeedsAButton()
    {
        var page = new QuestionPage("End Notepad?", "Notepad isn't responding.", null, [], 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => QuestionDialog.Show("End App", page, null, null, false, _ => { }, _ => { }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void TheDefaultButtonMustBeOneOfTheButtons(int defaultButton)
    {
        var page = new QuestionPage("End Notepad?", "Notepad isn't responding.", null, ["End app", "Cancel"], defaultButton);

        Assert.Throws<ArgumentOutOfRangeException>(() => QuestionDialog.Show("End App", page, null, null, false, _ => { }, _ => { }));
    }
}
