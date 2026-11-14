using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// What a placeholder keeps about its file or folder on the server: where it is, relative to the
/// server's folder, and the version the PC's copy came from, by size and modification time. Windows
/// keeps it in the placeholder, so it stays across restarts, renames and moves on the PC.
/// </summary>
/// <param name="RelativePath">The path on the server below the server's folder, with <c>/</c> between folders; empty for the folder itself.</param>
public sealed record ItemIdentity(string RelativePath, bool IsDirectory, long Size, DateTime LastWriteTimeUtc)
{
    /// <summary>The most bytes Windows keeps in a placeholder's identity.</summary>
    public const int MaximumLength = 4 * 1024;

    // Format: version, flags, size, time in ticks, then the path in UTF-8.
    private const byte FormatVersion = 1;
    private const int HeaderLength = 1 + 1 + 8 + 8;

    /// <summary>Whether a placeholder can keep the identity of <paramref name="relativePath"/>.</summary>
    public static bool Fits(string relativePath) => HeaderLength + Encoding.UTF8.GetByteCount(relativePath) <= MaximumLength;

    /// <summary>The identity of an entry on the server, in the folder at <paramref name="relativeFolder"/>.</summary>
    public static ItemIdentity Of(string relativeFolder, string name, bool isDirectory, long size, DateTime lastWriteTimeUtc) =>
        new(Join(relativeFolder, name), isDirectory, isDirectory ? 0 : size, lastWriteTimeUtc);

    /// <summary>Joins a relative folder and a name with <c>/</c>.</summary>
    public static string Join(string relativeFolder, string name) => relativeFolder.Length == 0 ? name : relativeFolder + "/" + name;

    /// <summary>The path on the server: <paramref name="remoteFolder"/>, the server's folder, followed by the relative path.</summary>
    public static string RemotePath(string remoteFolder, string relativePath) =>
        relativePath.Length == 0 ? remoteFolder : remoteFolder.TrimEnd('/') + "/" + relativePath;

    /// <summary>The name of the file or folder, the end of its path.</summary>
    public string Name => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];

    /// <summary>Whether the server's file is still the version the PC's copy came from.</summary>
    public bool IsSameVersion(long size, DateTime lastWriteTimeUtc) => IsDirectory || (Size == size && LastWriteTimeUtc == lastWriteTimeUtc);

    /// <summary>The bytes a placeholder keeps.</summary>
    /// <exception cref="InvalidOperationException">The path is too long for a placeholder; check <see cref="Fits"/> first.</exception>
    public byte[] ToBytes()
    {
        byte[] path = Encoding.UTF8.GetBytes(RelativePath);
        if (HeaderLength + path.Length > MaximumLength)
        {
            throw new InvalidOperationException("The path is too long for a placeholder's identity.");
        }

        byte[] bytes = new byte[HeaderLength + path.Length];
        bytes[0] = FormatVersion;
        bytes[1] = IsDirectory ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(2), Size);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(10), LastWriteTimeUtc.Ticks);
        path.CopyTo(bytes, HeaderLength);
        return bytes;
    }

    /// <summary>The identity in a placeholder's bytes, or <see langword="null"/> when they aren't one of this format, such as a placeholder another program made.</summary>
    public static ItemIdentity? FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength || bytes.Length > MaximumLength || bytes[0] != FormatVersion || bytes[1] > 1)
        {
            return null;
        }

        long size = BinaryPrimitives.ReadInt64LittleEndian(bytes[2..]);
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes[10..]);
        if (size < 0 || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return null;
        }

        try
        {
            string path = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes[HeaderLength..]);
            return path.StartsWith('/') || path.EndsWith('/') || path.Contains("//", StringComparison.Ordinal) || path.Split('/').Any(part => part is "." or "..")
                ? null
                : new ItemIdentity(path, bytes[1] == 1, size, new DateTime(ticks, DateTimeKind.Utc));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}

/// <summary>Names of conflict copies: the PC's version of a file that also changed on the server.</summary>
public static class ConflictNames
{
    /// <summary>
    /// The conflict copy's name for <paramref name="name"/>, such as <c>Report (conflict 14-11-2026 15.30).docx</c>,
    /// with the words in the language of <paramref name="language"/> and the date and time in the
    /// formats of <paramref name="formats"/>, such as the user's regional settings, made safe for file
    /// names. A name that would be too long is shortened before the extension.
    /// </summary>
    public static string For(string name, DateTime localTime, CultureInfo language, CultureInfo formats)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(formats);
        string extension = Path.GetExtension(name);
        string stem = name[..^extension.Length];
        string when = Safe(localTime.ToString("d", formats)) + " " + Safe(localTime.ToString("t", formats));
        string pattern = Strings.ResourceManager.GetString(nameof(Strings.RemoteFoldersConflictName), language) ?? "{0} (conflict {1})";
        string result = string.Format(formats, pattern, stem, when) + extension;
        if (result.Length <= FileNaming.MaximumNameLength)
        {
            return result;
        }

        int over = result.Length - FileNaming.MaximumNameLength;
        return string.Format(formats, pattern, stem[..Math.Max(1, stem.Length - over)], when) + extension;
    }

    /// <summary>
    /// Another conflict copy's name, when <paramref name="conflictName"/> is taken, as by a conflict
    /// in the same minute: <c>Report (conflict 14-11-2026 15.30) 2.docx</c>.
    /// </summary>
    public static string Numbered(string conflictName, int number)
    {
        ArgumentNullException.ThrowIfNull(conflictName);
        string extension = Path.GetExtension(conflictName);
        string stem = conflictName[..^extension.Length];
        string suffix = " " + number.ToString(CultureInfo.InvariantCulture);
        int over = stem.Length + suffix.Length + extension.Length - FileNaming.MaximumNameLength;
        return (over > 0 ? stem[..Math.Max(1, stem.Length - over)] : stem) + suffix + extension;
    }

    // Dates and times can hold characters file names can't, such as / and :.
    private static string Safe(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            builder.Append(c switch
            {
                ':' => '.',
                _ when Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 => '-',
                _ => c,
            });
        }

        return builder.ToString();
    }
}
