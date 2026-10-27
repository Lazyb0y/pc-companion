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
    /// Recognizes each of a profile's displays: by its model and serial number on any connection,
    /// when no other connected display of that model has that serial number; then by its
    /// connection; a built-in display by its model; then by its model alone, when only one display
    /// of that model is connected; and last, identical displays in the order of their connections.
    /// The displays that are on come first at each step, so a display that's off never takes one
    /// they need.
    /// </summary>
    public static Recognition Recognize(DisplayProfile profile, CurrentSetup current)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);

        var wanted = profile.Displays;
        var connected = current.Setup.Displays;
        var found = new ConnectedDisplay?[wanted.Count];
        var taken = new HashSet<DisplayTarget>();
        var order = Enumerable.Range(0, wanted.Count).OrderBy(i => !wanted[i].On).ToList();

        void Take(Func<ProfileDisplay, IEnumerable<ConnectedDisplay>, ConnectedDisplay?> rule)
        {
            foreach (var i in order.Where(i => found[i] is null))
            {
                if (rule(wanted[i], connected.Where(display => !taken.Contains(display.Target))) is { } display)
                {
                    found[i] = display;
                    taken.Add(display.Target);
                }
            }
        }

        Take((display, free) => display.Serial.Length == 0
            ? null
            : OnlyFree(connected.Where(other => other.Model == display.Model && current.SerialOf(other) == display.Serial), free));
        Take((display, free) => display.Connection.Length == 0
            ? null
            : free.FirstOrDefault(other => other.Model == display.Model && string.Equals(other.DevicePath, display.Connection, StringComparison.OrdinalIgnoreCase)));
        Take((display, free) => display.BuiltIn
            ? Only(free.Where(other => other.Connection == DisplayConnection.BuiltIn && other.Model == display.Model))
            : null);
        Take((display, free) => display.Model.Length == 0
            ? null
            : OnlyFree(connected.Where(other => other.Model == display.Model), free));

        // Identical displays that nothing else tells apart.
        var inConnectionOrder = false;
        foreach (var model in order.Where(i => found[i] is null && wanted[i].Model.Length > 0).Select(i => wanted[i].Model).Distinct().ToList())
        {
            var left = order
                .Where(i => found[i] is null && wanted[i].Model == model)
                .OrderBy(i => !wanted[i].On)
                .ThenBy(i => wanted[i].Connection, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var free = connected
                .Where(display => !taken.Contains(display.Target) && display.Model == model)
                .OrderBy(display => display.DevicePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            inConnectionOrder |= left.Count > 1 && free.Count > 1;
            foreach (var (i, display) in left.Zip(free))
            {
                found[i] = display;
                taken.Add(display.Target);
            }
        }

        var canBeUsed = Enumerable.Range(0, wanted.Count).All(i => !wanted[i].On || found[i] is not null);
        return new Recognition(found, inConnectionOrder, canBeUsed);
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

    // The one display of these, when there's exactly one and it's free.
    private static ConnectedDisplay? OnlyFree(IEnumerable<ConnectedDisplay> displays, IEnumerable<ConnectedDisplay> free) =>
        Only(displays) is { } display && free.Contains(display) ? display : null;

    private static ConnectedDisplay? Only(IEnumerable<ConnectedDisplay> displays)
    {
        using var enumerator = displays.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return null;
        }

        var only = enumerator.Current;
        return enumerator.MoveNext() ? null : only;
    }
}
