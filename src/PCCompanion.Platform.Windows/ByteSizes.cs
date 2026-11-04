using System;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>Sizes in bytes, written as File Explorer writes them, such as <c>12.3 GB</c>.</summary>
public static class ByteSizes
{
    private const int BufferLength = 64;

    /// <summary>Writes <paramref name="bytes"/> as File Explorer does, in Windows' display language.</summary>
    public static unsafe string Format(long bytes)
    {
        var buffer = stackalloc char[BufferLength];
        var result = PInvoke.StrFormatByteSizeEx((ulong)Math.Max(0, bytes), SFBS_FLAGS.SFBS_FLAGS_ROUND_TO_NEAREST_DISPLAYED_DIGIT, buffer, BufferLength);
        return result.Succeeded ? new string(buffer) : bytes.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }
}
