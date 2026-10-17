# Features

## Terms

- **Module:** a group of related features, with one project and one section in the settings app's navigation pane. Example: **Input & Triggers**.
- **Feature:** something the user turns on or off, with its own settings, settings page and Home card. Example: **Hot Corners**, in Input & Triggers.

## Projects

- `PCCompanion.Modules.<Module>` holds the module's features: the logic that runs in the host, each feature's settings type and its JSON format. It references only Core, Platform.Windows and Localization, never another module, and has no UI framework. Hot Corners goes in `PCCompanion.Modules.Input`.
- Feature pages are in the settings app, under `src/PCCompanion.App/Features/<Feature>/`, so the host never loads WinUI and modules need no UI projects.
- The host and the settings app reference every module project.

## Settings

- Each feature owns one section in `settings.json`, under `features`, named after the feature's ID: `features.hotCorners`. Its module defines the settings record, the defaults and a JSON source-generation context; Infrastructure stores each section as JSON. Adding a feature changes no shared project, and sections of unknown features are kept when the file is saved.
- Every section has `enabled`, `false` by default. A missing section means the feature is off with its defaults.
- Only the settings app saves. The host loads the sections at startup and after every save.

## Running in the host

```csharp
// PCCompanion.Core
public sealed record FeatureInfo(string Id, string ModuleId, string NameKey, string DescriptionKey, string Glyph);

public interface IFeature : IDisposable
{
    FeatureInfo Info { get; }

    // Whether this PC can run the feature, for example "needs Windows 11"; shared with the settings app.
    FeatureSupport Support { get; }

    // Called on the host's UI thread at startup and after every settings change, with the feature's
    // section (null when there is none). Starts, stops or reconfigures the feature.
    void Apply(JsonElement? settings);
}
```

- Each module registers its features with the host: `services.AddInputModule()`.
- `Apply` runs on the host's UI thread, which runs the message loop that input monitoring and hidden windows need. Long work goes to the thread pool; nothing blocks the message loop.
- **Isolation:** the host calls each feature inside its own error boundary. When `Apply` throws, the host logs the error, stops that feature for the rest of the session, and the other features keep running.
- A feature that isn't supported on this PC is never started.

## In the settings app

- The navigation pane has a section per module with an item per feature, above **General**.
- **Home** shows a card per feature: icon, name, description, on/off switch, and **Not available on this PC** when it isn't supported.
- A feature page has a header (icon, name, description, on/off switch), then its settings in cards, then a **Permissions & data** card that says what the feature reads and changes.
- The settings app maps each `FeatureInfo` to its page in one place, `Features/FeatureCatalog.cs`.

## Strings, logging and tests

- Strings go in `Strings.resx`, prefixed with the feature name: `HotCornersActionTaskView`.
- Log with `ILogger<T>` and `[LoggerMessage]`. Use Debug for what happened and why ("top-left corner: Task View"); never log raw input such as every pointer position.
- Keep a feature's decisions in classes that tests can drive without Windows input: fake displays, a fake clock, fake pointer events. Add runtime checks to `scripts/sandbox/runtime-checks.ps1`.

## Adding a feature

1. Spec in `docs/features/<module>/<feature>.md`, including a security and privacy review, approved before code.
2. Feature class and settings in the module project; register it in the module's `Add<Module>Module`.
3. Page in `src/PCCompanion.App/Features/<Feature>/` and an entry in `FeatureCatalog.cs`.
4. Strings with translations marked "Needs native review."
5. Tests, Sandbox checks, docs, and `NOTICE.md` and `ThirdPartyNotices.txt` if a new package ships.
