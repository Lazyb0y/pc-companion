using System;
using System.Text.Json.Serialization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// A listed app, as the taskbar groups apps, and the input method it switches to each time it
/// comes to the front. It has an app ID or a program, not both, like a <see cref="ListedApp"/>.
/// </summary>
public sealed record AppLanguage
{
    /// <summary>The name the page shows, as it was when the app was added.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The app ID (AppUserModelID) that the app's windows or its Store package have.</summary>
    public string? AppId { get; set; }

    /// <summary>The full path of the app's program file, for an app without an app ID.</summary>
    public string? Program { get; set; }

    /// <summary>
    /// Windows' identifier of the input method, such as <c>0407:00000407</c> (see
    /// <see cref="InputMethodId"/>), as the file has it: one that doesn't parse leaves the rule out.
    /// </summary>
    public string? InputMethod { get; set; }

    /// <summary>The app, without its input method.</summary>
    [JsonIgnore]
    public ListedApp App => new() { Name = Name, AppId = AppId, Program = Program };

    /// <summary>The rule for <paramref name="app"/>, switching it to <paramref name="inputMethod"/>.</summary>
    public static AppLanguage For(ListedApp app, InputMethodId inputMethod)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new AppLanguage { Name = app.Name, AppId = app.AppId, Program = app.Program, InputMethod = inputMethod.ToString() };
    }

    /// <summary>Reads <see cref="InputMethod"/>; false when it doesn't parse.</summary>
    public bool TryGetInputMethod(out InputMethodId inputMethod) => InputMethodId.TryParse(InputMethod, out inputMethod);
}
