using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>What keeps a profile out of the menu, or a name out of the name dialog.</summary>
public enum DisplayProfileProblem
{
    None,
    NoName,
    NameTooLong,

    /// <summary>The name holds control characters, such as a line break.</summary>
    InvalidName,

    /// <summary>Another profile has the same name, ignoring case.</summary>
    NameInUse,

    /// <summary>The profile has more than <see cref="DisplayProfileCheck.MaximumDisplays"/> displays.</summary>
    TooManyDisplays,

    /// <summary>
    /// A display's model, serial number hash, connection or name isn't valid, or the display has
    /// neither a model nor a connection to recognize it by.
    /// </summary>
    InvalidDisplay,

    /// <summary>Two displays have the same connection.</summary>
    SameDisplayTwice,

    NoDisplayOn,

    /// <summary>
    /// A resolution, position or refresh rate is out of range, or an orientation isn't one of the four.
    /// </summary>
    OutOfRange,

    NoMainDisplay,
    SeveralMainDisplays,

    /// <summary>The main display isn't at the top-left corner of the desktop, (0, 0).</summary>
    MainNotAtOrigin,

    /// <summary>Displays overlap without showing the same picture.</summary>
    Overlap,

    /// <summary>The displays leave a gap between them.</summary>
    Gap,
}

/// <summary>
/// Checks a profile's name and setup, for the menu and the page. A hand-edited file can hold
/// anything, so every value is checked before it's used. See "Profiles" in
/// docs/features/display/display-profiles.md.
/// </summary>
public static class DisplayProfileCheck
{
    /// <summary>The longest name, which the menu shows.</summary>
    public const int MaximumNameLength = 60;

    /// <summary>The most displays a profile has, on and off.</summary>
    public const int MaximumDisplays = 16;

    /// <summary>The largest resolution's width or height.</summary>
    public const int MaximumResolution = 16384;

    /// <summary>The highest refresh rate, in hertz.</summary>
    public const int MaximumRefreshRate = 1000;

    // Windows' desktop coordinates; every display's part of the desktop is within them.
    private const int DesktopEdge = 32768;

    // A display's device path and name are much shorter; these only bound a hand-edited file.
    private const int MaximumConnectionLength = 512;
    private const int MaximumDisplayNameLength = 64;

    /// <summary>
    /// The first problem of a profile, or <see cref="DisplayProfileProblem.None"/>. A name that one
    /// of <paramref name="otherNames"/> has, ignoring case, is in use.
    /// </summary>
    public static DisplayProfileProblem Check(DisplayProfile profile, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var problem = CheckName(profile.Name, otherNames);
        return problem != DisplayProfileProblem.None ? problem : CheckDisplays(profile.Displays);
    }

    public static DisplayProfileProblem CheckName(string name, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(otherNames);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return DisplayProfileProblem.NoName;
        }

        if (trimmed.Length > MaximumNameLength)
        {
            return DisplayProfileProblem.NameTooLong;
        }

        if (HasControlCharacters(trimmed))
        {
            return DisplayProfileProblem.InvalidName;
        }

        return otherNames.Any(other => string.Equals(other.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ? DisplayProfileProblem.NameInUse
            : DisplayProfileProblem.None;
    }

    /// <summary>
    /// The first problem of a profile's displays: what recognizes and names each one, and for the
    /// displays that are on, their modes, the main display and the arrangement. Displays with the
    /// same part of the desktop show the same picture.
    /// </summary>
    public static DisplayProfileProblem CheckDisplays(IReadOnlyList<ProfileDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);

        if (displays.Count > MaximumDisplays)
        {
            return DisplayProfileProblem.TooManyDisplays;
        }

        if (!displays.All(IsValidIdentity))
        {
            return DisplayProfileProblem.InvalidDisplay;
        }

        var connections = displays.Where(display => display.Connection.Length > 0).Select(display => display.Connection);
        if (connections.Count() != connections.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            return DisplayProfileProblem.SameDisplayTwice;
        }

        var on = displays.Where(display => display.On).ToList();
        if (on.Count == 0)
        {
            return DisplayProfileProblem.NoDisplayOn;
        }

        if (!on.All(IsInRange))
        {
            return DisplayProfileProblem.OutOfRange;
        }

        var main = on.Where(display => display.Main).ToList();
        if (main.Count == 0)
        {
            return DisplayProfileProblem.NoMainDisplay;
        }

        if (main.Count > 1)
        {
            return DisplayProfileProblem.SeveralMainDisplays;
        }

        if (main[0].X != 0 || main[0].Y != 0)
        {
            return DisplayProfileProblem.MainNotAtOrigin;
        }

        // Displays with the same area show the same picture, so each area counts once.
        var areas = on.Select(display => display.Area()).Distinct().ToList();
        if (areas.Any(area => areas.Any(other => other != area && other.IntersectsWith(area))))
        {
            return DisplayProfileProblem.Overlap;
        }

        return AreConnected(areas) ? DisplayProfileProblem.None : DisplayProfileProblem.Gap;
    }

    private static bool IsValidIdentity(ProfileDisplay display) =>
        (display.Model.Length > 0 || display.Connection.Length > 0)
        && (display.Model.Length == 0 || IsModel(display.Model))
        && (display.Serial.Length == 0 || IsSerialHash(display.Serial))
        && display.Connection.Length <= MaximumConnectionLength && !HasControlCharacters(display.Connection)
        && display.Name.Length <= MaximumDisplayNameLength && !HasControlCharacters(display.Name);

    // Three capital letters for the maker and four hexadecimal digits for the product, such as DELA0A3.
    private static bool IsModel(string model) =>
        model.Length == 7 && model[..3].All(char.IsAsciiLetterUpper) && model[3..].All(char.IsAsciiHexDigitUpper);

    private static bool IsSerialHash(string serial) =>
        serial.Length == 16 && serial.All(char.IsAsciiHexDigitLower);

    private static bool IsInRange(ProfileDisplay display)
    {
        if (display.Width is < 1 or > MaximumResolution || display.Height is < 1 or > MaximumResolution
            || !Enum.IsDefined(display.Orientation)
            || display.RefreshRate.Numerator == 0 || display.RefreshRate.Denominator == 0
            || display.RefreshRate.Hertz() > MaximumRefreshRate)
        {
            return false;
        }

        var area = display.Area();
        return area.Left >= -DesktopEdge && area.Top >= -DesktopEdge && (long)area.Left + area.Width <= DesktopEdge && (long)area.Top + area.Height <= DesktopEdge;
    }

    // Whether every area can be reached from the first through areas that touch, at an edge or a corner.
    private static bool AreConnected(List<Rectangle> areas)
    {
        var reached = new HashSet<Rectangle> { areas[0] };
        var waiting = new Queue<Rectangle>(reached);
        while (waiting.TryDequeue(out var area))
        {
            foreach (var other in areas.Where(other => !reached.Contains(other) && Touch(area, other)))
            {
                reached.Add(other);
                waiting.Enqueue(other);
            }
        }

        return reached.Count == areas.Count;
    }

    private static bool Touch(Rectangle a, Rectangle b) =>
        a.Left <= b.Right && b.Left <= a.Right && a.Top <= b.Bottom && b.Top <= a.Bottom;

    private static bool HasControlCharacters(string text) => text.Any(char.IsControl);
}
