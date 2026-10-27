using System;
using System.Threading.Tasks;
using PCCompanion.Modules.Files.FolderRules;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>
/// Asks the host about Folder Rules through its pipe, off the UI thread. Only the host knows the
/// history and changes files; it serves the pipe while Folder Rules is on.
/// </summary>
public static class FolderRulesHost
{
    /// <summary>How long a quick request may take: the history, Run now and Clear history.</summary>
    public static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long Run now's list may take, for a large folder on a network location.</summary>
    public static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long Undo may take: a copy too big for the Recycle Bin waits for Windows' question.</summary>
    public static readonly TimeSpan UndoTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The host's answer, or <see langword="null"/> when it doesn't answer, as while Folder Rules is off.</summary>
    public static Task<FolderRulesAnswer?> AskAsync(FolderRulesRequest request, TimeSpan timeout) =>
        Task.Run(() => FolderRulesPipe.AskAsync(FolderRulesPipe.Name, request, timeout));
}
