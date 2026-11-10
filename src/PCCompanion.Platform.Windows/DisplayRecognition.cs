using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A display as a feature keeps it, to recognize it again among the connected ones.
/// </summary>
/// <param name="Model">The maker's code and the product code from its EDID; empty when it reported none.</param>
/// <param name="Serial">A hash of its serial number (<see cref="DisplayRecognition.HashSerial"/>); empty when it has none.</param>
/// <param name="Connection">Its device path when it was kept; empty when it isn't known.</param>
/// <param name="BuiltIn">Whether it's a laptop's or tablet's own screen.</param>
public readonly record struct KnownDisplay(string Model, string Serial, string Connection, bool BuiltIn);

/// <summary>Which connected display each known display is.</summary>
/// <param name="Displays">
/// For each known display, in its order, the connected display it is; <see langword="null"/> for
/// one that isn't connected or can't be recognized.
/// </param>
/// <param name="InConnectionOrder">
/// Whether identical displays couldn't be told apart, and were taken in the order of their connections.
/// </param>
public sealed record RecognizedDisplays(IReadOnlyList<ConnectedDisplay?> Displays, bool InConnectionOrder);

/// <summary>
/// Recognizes displays that features keep, such as a display profile's, among the connected ones,
/// on any connection. See "Recognizing displays" in docs/features/display/display-profiles.md.
/// </summary>
public static class DisplayRecognition
{
    // A hash of 8 bytes tells a few displays apart, and is all that's kept.
    private const int SerialHashBytes = 8;

    /// <summary>
    /// A hash of a display's serial number, 16 hexadecimal digits, so the settings keep no serial
    /// number; empty for a display without one. See "Security and privacy" in
    /// docs/features/display/display-profiles.md.
    /// </summary>
    public static string HashSerial(DisplaySerialNumber? serial)
    {
        if (serial is not { } value)
        {
            return string.Empty;
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"{value.Number:X8}:{value.Text}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, SerialHashBytes);
    }

    /// <summary>
    /// Recognizes each known display: by its model and serial number on any connection, when no
    /// other connected display of that model has that serial number; then by its connection; a
    /// built-in display by its model; then by its model alone, when only one display of that model
    /// is connected; and last, identical displays in the order of their connections. The displays
    /// that come first are tried first at each step, so the others never take one they need.
    /// </summary>
    /// <param name="known">The displays to recognize.</param>
    /// <param name="connected">The connected displays.</param>
    /// <param name="serialOf">The serial number hash of a connected display; empty when it has none.</param>
    /// <param name="comesFirst">
    /// Whether the known display at an index is tried first, such as a profile's displays that are
    /// on; every display is equal when it's <see langword="null"/>.
    /// </param>
    public static RecognizedDisplays Recognize(
        IReadOnlyList<KnownDisplay> known,
        IReadOnlyList<ConnectedDisplay> connected,
        Func<ConnectedDisplay, string> serialOf,
        Func<int, bool>? comesFirst = null)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentNullException.ThrowIfNull(serialOf);

        var first = comesFirst ?? (_ => true);
        var found = new ConnectedDisplay?[known.Count];
        var taken = new HashSet<DisplayTarget>();
        var order = Enumerable.Range(0, known.Count).OrderBy(i => !first(i)).ToList();

        void Take(Func<KnownDisplay, IEnumerable<ConnectedDisplay>, ConnectedDisplay?> rule)
        {
            foreach (var i in order.Where(i => found[i] is null))
            {
                if (rule(known[i], connected.Where(display => !taken.Contains(display.Target))) is { } display)
                {
                    found[i] = display;
                    taken.Add(display.Target);
                }
            }
        }

        Take((display, free) => display.Serial.Length == 0
            ? null
            : OnlyFree(connected.Where(other => other.Model == display.Model && serialOf(other) == display.Serial), free));
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
        foreach (var model in order.Where(i => found[i] is null && known[i].Model.Length > 0).Select(i => known[i].Model).Distinct().ToList())
        {
            var left = order
                .Where(i => found[i] is null && known[i].Model == model)
                .OrderBy(i => !first(i))
                .ThenBy(i => known[i].Connection, StringComparer.OrdinalIgnoreCase)
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

        return new RecognizedDisplays(found, inConnectionOrder);
    }

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
