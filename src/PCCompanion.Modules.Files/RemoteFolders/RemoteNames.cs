using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>Why an entry on the server isn't shown on the PC.</summary>
public enum HiddenReason
{
    /// <summary>It's shown.</summary>
    None,

    /// <summary>Windows doesn't allow the name: a reserved name, a character such as <c>:</c>, or a dot or space at the end.</summary>
    InvalidName,

    /// <summary>The name is longer than 255 characters, or its path too long for its placeholder.</summary>
    TooLong,

    /// <summary>The server's name for it isn't valid UTF-8, so it couldn't go back under its own name.</summary>
    NotUtf8,

    /// <summary>It differs only in case from an entry shown before it, which Windows treats as the same name.</summary>
    SameNameOtherCase,

    /// <summary>It's one of PC Companion's own temporary files on the server.</summary>
    OwnTemporaryFile,

    /// <summary>The folder has more entries than are shown.</summary>
    TooMany,
}

/// <summary>A folder's entries that are shown, and how many aren't, by reason.</summary>
public sealed record NameSelection<T>(IReadOnlyList<T> Shown, IReadOnlyDictionary<HiddenReason, int> Hidden)
{
    public int HiddenCount => Hidden.Values.Sum();
}

/// <summary>
/// Names from a server, which are untrusted: which entries the PC shows, and how. See "Names from
/// the server" in docs/features/files/remote-folders.md.
/// </summary>
public static class RemoteNames
{
    /// <summary>The most entries of a folder that are shown.</summary>
    public const int MaximumEntries = 100_000;

    /// <summary>The start of the temporary names files are uploaded under, before they're renamed into place.</summary>
    public const string UploadPrefix = ".pcc-upload-";

    /// <summary>What's added to a file's name while it steps aside for a new version, on servers without posix-rename.</summary>
    public const string AsideMarker = ".pcc-old-";

    // Device names Windows also keeps, besides those FileNaming knows.
    private static readonly HashSet<string> ConsoleNames = new(StringComparer.OrdinalIgnoreCase) { "CONIN$", "CONOUT$" };

    /// <summary>Whether the PC can show an entry named <paramref name="name"/>, and if not, why.</summary>
    public static HiddenReason Check(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains('�', StringComparison.Ordinal))
        {
            return HiddenReason.NotUtf8;
        }

        if (name.StartsWith(UploadPrefix, StringComparison.Ordinal) || name.Contains(AsideMarker, StringComparison.Ordinal))
        {
            return HiddenReason.OwnTemporaryFile;
        }

        if (ConsoleNames.Contains(name.Split('.')[0]))
        {
            return HiddenReason.InvalidName;
        }

        return FileNaming.CheckName(name) switch
        {
            NamingProblem.None => HiddenReason.None,
            NamingProblem.NameTooLong => HiddenReason.TooLong,
            _ => HiddenReason.InvalidName,
        };
    }

    /// <summary>
    /// The entries the PC shows, in the server's order: those whose names pass <see cref="Check"/>,
    /// without a second one whose name differs only in case, up to <see cref="MaximumEntries"/>.
    /// </summary>
    public static NameSelection<T> Select<T>(IReadOnlyList<T> entries, Func<T, string> name, bool truncated = false, int maximum = MaximumEntries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(name);
        var shown = new List<T>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hidden = new Dictionary<HiddenReason, int>();
        void Hide(HiddenReason reason) => hidden[reason] = hidden.GetValueOrDefault(reason) + 1;

        foreach (var entry in entries)
        {
            var reason = Check(name(entry));
            if (reason == HiddenReason.None && shown.Count == maximum)
            {
                reason = HiddenReason.TooMany;
            }
            else if (reason == HiddenReason.None && !names.Add(name(entry)))
            {
                reason = HiddenReason.SameNameOtherCase;
            }

            if (reason == HiddenReason.None)
            {
                shown.Add(entry);
            }
            else
            {
                Hide(reason);
            }
        }

        if (truncated)
        {
            Hide(HiddenReason.TooMany);
        }

        return new NameSelection<T>(shown, hidden);
    }

    /// <summary>Whether File Explorer shows it only with Hidden items on: names that start with a dot, which Linux hides.</summary>
    public static bool IsHiddenOnPc(string name) => name.StartsWith('.');

    /// <summary>
    /// Whether a file made on the PC stays there, never uploaded: the names programs give their
    /// temporary and lock files, <c>~$*</c>, <c>~*.tmp</c>, <c>.~lock.*#</c>, <c>Thumbs.db</c> and <c>desktop.ini</c>.
    /// </summary>
    public static bool StaysOnPc(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith("~$", StringComparison.Ordinal)
            || (name.StartsWith(".~lock.", StringComparison.Ordinal) && name.EndsWith('#'))
            || (name.StartsWith('~') && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            || string.Equals(name, "Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase);
    }
}
