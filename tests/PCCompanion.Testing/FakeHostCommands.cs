using System;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Core.Features;

namespace PCCompanion.Testing;

/// <summary>
/// Records the host's commands, from any thread; no settings window, browser or program opens and
/// nothing exits.
/// </summary>
public sealed class FakeHostCommands : IHostCommands
{
    private readonly List<Uri> _webPages = [];
    private readonly List<string> _pages = [];
    private readonly List<(string Program, IReadOnlyList<string> Arguments)> _programs = [];
    private readonly List<string> _apps = [];
    private int _settings;
    private int _exits;

    /// <summary>What <see cref="OpenWebPage"/> answers.</summary>
    public bool OpensWebPages { get; set; } = true;

    /// <summary>The error code <see cref="StartProgram"/> answers with; 0 starts the program.</summary>
    public int ProgramErrorCode { get; set; }

    /// <summary>What <see cref="StartProgram"/> throws, or <see langword="null"/>.</summary>
    public Exception? ProgramFailure { get; set; }

    /// <summary>The programs asked for so far, with their arguments.</summary>
    public IReadOnlyList<(string Program, IReadOnlyList<string> Arguments)> Programs
    {
        get
        {
            lock (_programs)
            {
                return [.. _programs];
            }
        }
    }

    /// <summary>The error code <see cref="StartApp"/> answers with; 0 opens the app.</summary>
    public int AppErrorCode { get; set; }

    /// <summary>The app IDs asked for so far.</summary>
    public IReadOnlyList<string> Apps
    {
        get
        {
            lock (_apps)
            {
                return [.. _apps];
            }
        }
    }

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

    /// <summary>The features whose settings pages were asked for so far.</summary>
    public IReadOnlyList<string> FeaturePagesOpened
    {
        get
        {
            lock (_pages)
            {
                return [.. _pages];
            }
        }
    }

    public void OpenSettings() => Interlocked.Increment(ref _settings);

    public void OpenFeatureSettings(string featureId)
    {
        lock (_pages)
        {
            _pages.Add(featureId);
        }
    }

    public void ExitApp() => Interlocked.Increment(ref _exits);

    public bool OpenWebPage(Uri address)
    {
        lock (_webPages)
        {
            _webPages.Add(address);
        }

        return OpensWebPages;
    }

    public bool StartProgram(string program, IReadOnlyList<string> arguments, out int errorCode)
    {
        if (ProgramFailure is { } failure)
        {
            throw failure;
        }

        lock (_programs)
        {
            _programs.Add((program, [.. arguments]));
        }

        errorCode = ProgramErrorCode;
        return errorCode == 0;
    }

    public bool StartApp(string appId, out int errorCode)
    {
        lock (_apps)
        {
            _apps.Add(appId);
        }

        errorCode = AppErrorCode;
        return errorCode == 0;
    }
}
