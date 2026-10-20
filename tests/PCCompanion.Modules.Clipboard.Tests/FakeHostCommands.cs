using System;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Clipboard.Tests;

/// <summary>Records the host's commands; no browser opens.</summary>
internal sealed class FakeHostCommands : IHostCommands
{
    private readonly List<Uri> _webPages = [];
    private int _settings;

    /// <summary>What <see cref="OpenWebPage"/> answers.</summary>
    public bool OpensWebPages { get; set; } = true;

    public int SettingsOpened => Volatile.Read(ref _settings);

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

    public void ExitApp() => throw new NotSupportedException();

    public bool OpenWebPage(Uri address)
    {
        lock (_webPages)
        {
            _webPages.Add(address);
        }

        return OpensWebPages;
    }
}
