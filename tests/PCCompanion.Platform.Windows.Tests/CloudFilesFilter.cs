using System.IO;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// How Windows answers Cloud Files requests on the tests' own files. On GitHub's build machines it
/// answers some of them with <c>ERROR_INVALID_FUNCTION</c>, as it does on a volume its Cloud Files
/// filter isn't attached to, instead of the answer a test checks, so that test is skipped there.
/// </summary>
internal static class CloudFilesFilter
{
    // HRESULT_FROM_WIN32(ERROR_INVALID_FUNCTION)
    private const int NotAnswered = unchecked((int)0x80070001);

    private const string Reason = "Windows' Cloud Files filter doesn't answer for the test folder's volume, as on the build machines.";

    /// <summary>Checks that Windows answered <paramref name="expected"/>, or skips the test when its filter didn't answer.</summary>
    public static void AssertAnswer(int expected, int actual)
    {
        Assert.SkipWhen(actual == NotAnswered, Reason);
        Assert.Equal(expected, actual);
    }

    /// <summary>Reads a placeholder's state as <see cref="Placeholders.Read"/> does, or skips the test when the filter didn't answer.</summary>
    public static PlaceholderState? Read(string path)
    {
        try
        {
            return Placeholders.Read(path);
        }
        catch (IOException exception) when (exception.HResult == NotAnswered)
        {
            Assert.Skip(Reason);
            throw;
        }
    }
}
