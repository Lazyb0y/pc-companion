using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Runs the action chosen in the menu: opens its address in the browser, without admin rights,
/// or starts its program. See "Run a program" in docs/features/clipboard/clipboard-actions.md.
/// Run it off the host's UI thread: Windows can take a while to start a program.
/// </summary>
/// <param name="isAdministrator">Whether PC Companion runs as administrator; programs don't start then.</param>
/// <param name="fileExists">Whether a file exists.</param>
/// <param name="start">Starts a process.</param>
public sealed partial class ClipboardActionRunner(IHostCommands commands, Func<bool> isAdministrator, Func<string, bool> fileExists, Action<ProcessStartInfo> start, ILogger logger)
{
    public void Run(ClipboardOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (offer.Kind == OfferKind.RunProgram)
        {
            RunProgram(offer);
        }
        else if (offer.Address is { } address && commands.OpenWebPage(address))
        {
            LogOpened(logger, offer.Rule);
        }
        else
        {
            LogNotOpened(logger, offer.Rule);
        }
    }

    // Problems are logged with the program's file name only: its folder and arguments can hold
    // personal paths, and the arguments hold the copied text.
    private void RunProgram(ClipboardOffer offer)
    {
        if (isAdministrator())
        {
            LogAdministrator(logger, offer.Rule);
            return;
        }

        var fileName = Path.GetFileName(offer.Program);
        var problem = ClipboardRuleCheck.CheckProgram(offer.Program, fileExists);
        if (problem != RuleProgramProblem.None)
        {
            LogNotStarted(logger, offer.Rule, fileName, problem);
            return;
        }

        if (ProgramFile.TryStart(offer.Program, offer.Arguments ?? [], start, out var errorCode))
        {
            LogStarted(logger, offer.Rule, fileName);
        }
        else
        {
            LogCantStart(logger, fileName, errorCode);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions opened the address of rule {Rule} (0 is Open in browser).")]
    private static partial void LogOpened(ILogger logger, int rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions couldn't open the address of rule {Rule} (0 is Open in browser).")]
    private static partial void LogNotOpened(ILogger logger, int rule);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions rule {Rule} started {FileName}.")]
    private static partial void LogStarted(ILogger logger, int rule, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions rule {Rule} doesn't run a program while PC Companion runs as administrator.")]
    private static partial void LogAdministrator(ILogger logger, int rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions rule {Rule} didn't start {FileName}: {Problem}.")]
    private static partial void LogNotStarted(ILogger logger, int rule, string fileName, RuleProgramProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows couldn't start {FileName} (error {ErrorCode}).")]
    private static partial void LogCantStart(ILogger logger, string fileName, int errorCode);
}
