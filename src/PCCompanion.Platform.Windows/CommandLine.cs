using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Windows command lines.
/// </summary>
public static class CommandLine
{
    /// <summary>
    /// Joins arguments into one command line that Windows programs read back as the same
    /// arguments, by the rules <see cref="SplitArguments"/> follows: an argument with a space, a tab
    /// or a quote, or an empty one, is quoted, and backslashes before a quote are escaped.
    /// </summary>
    public static string Join(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Join(' ', arguments.Select(Quote));
    }

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

    private static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        // Backslashes are literal unless a quote follows them, as the closing quote does.
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes);
            quoted.Append(character);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
