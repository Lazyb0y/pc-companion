using System;
using System.Text;
using PCCompanion.Core;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

// What a host running as administrator passes to its copy without admin rights to open an app: the
// name of a pipe on the command line, and the app ID through the pipe.
public sealed class OpenAppArgumentsTests
{
    private const string PipeName = "PCCompanion.OpenApp.1.0123456789abcdef0123456789abcdef";

    [Fact]
    public void TheCopyReadsThePipeTheHostPasses()
    {
        var arguments = HostProcess.OpenAppArguments(PipeName);

        // Split as Windows splits a command line without quotes.
        Assert.Equal(["--open-app", PipeName], arguments.Split(' '));
        Assert.Equal(PipeName, HostProcess.ReadOpenAppPipe(arguments.Split(' ')));
    }

    [Theory]
    [InlineData("--open-app")]
    [InlineData("--open-app", @"\\server\pipe\name")]
    [InlineData("--run-program", PipeName)]
    public void TheCopyReadsNothingButAPipeName(params string[] arguments)
    {
        Assert.Null(HostProcess.ReadOpenAppPipe(arguments));
    }

    [Theory]
    [InlineData("MSEdge")]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App")]
    [InlineData("Chrome.UserData.Profile1")]
    public void AnAppIdArrivesAsSent(string appId)
    {
        Assert.True(HostProcess.IsAppId(appId));
        Assert.Equal(appId, HostProcess.ReadAppId(HostProcess.AppIdHandOff(appId)));
    }

    // An app ID names an app in Start's Apps folder and no other place.
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"..\..\Windows\System32\cmd.exe")]
    [InlineData("../other")]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\App\App.exe")]
    [InlineData("line\nbreak")]
    public void WhatIsntAnAppIdIsNeitherSentNorRead(string appId)
    {
        Assert.False(HostProcess.IsAppId(appId));
        Assert.Throws<ArgumentException>(() => HostProcess.AppIdHandOff(appId));
        Assert.Null(HostProcess.ReadAppId(Encoding.UTF8.GetBytes(appId)));
    }

    [Fact]
    public void AnAppIdHasAtMost128Characters()
    {
        Assert.True(HostProcess.IsAppId(new string('a', 128)));
        Assert.False(HostProcess.IsAppId(new string('a', 129)));
    }
}
