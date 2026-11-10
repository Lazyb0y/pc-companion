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

    /// <summary>What the shared pointer hook did with the middle-click stamped <paramref name="time"/>.</summary>
    PointerInputOutcome Outcome(uint time);

    /// <summary>Sets the shared pointer hook again, as after Windows removed it.</summary>
    void SetHookAgain();

    /// <summary>The window with the keyboard focus, else the window in front, or 0.</summary>
    nint FocusWindow { get; }

    /// <summary>
    /// Reads the text selected in the window in front, on the feature's UI Automation thread, at
    /// most <see cref="MiddleClickPasteController.MaxLength"/> characters; a selection that is all
    /// of the text of the box at <paramref name="clickedAt"/> doesn't count. The task never fails.
    /// </summary>
    Task<SelectedTextResult> ReadAsync(Point clickedAt);

    /// <summary>Which of Windows' own text boxes <paramref name="window"/> is, if any.</summary>
    EditBoxKind KindOf(nint window);

    /// <summary>Puts the text into a plain box where the point is, off the host's UI thread; see <see cref="EditBoxes.InsertAt"/>.</summary>
    Task<EditBoxResult> InsertAtAsync(nint box, Point point, string text);

    /// <summary>Puts the text cursor of the box under the point there, with UI Automation. The task never fails.</summary>
    Task<bool> PlaceCursorAsync(Point point);

    /// <summary>Puts the text into a box at its text cursor, off the host's UI thread; see <see cref="EditBoxes.InsertAtCursor"/>.</summary>
    Task<EditBoxResult> InsertAtCursorAsync(nint box, string text);

    /// <summary>
    /// Brings <paramref name="root"/> to the front and gives the box under the point the keyboard
    /// focus; <paramref name="box"/> is the box's own window, or 0. The task never fails.
    /// </summary>
    Task<bool> FocusAsync(nint root, nint box, Point point);

    /// <summary>Drops the text at the point; see <see cref="TextDrop"/>. The task never fails.</summary>
    Task<TextDropResult> DropAsync(Point point, string text);
}
