using System;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Core.Features;

namespace PCCompanion.Testing;

/// <summary>
/// Records the host's commands, from any thread; no settings window or browser opens and nothing exits.
/// </summary>
public sealed class FakeHostCommands : IHostCommands
{
    private readonly List<Uri> _webPages = [];
    private int _settings;
    private int _exits;

    /// <summary>What <see cref="OpenWebPage"/> answers.</summary>
    public bool OpensWebPages { get; set; } = true;

    public int SettingsOpened => Volatile.Read(ref _settings);

    public int Exits => Volatile.Read(ref _exits);

    /// <summary>The web pages asked for so far.</summary>
    public IReadOnlyList<Uri> WebPages
    {
        get
        {
            lock (_webPages)
            {
                return [.. _webPages];
            }
        }
    }

    public void OpenSettings() => Interlocked.Increment(ref _settings);

    public void ExitApp() => Interlocked.Increment(ref _exits);

    public bool OpenWebPage(Uri address)
    {
        lock (_webPages)
        {
            _webPages.Add(address);
        }

        return OpensWebPages;
    }
}
