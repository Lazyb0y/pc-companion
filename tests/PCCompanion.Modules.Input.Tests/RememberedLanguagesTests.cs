using System.Globalization;
using System.Linq;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class RememberedLanguagesTests
{
    private static readonly InputMethodId English = FakeInputLanguageSystem.UnitedStates.Id;
    private static readonly InputMethodId German = FakeInputLanguageSystem.German.Id;
    private static readonly InputMethodId Japanese = FakeInputLanguageSystem.Japanese.Id;

    private readonly RememberedLanguages _remembered = new();

    [Fact]
    public void AnAppIsKnownByItsAppIdOtherwiseByItsProgramWhateverTheirCase()
    {
        _remembered.Remember(new AppOfWindow("Browser.Profile1", null, @"C:\Apps\Browser.exe", 10), German);
        _remembered.Remember(new AppOfWindow(null, null, @"C:\Apps\Editor.exe", 20), English);

        Assert.Equal(German, _remembered.Of(new AppOfWindow("browser.profile1", null, @"C:\Apps\Browser.exe", 11)));
        Assert.Null(_remembered.Of(new AppOfWindow("Browser.Profile2", null, @"C:\Apps\Browser.exe", 10)));
        Assert.Null(_remembered.Of(new AppOfWindow(null, null, @"C:\Apps\Browser.exe", 10)));
        Assert.Equal(English, _remembered.Of(new AppOfWindow(null, null, @"c:\apps\editor.exe", 21)));
        Assert.Null(_remembered.Of(new AppOfWindow(null, "Editor.Package!App", @"C:\Apps\Editor.exe", 20)));
    }

    [Fact]
    public void AnAppWithNeitherAnAppIdNorAProgramIsKnownByItsProcess()
    {
        _remembered.Remember(new AppOfWindow(null, null, null, 30), Japanese);

        Assert.Equal(Japanese, _remembered.Of(new AppOfWindow(null, null, null, 30)));
        Assert.Null(_remembered.Of(new AppOfWindow(null, null, null, 31)));
    }

    [Fact]
    public void RememberSaysWhetherWhatsRememberedOrItsOrderChanged()
    {
        Assert.True(_remembered.Remember(App(1), German));
        Assert.False(_remembered.Remember(App(1), German));
        Assert.True(_remembered.Remember(App(1), English));
        Assert.True(_remembered.Remember(App(2), English));
        Assert.True(_remembered.Remember(App(1), English));
        Assert.Equal(2, _remembered.Count);
    }

    [Fact]
    public void WithMoreThan500AppsTheOneUsedLongestAgoGoes()
    {
        for (var app = 0; app < RememberedLanguages.Limit; app++)
        {
            _remembered.Remember(App(app), German);
        }

        _remembered.Remember(App(0), English);
        _remembered.Remember(App(RememberedLanguages.Limit), German);

        Assert.Equal(RememberedLanguages.Limit, _remembered.Count);
        Assert.Null(_remembered.Of(App(1)));
        Assert.Equal(English, _remembered.Of(App(0)));
        Assert.Equal(German, _remembered.Of(App(2)));
        Assert.Equal(German, _remembered.Of(App(RememberedLanguages.Limit)));
    }

    [Fact]
    public void ForgetForgetsEveryApp()
    {
        _remembered.Remember(App(1), German);
        _remembered.Remember(new AppOfWindow(null, null, null, 30), Japanese);

        _remembered.Forget();

        Assert.Equal(0, _remembered.Count);
        Assert.Null(_remembered.Of(App(1)));
    }

    [Fact]
    public void WhileTheFileIsReadNoInputMethodIsFoundButAppsAreStillRemembered()
    {
        _remembered.WaitForLoad();
        _remembered.Remember(App(1), German);

        Assert.False(_remembered.IsLoaded);
        Assert.Null(_remembered.Of(App(1)));

        _remembered.Load([]);

        Assert.True(_remembered.IsLoaded);
        Assert.Equal(German, _remembered.Of(App(1)));
    }

    [Fact]
    public void TheFilesAppsComeAfterTheOnesRememberedSinceAndOnesThatDontMatchTheFormatAreLeftOut()
    {
        _remembered.WaitForLoad();
        _remembered.Remember(App(1), German);

        var added = _remembered.Load(
        [
            new RememberedLanguage { Program = @"c:\apps\app1.exe", InputMethod = "0409:00000409" },
            new RememberedLanguage { AppId = "Browser.Profile1", InputMethod = "0411" },
            null,
            new RememberedLanguage { Program = "  ", InputMethod = "0407:00000407" },
            new RememberedLanguage { AppId = "Both", Program = @"C:\Apps\Both.exe", InputMethod = "0407:00000407" },
            new RememberedLanguage { Program = @"C:\Apps\Unknown.exe", InputMethod = "German" },
            new RememberedLanguage { AppId = "browser.profile1", InputMethod = "0407:00000407" },
            new RememberedLanguage { Program = @"C:\Apps\App2.exe", InputMethod = "0407:00000407" },
        ]);

        Assert.Equal(2, added);
        Assert.Equal(German, _remembered.Of(App(1)));
        Assert.Equal(Japanese, _remembered.Of(new AppOfWindow("Browser.Profile1", null, @"C:\Apps\Browser.exe", 10)));
        Assert.Equal(
            [
                new RememberedLanguage { Program = @"C:\Apps\App1.exe", InputMethod = "0407:00000407" },
                new RememberedLanguage { AppId = "Browser.Profile1", InputMethod = "0411" },
                new RememberedLanguage { Program = @"C:\Apps\App2.exe", InputMethod = "0407:00000407" },
            ],
            _remembered.Saved);
    }

    [Fact]
    public void TheFileAddsAppsOnlyUpToTheLimit()
    {
        _remembered.Remember(App(0), German);

        var added = _remembered.Load(Enumerable.Range(1, RememberedLanguages.Limit).Select(number => new RememberedLanguage { Program = App(number).Program, InputMethod = "0409:00000409" }));

        Assert.Equal(RememberedLanguages.Limit - 1, added);
        Assert.Equal(RememberedLanguages.Limit, _remembered.Count);
        Assert.Null(_remembered.Of(App(RememberedLanguages.Limit)));
    }

    [Fact]
    public void AppsKnownOnlyByTheirProcessArentSaved()
    {
        _remembered.Remember(new AppOfWindow(null, null, null, 30), Japanese);
        _remembered.Remember(new AppOfWindow(null, "Store.App!App", @"C:\Program Files\WindowsApps\App.exe", 31), German);

        Assert.Equal([new RememberedLanguage { AppId = "Store.App!App", InputMethod = "0407:00000407" }], _remembered.Saved);
    }

    private static AppOfWindow App(int number) => new(null, null, string.Create(CultureInfo.InvariantCulture, $@"C:\Apps\App{number}.exe"), (uint)number);
}
