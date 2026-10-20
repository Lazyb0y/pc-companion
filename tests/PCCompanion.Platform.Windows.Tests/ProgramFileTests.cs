using System;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ProgramFileTests
{
    private const string Program = @"C:\Program Files\Some App\App.exe";

    [Theory]
    [InlineData(null, ProgramProblem.NotChosen)]
    [InlineData("  ", ProgramProblem.NotChosen)]
    [InlineData("App.exe", ProgramProblem.NotFullPath)]
    [InlineData(@"Tools\App.exe", ProgramProblem.NotFullPath)]
    [InlineData(@"C:App.exe", ProgramProblem.NotFullPath)]
    [InlineData(@"C:\Scripts\Clean.bat", ProgramProblem.NotExe)]
    [InlineData(@"C:\Scripts\Clean.cmd", ProgramProblem.NotExe)]
    [InlineData(@"C:\Users\Public\Desktop\App.lnk", ProgramProblem.NotExe)]
    [InlineData(@"C:\Missing\App.exe", ProgramProblem.NotFound)]
    [InlineData(Program, ProgramProblem.None)]
    [InlineData(@"C:\PROGRAM FILES\SOME APP\APP.EXE", ProgramProblem.None)]
    [InlineData(@"\\server\share\App.exe", ProgramProblem.NotFound)]
    public void CheckAcceptsOnlyTheFullPathOfAnExistingExe(string? program, ProgramProblem expected)
    {
        Assert.Equal(expected, ProgramFile.Check(program, Exists));
    }

    private static bool Exists(string path) =>
        new[] { Program, @"C:\Scripts\Clean.bat", @"C:\Scripts\Clean.cmd", @"C:\Users\Public\Desktop\App.lnk" }
            .Contains(path, StringComparer.OrdinalIgnoreCase);
}
