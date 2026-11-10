using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>Which connected display each of a profile's displays is.</summary>
/// <param name="Displays">
/// For each of the profile's displays, in its order, the connected display it is; <see langword="null"/>
/// for one that isn't connected.
/// </param>
/// <param name="InConnectionOrder">
/// Whether identical displays couldn't be told apart, and were taken in the order of their connections.
/// </param>
/// <param name="CanBeUsed">Whether every display that's on in the profile is connected and recognized.</param>
public sealed record Recognition(IReadOnlyList<ConnectedDisplay?> Displays, bool InConnectionOrder, bool CanBeUsed);

/// <summary>
/// Recognizes a profile's displays among the connected ones, and tells whether the setup in use is
/// a profile's. See "Recognizing displays" and "Profiles" in docs/features/display/display-profiles.md.
/// </summary>
public static class ProfileRecognition
{
    /// <summary>
    /// Recognizes each of a profile's displays as <see cref="DisplayRecognition.Recognize"/> does,
    /// with the displays that are on first at each step, so a display that's off never takes one
    /// they need.
    /// </summary>
    public static Recognition Recognize(DisplayProfile profile, CurrentSetup current)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);

        var wanted = profile.Displays;
        var known = wanted.Select(display => new KnownDisplay(display.Model, display.Serial, display.Connection, display.BuiltIn)).ToList();
        var recognized = DisplayRecognition.Recognize(known, current.Setup.Displays, current.SerialOf, i => wanted[i].On);
        var canBeUsed = Enumerable.Range(0, wanted.Count).All(i => !wanted[i].On || recognized.Displays[i] is not null);
        return new Recognition(recognized.Displays, recognized.InConnectionOrder, canBeUsed);
    }

    /// <summary>
    /// Whether the setup in use is a profile's: the same displays are on, with the same main
    /// display, positions, resolutions and orientations, the same refresh rates to two decimal
    /// places, as Windows Settings shows them, and the same HDR on displays that support it.
    /// </summary>
    public static bool Matches(DisplayProfile profile, CurrentSetup current)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);

        var recognition = Recognize(profile, current);
        return recognition.CanBeUsed
            && profile.Displays.Count(display => display.On) == current.Setup.Paths.Count
            && DisplaysNotAsInProfile(profile, current, recognition).Count == 0;
    }

    /// <summary>
    /// The displays that are on in a profile but aren't as the profile has them: not connected,
    /// off, or in another place or mode. After a switch, these are the ones Windows applied in its
    /// own way.
    /// </summary>
    /// <returns>Their places in the profile's list.</returns>
    public static IReadOnlyList<int> DisplaysNotAsInProfile(DisplayProfile profile, CurrentSetup current, Recognition recognition)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(recognition);

        return [.. Enumerable.Range(0, profile.Displays.Count).Where(i =>
            profile.Displays[i].On
            && !(recognition.Displays[i] is { } display && current.PathOf(display) is { } path && Matches(profile.Displays[i], path)))];
    }

    private static bool Matches(ProfileDisplay wanted, DisplayPath path) =>
        wanted.Area() == path.Area
        && wanted.Orientation == path.Orientation
        && Math.Round(wanted.RefreshRate.Hertz(), 2) == Math.Round(path.RefreshRate.Hertz, 2)
        && (wanted.Hdr is not { } hdr || path.Hdr == DisplayHdr.NotSupported || hdr == (path.Hdr == DisplayHdr.On));
}
