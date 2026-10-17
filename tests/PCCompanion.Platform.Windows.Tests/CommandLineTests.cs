using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoArgumentsSplitIntoNone(string? arguments)
    {
        Assert.Empty(CommandLine.SplitArguments(arguments));
    }

    [Theory]
    [InlineData("a b  c", new[] { "a", "b", "c" })]
    [InlineData("--new \"two words\" x", new[] { "--new", "two words", "x" })]
    [InlineData(@"C:\Users\Public\file.txt", new[] { @"C:\Users\Public\file.txt" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    [InlineData(@"a\""b", new[] { "a\"b" })]
    [InlineData(@"""C:\Folder\\"" b", new[] { @"C:\Folder\", "b" })]
    [InlineData("-File \"C:\\Scripts\\My script.ps1\"", new[] { "-File", @"C:\Scripts\My script.ps1" })]
    public void ArgumentsSplitByTheWindowsRules(string arguments, string[] expected)
    {
        Assert.Equal(expected, CommandLine.SplitArguments(arguments));
    }
}
