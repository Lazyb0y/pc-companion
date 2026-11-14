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

    [Theory]
    [InlineData(new[] { "--run", "networkProfiles", "use", "Office" }, "--run networkProfiles use Office")]
    [InlineData(new[] { "use", "Office 2" }, "use \"Office 2\"")]
    [InlineData(new[] { "", "x" }, "\"\" x")]
    [InlineData(new[] { "say \"hi\"" }, @"""say \""hi\""""")]
    [InlineData(new[] { @"C:\Folder\" }, @"C:\Folder\")]
    [InlineData(new[] { @"C:\My Folder\" }, @"""C:\My Folder\\""")]
    public void ArgumentsJoinAsWindowsProgramsReadThem(string[] arguments, string expected)
    {
        Assert.Equal(expected, CommandLine.Join(arguments));
    }

    [Theory]
    [InlineData("Office")]
    [InlineData(" Büro ")]
    [InlineData("a\\\"b")]
    [InlineData(@"\\server\share\")]
    [InlineData("tab\there")]
    [InlineData("quote \" and backslashes \\\\\" end\\")]
    [InlineData("مكتب ٢")]
    public void JoinedArgumentsSplitBackIntoThemselves(string argument)
    {
        Assert.Equal(["first", argument, "last"], CommandLine.SplitArguments(CommandLine.Join(["first", argument, "last"])));
    }
}
