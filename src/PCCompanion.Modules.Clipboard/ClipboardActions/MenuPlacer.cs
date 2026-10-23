using System;
using System.Drawing;
using System.Threading.Tasks;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>Where the menu opens, and why; logged by name.</summary>
public enum MenuPlace
{
    /// <summary>At Windows' text cursor of the app in front.</summary>
    AtTextCursor,

    /// <summary>At the app's accessibility text cursor, which it reported when asked.</summary>
    AtAppTextCursor,

    /// <summary>At the pointer: the app reported no text cursor, or wasn't asked.</summary>
    AtPointer,

    /// <summary>At the pointer: the app didn't answer in time.</summary>
    AtPointerNoAnswer,

    /// <summary>At the pointer: an earlier request still waits for its app, so the app wasn't asked.</summary>
    AtPointerStillAsking,
}

/// <param name="Point">Where the menu starts, in screen pixels.</param>
/// <param name="KeepClear">The text cursor's line, which the menu doesn't cover; null at the pointer.</param>
public readonly record struct MenuPosition(Point Point, Rectangle? KeepClear, MenuPlace Place);

/// <summary>
/// Finds where the menu opens at each press, on the feature's thread, as "Where the menu opens" in
/// docs/features/clipboard/clipboard-actions.md says: at Windows' text cursor, else at the app's
/// accessibility text cursor, which a press waits for at most <see cref="AnswerLimit"/>, else at
/// the pointer.
/// </summary>
public sealed class MenuPlacer(IClipboardActionsSystem system)
{
    /// <summary>How long a press waits for the app's accessibility text cursor.</summary>
    public static readonly TimeSpan AnswerLimit = TimeSpan.FromMilliseconds(100);

    // A request that an app didn't answer in time, until it ends.
    private Task<Rectangle?>? _waiting;

    public MenuPosition Find()
    {
        var front = system.ReadTextCursor();
        if (front.TextCursor is { } cursor)
        {
            return AtTextCursor(cursor, MenuPlace.AtTextCursor);
        }

        if (_waiting is { IsCompleted: false })
        {
            return AtPointer(MenuPlace.AtPointerStillAsking);
        }

        _waiting = null;
        if (front.Window == 0 || !system.CanAskTextCursor(front.Window))
        {
            return AtPointer(MenuPlace.AtPointer);
        }

        var answer = system.AskTextCursor(front.Window);
        if (!answer.Wait(AnswerLimit))
        {
            _waiting = answer;
            return AtPointer(MenuPlace.AtPointerNoAnswer);
        }

        return answer.Result is { } appCursor ? AtTextCursor(appCursor, MenuPlace.AtAppTextCursor) : AtPointer(MenuPlace.AtPointer);
    }

    // The menu starts at the text cursor and Windows moves it below or above the cursor's line.
    private static MenuPosition AtTextCursor(Rectangle cursor, MenuPlace place) => new(cursor.Location, cursor, place);

    private MenuPosition AtPointer(MenuPlace place) => new(system.PointerPosition(), null, place);
}
