using System;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>Middle-Click Paste's view of Windows, through the shared pointer hook and the feature's reader.</summary>
public sealed class WindowsMiddleClickPasteSystem(PointerHook hook, SelectedTextReader reader) : IMiddleClickPasteSystem
{
    /// <summary>
    /// How long a browser that is turning on its accessibility support is asked again what's under
    /// the pointer, about 0.3 seconds in the spike.
    /// </summary>
    public static readonly TimeSpan AskRetryFor = TimeSpan.FromSeconds(1);

    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsTextPointer => InputState.IsTextPointer;

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public nint RootAt(Point point) => WindowHitTest.WindowAt(point) is var window and not 0 ? WindowHitTest.Root(window) : 0;

    public SelectedTextApp CheckWindow(nint window) => SelectedText.CheckWindow(window);

    public Task<TextBoxAnswer> AskAsync(Point point) => reader.AskAsync(point, AskRetryFor);

    public void Claim(PointerClaim? claim) => hook.Claim(claim);

    public AppOfWindow? AppOf(nint root)
    {
        var processId = TopLevelWindows.ProcessIdOf(root);
        return processId == 0 ? null : new AppOfWindow(AppIdentity.OfWindow(root), AppIdentity.OfPackagedProcess(processId), AppIdentity.ProgramPath(processId), processId);
    }

    public bool Exists(nint window) => TopLevelWindows.Exists(window);
}
