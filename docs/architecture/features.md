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
- `FeatureSettings` in Core reads and writes a section with the feature's JSON context. Home changes only `enabled`, with `FeatureSettings.SetEnabled`, so it needs no feature's format.
- The settings record implements `IFeatureSettings<TSelf>` from Core: `Enabled`, `Read`, `Write` and `WithEnabled`, so code shared by every feature can read, turn on or off and save it.

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
- A feature that offers the host's Settings or Exit, as in its own menu, takes `IHostCommands` from the host. It may call them from any thread; the host runs them on its UI thread.
- **Isolation:** the host's `FeatureRunner` calls each feature inside its own error boundary. When `Apply` throws, it logs the error, stops that feature for the rest of the session, and the other features keep running.
- A feature guards its own handlers of window messages, input and timers the same way: an exception there would end the host.
- A feature that isn't supported on this PC is never started.

## Threads and processes

- Features run in the host process. A feature gets a process of its own only when it needs one: other rights, as the elevated helper has, a UI framework the host doesn't load, or code that could crash the host in a way an error boundary can't catch. Its spec says why.
- `Apply` and the features' handlers of window messages, input and timers run on the host's UI thread, which runs the message loop that input monitoring and hidden windows need. They run one at a time, so a slow handler delays every feature.
- Work on the UI thread takes no more than a few milliseconds. Work that can wait for the disk, the network or another program runs on the thread pool and passes its result back with `MessageWindow.BeginInvoke`: starting processes, file and registry access, listing processes, reading performance counters and network calls.
- Calls to other programs' windows don't wait for them: `SendMessageTimeout` rather than `SendMessage`, `ShowWindowAsync` rather than `ShowWindow`, and `SetWindowPos` with `SWP_ASYNCWINDOWPOS`. A program that stopped responding would otherwise hold the UI thread.
- A feature that works all the time, such as one that reads values every second, or that must answer input at once, such as a low-level keyboard or mouse hook, runs on a thread of its own, with its own message window and message loop. Windows waits for a low-level hook at every key press and pointer move, in every program, and removes a slow hook without telling it.
- An unhandled exception on any thread ends the host, so a feature guards its own threads as it guards its handlers.
- A feature's spec says which thread each part runs on.

## In the settings app

- The navigation pane has a section per module with an item per feature, above **General**.
- **Home** shows a card per feature: icon, name, description, on/off switch, and **Not available on this PC** when it isn't supported.
- A feature page has a header (icon, name, description, on/off switch), then its settings in cards, then a **Permissions & data** card that says what the feature reads and changes. Every feature page uses the same header control, `Controls/FeatureHeader`, which also shows when a change on the page couldn't be saved. The icon and the switch are centered on the name's capital letters as they're drawn, at any text size: the name's box is trimmed to them (`TextLineBounds="Tight"`), with the same padding above and below.
- Settings are cards of a regular kind, each a toolkit `SettingsCard` with its icon from `Glyph` (`Controls/IconCard`) and a control that screen readers name after the header: a setting that's on or off is a `SwitchCard`, on every page including Home, and a choice from a list is a `ChoiceCard`, whose items derive from `ViewModels/Choice` so that they show their `DisplayName`. A card that fits no kind uses `SettingsCard` itself.
- A feature page's view model derives from `FeatureViewModel<TSettings>`, which reads the settings, saves each change right away and gives the header its icon, `IsOn` and `SaveFailed`. A setting's property changes it with `Change`, or calls `Save` itself.
- The settings app maps each `FeatureInfo` to its page in one place, `Features/FeatureCatalog.cs`, which the navigation pane and Home are built from.
- A feature class has a static `Definition` (its `FeatureInfo`) and `CurrentSupport`, so the settings app doesn't create the feature.

## Strings, logging and tests

- Strings go in `Strings.resx`, prefixed with the feature name: `HotCornersActionTaskView`.
- Log with `ILogger<T>` and `[LoggerMessage]`. Use Debug for what happened and why ("top-left corner: Task View"); never log raw input such as every pointer position.
- Keep a feature's decisions in classes that tests can drive without Windows input: fake displays, a fake clock, fake pointer events. Add runtime checks to the feature's group in `scripts/sandbox/groups`; see [Adding a group](../development/testing.md#adding-a-group).

## Adding a feature

1. Spec in `docs/features/<module>/<feature>.md`, including a security and privacy review and the threads its parts run on, approved before code.
2. Feature class and settings, an `IFeatureSettings<TSelf>` record, in the module project; register it in the module's `Add<Module>Module`.
3. Page, with `FeatureHeader`, and its view model, a `FeatureViewModel<TSettings>`, in `src/PCCompanion.App/Features/<Feature>/`, and an entry in `FeatureCatalog.cs`.
4. Strings with translations marked "Needs native review."
5. Tests, Sandbox checks, docs, and `NOTICE.md` and `ThirdPartyNotices.txt` if a new package ships.
