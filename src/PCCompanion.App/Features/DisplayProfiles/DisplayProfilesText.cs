using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>The page's and the name dialog's text for profiles, their displays and their problems.</summary>
public static class DisplayProfilesText
{
    private static readonly CompositeFormat TooLongFormat = CompositeFormat.Parse(Strings.DisplayProfilesNameTooLong);
    private static readonly CompositeFormat ProblemFormat = CompositeFormat.Parse(Strings.DisplayProfilesSummaryProblem);
    private static readonly CompositeFormat TooManyDisplaysFormat = CompositeFormat.Parse(Strings.DisplayProfilesProblemTooManyDisplays);
    private static readonly CompositeFormat LineFormat = CompositeFormat.Parse(Strings.DisplayProfilesLine);
    private static readonly CompositeFormat ResolutionFormat = CompositeFormat.Parse(Strings.DisplayProfilesResolution);
    private static readonly CompositeFormat RefreshRateFormat = CompositeFormat.Parse(Strings.DisplayProfilesRefreshRate);
    private static readonly CompositeFormat UnnamedFormat = CompositeFormat.Parse(Strings.DisplayProfilesUnnamedDisplay);
    private static readonly CompositeFormat SameNameFormat = CompositeFormat.Parse(Strings.DisplayProfilesSameName);
    private static readonly CompositeFormat NotConnectedFormat = CompositeFormat.Parse(Strings.DisplayProfilesNotConnected);

    /// <summary>What to do about a problem, or an empty string for none.</summary>
    public static string Problem(DisplayProfileProblem problem) => problem switch
    {
        DisplayProfileProblem.None => string.Empty,
        DisplayProfileProblem.NoName => Strings.DisplayProfilesNameMissing,
        DisplayProfileProblem.NameTooLong => string.Format(CultureInfo.CurrentCulture, TooLongFormat, DisplayProfileCheck.MaximumNameLength.ToString("N0", CultureInfo.CurrentCulture)),
        DisplayProfileProblem.InvalidName => Strings.DisplayProfilesNameInvalid,
        DisplayProfileProblem.NameInUse => Strings.DisplayProfilesNameInUse,
        DisplayProfileProblem.TooManyDisplays => string.Format(CultureInfo.CurrentCulture, TooManyDisplaysFormat, DisplayProfileCheck.MaximumDisplays.ToString("N0", CultureInfo.CurrentCulture)),
        DisplayProfileProblem.InvalidDisplay => Strings.DisplayProfilesProblemInvalidDisplay,
        DisplayProfileProblem.SameDisplayTwice => Strings.DisplayProfilesProblemSameDisplayTwice,
        DisplayProfileProblem.NoDisplayOn => Strings.DisplayProfilesProblemNoDisplayOn,
        DisplayProfileProblem.OutOfRange => Strings.DisplayProfilesProblemOutOfRange,
        DisplayProfileProblem.NoMainDisplay => Strings.DisplayProfilesProblemNoMainDisplay,
        DisplayProfileProblem.SeveralMainDisplays => Strings.DisplayProfilesProblemSeveralMainDisplays,
        DisplayProfileProblem.MainNotAtOrigin => Strings.DisplayProfilesProblemMainNotAtOrigin,
        DisplayProfileProblem.Overlap => Strings.DisplayProfilesProblemOverlap,
        _ => Strings.DisplayProfilesProblemGap,
    };

    /// <summary>Why a profile isn't in the menu, such as "Not in the menu: displays overlap."</summary>
    public static string NotInMenu(DisplayProfileProblem problem) =>
        string.Format(CultureInfo.CurrentCulture, ProblemFormat, Problem(problem));

    /// <summary>
    /// A display's name: its name from its EDID, Built-in display for a laptop's own, or Display
    /// and its place in the profile; with its place too when another display of the profile has
    /// the same name.
    /// </summary>
    public static string DisplayName(DisplayProfile profile, int index)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var name = BaseName(profile.Displays[index], index);
        var shared = profile.Displays
            .Select((display, other) => (display, other))
            .Any(pair => pair.other != index && string.Equals(BaseName(pair.display, pair.other), name, StringComparison.OrdinalIgnoreCase));
        return shared
            ? string.Format(CultureInfo.CurrentCulture, SameNameFormat, name, (index + 1).ToString(CultureInfo.CurrentCulture))
            : name;
    }

    /// <summary>
    /// A line for each of a profile's displays, such as "DELL U2720Q: 3840 × 2160, 59.94 Hz, main
    /// display" or "Built-in display: off".
    /// </summary>
    public static IEnumerable<string> Lines(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Displays.Select((display, index) =>
            string.Format(CultureInfo.CurrentCulture, LineFormat, DisplayName(profile, index), Details(display)));
    }

    /// <summary>The note on a profile that can't be used now, naming the displays that aren't connected.</summary>
    public static string NotConnected(DisplayProfile profile, Recognition recognition)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(recognition);

        var missing = profile.Displays
            .Select((display, index) => (display, index))
            .Where(pair => pair.display.On && recognition.Displays[pair.index] is null)
            .Select(pair => DisplayName(profile, pair.index));
        return string.Format(CultureInfo.CurrentCulture, NotConnectedFormat, string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", missing));
    }

    /// <summary>The profile's displays at these places, by name, for notes after Use.</summary>
    public static string Names(DisplayProfile profile, IEnumerable<int> places)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(places);

        return string.Join(
            CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ",
            places.Where(place => place >= 0 && place < profile.Displays.Count).Select(place => DisplayName(profile, place)));
    }

    private static string BaseName(ProfileDisplay display, int index) =>
        display.BuiltIn ? Strings.DisplayProfilesBuiltInDisplay
        : display.Name.Trim().Length > 0 ? display.Name.Trim()
        : string.Format(CultureInfo.CurrentCulture, UnnamedFormat, (index + 1).ToString(CultureInfo.CurrentCulture));

    // The resolution and refresh rate, then the orientation, HDR and main display where they apply,
    // with the list separator of the user's region.
    private static string Details(ProfileDisplay display)
    {
        if (!display.On)
        {
            return Strings.DisplayProfilesOff;
        }

        var parts = new List<string>
        {
            string.Format(CultureInfo.CurrentCulture, ResolutionFormat, display.Width.ToString(CultureInfo.CurrentCulture), display.Height.ToString(CultureInfo.CurrentCulture)),
            string.Format(CultureInfo.CurrentCulture, RefreshRateFormat, Math.Round(display.RefreshRate.Hertz(), 2).ToString("0.##", CultureInfo.CurrentCulture)),
        };

        switch (display.Orientation)
        {
            case DisplayOrientation.Portrait:
                parts.Add(Strings.DisplayProfilesPortrait);
                break;
            case DisplayOrientation.LandscapeFlipped:
                parts.Add(Strings.DisplayProfilesLandscapeFlipped);
                break;
            case DisplayOrientation.PortraitFlipped:
                parts.Add(Strings.DisplayProfilesPortraitFlipped);
                break;
        }

        if (display.Hdr == true)
        {
            parts.Add(Strings.DisplayProfilesHdr);
        }

        if (display.Main)
        {
            parts.Add(Strings.DisplayProfilesMainDisplay);
        }

        return string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", parts);
    }
}
