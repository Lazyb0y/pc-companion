using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What <see cref="ExcludedWindows"/> reads about windows, so tests can supply it instead. None of
/// it waits for the windows' programs.
/// </summary>
public interface IListedAppWindows
{
    /// <summary>The top-level window's app, as the taskbar groups apps, or <see langword="null"/> when it has closed.</summary>
    AppOfWindow? AppOf(nint root);

    bool Exists(nint window);
}

/// <summary>
/// Tells which top-level windows belong to the apps on a feature's list, such as the apps that keep
/// their title-bar clicks in docs/features/windows/title-bar-actions.md. Only while the list has
/// apps, each window's app is read once and kept until the window closes. Use it on one thread,
/// the one its <see cref="IListedAppWindows"/> answers on.
/// </summary>
public sealed class ExcludedWindows(IListedAppWindows windows)
{
    // How many windows are kept before those that closed are dropped; it doubles when most are still open.
    private const int FirstCleanUp = 64;

    private readonly Dictionary<nint, bool> _known = [];
    private IReadOnlyList<ListedApp> _apps = [];
    private int _cleanUpAt = FirstCleanUp;

    /// <summary>
    /// Takes the list of apps, forgetting the windows read for the list before. Returns false when
    /// the list is the same.
    /// </summary>
    public bool SetApps(IReadOnlyList<ListedApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        if (apps.SequenceEqual(_apps))
        {
            return false;
        }

        _apps = apps;
        _known.Clear();
        _cleanUpAt = FirstCleanUp;
        return true;
    }

    /// <summary>Whether <paramref name="root"/>'s app is on the list.</summary>
    public bool Contains(nint root)
    {
        if (_apps.Count == 0)
        {
            return false;
        }

        if (_known.TryGetValue(root, out var listed))
        {
            return listed;
        }

        if (_known.Count >= _cleanUpAt)
        {
            foreach (var closed in _known.Keys.Where(window => !windows.Exists(window)).ToList())
            {
                _known.Remove(closed);
            }

            _cleanUpAt = Math.Max(FirstCleanUp, 2 * _known.Count);
        }

        listed = windows.AppOf(root) is { } app && _apps.Any(listedApp => listedApp.Matches(app));
        _known[root] = listed;
        return listed;
    }
}
