using System;
using System.Text;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.LauncherActions;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// The command's arguments, and its requests and answers through the pipe.
public sealed class LauncherCommandTests
{
    private static readonly FeatureAction UseOffice = new("networkProfiles", "use", "Office");

    [Fact]
    public void TwoOrThreeArgumentsNameAnAction()
    {
        Assert.Equal(new FeatureAction("hideApps", "showAll"), LauncherCommand.Read(["hideApps", "showAll"]));
        Assert.Equal(UseOffice, LauncherCommand.Read(["networkProfiles", "use", "Office"]));
    }

    [Theory]
    [InlineData]
    [InlineData("hideApps")]
    [InlineData("networkProfiles", "use", "Office", "Home")]
    public void FewerOrMoreArgumentsArentACommand(params string[] arguments) =>
        Assert.Null(LauncherCommand.Read(arguments));

    [Theory]
    [InlineData("network-profiles", "use")]
    [InlineData("networkProfiles", "use ")]
    [InlineData("", "use")]
    [InlineData("networkProfiles", "")]
    [InlineData("netzwerkprofilé", "use")]
    [InlineData("networkProfiles", "use\u0000")]
    public void IdsAreAsciiLettersAndDigits(string featureId, string actionId) =>
        Assert.Null(LauncherCommand.Read([featureId, actionId]));

    [Fact]
    public void IdsAreAtMost64Characters()
    {
        Assert.NotNull(LauncherCommand.Read([new string('a', 64), "use"]));
        Assert.Null(LauncherCommand.Read([new string('a', 65), "use"]));
        Assert.Null(LauncherCommand.Read(["networkProfiles", new string('a', 65)]));
    }

    [Theory]
    [InlineData(" Büro ")]
    [InlineData("C:\\Program Files\\Notes\\Notes.exe")]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App")]
    [InlineData("مكتب")]
    [InlineData("Office: \"2nd floor\"")]
    public void ATargetMayHaveAnyCharacterButControlCharacters(string target) =>
        Assert.Equal(new FeatureAction("networkProfiles", "use", target), LauncherCommand.Read(["networkProfiles", "use", target]));

    [Theory]
    [InlineData("")]
    [InlineData("Office\n")]
    [InlineData("Off\u0007ice")]
    public void AnEmptyTargetOrOneWithControlCharactersIsntOne(string target) =>
        Assert.Null(LauncherCommand.Read(["networkProfiles", "use", target]));

    [Fact]
    public void ATargetIsAtMost256Characters()
    {
        Assert.NotNull(LauncherCommand.Read(["networkProfiles", "use", new string('a', 256)]));
        Assert.Null(LauncherCommand.Read(["networkProfiles", "use", new string('a', 257)]));
    }

    [Fact]
    public void TheArgumentsRunTheAction()
    {
        Assert.Equal(["--run", "networkProfiles", "use", "Office"], LauncherCommand.Arguments(UseOffice));
        Assert.Equal(["--run", "hideApps", "showAll"], LauncherCommand.Arguments(new FeatureAction("hideApps", "showAll")));
    }

    [Fact]
    public void ARequestIsTheSpecsJson()
    {
        Assert.Equal("""{"feature":"networkProfiles","action":"use","target":"Office"}""", Encoding.UTF8.GetString(LauncherPipe.WriteRequest(UseOffice)));
        Assert.Equal("""{"feature":"hideApps","action":"showAll"}""", Encoding.UTF8.GetString(LauncherPipe.WriteRequest(new FeatureAction("hideApps", "showAll"))));
    }

    [Fact]
    public void ARequestReadsBackAsTheAction()
    {
        Assert.Equal(UseOffice, LauncherPipe.ReadRequest(LauncherPipe.WriteRequest(UseOffice)));
        Assert.Equal(new FeatureAction("hideApps", "showAll"), LauncherPipe.ReadRequest("""{"feature":"hideApps","action":"showAll"}"""u8));
    }

    [Fact]
    public void TheLongestCommandFitsInARequestWithEveryCharacterEscaped()
    {
        var longest = new FeatureAction(new string('a', 64), new string('b', 64), new string('é', 256));

        var request = LauncherPipe.WriteRequest(longest);

        Assert.InRange(request.Length, 1, LauncherPipe.MaximumRequestLength);
        Assert.Equal(longest, LauncherPipe.ReadRequest(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"feature":"networkProfiles"}""")]
    [InlineData("""{"feature":null,"action":"use"}""")]
    [InlineData("""{"feature":"networkProfiles","action":"use","target":7}""")]
    [InlineData("""{"feature":"network profiles","action":"use"}""")]
    [InlineData("""{"feature":"networkProfiles","action":"use","target":"Off\nice"}""")]
    [InlineData("""{"feature":"networkProfiles","action":"use","target":""}""")]
    public void ARequestThatIsntACommandIsntRead(string request) =>
        Assert.Null(LauncherPipe.ReadRequest(Encoding.UTF8.GetBytes(request)));

    [Fact]
    public void AnswersAreTheSpecsJson()
    {
        Assert.Equal("""{"result":"ran"}""", Encoding.UTF8.GetString(LauncherPipe.WriteAnswer(LauncherResult.Ran)));
        Assert.Equal("""{"result":"refused"}""", Encoding.UTF8.GetString(LauncherPipe.WriteAnswer(LauncherResult.Refused)));
        Assert.Equal("""{"result":"invalid"}""", Encoding.UTF8.GetString(LauncherPipe.WriteAnswer(LauncherResult.Invalid)));
        Assert.All(Enum.GetValues<LauncherResult>(), result => Assert.Equal(result, LauncherPipe.ReadAnswer(LauncherPipe.WriteAnswer(result))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"result":"done"}""")]
    [InlineData("""{"result":7}""")]
    public void AnAnswerThatIsntOneIsntRead(string answer) =>
        Assert.Null(LauncherPipe.ReadAnswer(Encoding.UTF8.GetBytes(answer)));

    [Fact]
    public void ThePipeIsNamedForTheSession() =>
        Assert.StartsWith("PCCompanion.Actions.", LauncherPipe.Name, StringComparison.Ordinal);
}
