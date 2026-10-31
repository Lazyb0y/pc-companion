using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// The input method each app had last, for Remember each app's language, the app used last first,
/// for up to <see cref="Limit"/> apps. Apps are known by app ID, otherwise by program; an app with
/// neither is known by its process, for the session only. Use it on the feature's thread.
/// </summary>
public sealed class RememberedLanguages
{
    /// <summary>How many apps are remembered; when there are more, the one used longest ago goes.</summary>
    public const int Limit = 500;

    // The app used last first.
    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    /// <summary>The input method <paramref name="app"/> had last, or <see langword="null"/> when none is remembered.</summary>
    public InputMethodId? Of(AppOfWindow app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var index = IndexOf(app);
        return index < 0 ? null : _entries[index].InputMethod;
    }

    /// <summary>
    /// Remembers the input method <paramref name="app"/> has, as the app used last. Returns whether
    /// that changed what's remembered or its order.
    /// </summary>
    public bool Remember(AppOfWindow app, InputMethodId inputMethod)
    {
        ArgumentNullException.ThrowIfNull(app);

        var index = IndexOf(app);
        if (index == 0 && _entries[0].InputMethod == inputMethod)
        {
            return false;
        }

        if (index > 0)
        {
            _entries.RemoveAt(index);
        }

        var entry = new Entry(app.AppId, app.AppId is null ? app.Program : null, app.ProcessId, inputMethod);
        if (index == 0)
        {
            _entries[0] = entry;
        }
        else
        {
            _entries.Insert(0, entry);
        }

        if (_entries.Count > Limit)
        {
            _entries.RemoveAt(Limit);
        }

        return true;
    }

    /// <summary>Forgets every app's input method.</summary>
    public void Forget() => _entries.Clear();

    private int IndexOf(AppOfWindow app) => _entries.FindIndex(entry => entry.Matches(app));

    private sealed record Entry(string? AppId, string? Program, uint ProcessId, InputMethodId InputMethod)
    {
        // As the taskbar groups apps: by app ID, otherwise by program, otherwise by process.
        public bool Matches(AppOfWindow app) =>
            app.AppId is { } appId ? string.Equals(AppId, appId, StringComparison.OrdinalIgnoreCase)
            : app.Program is { } program ? AppId is null && string.Equals(Program, program, StringComparison.OrdinalIgnoreCase)
            : AppId is null && Program is null && ProcessId == app.ProcessId;
    }
}
