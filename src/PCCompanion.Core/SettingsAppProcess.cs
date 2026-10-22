using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Core;

/// <summary>
/// What the background host needs to know about the settings app.
/// </summary>
public static class SettingsAppProcess
{
    /// <summary>The settings app's executable, installed next to the host.</summary>
    public const string FileName = "PCCompanion.exe";

    /// <summary>
    /// The signal that asks the settings app to close, one per user session. The host sends it
    /// when the user chooses Exit in its tray menu, and before setup removes or replaces the app's files.
    /// </summary>
    public const string ExitSignalName = @"Local\PCCompanion.App.Exit";

    /// <summary>
    /// Followed by a feature's ID, opens the settings app on that feature's page, such as
    /// <c>PCCompanion.exe --feature networkProfiles</c>. A settings app that's open shows the page.
    /// </summary>
    public const string FeatureArgument = "--feature";

    /// <summary>The arguments that open the settings app on the page of the feature with <paramref name="featureId"/>.</summary>
    public static IReadOnlyList<string> FeatureArguments(string featureId)
    {
        if (!IsFeatureId(featureId))
        {
            throw new ArgumentException("A feature's ID has only ASCII letters and digits.", nameof(featureId));
        }

        return [FeatureArgument, featureId];
    }

    /// <summary>
    /// The ID of the feature whose page <paramref name="arguments"/> ask for, or null. Anything that
    /// isn't an ID, of ASCII letters and digits only, is ignored.
    /// </summary>
    public static string? FeatureFromArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], FeatureArgument, StringComparison.OrdinalIgnoreCase) && IsFeatureId(arguments[index + 1]))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static bool IsFeatureId(string? text) => !string.IsNullOrEmpty(text) && text.All(char.IsAsciiLetterOrDigit);
}
