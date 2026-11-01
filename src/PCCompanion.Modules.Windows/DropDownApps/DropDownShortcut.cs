using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// The shortcut that drops a listed app down. The keys are found by their position on the
/// keyboard. The names are stored in the settings file, so never rename one.
/// </summary>
public enum DropDownShortcut
{
    /// <summary>No shortcut.</summary>
    None,

    /// <summary>The Windows key, Ctrl and the key above Tab.</summary>
    WindowsControlAboveTab,

    /// <summary>The Windows key, Alt and the key above Tab.</summary>
    WindowsAltAboveTab,

    /// <summary>The Windows key, Ctrl, Alt and 1, as for the next ones.</summary>
    WindowsControlAlt1,
    WindowsControlAlt2,
    WindowsControlAlt3,
    WindowsControlAlt4,
    WindowsControlAlt5,
    WindowsControlAlt6,
    WindowsControlAlt7,
    WindowsControlAlt8,
    WindowsControlAlt9,
}

/// <summary>Why a listed app has no shortcut although the file gives it one.</summary>
public enum ShortcutProblem
{
    None,

    /// <summary>The file names a shortcut that doesn't exist.</summary>
    Unknown,

    /// <summary>An app earlier in the list has the same shortcut.</summary>
    Repeated,
}

/// <summary>The shortcuts Drop-down Apps offers, and the keys they're registered with.</summary>
public static class DropDownShortcuts
{
    // The number keys 1 to 9 have the scan codes 2 to 10 on every keyboard.
    private const uint KeyOne = 0x02;

    /// <summary>Every shortcut but None, in the order a newly added app gets the first free one.</summary>
    public static IReadOnlyList<DropDownShortcut> All { get; } =
        [.. Enumerable.Range((int)DropDownShortcut.WindowsControlAboveTab, 11).Select(value => (DropDownShortcut)value)];

    /// <summary>The keys <paramref name="shortcut"/> is registered with, or <see langword="null"/> for None or an unknown one.</summary>
    public static KeyPositionShortcut? KeysOf(DropDownShortcut shortcut) => shortcut switch
    {
        DropDownShortcut.WindowsControlAboveTab => new(HotKeyModifiers.Windows | HotKeyModifiers.Control, ShortcutKeeper.KeyAboveTab),
        DropDownShortcut.WindowsAltAboveTab => new(HotKeyModifiers.Windows | HotKeyModifiers.Alt, ShortcutKeeper.KeyAboveTab),
        >= DropDownShortcut.WindowsControlAlt1 and <= DropDownShortcut.WindowsControlAlt9 =>
            new(HotKeyModifiers.Windows | HotKeyModifiers.Control | HotKeyModifiers.Alt, KeyOne + (uint)(shortcut - DropDownShortcut.WindowsControlAlt1)),
        _ => null,
    };

    /// <summary>The first shortcut in <see cref="All"/> that none of <paramref name="taken"/> is, or None when every one is.</summary>
    public static DropDownShortcut FirstFree(IEnumerable<DropDownShortcut> taken)
    {
        var used = taken.ToHashSet();
        return All.FirstOrDefault(shortcut => !used.Contains(shortcut), DropDownShortcut.None);
    }

    /// <summary>
    /// The shortcut each of <paramref name="apps"/> gets, in order, and why one the file gives it
    /// is None instead: an unknown shortcut, or one that an app earlier in the list has.
    /// </summary>
    public static IReadOnlyList<(DropDownShortcut Shortcut, ShortcutProblem Problem)> Assign(IReadOnlyList<DropDownApp> apps)
    {
        var used = new HashSet<DropDownShortcut>();
        var assigned = new List<(DropDownShortcut, ShortcutProblem)>(apps.Count);
        foreach (var app in apps)
        {
            if (app.Shortcut == DropDownShortcut.None)
            {
                assigned.Add((DropDownShortcut.None, ShortcutProblem.None));
            }
            else if (KeysOf(app.Shortcut) is null)
            {
                assigned.Add((DropDownShortcut.None, ShortcutProblem.Unknown));
            }
            else if (!used.Add(app.Shortcut))
            {
                assigned.Add((DropDownShortcut.None, ShortcutProblem.Repeated));
            }
            else
            {
                assigned.Add((app.Shortcut, ShortcutProblem.None));
            }
        }

        return assigned;
    }
}
