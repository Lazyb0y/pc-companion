using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>
/// The command that runs an action through the host: <c>PCCompanion.exe --run &lt;feature&gt;
/// &lt;action&gt; [&lt;target&gt;]</c>. See "The command" in docs/features/input/launcher-actions.md.
/// </summary>
public static class LauncherCommand
{
    /// <summary>The settings app's argument that runs an action instead of opening a window.</summary>
    public const string RunArgument = "--run";

    /// <summary>The longest feature or action ID, far more than any has.</summary>
    public const int MaximumIdLength = 64;

    /// <summary>The longest target: a profile's name, or an app's app ID or program.</summary>
    public const int MaximumTargetLength = 256;

    /// <summary>
    /// The action that the arguments after <see cref="RunArgument"/> name, or <see langword="null"/>
    /// when they aren't a command: two or three of them, IDs of ASCII letters and digits, and a
    /// target without control characters, each no longer than its limit.
    /// </summary>
    public static FeatureAction? Read(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count is < 2 or > 3)
        {
            return null;
        }

        var action = new FeatureAction(arguments[0], arguments[1], arguments.Count == 3 ? arguments[2] : null);
        return IsValid(action) ? action : null;
    }

    /// <summary>Whether <paramref name="action"/> keeps the command's rules, which the host checks again for each request.</summary>
    public static bool IsValid(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return IsId(action.FeatureId) && IsId(action.ActionId) && (action.Target is null || IsTarget(action.Target));
    }

    /// <summary>The arguments that run <paramref name="action"/>: <see cref="RunArgument"/>, its IDs and its target, if any.</summary>
    public static IReadOnlyList<string> Arguments(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action.Target is { } target
            ? [RunArgument, action.FeatureId, action.ActionId, target]
            : [RunArgument, action.FeatureId, action.ActionId];
    }

    private static bool IsId(string? id) => id is { Length: > 0 and <= MaximumIdLength } && id.All(char.IsAsciiLetterOrDigit);

    private static bool IsTarget(string? target) => target is { Length: > 0 and <= MaximumTargetLength } && !target.Any(char.IsControl);
}
