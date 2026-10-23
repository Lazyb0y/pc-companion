using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// The note the Hot Corners page shows instead of, or without, the recent results.
/// </summary>
public enum CornerResultsNote
{
    /// <summary>The results show, without a note.</summary>
    None,

    /// <summary>No corner has been reached yet.</summary>
    NoCornerReached,

    /// <summary>Hot Corners is off, so results show once it's on.</summary>
    HotCornersOff,

    /// <summary>Hot Corners is on, but the host gave no results for a few reads in a row.</summary>
    NotAvailable,
}

/// <summary>
/// What the Hot Corners page shows from its reads of the recent results, about once a second. While
/// Hot Corners is on, a read without an answer, as for a moment after it's turned on or while the
/// host is busy, keeps what's shown, until <see cref="MissedReadsBeforeNote"/> in a row. See "Recent
/// results" in docs/features/input/hot-corners.md.
/// </summary>
public sealed class CornerResultsReading
{
    /// <summary>How many reads in a row get no answer before the page says results aren't available.</summary>
    public const int MissedReadsBeforeNote = 3;

    private int _missedReads;

    /// <summary>The results the page shows, newest first.</summary>
    public IReadOnlyList<CornerResult> Results { get; private set; } = [];

    public CornerResultsNote Note { get; private set; }

    /// <summary>
    /// Takes a read's results, or <see langword="null"/> when it got no answer, and returns whether
    /// what the page shows changed.
    /// </summary>
    /// <param name="isOn">Whether Hot Corners is on in the settings.</param>
    public bool Read(bool isOn, IReadOnlyList<CornerResult>? results)
    {
        IReadOnlyList<CornerResult> shown;
        CornerResultsNote note;
        if (!isOn)
        {
            _missedReads = 0;
            (shown, note) = ([], CornerResultsNote.HotCornersOff);
        }
        else if (results is not null)
        {
            _missedReads = 0;
            (shown, note) = (results, results.Count == 0 ? CornerResultsNote.NoCornerReached : CornerResultsNote.None);
        }
        else
        {
            _missedReads = Math.Min(_missedReads + 1, MissedReadsBeforeNote);
            if (_missedReads < MissedReadsBeforeNote)
            {
                return false;
            }

            (shown, note) = ([], CornerResultsNote.NotAvailable);
        }

        if (note == Note && shown.SequenceEqual(Results))
        {
            return false;
        }

        (Results, Note) = (shown, note);
        return true;
    }
}
