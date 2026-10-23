using System;
using System.Text;
using PCCompanion.Core;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

// What a host running as administrator passes to its copy without admin rights to start a program:
// the name of a pipe on the command line, and the program and its arguments through the pipe.
public sealed class RunProgramArgumentsTests
{
    private const string PipeName = "PCCompanion.RunProgram.1.0123456789abcdef0123456789abcdef";

    [Fact]
    public void TheCopyReadsThePipeTheHostPasses()
    {
        var arguments = HostProcess.RunProgramArguments(PipeName);

        // Split as Windows splits a command line without quotes.
        Assert.Equal(["--run-program", PipeName], arguments.Split(' '));
        Assert.Equal(PipeName, HostProcess.ReadRunProgramPipe(arguments.Split(' ')));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("quote\"d")]
    [InlineData(@"..\other")]
    public void OnlyPipeNamesArePassed(string name)
    {
        Assert.Throws<ArgumentException>(() => HostProcess.RunProgramArguments(name));
    }

    [Theory]
    [InlineData("--run-program")]
    [InlineData("--run-program", @"\\server\pipe\name")]
    [InlineData("--open-web-page", PipeName)]
    public void TheCopyReadsNothingButAPipeName(params string[] arguments)
    {
        Assert.Null(HostProcess.ReadRunProgramPipe(arguments));
    }

    [Fact]
    public void TheProgramAndItsArgumentsArriveAsSent()
    {
        var text = "Copied text, " + new string('y', 4096);
        var sent = new ProgramToStart(@"C:\Program Files\Some App\App.exe", ["one", "two words", "a \"quoted\" one", @"trailing\", text]);

        var read = ProgramToStart.FromJson(sent.ToJson());

        Assert.NotNull(read);
        Assert.Equal(sent.Program, read.Program);
        Assert.Equal(sent.Arguments, read.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "arguments": [] }""")]
    [InlineData("""{ "program": "", "arguments": [] }""")]
    [InlineData("""{ "program": "C:\\App.exe" }""")]
    [InlineData("""{ "program": "C:\\App.exe", "arguments": [null] }""")]
    public void AHandOffThatIsntOneIsNotRead(string json)
    {
        Assert.Null(ProgramToStart.FromJson(Encoding.UTF8.GetBytes(json)));
    }
}
