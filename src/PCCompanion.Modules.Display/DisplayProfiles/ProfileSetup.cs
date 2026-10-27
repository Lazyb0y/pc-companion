using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>
/// Turns a profile into a setup Windows can apply, on the adapters and connections its displays
/// are on now, and tells what a switch to it changes. See "Switching" and "Keep or revert" in
/// docs/features/display/display-profiles.md.
/// </summary>
public static class ProfileSetup
{
    /// <summary>
    /// The setup of a profile that can be used: a path for each display that's on in it, on its
    /// current adapter and connection, with the profile's area, refresh rate, orientation and HDR.
    /// Displays with the same area share a source, as with Duplicate; a display keeps the source
    /// it's on when it can. Other connected displays get no path, which turns them off.
    /// </summary>
    /// <returns>The paths; <see langword="null"/> when the adapters don't have a source for each picture.</returns>
    public static IReadOnlyList<DisplayPath>? Build(DisplayProfile profile, CurrentSetup current, Recognition recognition)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(recognition);
        if (!recognition.CanBeUsed)
        {
            throw new ArgumentException("Every display that's on in the profile must be connected.", nameof(recognition));
        }

        var on = Enumerable.Range(0, profile.Displays.Count)
            .Where(i => profile.Displays[i].On)
            .Select(i => (Wanted: profile.Displays[i], Display: recognition.Displays[i]!))
            .ToList();

        // Each picture gets a source that all its displays can show; the pictures with the fewest
        // choices choose first.
        var sources = new Dictionary<Rectangle, DisplaySource>();
        var pictures = on
            .GroupBy(pair => pair.Wanted.Area())
            .Select(group => (Area: group.Key, Displays: group.Select(pair => pair.Display).ToList()))
            .Select(picture => (picture.Area, picture.Displays, Choices: picture.Displays.Skip(1).Aggregate(
                picture.Displays[0].Sources.AsEnumerable(),
                (common, display) => common.Intersect(display.Sources)).ToList()))
            .OrderBy(picture => picture.Choices.Count);
        foreach (var (area, displays, choices) in pictures)
        {
            var free = choices.Where(source => !sources.ContainsValue(source)).ToList();
            if (free.Count == 0)
            {
                return null;
            }

            var inUse = displays.Select(display => current.PathOf(display)?.Source).FirstOrDefault(source => source is { } value && free.Contains(value));
            sources[area] = inUse ?? free.MinBy(source => source.Id);
        }

        return [.. on.Select(pair => new DisplayPath(
            sources[pair.Wanted.Area()],
            pair.Display.Target,
            pair.Wanted.Area(),
            new DisplayRefreshRate(pair.Wanted.RefreshRate.Numerator, pair.Wanted.RefreshRate.Denominator),
            pair.Wanted.Orientation,
            pair.Wanted.Hdr switch
            {
                true => DisplayHdr.On,
                false => DisplayHdr.Off,
                null => DisplayHdr.NotSupported,
            }))];
    }

    /// <summary>
    /// Whether a switch from one setup to another could leave a display black, so the user is
    /// asked to keep it: a display turns on or off, or a display's resolution, refresh rate,
    /// orientation or HDR changes. Moving displays or changing the main display can't.
    /// </summary>
    public static bool AsksToKeep(DisplaySetup before, IReadOnlyList<DisplayPath> setup)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(setup);

        if (!before.Paths.Select(path => path.Target).ToHashSet().SetEquals(setup.Select(path => path.Target)))
        {
            return true;
        }

        return setup.Any(after => before.Paths.First(path => path.Target == after.Target) is var earlier
            && (earlier.Area.Size != after.Area.Size
                || earlier.Orientation != after.Orientation
                || Math.Round(earlier.RefreshRate.Hertz, 2) != Math.Round(after.RefreshRate.Hertz, 2)
                || (after.Hdr != DisplayHdr.NotSupported && earlier.Hdr != DisplayHdr.NotSupported && after.Hdr != earlier.Hdr)));
    }

    /// <summary>
    /// The HDR changes a switch still needs once Windows applied its setup: HDR on or off on each
    /// display that supports it now, and has it otherwise than the setup wants.
    /// </summary>
    public static IReadOnlyList<(DisplayTarget Target, bool On)> HdrToSet(IReadOnlyList<DisplayPath> setup, DisplaySetup after)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(after);

        return [.. setup
            .Where(wanted => wanted.Hdr != DisplayHdr.NotSupported)
            .Where(wanted => after.Paths.FirstOrDefault(path => path.Target == wanted.Target) is { } path
                && path.Hdr != DisplayHdr.NotSupported && path.Hdr != wanted.Hdr)
            .Select(wanted => (wanted.Target, wanted.Hdr == DisplayHdr.On))];
    }

    /// <summary>
    /// The displays that are on in a profile, and on now, whose resolution and refresh rate aren't
    /// among the modes Windows lists for them, which is why Windows refuses a setup. Displays that
    /// are off now can't be asked.
    /// </summary>
    /// <param name="modesOf">The modes Windows lists for the display a source is shown on.</param>
    /// <returns>Their places in the profile's list.</returns>
    public static IReadOnlyList<int> ModesNotOffered(
        DisplayProfile profile,
        CurrentSetup current,
        Recognition recognition,
        Func<DisplaySource, IReadOnlyList<DisplayMode>> modesOf)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(modesOf);

        return [.. Enumerable.Range(0, profile.Displays.Count).Where(i =>
            profile.Displays[i] is { On: true } wanted
            && recognition.Displays[i] is { } display
            && current.PathOf(display) is { } path
            && !modesOf(path.Source).Any(mode =>
                mode.Width == wanted.Width && mode.Height == wanted.Height && Math.Abs(mode.RefreshRate - wanted.RefreshRate.Hertz()) < 1))];
    }
}
