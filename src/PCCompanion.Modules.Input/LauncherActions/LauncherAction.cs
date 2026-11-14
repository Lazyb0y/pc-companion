using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>
/// An action that launchers and the command run, with its name and its feature's in the user's
/// language, such as "Use Office" and "Network Profiles".
/// </summary>
public sealed record LauncherAction(FeatureAction Action, string Name, string FeatureName);

/// <summary>A shortcut in Start that runs an action: its file name without <c>.lnk</c>, its arguments, and its description.</summary>
/// <param name="Arguments">The command's arguments after the program, as one command line.</param>
/// <param name="Description">The feature's name, which launchers show under the entry's name.</param>
public sealed record StartEntry(string Name, string Arguments, string Description);

/// <summary>
/// What the settings app and the command make of the actions: their Start entries, their commands,
/// and the action a command names. See "Start entries" in docs/features/input/launcher-actions.md.
/// </summary>
public static class LauncherActionList
{
    /// <summary>The most characters an entry's name has, without <c>.lnk</c>.</summary>
    public const int MaximumNameLength = 80;

    /// <summary>
    /// The Start entries for <paramref name="actions"/>, in their order, each named with
    /// <paramref name="nameFormat"/>, such as "{0} - {1}" for "Use Office - Network Profiles".
    /// Characters Windows doesn't allow in file names become spaces, a long name is cut, and a name
    /// that's already taken, ignoring case, gets " (2)", " (3)" and so on.
    /// </summary>
    public static IReadOnlyList<StartEntry> Entries(IEnumerable<LauncherAction> actions, string nameFormat)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(nameFormat);

        var format = CompositeFormat.Parse(nameFormat);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<StartEntry>();
        foreach (var action in actions)
        {
            var name = FileNameOf(string.Format(CultureInfo.CurrentCulture, format, action.Name, action.FeatureName));
            var unique = name;
            for (var number = 2; !taken.Add(unique); number++)
            {
                var suffix = string.Create(CultureInfo.InvariantCulture, $" ({number})");
                unique = Cut(name, MaximumNameLength - suffix.Length) + suffix;
            }

            entries.Add(new StartEntry(unique, CommandLine.Join(LauncherCommand.Arguments(action.Action)), action.FeatureName));
        }

        return entries;
    }

    /// <summary>
    /// The command that runs <paramref name="action"/> with the program at <paramref name="program"/>,
    /// as Copy command writes it, such as <c>"C:\Program Files\PC Companion\PCCompanion.exe" --run networkProfiles use Office</c>.
    /// </summary>
    public static string Command(string program, FeatureAction action)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(action);

        return CommandLine.Join([program, .. LauncherCommand.Arguments(action)]);
    }

    /// <summary>
    /// The action that <paramref name="asked"/> names among <paramref name="actions"/>, with the IDs
    /// as they're written there, since a command's IDs ignore case, and the target as asked, which
    /// its feature matches as it does. <paramref name="asked"/> itself when none of them has its IDs.
    /// </summary>
    public static FeatureAction WithIdsOf(IEnumerable<LauncherAction> actions, FeatureAction asked)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(asked);

        return actions.FirstOrDefault(known =>
                string.Equals(known.Action.FeatureId, asked.FeatureId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(known.Action.ActionId, asked.ActionId, StringComparison.OrdinalIgnoreCase)) is { } found
            ? asked with { FeatureId = found.Action.FeatureId, ActionId = found.Action.ActionId }
            : asked;
    }

    /// <summary>
    /// <paramref name="name"/> as a file name Windows takes and Start shows as it is: characters
    /// Windows doesn't allow become spaces, such as a colon, which would save the name as a file with
    /// a hidden stream instead; spaces and dots at the end, which Windows drops, are left out; and a
    /// long name is cut to <see cref="MaximumNameLength"/> characters.
    /// </summary>
    public static string FileNameOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(character => Array.IndexOf(invalid, character) >= 0 ? ' ' : character)]);
        return Cut(cleaned.Trim(), MaximumNameLength);
    }

    // Never between the two halves of a character outside the Basic Multilingual Plane.
    private static string Cut(string name, int length)
    {
        if (name.Length > length)
        {
            name = name[..(char.IsHighSurrogate(name[length - 1]) ? length - 1 : length)];
        }

        return name.TrimEnd(' ', '.');
    }
}
