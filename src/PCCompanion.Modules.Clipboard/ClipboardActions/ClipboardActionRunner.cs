using System;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Runs the action chosen in the menu: opens its address in the browser, or starts its program,
/// both without admin rights, through the host's commands. See "Run a program" in
/// docs/features/clipboard/clipboard-actions.md. Run it off the host's UI thread: Windows can take
/// a while to start a program.
/// </summary>
/// <param name="fileExists">Whether a file exists.</param>
public sealed partial class ClipboardActionRunner(IHostCommands commands, Func<string, bool> fileExists, ILogger logger)
{
    /// <summary>Runs the action, and returns whether its address opened or its program started.</summary>
    public bool Run(ClipboardOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (offer.Kind == OfferKind.RunProgram)
        {
            return RunProgram(offer);
        }

        if (offer.Address is { } address && commands.OpenWebPage(address))
        {
            LogOpened(logger, offer.Rule);
            return true;
        }

        LogNotOpened(logger, offer.Rule);
        return false;
    }

    // Problems are logged with the program's file name only: its folder and arguments can hold
    // personal paths, and the arguments hold the copied text.
    private bool RunProgram(ClipboardOffer offer)
    {
        var fileName = Path.GetFileName(offer.Program);
        var problem = ClipboardRuleCheck.CheckProgram(offer.Program, fileExists);
        if (problem != RuleProgramProblem.None)
        {
            LogNotStarted(logger, offer.Rule, fileName, problem);
            return false;
        }

        if (commands.StartProgram(offer.Program, offer.Arguments ?? [], out var errorCode))
        {
            LogStarted(logger, offer.Rule, fileName);
            return true;
        }

        LogCantStart(logger, fileName, errorCode);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions opened the address of rule {Rule} (0 is Open in browser).")]
    private static partial void LogOpened(ILogger logger, int rule);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions couldn't open the address of rule {Rule} (0 is Open in browser).")]
    private static partial void LogNotOpened(ILogger logger, int rule);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clipboard Actions rule {Rule} started {FileName}.")]
    private static partial void LogStarted(ILogger logger, int rule, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions rule {Rule} didn't start {FileName}: {Problem}.")]
    private static partial void LogNotStarted(ILogger logger, int rule, string fileName, RuleProgramProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows couldn't start {FileName} (error {ErrorCode}).")]
    private static partial void LogCantStart(ILogger logger, string fileName, int errorCode);
}
