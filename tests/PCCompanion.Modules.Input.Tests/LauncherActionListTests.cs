using System.Linq;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.LauncherActions;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// Start entries, commands, and the action a command names.
public sealed class LauncherActionListTests
{
    private const string NameFormat = "{0} - {1}";

    private static readonly LauncherAction UseOffice = new(new FeatureAction("networkProfiles", "use", "Office"), "Use Office", "Network Profiles");
    private static readonly LauncherAction ShowAll = new(new FeatureAction("hideApps", "showAll"), "Show all hidden windows", "Hide Apps");

    [Fact]
    public void EachActionIsAnEntryNamedForItAndItsFeatureThatRunsTheCommand()
    {
        var entries = LauncherActionList.Entries([UseOffice, ShowAll], NameFormat);

        Assert.Equal(
            [
                new StartEntry("Use Office - Network Profiles", "--run networkProfiles use Office", "Network Profiles"),
                new StartEntry("Show all hidden windows - Hide Apps", "--run hideApps showAll", "Hide Apps"),
            ],
            entries);
    }

    [Fact]
    public void TranslationsCanReorderTheName()
    {
        var entry = Assert.Single(LauncherActionList.Entries([UseOffice], "{1}: {0}"));

        Assert.Equal("Network Profiles  Use Office", entry.Name);
    }

    [Fact]
    public void ATargetWithSpacesOrQuotesIsQuoted()
    {
        var action = UseOffice with { Action = new FeatureAction("networkProfiles", "use", "Office \"2\"") };

        Assert.Equal("--run networkProfiles use \"Office \\\"2\\\"\"", Assert.Single(LauncherActionList.Entries([action], NameFormat)).Arguments);
    }

    [Theory]
    [InlineData("Use Office: 2nd floor", "Use Office  2nd floor - Network Profiles")]
    [InlineData("Use A/B", "Use A B - Network Profiles")]
    [InlineData("Use <Home>?", "Use  Home   - Network Profiles")]
    [InlineData("Use \"Lab\" | *", "Use  Lab      - Network Profiles")]
    [InlineData("Use C:\\Lab", "Use C  Lab - Network Profiles")]
    public void CharactersWindowsDoesntAllowInFileNamesBecomeSpaces(string name, string expected) =>
        Assert.Equal(expected, Assert.Single(LauncherActionList.Entries([UseOffice with { Name = name }], NameFormat)).Name);

    [Fact]
    public void ANameDoesntEndWithASpaceOrADot()
    {
        var entry = Assert.Single(LauncherActionList.Entries([UseOffice with { Name = "Profile", FeatureName = "Network..." }], NameFormat));

        Assert.Equal("Profile - Network", entry.Name);
    }

    [Fact]
    public void ALongNameIsCutTo80Characters()
    {
        var entry = Assert.Single(LauncherActionList.Entries([UseOffice with { Name = "Use " + new string('x', 100) }], NameFormat));

        Assert.Equal(80, entry.Name.Length);
        Assert.StartsWith("Use xxx", entry.Name, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ANameIsNeverCutInsideACharacter()
    {
        var entry = Assert.Single(LauncherActionList.Entries([UseOffice with { Name = new string('x', 79) + "😀😀" }], NameFormat));

        Assert.Equal(new string('x', 79), entry.Name);
    }

    [Fact]
    public void ANameAlreadyTakenGetsANumberIgnoringCase()
    {
        var entries = LauncherActionList.Entries([UseOffice, UseOffice with { Name = "use office" }, UseOffice with { Name = "Use: Office" }, UseOffice], NameFormat);

        Assert.Equal(["Use Office - Network Profiles", "use office - Network Profiles (2)", "Use  Office - Network Profiles", "Use Office - Network Profiles (3)"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public void ALongNameAlreadyTakenStaysWithin80CharactersWithItsNumber()
    {
        var longName = UseOffice with { Name = "Use " + new string('x', 100) };

        var entries = LauncherActionList.Entries([longName, longName], NameFormat);

        Assert.Equal(80, entries[1].Name.Length);
        Assert.EndsWith(" (2)", entries[1].Name, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ARightToLeftNameKeepsItsCharacters()
    {
        var entry = Assert.Single(LauncherActionList.Entries([new LauncherAction(new FeatureAction("networkProfiles", "use", "مكتب"), "استخدام مكتب", "ملفات تعريف الشبكة")], NameFormat));

        Assert.Equal("استخدام مكتب - ملفات تعريف الشبكة", entry.Name);
    }

    [Fact]
    public void TheCommandRunsTheProgramWithTheActionsArguments()
    {
        Assert.Equal(
            "\"C:\\Program Files\\PC Companion\\PCCompanion.exe\" --run networkProfiles use \"Office 2\"",
            LauncherActionList.Command(@"C:\Program Files\PC Companion\PCCompanion.exe", new FeatureAction("networkProfiles", "use", "Office 2")));
    }

    [Theory]
    [InlineData("networkprofiles", "USE")]
    [InlineData("NetworkProfiles", "use")]
    [InlineData("networkProfiles", "use")]
    public void ACommandsIdsIgnoreCaseAndKeepItsTarget(string featureId, string actionId)
    {
        var asked = new FeatureAction(featureId, actionId, "OFFICE ");

        Assert.Equal(new FeatureAction("networkProfiles", "use", "OFFICE "), LauncherActionList.WithIdsOf([ShowAll, UseOffice], asked));
    }

    [Fact]
    public void ACommandWithIdsNoActionHasStaysAsAsked()
    {
        var asked = new FeatureAction("networkprofile", "use", "Office");

        Assert.Same(asked, LauncherActionList.WithIdsOf([UseOffice], asked));
    }
}
