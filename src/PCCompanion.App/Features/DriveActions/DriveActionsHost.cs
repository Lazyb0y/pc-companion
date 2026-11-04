using System;
using System.Threading.Tasks;
using PCCompanion.Modules.Files.DriveActions;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>
/// Asks the host about Drive Actions through its pipe, off the UI thread. Only the host knows the
/// connected drives and the history, and writes on drives; it serves the pipe while Drive Actions is on.
/// </summary>
public static class DriveActionsHost
{
    /// <summary>
    /// How long a quick request may take, such as the connected drives or adding one. The host
    /// answers between two files of a run, so a large file being copied can hold the answer up.
    /// </summary>
    public static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long Run now's list may take, for backups of large folders.</summary>
    public static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long Undo may take: a copy too big for the Recycle Bin waits for Windows' question.</summary>
    public static readonly TimeSpan UndoTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The host's answer, or <see langword="null"/> when it doesn't answer, as while Drive Actions is off.</summary>
    public static Task<DriveActionsAnswer?> AskAsync(DriveActionsRequest request, TimeSpan timeout) =>
        Task.Run(() => DriveActionsPipe.AskAsync(DriveActionsPipe.Name, request, timeout));
}
