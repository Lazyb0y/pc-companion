using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

public sealed class ShortcutsAndMouseButtonsSettingsTests
{
    // The example in docs/features/input/shortcuts-and-mouse-buttons.md.
    private const string SpecExample = """
        {
          "enabled": true,
          "triggers": [
            {
              "action": { "feature": "hideApps", "id": "hideOthers" },
              "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyH" }
            },
            {
              "action": { "feature": "dropDownApps", "id": "showOrHide", "target": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" },
              "shortcut": { "modifiers": [], "key": "F13" }
            },
            {
              "action": { "feature": "displayProfiles", "id": "use", "target": "Desk" },
              "mouseButton": { "button": "Forward", "modifiers": [] },
              "excludedApps": [
                { "name": "Microsoft Edge", "appId": "MSEdge" }
              ]
            },
            {
              "action": { "feature": "shortcutsAndMouseButtons", "id": "lock" },
              "mouseButton": { "button": "Middle", "modifiers": [ "Windows" ] }
            }
          ],
          "pauseMouseButtonsInFullScreen": true
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoTriggersAndThePauseOn()
    {
        var settings = ShortcutsAndMouseButtonsSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Triggers);
        Assert.True(settings.PauseMouseButtonsInFullScreen);
    }

    [Fact]
    public void TheSpecsExampleReadsAsFourUsableTriggers()
    {
        var settings = ShortcutsAndMouseButtonsSettings.Read(Section(SpecExample));

        Assert.True(settings.Enabled);
        Assert.Equal(4, settings.Triggers.Count);
        Assert.All(TriggerCheck.CheckAll(settings.Triggers), trigger => Assert.True(trigger.IsUsable));
        Assert.Equal(new FeatureAction("dropDownApps", "showOrHide", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"), settings.Triggers[1].Action.ToFeatureAction());
        Assert.Equal(TriggerShortcut.For(HotKeyModifiers.Control | HotKeyModifiers.Alt, "KeyH"), settings.Triggers[0].Shortcut);
        Assert.Equal(TriggerMouseButton.For(TriggerButton.Middle, HotKeyModifiers.Windows), settings.Triggers[3].MouseButton);
        Assert.Equal("MSEdge", Assert.Single(settings.Triggers[2].ExcludedApps).AppId);
    }

    [Fact]
    public void TheSettingsReadBackAsWrittenAndEqual()
    {
        var settings = ShortcutsAndMouseButtonsSettings.Read(Section(SpecExample));

        var read = ShortcutsAndMouseButtonsSettings.Read(settings.Write());

        Assert.Equal(settings, read);
        Assert.Equal(settings, ShortcutsAndMouseButtonsSettings.Read(Section(SpecExample)));
    }

    [Fact]
    public void TheFormatMatchesTheSpec()
    {
        var json = JsonSerializer.Serialize(new ShortcutsAndMouseButtonsSettings
        {
            Enabled = true,
            Triggers =
            [
                new Trigger { Action = new TriggerAction { Feature = "hideApps", Id = "hideOthers" }, Shortcut = TriggerShortcut.For(HotKeyModifiers.Control | HotKeyModifiers.Alt, "KeyH") },
                new Trigger { Action = new TriggerAction { Feature = "displayProfiles", Id = "use", Target = "Desk" }, MouseButton = TriggerMouseButton.For(TriggerButton.Forward, HotKeyModifiers.None) },
            ],
        }.Write());

        Assert.Equal(
            """{"enabled":true,"triggers":[{"action":{"feature":"hideApps","id":"hideOthers"},"shortcut":{"modifiers":["Control","Alt"],"key":"KeyH"},"excludedApps":[]},{"action":{"feature":"displayProfiles","id":"use","target":"Desk"},"mouseButton":{"button":"Forward","modifiers":[]},"excludedApps":[]}],"pauseMouseButtonsInFullScreen":true}""",
            json);
    }

    [Fact]
    public void AHandEditedSectionIsCleanedUp()
    {
        var settings = ShortcutsAndMouseButtonsSettings.Read(Section("""
            {
              "enabled": true,
              "triggers": [
                null,
                {
                  "action": { "feature": " hideApps ", "id": "hideApp", "target": "  " },
                  "shortcut": { "modifiers": [ "Windows", null ], "key": " KeyJ " },
                  "excludedApps": [ null, { "name": "Both", "appId": "A", "program": "C:\\A.exe" }, { "name": "Notes", "program": "C:\\Notes.exe" } ]
                }
              ]
            }
            """));

        var trigger = Assert.Single(settings.Triggers);
        Assert.Equal(new TriggerAction { Feature = "hideApps", Id = "hideApp" }, trigger.Action);
        Assert.Equal(TriggerShortcut.For(HotKeyModifiers.Windows, "KeyJ"), trigger.Shortcut);
        Assert.Equal("Notes", Assert.Single(trigger.ExcludedApps).Name);
    }

    [Theory]
    [InlineData("""{ "enabled": true, "triggers": 3 }""")]
    [InlineData("""{ "enabled": true, "triggers": [ { "action": null } ] }""")]
    [InlineData("""{ "enabled": true, "triggers": [ { "mouseButton": { "button": "Wheel" } } ] }""")]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ShortcutsAndMouseButtonsSettings.Read(Section(json)));
    }

    [Fact]
    public void TheTriggersAndThePauseCountInTheSettingsEquality()
    {
        var settings = ShortcutsAndMouseButtonsSettings.Read(Section(SpecExample));

        Assert.NotEqual(settings, settings with { PauseMouseButtonsInFullScreen = false });
        Assert.NotEqual(settings, settings with { Triggers = [.. settings.Triggers.Skip(1)] });
        Assert.NotEqual(settings, settings with { Triggers = [.. settings.Triggers.Take(2), settings.Triggers[2] with { ExcludedApps = [] }, settings.Triggers[3]] });
    }
}
