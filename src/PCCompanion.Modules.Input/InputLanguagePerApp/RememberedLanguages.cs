using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Whether the remembered input methods can be used: not while the file is being read.</summary>
    public bool IsLoaded { get; private set; } = true;

    /// <summary>The apps known by app ID or program, the app used last first, as the file keeps them.</summary>
    public IReadOnlyList<RememberedLanguage> Saved =>
        [.. _entries
            .Where(entry => entry.AppId is not null || entry.Program is not null)
            .Select(entry => new RememberedLanguage { AppId = entry.AppId, Program = entry.Program, InputMethod = entry.InputMethod.ToString() })];

    /// <summary>
    /// The input method <paramref name="app"/> had last, or <see langword="null"/> when none is
    /// remembered or the file is being read.
    /// </summary>
    public InputMethodId? Of(AppOfWindow app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var index = IsLoaded ? IndexOf(app) : -1;
        return index < 0 ? null : _entries[index].InputMethod;
    }

    /// <summary>
    /// Remembers the input method <paramref name="app"/> has, as the app used last, also while the
    /// file is being read. Returns whether that changed what's remembered or its order.
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

    /// <summary>Uses no remembered input method until <see cref="Load"/>, while the file is being read.</summary>
    public void WaitForLoad() => IsLoaded = false;

    /// <summary>
    /// Adds the apps read from the file after the ones remembered since, which are newer, up to
    /// <see cref="Limit"/>, and uses the remembered input methods from then on. An entry is
    /// checked as the settings are: one whose identifier doesn't parse, or with neither or both
    /// an app ID and a program, is left out, as is an app listed a second time. Returns how many
    /// were added.
    /// </summary>
    public int Load(IEnumerable<RememberedLanguage?> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        IsLoaded = true;
        var added = 0;
        foreach (var app in apps)
        {
            if (_entries.Count >= Limit)
            {
                break;
            }

            var appId = string.IsNullOrWhiteSpace(app?.AppId) ? null : app.AppId;
            var program = string.IsNullOrWhiteSpace(app?.Program) ? null : app.Program;
            if ((appId is null) == (program is null) || !InputMethodId.TryParse(app?.InputMethod, out var inputMethod))
            {
                continue;
            }

            var entry = new Entry(appId, program, 0, inputMethod);
            if (!_entries.Any(listed => listed.IsSameAppAs(entry)))
            {
                _entries.Add(entry);
                added++;
            }
        }

        return added;
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

        public bool IsSameAppAs(Entry other) =>
            AppId is not null ? string.Equals(AppId, other.AppId, StringComparison.OrdinalIgnoreCase)
            : other.AppId is null && string.Equals(Program, other.Program, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>An app and the input method it had last, as <c>input-language-per-app.json</c> keeps it.</summary>
public sealed record RememberedLanguage
{
    /// <summary>The app ID (AppUserModelID) the app's windows or its Store package have.</summary>
    public string? AppId { get; set; }

    /// <summary>The full path of the app's program file, for an app without an app ID.</summary>
    public string? Program { get; set; }

    /// <summary>Windows' identifier of the input method, such as <c>0407:00000407</c>.</summary>
    public string? InputMethod { get; set; }
}
