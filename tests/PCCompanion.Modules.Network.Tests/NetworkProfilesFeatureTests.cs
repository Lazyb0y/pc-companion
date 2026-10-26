using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Network.Tests;

public sealed class NetworkProfilesFeatureTests : IDisposable
{
    private const string Office = """{ "name": "Office", "proxy": { "useServer": true, "server": "proxy.example.com:8080", "exceptions": ["*.example.com"], "bypassLocal": true } }""";
    private const string Home = """{ "name": "Home", "proxy": { "autoDetect": true } }""";
    private const string ChangedOffice = """{ "name": "Office", "proxy": { "useServer": true, "server": "other.example.com:3128" } }""";
    private static readonly string Settings = $"{NetworkProfilesMenu.SettingsItem} {Strings.NetworkProfilesMenuSettings}";

    private readonly FakeNetworkProfilesSystem _system = new();
    private readonly FakeHostCommands _commands = new();
    private readonly RecordingLogger<NetworkProfilesFeature> _logger = new();
    private readonly NetworkProfilesFeature _feature;

    public NetworkProfilesFeatureTests()
    {
        _feature = new NetworkProfilesFeature(_system, _commands, _logger);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsOffWithoutASection()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.False(_system.IsWatching);
        Assert.Empty(_feature.GetMenuItems());
    }

    [Fact]
    public void TurnedOnItWatchesAndReadsTheSettingsAfterAShortWait()
    {
        _feature.Apply(On(Office, Home));

        Assert.True(_feature.IsRunning);
        Assert.True(_system.IsWatching);
        Assert.Equal([TimeSpan.FromMilliseconds(50)], _system.Delays);

        _system.RunBackground();

        Assert.Equal(["1 Office", "2 Home (checked)", "-", Settings], Menu());
        Assert.Contains((LogLevel.Debug, "Windows' proxy settings match network profile 2."), _logger.Entries);
    }

    [Fact]
    public void TurnedOffItStopsWatchingAndAddsNoMenu()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        _feature.Apply(Section("""{ "enabled": false, "profiles": [] }"""));

        Assert.False(_feature.IsRunning);
        Assert.False(_system.IsWatching);
        Assert.Equal(1, _system.InvokersDisposed);
        Assert.Empty(_feature.GetMenuItems());
    }

    [Fact]
    public void ChoosingAProfileSwitchesWindowsToIt()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        Choose(1);
        _system.RunBackground();

        Assert.Equal([new ProxySettings(false, false, "", true, "proxy.example.com:8080", "*.example.com;<local>")], _system.Written);
        Assert.Equal(["1 Office (checked)", "2 Home", "-", Settings], Menu());
        Assert.Contains((LogLevel.Debug, "Switched to network profile 1."), _logger.Entries);
    }

    [Fact]
    public void AProfileMovedWhileTheMenuIsOpenIsTheOneSwitchedTo()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();
        _feature.GetMenuItems();

        _feature.Apply(On(Home, Office));
        _feature.ChooseMenuItem(1);
        _system.RunBackground();

        Assert.Equal([new ProxySettings(false, false, "", true, "proxy.example.com:8080", "*.example.com;<local>")], _system.Written);
        Assert.Contains((LogLevel.Debug, "Switched to network profile 2."), _logger.Entries);
    }

    [Theory]
    [InlineData(ChangedOffice + "," + Home)]
    [InlineData(Home)]
    public void AProfileChangedOrDeletedWhileTheMenuIsOpenIsntApplied(string profiles)
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();
        _feature.GetMenuItems();

        _feature.Apply(On(profiles));
        _feature.ChooseMenuItem(1);
        _system.RunBackground();

        Assert.Empty(_system.Written);
        Assert.Contains((LogLevel.Debug, "Network profile 1 was chosen, but there's no such profile any more."), _logger.Entries);
    }

    [Fact]
    public void AChangeMadeElsewhereMovesTheMark()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        _system.ChangeElsewhere(new ProxySettings(false, false, "", true, "other.example.com:3128", ""));
        _system.RunBackground();

        Assert.Equal([$"0 {Strings.NetworkProfilesMenuOther} (dimmed)", "1 Office", "2 Home", "-", Settings], Menu());
        Assert.Contains((LogLevel.Debug, "Windows' proxy settings match none of the network profiles."), _logger.Entries);
    }

    [Fact]
    public void SignalsWhileAReadWaitsOrRunsLeadToOneMoreRead()
    {
        _feature.Apply(On(Office, Home));
        _system.Signal();
        _system.Signal();
        Assert.Equal(1, _system.PendingBackground);

        var signaled = false;
        _system.DuringRead = () =>
        {
            if (!signaled)
            {
                signaled = true;
                _system.Signal();
                _system.Signal();
            }
        };
        _system.RunBackground();

        Assert.Equal(2, _system.Reads);
    }

    [Fact]
    public void ManagedSettingsDimTheProfilesAndBlockSwitching()
    {
        _system.Restrictions = ProxyRestrictions.PreventChanges;
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        Choose(1);

        Assert.Equal([$"0 {Strings.NetworkProfilesMenuManaged} (dimmed)", "1 Office (dimmed)", "2 Home (checked) (dimmed)", "-", Settings], Menu());
        Assert.Equal(0, _system.PendingBackground);
        Assert.Contains((LogLevel.Warning, "Network profile 1 wasn't applied: Group Policy manages the proxy settings."), _logger.Entries);
    }

    [Fact]
    public void APolicyThatAppearsBeforeTheSwitchBlocksIt()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        Choose(1);
        _system.Restrictions = ProxyRestrictions.PerMachine;
        _system.RunBackground();

        Assert.Empty(_system.Written);
        Assert.Equal($"0 {Strings.NetworkProfilesMenuManaged} (dimmed)", Menu().First());
    }

    [Fact]
    public void ARefusedSwitchIsLoggedAndTheMarkStays()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();
        _system.WriteError = 5;

        Choose(1);
        _system.RunBackground();

        Assert.Contains((LogLevel.Warning, "Windows refused network profile 1 (error 5)."), _logger.Entries);
        Assert.Equal(["1 Office", "2 Home (checked)", "-", Settings], Menu());
    }

    [Fact]
    public void ASwitchWindowsReportsButDoesntMakeIsLogged()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();
        _system.IgnoresWrites = true;

        Choose(1);
        _system.RunBackground();

        Assert.Contains((LogLevel.Warning, "Windows reported switching to network profile 1, but its proxy settings don't match the profile."), _logger.Entries);
        Assert.Equal(["1 Office", "2 Home (checked)", "-", Settings], Menu());
    }

    [Fact]
    public void ASwitchChosenBeforeTurningOffChangesNothing()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        Choose(1);
        _feature.Apply(Section("""{ "enabled": false }"""));
        _system.RunBackground();

        Assert.Empty(_system.Written);
    }

    [Fact]
    public void AReadThatEndsAfterTurningOffIsDropped()
    {
        _feature.Apply(On(Office, Home));
        _system.DuringRead = () => _feature.Apply(Section("""{ "enabled": false }"""));

        _system.RunBackground();

        Assert.False(_feature.IsRunning);
        Assert.Empty(_feature.GetMenuItems());
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.StartsWith("Windows' proxy settings match", StringComparison.Ordinal));
    }

    [Fact]
    public void NetworkProfilesSettingsOpensTheFeaturesPage()
    {
        _feature.Apply(On(Office));

        Choose(NetworkProfilesMenu.SettingsItem);

        Assert.Equal(["networkProfiles"], _commands.FeaturePagesOpened);
    }

    [Fact]
    public void ProfilesThatCantBeUsedAreLeftOutWithAWarning()
    {
        _feature.Apply(On(Office, """{ "name": "Bad", "proxy": { "useServer": true, "server": "not a server" } }""", """{ "name": "office", "proxy": {} }"""));
        _system.RunBackground();

        Assert.Equal([$"0 {Strings.NetworkProfilesMenuOther} (dimmed)", "1 Office", "-", Settings], Menu());
        Assert.Contains((LogLevel.Warning, "Network profile 2 can't be used (InvalidServer), so it's left out of the menu."), _logger.Entries);
        Assert.Contains((LogLevel.Warning, "Network profile 3 can't be used (NameInUse), so it's left out of the menu."), _logger.Entries);
    }

    [Fact]
    public void OnlyTheFirst20ProfilesAreUsed()
    {
        _feature.Apply(On([.. Enumerable.Range(1, 21).Select(number => $$"""{ "name": "P{{number}}", "proxy": {} }""")]));

        Assert.Equal(20, Menu().Count(item => Enumerable.Range(1, 21).Any(number => item == $"{number} P{number}")));
        Assert.Contains((LogLevel.Warning, "There are 21 network profiles; only the first 20 are used."), _logger.Entries);
    }

    [Fact]
    public void AReadWindowsRefusesIsLoggedAndTheFeatureKeepsRunning()
    {
        _system.ReadError = 5;

        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        Assert.True(_feature.IsRunning);
        Assert.Equal(["1 Office", "2 Home", "-", Settings], Menu());
        Assert.Contains((LogLevel.Warning, "Windows refused to read the proxy settings (error 5)."), _logger.Entries);
    }

    [Fact]
    public void PoliciesWindowsRefusesToReadStayAsTheyWereLastRead()
    {
        _system.Restrictions = ProxyRestrictions.PreventChanges;
        _feature.Apply(On(Office, Home));
        _system.RunBackground();

        _system.Restrictions = ProxyRestrictions.None;
        _system.Failure = new UnauthorizedAccessException();
        _system.Signal();
        _system.RunBackground();

        Assert.True(_feature.IsRunning);
        Assert.Equal($"0 {Strings.NetworkProfilesMenuManaged} (dimmed)", Menu().First());

        // E_ACCESSDENIED, which UnauthorizedAccessException carries.
        Assert.Contains((LogLevel.Warning, "Windows refused to read the proxy policies (error -2147024891)."), _logger.Entries);
    }

    [Fact]
    public void PoliciesWindowsRefusesToReadBlockTheSwitch()
    {
        _feature.Apply(On(Office, Home));
        _system.RunBackground();
        _system.Failure = new UnauthorizedAccessException();

        Choose(1);
        _system.RunBackground();

        Assert.True(_feature.IsRunning);
        Assert.Empty(_system.Written);
        Assert.Contains((LogLevel.Warning, "Windows refused network profile 1 (error -2147024891)."), _logger.Entries);
    }

    [Fact]
    public void AFailureTurnsTheFeatureOffUntilTheHostRestarts()
    {
        _system.Failure = new InvalidOperationException("Can't read.");
        _feature.Apply(On(Office, Home));

        _system.RunBackground();
        _feature.Apply(On(Office, Home));

        Assert.False(_feature.IsRunning);
        Assert.False(_system.IsWatching);
        Assert.Contains((LogLevel.Error, "Network Profiles failed, so it's off until PC Companion restarts."), _logger.Entries);
    }

    [Fact]
    public void ASectionThatCantBeReadTurnsTheFeatureOff()
    {
        _feature.Apply(On(Office));

        _feature.Apply(Section("""{ "enabled": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The Network Profiles settings can't be read, so Network Profiles is off."), _logger.Entries);
    }

    [Fact]
    public void TheDefinitionNamesTheFeatureAndItsModule()
    {
        Assert.Equal("networkProfiles", NetworkProfilesFeature.Definition.Id);
        Assert.Equal(NetworkModule.Id, NetworkProfilesFeature.Definition.ModuleId);
    }

    private static JsonElement On(params string[] profiles) => Section($$"""{ "enabled": true, "profiles": [{{string.Join(",", profiles)}}] }""");

    // As the host does: an item of the menu that opened last is chosen.
    private void Choose(int id)
    {
        _feature.GetMenuItems();
        _feature.ChooseMenuItem(id);
    }

    private List<string> Menu() => [.. NetworkProfilesMenuTests.Describe(_feature.GetMenuItems())];
}
