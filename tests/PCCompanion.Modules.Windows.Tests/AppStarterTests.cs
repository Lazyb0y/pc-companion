using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class AppStarterTests
{
    private const string Program = @"C:\Apps\Notes.exe";

    private readonly FakeHostCommands _commands = new();

    [Fact]
    public void AProgramStartsWithoutArguments()
    {
        Assert.Equal(DropDownOutcome.Started, Start(new DropDownApp { Program = Program }));

        var (program, arguments) = Assert.Single(_commands.Programs);
        Assert.Equal(Program, program);
        Assert.Empty(arguments);
    }

    [Theory]
    [InlineData(@"C:\Apps\Missing.exe")]
    [InlineData(@"C:\Apps\Notes.bat")]
    [InlineData("Notes.exe")]
    public void AMissingProgramOrOneThatIsntAnExeIsntStarted(string program)
    {
        Assert.Equal(DropDownOutcome.ProgramMissing, AppStarter.Start(new DropDownApp { Program = program }, _commands, path => path != @"C:\Apps\Missing.exe"));

        Assert.Empty(_commands.Programs);
    }

    [Theory]
    [InlineData(740, DropDownOutcome.NeedsAdminRights)]
    [InlineData(2, DropDownOutcome.ProgramMissing)]
    [InlineData(1260, DropDownOutcome.StartFailed)]
    public void WindowsAnswerToAProgramIsTheOutcome(int errorCode, DropDownOutcome outcome)
    {
        _commands.ProgramErrorCode = errorCode;

        Assert.Equal(outcome, Start(new DropDownApp { Program = Program }));
    }

    [Theory]
    [InlineData(0, DropDownOutcome.Started)]
    [InlineData(2, DropDownOutcome.AppIdNotFound)]
    [InlineData(1460, DropDownOutcome.StartFailed)]
    public void AnAppIdOpensThroughTheHost(int errorCode, DropDownOutcome outcome)
    {
        _commands.AppErrorCode = errorCode;

        Assert.Equal(outcome, Start(new DropDownApp { AppId = "MSEdge" }));

        Assert.Equal(["MSEdge"], _commands.Apps);
        Assert.Empty(_commands.Programs);
    }

    private DropDownOutcome Start(DropDownApp app) => AppStarter.Start(app, _commands, _ => true);
}
