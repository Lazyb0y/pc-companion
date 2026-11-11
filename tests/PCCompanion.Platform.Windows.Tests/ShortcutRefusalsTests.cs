using System.Text;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ShortcutRefusalsTests
{
    [Fact]
    public void NoShortcutIsRefusedAtFirst()
    {
        Assert.Empty(new ShortcutRefusals().All);
    }

    [Fact]
    public void AFeatureSaysWhichOfItsShortcutsWereRefused()
    {
        var refusals = new ShortcutRefusals();

        refusals.Set("hideApps", ["hideOthers"]);
        refusals.Set("endApp", ["chooseApp"]);

        Assert.Equal([new RefusedShortcut("hideApps", "hideOthers"), new RefusedShortcut("endApp", "chooseApp")], refusals.All);
    }

    [Fact]
    public void AFeatureReplacesWhatItSaidBefore()
    {
        var refusals = new ShortcutRefusals();
        refusals.Set("hideApps", ["hideApp", "hideOthers"]);

        refusals.Set("hideApps", ["hideApp"]);

        Assert.Equal([new RefusedShortcut("hideApps", "hideApp")], refusals.All);
    }

    [Fact]
    public void NoneRefusedClearsTheFeature()
    {
        var refusals = new ShortcutRefusals();
        refusals.Set("hideApps", ["hideApp"]);

        refusals.Set("hideApps", []);

        Assert.Empty(refusals.All);
    }

    [Fact]
    public void AShortcutNamedTwiceIsListedOnce()
    {
        var refusals = new ShortcutRefusals();

        refusals.Set("dropDownApps", ["WindowsControlAlt1", "WindowsControlAlt1"]);

        Assert.Single(refusals.All);
    }

    [Fact]
    public void TheListReadsBackAsWritten()
    {
        var refusals = new ShortcutRefusals();
        refusals.Set("clipboardActions", ["copiedText", "selectedText"]);

        Assert.Equal(refusals.All, ShortcutRefusals.FromJson(refusals.ToJson()));
    }

    [Fact]
    public void ThePipeGivesOnlyTheFeaturesAndTheirShortcuts()
    {
        var refusals = new ShortcutRefusals();
        refusals.Set("clipboardActions", ["selectedText"]);

        Assert.Equal("""[{"feature":"clipboardActions","shortcut":"selectedText"}]""", Encoding.UTF8.GetString(refusals.ToJson()));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("""{"feature":"hideApps","shortcut":"hideApp"}""")]
    [InlineData("""[{"feature":"hideApps"}]""")]
    [InlineData("""[{"feature":"hideApps","shortcut":null}]""")]
    [InlineData("[null]")]
    public void AListThatCantBeReadIsNone(string json)
    {
        Assert.Null(ShortcutRefusals.FromJson(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void APageFindsItsOwnShortcuts()
    {
        RefusedShortcut[] refused = [new("hideApps", "hideApp"), new("endApp", "chooseApp"), new("hideApps", "hideOthers")];

        Assert.Equal(["hideApp", "hideOthers"], ShortcutRefusals.Of(refused, "hideApps"));
        Assert.Empty(ShortcutRefusals.Of(refused, "minimizeToTray"));
    }
}
