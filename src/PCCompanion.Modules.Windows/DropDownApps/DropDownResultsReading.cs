using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// The note the Drop-down Apps page shows instead of the recent results.
/// </summary>
public enum DropDownResultsNote
{
    /// <summary>The results show, without a note.</summary>
    None,

    /// <summary>No shortcut has been pressed yet.</summary>
    NothingPressed,

    /// <summary>Drop-down Apps is off, so results show once it's on.</summary>
    FeatureOff,

    /// <summary>Drop-down Apps is on, but the host gave no results for a few reads in a row.</summary>
    NotAvailable,
}

/// <summary>
/// What the Drop-down Apps page shows from its reads of the recent results, about once a second.
/// While the feature is on, a read without an answer, as for a moment after it's turned on or
/// while the host is busy, keeps what's shown, until <see cref="MissedReadsBeforeNote"/> in a row,
/// as on the Hot Corners page. See "Recent results" in docs/features/windows/drop-down-apps.md.
/// </summary>
public sealed class DropDownResultsReading
{
    /// <summary>How many reads in a row get no answer before the page says results aren't available.</summary>
    public const int MissedReadsBeforeNote = 3;

    private int _missedReads;

    /// <summary>The results the page shows, newest first.</summary>
    public IReadOnlyList<DropDownResult> Results { get; private set; } = [];

    public DropDownResultsNote Note { get; private set; }

    /// <summary>
    /// Takes a read's results, or <see langword="null"/> when it got no answer, and returns whether
    /// what the page shows changed.
    /// </summary>
    /// <param name="isOn">Whether Drop-down Apps is on in the settings.</param>
    public bool Read(bool isOn, IReadOnlyList<DropDownResult>? results)
    {
        IReadOnlyList<DropDownResult> shown;
        DropDownResultsNote note;
        if (!isOn)
        {
            _missedReads = 0;
            (shown, note) = ([], DropDownResultsNote.FeatureOff);
        }
        else if (results is not null)
        {
            _missedReads = 0;
            (shown, note) = (results, results.Count == 0 ? DropDownResultsNote.NothingPressed : DropDownResultsNote.None);
        }
        else
        {
            _missedReads = Math.Min(_missedReads + 1, MissedReadsBeforeNote);
            if (_missedReads < MissedReadsBeforeNote)
            {
                return false;
            }

            (shown, note) = ([], DropDownResultsNote.NotAvailable);
        }

        if (note == Note && shown.SequenceEqual(Results))
        {
            return false;
        }

        (Results, Note) = (shown, note);
        return true;
    }
}
