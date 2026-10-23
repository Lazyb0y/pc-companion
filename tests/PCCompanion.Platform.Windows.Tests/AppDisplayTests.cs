using System;
using System.IO;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads names and icons only; nothing is changed.
public sealed class AppDisplayTests
{
    private static readonly string CommandPrompt = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void AProgramsNameIsItsDescription()
    {
        var name = AppDisplay.NameOfProgram(CommandPrompt);

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.NotEqual("cmd", name);
    }

    [Fact]
    public void AMissingProgramsNameIsItsFileName()
    {
        Assert.Equal("Missing", AppDisplay.NameOfProgram(@"C:\PCCompanion.Tests\Missing.exe"));
    }

    [Fact]
    public void AProgramHasAnIconWithPixels()
    {
        var icon = AppDisplay.IconOfProgram(CommandPrompt, 32);

        Assert.NotNull(icon);
        Assert.InRange(icon.Width, 16, 256);
        Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length);
        Assert.Contains(icon.Pixels.ToArray(), value => value != 0);
    }

    [Fact]
    public void AMissingProgramHasNoIcon()
    {
        Assert.Null(AppDisplay.IconOfProgram(@"C:\PCCompanion.Tests\Missing.exe", 32));
    }

    [Fact]
    public void AnUnknownAppIdHasNoNameAndNoIcon()
    {
        const string appId = "PCCompanion.Tests.NoSuchApp";

        Assert.Null(AppDisplay.NameOfAppId(appId));
        Assert.Null(AppDisplay.IconOfAppId(appId, 32));
    }
}
