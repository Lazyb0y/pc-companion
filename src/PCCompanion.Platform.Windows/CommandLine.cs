using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Windows command lines.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// Splits arguments typed on one line into separate arguments, by the rules Windows programs
    /// use to read their command line: spaces separate arguments, quotes group them, and a
    /// backslash before a quote escapes it.
    /// </summary>
    public static unsafe IReadOnlyList<string> SplitArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        // The first item of a command line is the program, which follows other rules; a placeholder takes its place.
        var items = PInvoke.CommandLineToArgv("x " + arguments, out var count);
        if (items is null)
        {
            throw new Win32Exception();
        }

        try
        {
            var result = new string[count - 1];
            for (var index = 1; index < count; index++)
            {
                result[index - 1] = items[index].ToString();
            }

            return result;
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)(nint)items);
        }
    }
}
