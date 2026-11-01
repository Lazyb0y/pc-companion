using System.Linq;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class DropDownAppsSettingsTests
{
    private static readonly ListedApp Terminal = new() { Name = "Terminal", AppId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" };
    private static readonly ListedApp Notes = new() { Name = "Notes", Program = @"C:\Apps\Notes.exe" };
    private static readonly ListedApp Mail = new() { Name = "Mail", Program = @"C:\Apps\Mail.exe" };

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithTheSpecsDefaults()
    {
        var settings = DropDownAppsSettings.Read(null);

        Assert.Equal(
            new DropDownAppsSettings
            {
                Enabled = false,
                Apps = [],
                Display = DropDownDisplay.WithPointer,
                Width = 100,
                Height = 50,
                Slide = true,
                HideWhenInactive = HideWhenInactive.AnyDisplay,
                KeepOnTop = false,
            },
            settings);
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = DropDownAppsSettings.Read(Section("""
            {
              "enabled": true,
              "apps": [
                { "name": "Terminal", "appId": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "shortcut": "WindowsControlAboveTab" },
                { "name": "Obsidian", "program": "C:\\Users\\Me\\AppData\\Local\\Programs\\Obsidian\\Obsidian.exe", "shortcut": "WindowsControlAlt1" }
              ],
              "display": "WithPointer",
              "width": 100,
              "height": 50,
              "slide": true,
              "hideWhenInactive": "AnyDisplay",
              "keepOnTop": false
            }
            """));

        Assert.Equal(
            new DropDownAppsSettings
            {
                Enabled = true,
                Apps =
                [
                    DropDownApp.For(Terminal, DropDownShortcut.WindowsControlAboveTab),
                    DropDownApp.For(new ListedApp { Name = "Obsidian", Program = @"C:\Users\Me\AppData\Local\Programs\Obsidian\Obsidian.exe" }, DropDownShortcut.WindowsControlAlt1),
                ],
            },
            settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new DropDownAppsSettings { Enabled = true }, DropDownAppsSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new DropDownAppsSettings
        {
            Enabled = true,
            Apps = [DropDownApp.For(Notes, DropDownShortcut.WindowsAltAboveTab), DropDownApp.For(Terminal, DropDownShortcut.None)],
            Display = DropDownDisplay.Main,
            Width = 60,
            Height = 35,
            Slide = false,
            HideWhenInactive = HideWhenInactive.SameDisplay,
            KeepOnTop = true,
        };

        Assert.Equal(settings, DropDownAppsSettings.Read(settings.Write()));
    }

    [Fact]
    public void ListedAppsThatDontMatchTheFormatOrAreListedTwiceAreLeftOut()
    {
        var settings = DropDownAppsSettings.Read(Section("""
            {
              "apps": [
                null,
                { "name": "Blank", "program": "  " },
                { "name": "Both", "program": "C:\\a.exe", "appId": "A" },
                { "name": "Neither" },
                { "name": "Kept", "appId": "B", "program": "", "shortcut": "WindowsControlAlt2" },
                { "name": "Again", "appId": "b" }
              ]
            }
            """));

        Assert.Equal([new DropDownApp { Name = "Kept", AppId = "B", Shortcut = DropDownShortcut.WindowsControlAlt2 }], settings.Apps);
    }

    [Fact]
    public void UnknownChoicesTakeTheDefaultsAndSizesAreBroughtIntoRange()
    {
        var settings = DropDownAppsSettings.Read(Section("""{ "display": 7, "hideWhenInactive": 9, "width": 10, "height": 500 }"""));

        Assert.Equal((DropDownDisplay.WithPointer, HideWhenInactive.AnyDisplay, 30, 100), (settings.Display, settings.HideWhenInactive, settings.Width, settings.Height));
        Assert.Equal((100, 20), (DropDownAppsSettings.Read(Section("""{ "width": 101, "height": -5 }""")).Width, DropDownAppsSettings.Read(Section("""{ "height": -5 }""")).Height));
    }

    [Fact]
    public void AnAddedAppGetsTheFirstShortcutNoOtherAppHas()
    {
        var settings = new DropDownAppsSettings().WithApp(Terminal).WithApp(Notes);
        Assert.Equal([DropDownShortcut.WindowsControlAboveTab, DropDownShortcut.WindowsAltAboveTab], settings.Apps.Select(app => app.Shortcut));

        settings = settings.WithShortcutAt(0, DropDownShortcut.None).WithApp(Mail);

        Assert.Equal([DropDownShortcut.None, DropDownShortcut.WindowsAltAboveTab, DropDownShortcut.WindowsControlAboveTab], settings.Apps.Select(app => app.Shortcut));
    }

    [Fact]
    public void AnAppAddedWhenEveryShortcutIsTakenGetsNone()
    {
        var settings = new DropDownAppsSettings();
        for (var number = 0; number < 11; number++)
        {
            settings = settings.WithApp(new ListedApp { Name = "App", Program = $@"C:\Apps\App{number}.exe" });
        }

        settings = settings.WithApp(Notes);

        Assert.Equal(DropDownShortcuts.All, settings.Apps.Take(11).Select(app => app.Shortcut));
        Assert.Equal(DropDownShortcut.None, settings.Apps[^1].Shortcut);
    }

    [Fact]
    public void AnAppListedAlreadyOrInvalidIsNotAdded()
    {
        var settings = new DropDownAppsSettings().WithApp(Terminal);

        Assert.Same(settings, settings.WithApp(Terminal with { Name = "Other name", AppId = Terminal.AppId!.ToUpperInvariant() }));
        Assert.Same(settings, settings.WithApp(new ListedApp { Name = "Neither" }));
    }

    [Fact]
    public void ChoosingAnotherAppsShortcutLeavesThatAppWithNone()
    {
        var settings = new DropDownAppsSettings().WithApp(Terminal).WithApp(Notes);

        settings = settings.WithShortcutAt(1, DropDownShortcut.WindowsControlAboveTab);

        Assert.Equal([DropDownShortcut.None, DropDownShortcut.WindowsControlAboveTab], settings.Apps.Select(app => app.Shortcut));
    }

    [Fact]
    public void RemovingAnAppKeepsTheOthers()
    {
        var settings = new DropDownAppsSettings().WithApp(Terminal).WithApp(Notes).WithApp(Mail);

        Assert.Equal(["Terminal", "Mail"], settings.WithoutApp(1).Apps.Select(app => app.Name));
    }

    [Fact]
    public void AnUnknownOrRepeatedShortcutIsNoneWithItsProblem()
    {
        var apps = DropDownAppsSettings.Read(Section("""
            {
              "apps": [
                { "name": "A", "appId": "A", "shortcut": "WindowsControlAlt3" },
                { "name": "B", "appId": "B", "shortcut": 99 },
                { "name": "C", "appId": "C", "shortcut": "WindowsControlAlt3" },
                { "name": "D", "appId": "D" }
              ]
            }
            """)).Apps;

        Assert.Equal(
            [
                (DropDownShortcut.WindowsControlAlt3, ShortcutProblem.None),
                (DropDownShortcut.None, ShortcutProblem.Unknown),
                (DropDownShortcut.None, ShortcutProblem.Repeated),
                (DropDownShortcut.None, ShortcutProblem.None),
            ],
            DropDownShortcuts.Assign(apps));
    }

    [Fact]
    public void EachShortcutHasItsKeysByPosition()
    {
        var windowsControl = HotKeyModifiers.Windows | HotKeyModifiers.Control;

        Assert.Equal(new KeyPositionShortcut(windowsControl, 0x29), DropDownShortcuts.KeysOf(DropDownShortcut.WindowsControlAboveTab));
        Assert.Equal(new KeyPositionShortcut(HotKeyModifiers.Windows | HotKeyModifiers.Alt, 0x29), DropDownShortcuts.KeysOf(DropDownShortcut.WindowsAltAboveTab));
        Assert.Equal(new KeyPositionShortcut(windowsControl | HotKeyModifiers.Alt, 0x02), DropDownShortcuts.KeysOf(DropDownShortcut.WindowsControlAlt1));
        Assert.Equal(new KeyPositionShortcut(windowsControl | HotKeyModifiers.Alt, 0x0A), DropDownShortcuts.KeysOf(DropDownShortcut.WindowsControlAlt9));
        Assert.Null(DropDownShortcuts.KeysOf(DropDownShortcut.None));
        Assert.Equal(11, DropDownShortcuts.All.Select(DropDownShortcuts.KeysOf).Distinct().Count());
    }
}
