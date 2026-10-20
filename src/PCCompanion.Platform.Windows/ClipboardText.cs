using System;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

namespace PCCompanion.Platform.Windows;

/// <summary>What <see cref="ClipboardText.Read"/> found on the clipboard.</summary>
public enum ClipboardTextStatus
{
    /// <summary>Text, of up to <see cref="ClipboardText.MaxLength"/> characters.</summary>
    Text,

    /// <summary>No text, such as a picture or copied files.</summary>
    NoText,

    /// <summary>The app that copied it marked it private, so it wasn't read.</summary>
    Private,

    /// <summary>Text longer than <see cref="ClipboardText.MaxLength"/> characters, which wasn't kept.</summary>
    TooLong,

    /// <summary>Another app kept the clipboard open.</summary>
    Busy,

    /// <summary>
    /// The app that copied the text didn't provide it when asked, for example because it stopped
    /// responding or ended.
    /// </summary>
    Unavailable,
}

/// <param name="Text">The text, when <paramref name="Status"/> is <see cref="ClipboardTextStatus.Text"/>.</param>
public sealed record ClipboardTextResult(ClipboardTextStatus Status, string? Text = null);

/// <summary>
/// Reads the text on the clipboard, unless the app that copied it marked it private, as password
/// managers do. See "Reading the clipboard" in docs/features/clipboard/clipboard-actions.md.
/// </summary>
public static class ClipboardText
{
    /// <summary>The longest text that is read.</summary>
    public const int MaxLength = 4096;

    private const int Tries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    // Marks apps put on the clipboard to keep what they copy out of clipboard history and tools.
    private const string ExcludeMark = "ExcludeClipboardContentFromMonitorProcessing";
    private const string HistoryMark = "CanIncludeInClipboardHistory";
    private const string ViewerIgnoreMark = "Clipboard Viewer Ignore";

    /// <summary>
    /// Reads the clipboard's text with <paramref name="window"/>, a window of the calling thread.
    /// The clipboard stays open only while the text is copied. When another app has it open, it
    /// tries again, five times in all, 20 ms apart. An app that provides its text only when asked
    /// makes this wait for it, up to 30 seconds, so don't call it on the host's UI thread.
    /// </summary>
    public static unsafe ClipboardTextResult Read(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!Open(new HWND(window.Handle)))
        {
            return new ClipboardTextResult(ClipboardTextStatus.Busy);
        }

        try
        {
            if (Has(ExcludeMark) || Has(ViewerIgnoreMark) || ReadNumber(HistoryMark) == 0)
            {
                return new ClipboardTextResult(ClipboardTextStatus.Private);
            }

            const uint textFormat = (uint)CLIPBOARD_FORMAT.CF_UNICODETEXT;
            if (!PInvoke.IsClipboardFormatAvailable(textFormat))
            {
                return new ClipboardTextResult(ClipboardTextStatus.NoText);
            }

            var data = (HGLOBAL)PInvoke.GetClipboardData(textFormat).Value;
            var characters = (char*)PInvoke.GlobalLock(data);
            if (characters is null)
            {
                return new ClipboardTextResult(ClipboardTextStatus.Unavailable);
            }

            try
            {
                var length = (int)Math.Min((long)(PInvoke.GlobalSize(data) / sizeof(char)), MaxLength + 1);
                return FromBuffer(new ReadOnlySpan<char>(characters, length));
            }
            finally
            {
                PInvoke.GlobalUnlock(data);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>
    /// The text in the start of a clipboard text buffer, of up to <see cref="MaxLength"/> + 1
    /// characters: the text ends at the first NUL character, or at the end of a shorter buffer.
    /// Without a NUL in a longer buffer, the text is too long.
    /// </summary>
    public static ClipboardTextResult FromBuffer(ReadOnlySpan<char> buffer)
    {
        var end = buffer.IndexOf('\0');
        if (end < 0 && buffer.Length > MaxLength)
        {
            return new ClipboardTextResult(ClipboardTextStatus.TooLong);
        }

        return new ClipboardTextResult(ClipboardTextStatus.Text, new string(end < 0 ? buffer : buffer[..end]));
    }

    private static bool Open(HWND window)
    {
        for (var attempt = 1; attempt <= Tries; attempt++)
        {
            if (PInvoke.OpenClipboard(window))
            {
                return true;
            }

            if (attempt < Tries)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        return false;
    }

    private static bool Has(string mark)
    {
        var format = PInvoke.RegisterClipboardFormat(mark);
        return format != 0 && PInvoke.IsClipboardFormatAvailable(format);
    }

    // The number a mark holds, or null when it isn't there or holds none.
    private static unsafe uint? ReadNumber(string mark)
    {
        var format = PInvoke.RegisterClipboardFormat(mark);
        if (format == 0 || !PInvoke.IsClipboardFormatAvailable(format))
        {
            return null;
        }

        var data = (HGLOBAL)PInvoke.GetClipboardData(format).Value;
        var value = (uint*)PInvoke.GlobalLock(data);
        if (value is null)
        {
            return null;
        }

        try
        {
            return PInvoke.GlobalSize(data) >= sizeof(uint) ? *value : null;
        }
        finally
        {
            PInvoke.GlobalUnlock(data);
        }
    }
}
