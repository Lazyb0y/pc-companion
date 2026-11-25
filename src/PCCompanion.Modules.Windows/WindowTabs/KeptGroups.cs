using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// The groups Keep groups after a restart keeps: each group's tabs' apps, in the strip's order,
/// and which tab was in front, the group changed last first, for up to <see cref="Limit"/>
/// groups. A group is known by a key its controller gives it. Use it on the feature's thread.
/// </summary>
public sealed class KeptGroups
{
    /// <summary>How many groups are kept; when there are more, the one changed longest ago goes.</summary>
    public const int Limit = 50;

    // The group changed last first.
    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    /// <summary>How many groups read from the file wait to be made again.</summary>
    public int Waiting => _entries.Count(entry => entry.Waiting);

    /// <summary>
    /// The groups as the file keeps them, the group changed last first: tabs whose app has neither
    /// an app ID nor a program are left out, and so is a group with fewer than two tabs left.
    /// </summary>
    public IReadOnlyList<KeptGroup> Saved
    {
        get
        {
            var saved = new List<KeptGroup>();
            foreach (var entry in _entries)
            {
                var tabs = new List<KeptTab>();
                var front = 0;
                for (var index = 0; index < entry.Tabs.Count; index++)
                {
                    if (entry.Tabs[index] is not { } tab)
                    {
                        continue;
                    }

                    if (index == entry.Front)
                    {
                        front = tabs.Count;
                    }

                    tabs.Add(tab);
                }

                if (tabs.Count >= 2)
                {
                    saved.Add(new KeptGroup { Tabs = tabs, Front = front });
                }
            }

            return saved;
        }
    }

    /// <summary>
    /// Keeps <paramref name="key"/>'s group with <paramref name="tabs"/>, a tab whose app can't be
    /// kept being <see langword="null"/>, and <paramref name="front"/>, as the group changed last.
    /// Returns whether that changed what's kept or its order.
    /// </summary>
    public bool Keep(object key, IReadOnlyList<KeptTab?> tabs, int front)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(tabs);

        var index = _entries.FindIndex(entry => entry.Key == key);
        if (index >= 0 && _entries[index].Front == front && _entries[index].Tabs.SequenceEqual(tabs))
        {
            return false;
        }

        if (index >= 0)
        {
            _entries.RemoveAt(index);
        }

        _entries.Insert(0, new Entry(key, [.. tabs], front, Waiting: false));
        if (_entries.Count > Limit)
        {
            _entries.RemoveAt(Limit);
        }

        return true;
    }

    /// <summary>Forgets <paramref name="key"/>'s group, as after it ended. Returns whether it was kept.</summary>
    public bool Forget(object key) => _entries.RemoveAll(entry => entry.Key == key) > 0;

    /// <summary>Forgets every group. Returns whether any was kept.</summary>
    public bool ForgetAll()
    {
        var any = _entries.Count > 0;
        _entries.Clear();
        return any;
    }

    /// <summary>
    /// Adds the groups read from the file after the ones kept since, which are newer, up to
    /// <see cref="Limit"/>, each waiting to be made again. A tab with neither or both an app ID
    /// and a program is left out, and so is a group with fewer than two tabs left; a tab in front
    /// that isn't there makes the first tab the one in front. Returns how many were added.
    /// </summary>
    public int Load(IEnumerable<KeptGroup?> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var added = 0;
        foreach (var group in groups)
        {
            if (_entries.Count >= Limit)
            {
                break;
            }

            var tabs = new List<KeptTab?>();
            var front = 0;
            var source = group?.Tabs ?? [];
            for (var index = 0; index < source.Count; index++)
            {
                if (KeptTab.Checked(source[index]) is not { } tab)
                {
                    continue;
                }

                if (index == group!.Front)
                {
                    front = tabs.Count;
                }

                tabs.Add(tab);
            }

            if (tabs.Count >= 2)
            {
                _entries.Add(new Entry(new object(), tabs, front, Waiting: true));
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Finds windows for the groups that wait to be made again, among <paramref name="candidates"/>,
    /// windows in no group, the front-most first: each tab takes the front-most window of its app
    /// that no other tab took. A group is found once each of its tabs has a window, or, when
    /// <paramref name="final"/>, as the 60 seconds are up, once at least two have; its tab in front
    /// is then the remembered one's window, or else the front-most of its windows. A group found
    /// waits no more. Returns the groups found, with their keys, the group changed last first.
    /// </summary>
    public IReadOnlyList<KeptRegroup> Match(IReadOnlyList<(nint Window, AppOfWindow App)> candidates, bool final)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var taken = new HashSet<nint>();
        var found = new List<KeptRegroup>();
        for (var entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
        {
            var entry = _entries[entryIndex];
            if (!entry.Waiting)
            {
                continue;
            }

            // The candidate each tab takes, by its place among the candidates, or -1.
            var picks = new List<int>();
            var mine = new HashSet<int>();
            foreach (var tab in entry.Tabs)
            {
                var pick = -1;
                for (var index = 0; index < candidates.Count; index++)
                {
                    if (!taken.Contains(candidates[index].Window) && !mine.Contains(index) && tab is not null && tab.Matches(candidates[index].App))
                    {
                        pick = index;
                        break;
                    }
                }

                picks.Add(pick);
                if (pick >= 0)
                {
                    mine.Add(pick);
                }
            }

            if (mine.Count < 2 || (mine.Count < entry.Tabs.Count && !final))
            {
                continue;
            }

            var windows = new List<nint>();
            var front = -1;
            var frontMost = int.MaxValue;
            var frontMostTab = 0;
            for (var tab = 0; tab < picks.Count; tab++)
            {
                if (picks[tab] < 0)
                {
                    continue;
                }

                if (tab == entry.Front)
                {
                    front = windows.Count;
                }

                if (picks[tab] < frontMost)
                {
                    frontMost = picks[tab];
                    frontMostTab = windows.Count;
                }

                windows.Add(candidates[picks[tab]].Window);
                taken.Add(candidates[picks[tab]].Window);
            }

            _entries[entryIndex] = entry with { Waiting = false };
            found.Add(new KeptRegroup(entry.Key, windows, front >= 0 ? front : frontMostTab));
        }

        return found;
    }

    /// <summary>No group waits any more, as once the 60 seconds are up; those not made stay kept.</summary>
    public void StopWaiting()
    {
        for (var index = 0; index < _entries.Count; index++)
        {
            _entries[index] = _entries[index] with { Waiting = false };
        }
    }

    private sealed record Entry(object Key, IReadOnlyList<KeptTab?> Tabs, int Front, bool Waiting);
}

/// <summary>A kept group whose windows were found: its key, its windows in the strip's order, and the one in front.</summary>
public sealed record KeptRegroup(object Key, IReadOnlyList<nint> Windows, int Front);
