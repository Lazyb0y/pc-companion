using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// Input Language per App settings, stored in <c>features.inputLanguagePerApp</c>. See
/// docs/features/input/input-language-per-app.md.
/// </summary>
public sealed record InputLanguagePerAppSettings : IFeatureSettings<InputLanguagePerAppSettings>
{
    public bool Enabled { get; set; }

    /// <summary>The listed apps, each with the input method it switches to.</summary>
    public IReadOnlyList<AppLanguage> Apps { get; set; } = [];

    /// <summary>What apps without a rule get.</summary>
    public OtherApps OtherApps { get; set; } = OtherApps.Remember;

    /// <summary>
    /// Windows' identifier of the input method for <see cref="OtherApps.Use"/>, as the file has it,
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? OtherAppsInputMethod { get; set; }

    /// <summary>Whether a notice shows the new language when the feature changed it.</summary>
    public bool ShowNotice { get; set; } = true;

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. Listed apps are read
    /// as Hot Corners reads its list: a blank app ID or program counts as none, and an app with
    /// neither or both, or a null, is left out, as is an app listed a second time. Identifiers are
    /// written as Windows writes them; one that doesn't parse is kept as it is, for the feature to
    /// leave out with a warning. An unknown behavior for other apps is Remember.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static InputLanguagePerAppSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, InputLanguagePerAppJsonContext.Default.InputLanguagePerAppSettings, () => new InputLanguagePerAppSettings());

        // The JSON reader checks nullable annotations on properties but not on a list's items, so a
        // null in the file's list of apps reads as one.
        var apps = new List<AppLanguage>();
        foreach (var rule in settings.Apps.Where(rule => rule is not null).Select(Checked))
        {
            if (rule.App.IsValid && !apps.Any(listed => listed.App.IsSameAppAs(rule.App)))
            {
                apps.Add(rule);
            }
        }

        return settings with
        {
            Apps = apps,

            // A number in the file reads as any enum value, named or not.
            OtherApps = Enum.IsDefined(settings.OtherApps) ? settings.OtherApps : OtherApps.Remember,
            OtherAppsInputMethod = AsWindowsWritesIt(settings.OtherAppsInputMethod),
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, InputLanguagePerAppJsonContext.Default.InputLanguagePerAppSettings);

    public InputLanguagePerAppSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Reads <see cref="OtherAppsInputMethod"/>; false when there's none or it doesn't parse.</summary>
    public bool TryGetOtherAppsInputMethod(out InputMethodId inputMethod) => InputMethodId.TryParse(OtherAppsInputMethod, out inputMethod);

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => Apps.Any(rule => rule.App.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, switching to
    /// <paramref name="inputMethod"/>, unless it's listed already or has neither or both of an app
    /// ID and a program.
    /// </summary>
    public InputLanguagePerAppSettings WithApp(ListedApp app, InputMethodId inputMethod)
    {
        ArgumentNullException.ThrowIfNull(app);

        return !app.IsValid || Lists(app) ? this : this with { Apps = [.. Apps, AppLanguage.For(app, inputMethod)] };
    }

    /// <summary>The settings without the listed app at <paramref name="index"/>.</summary>
    public InputLanguagePerAppSettings WithoutApp(int index) =>
        this with { Apps = [.. Apps.Where((_, at) => at != index)] };

    /// <summary>The settings with the listed app at <paramref name="index"/> switching to <paramref name="inputMethod"/>.</summary>
    public InputLanguagePerAppSettings WithInputMethodAt(int index, InputMethodId inputMethod) =>
        this with { Apps = [.. Apps.Select((rule, at) => at == index ? rule with { InputMethod = inputMethod.ToString() } : rule)] };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(InputLanguagePerAppSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && Apps.SequenceEqual(other.Apps)
        && OtherApps == other.OtherApps
        && OtherAppsInputMethod == other.OtherAppsInputMethod
        && ShowNotice == other.ShowNotice;

    public override int GetHashCode() => HashCode.Combine(Enabled, Apps.Count, OtherApps, OtherAppsInputMethod, ShowNotice);

    private static AppLanguage Checked(AppLanguage rule)
    {
        var app = rule.App.Checked();
        return rule with { AppId = app.AppId, Program = app.Program, InputMethod = AsWindowsWritesIt(rule.InputMethod) };
    }

    private static string? AsWindowsWritesIt(string? inputMethod) =>
        InputMethodId.TryParse(inputMethod, out var id) ? id.ToString() : inputMethod;
}
