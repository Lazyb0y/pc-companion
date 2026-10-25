using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

// Fake host commands record what would open or start; nothing runs.
public sealed class ClipboardActionRunnerTests
{
    private const string Player = @"C:\Program Files\Example\player.exe";

    private readonly FakeHostCommands _commands = new();
    private readonly RecordingLogger<ClipboardActionRunnerTests> _logger = new();

    [Fact]
    public void LinksAndAddressesOpenThroughTheHost()
    {
        var link = new Uri("https://example.com/");
        var address = new Uri("https://tracking.example.com/?n=1");

        Assert.True(Run(new ClipboardOffer(OfferKind.OpenLink, string.Empty, 0, link)));
        Assert.True(Run(new ClipboardOffer(OfferKind.OpenAddress, "Track", 3, address)));

        Assert.Equal([link, address], _commands.WebPages);
        Assert.Equal(
            [
                (LogLevel.Debug, "Clipboard Actions opened the address of rule 0 (0 is Open in browser)."),
                (LogLevel.Debug, "Clipboard Actions opened the address of rule 3 (0 is Open in browser)."),
            ],
            _logger.Entries);
    }

    [Fact]
    public void AnAddressTheHostCantOpenIsLogged()
    {
        _commands.OpensWebPages = false;

        Assert.False(Run(new ClipboardOffer(OfferKind.OpenAddress, "Track", 2, new Uri("https://example.com/"))));

        Assert.Equal([(LogLevel.Warning, "Clipboard Actions couldn't open the address of rule 2 (0 is Open in browser).")], _logger.Entries);
    }

    [Fact]
    public void TheHostStartsTheProgramWithTheTextInOneArgument()
    {
        Assert.True(Run(Program(Player, "--fullscreen", "say \"hi\" & del x")));

        var (program, arguments) = Assert.Single(_commands.Programs);
        Assert.Equal(Player, program);
        Assert.Equal(["--fullscreen", "say \"hi\" & del x"], arguments);
        Assert.Equal([(LogLevel.Debug, "Clipboard Actions rule 1 started player.exe.")], _logger.Entries);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", "CommandInterpreter")]
    [InlineData(@"C:\Missing\player.exe", "NotFound")]
    [InlineData(@"C:\Scripts\Open.bat", "NotExe")]
    public void AProgramThatCantRunIsLoggedByItsFileNameOnly(string program, string problem)
    {
        Assert.False(Run(Program(program, "secret-123")));

        Assert.Empty(_commands.Programs);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, $"Clipboard Actions rule 1 didn't start {System.IO.Path.GetFileName(program)}: {problem}."), entry);
    }

    [Fact]
    public void AStartThatWindowsRefusesIsLoggedWithItsErrorCode()
    {
        _commands.ProgramErrorCode = 740; // ERROR_ELEVATION_REQUIRED

        Assert.False(Run(Program(Player, "secret-123")));

        Assert.Equal([(LogLevel.Warning, "Windows couldn't start player.exe (error 740).")], _logger.Entries);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Program Files", StringComparison.Ordinal) || entry.Message.Contains("secret", StringComparison.Ordinal));
    }

    private static ClipboardOffer Program(string program, params string[] arguments) =>
        new(OfferKind.RunProgram, "Play", 1, Program: program, Arguments: arguments);

    private static bool Exists(string path) =>
        string.Equals(path, Player, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private bool Run(ClipboardOffer offer) => new ClipboardActionRunner(_commands, Exists, _logger).Run(offer);
}
