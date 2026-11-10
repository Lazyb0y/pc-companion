using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>
/// What Middle-Click Paste reads from Windows and asks apps, so tests can supply it instead. Use
/// it on the host's UI thread; nothing here waits for another program.
/// </summary>
public interface IMiddleClickPasteSystem : IListedAppWindows
{
    /// <summary>The pointer's position in physical pixels, or <see langword="null"/> when it can't be read.</summary>
    Point? PointerPosition { get; }

    /// <summary>Whether the pointer is Windows' own I-beam.</summary>
    bool IsTextPointer { get; }

    /// <summary>Whether a mouse button is held, as during a drag.</summary>
    bool IsMouseButtonHeld { get; }

    /// <summary>The top-level window under <paramref name="point"/>, or 0.</summary>
    nint RootAt(Point point);

    /// <summary>Whether the window's app may be asked, as for the selected text; see <see cref="SelectedText.CheckWindow"/>.</summary>
    SelectedTextApp CheckWindow(nint window);

    /// <summary>Asks, on the feature's UI Automation thread, whether the element under <paramref name="point"/> is a text box.</summary>
    Task<TextBoxAnswer> AskAsync(Point point);

    /// <summary>Keeps the claim's clicks from the apps from now on; <see langword="null"/> keeps nothing more.</summary>
    void Claim(PointerClaim? claim);
}
