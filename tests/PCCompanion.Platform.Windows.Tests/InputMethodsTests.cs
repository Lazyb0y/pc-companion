using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the user's input methods without switching any app's or adding any.
public sealed class InputMethodsTests
{
    [Theory]
    [InlineData("0407:00000407", 0x0407, 0x00000407u)]
    [InlineData("0409:00020409", 0x0409, 0x00020409u)]
    [InlineData("0409:0002040a", 0x0409, 0x0002040Au)]
    public void AKeyboardLayoutsIdentifierIsItsLanguageAndLayout(string text, int language, uint layout)
    {
        Assert.True(InputMethodId.TryParse(text, out var id));
        Assert.Equal(new InputMethodId((ushort)language, layout), id);
        Assert.False(id.IsImeLanguage);
        Assert.Equal(text.ToUpperInvariant(), id.ToString());
    }

    [Fact]
    public void ALanguagesImesAreKnownByTheLanguageAlone()
    {
        Assert.True(InputMethodId.TryParse("0411", out var id));
        Assert.Equal(new InputMethodId(0x0411, null), id);
        Assert.True(id.IsImeLanguage);
        Assert.Equal("0411", id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000")]
    [InlineData("0000:00000409")]
    [InlineData("407")]
    [InlineData("04070")]
    [InlineData("0407:")]
    [InlineData("0407-00000407")]
    [InlineData("0407:0000407")]
    [InlineData("0407:000004070")]
    [InlineData(" 0407")]
    [InlineData("0x07")]
    [InlineData("zz07:00000407")]
    [InlineData("0407:+0000407")]
    [InlineData("0411:{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}")]
    public void AnythingElseIsNoIdentifier(string? text)
    {
        Assert.False(InputMethodId.TryParse(text, out _));
    }

    [Fact]
    public void TheUsersInputMethodsAreTheirLayoutsWithNames()
    {
        var methods = InputMethods.Read();
        var handles = KeyboardLayout.Installed();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.Contains(method.Handle, handles);
            Assert.Equal((ushort)(method.Handle & 0xFFFF), method.Id.Language);
            Assert.False(string.IsNullOrWhiteSpace(method.LanguageName));
            Assert.Equal(method.Id, InputMethods.IdOf(method.Handle, methods));
            Assert.Equal(method.Handle, InputMethods.HandleOf(method.Id, methods));
        });
        Assert.All(methods.Where(method => !method.Id.IsImeLanguage), method => Assert.False(string.IsNullOrWhiteSpace(method.LayoutName)));
        Assert.Equal(methods.Count, methods.Select(method => method.Id).Distinct().Count());
    }

    [Fact]
    public void ReadingThemChangesNoLayout()
    {
        var thread = KeyboardLayout.OfCurrentThread();
        var front = KeyboardLayout.OfForegroundWindow();
        var handles = KeyboardLayout.Installed();

        _ = InputMethods.Read();

        Assert.Equal(thread, KeyboardLayout.OfCurrentThread());
        Assert.Equal(front, KeyboardLayout.OfForegroundWindow());
        Assert.Equal(handles, KeyboardLayout.Installed());
    }

    [Fact]
    public void AnInputMethodTheUserDoesntHaveHasNamesButNoHandle()
    {
        Assert.True(InputMethodId.TryParse("0407:00000407", out var german));
        var described = InputMethods.Describe(german);

        Assert.Equal(german, described.Id);
        Assert.Equal(0, described.Handle);
        Assert.False(string.IsNullOrWhiteSpace(described.LanguageName));
        Assert.False(string.IsNullOrWhiteSpace(described.LayoutName));
        Assert.Null(InputMethods.Describe(new InputMethodId(0x0411, null)).LayoutName);
    }

    [Fact]
    public void AnInputMethodNotInTheListHasNoHandle()
    {
        var methods = InputMethods.Read();

        Assert.Equal(0, InputMethods.HandleOf(new InputMethodId(0x0409, 0xFFFFFFFF), methods));
        Assert.Null(InputMethods.IdOf(0x7FFF7FFF, methods));
    }
}
