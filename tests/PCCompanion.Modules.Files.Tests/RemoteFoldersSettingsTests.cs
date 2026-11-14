using System.Linq;
using System.Text.Json;
using PCCompanion.Modules.Files.RemoteFolders;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Files.Tests;

public sealed class RemoteFoldersSettingsTests
{
    // The spec's example.
    private const string Example = """
        {
          "enabled": true,
          "servers": [
            {
              "id": "7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e",
              "name": "Home server",
              "protocol": "Sftp",
              "host": "nas.example.lan",
              "port": 22,
              "userName": "ana",
              "signIn": "Key",
              "keyFile": "C:\\Users\\Ana\\.ssh\\id_ed25519",
              "remoteFolder": "/home/ana",
              "folder": "C:\\Users\\Ana\\Remote Folders\\Home server",
              "hostKey": { "type": "ssh-ed25519", "sha256": "cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI" }
            }
          ]
        }
        """;

    private static RemoteServer Usable(string id = "7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e", string folder = @"C:\Users\Ana\Remote Folders\Home server") => new()
    {
        Id = id,
        Name = "Home server",
        Host = "nas.example.lan",
        UserName = "ana",
        RemoteFolder = "/home/ana",
        Folder = folder,
        HostKey = new RemoteHostKey { Type = "ssh-ed25519", Sha256 = "cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI" },
    };

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoServers()
    {
        var settings = RemoteFoldersSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Servers);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = RemoteFoldersSettings.Read(Section(Example));

        Assert.True(settings.Enabled);
        var server = Assert.Single(settings.Servers);
        Assert.Equal("7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e", server.Id);
        Assert.Equal("Home server", server.Name);
        Assert.Equal(RemoteProtocol.Sftp, server.KnownProtocol);
        Assert.Equal("nas.example.lan", server.Host);
        Assert.Equal(22, server.Port);
        Assert.Equal("ana", server.UserName);
        Assert.Equal(RemoteSignIn.Key, server.KnownSignIn);
        Assert.Equal(@"C:\Users\Ana\.ssh\id_ed25519", server.KeyFile);
        Assert.Equal("/home/ana", server.RemoteFolder);
        Assert.Equal(@"C:\Users\Ana\Remote Folders\Home server", server.Folder);
        Assert.Equal("ssh-ed25519", server.HostKey?.Type);
        Assert.Equal("cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI", server.HostKey?.Sha256);
        Assert.True(server.IsUsable);
        Assert.Same(server, Assert.Single(settings.UsableServers));
    }

    [Fact]
    public void ANewServerUsesPort22AndAPassword()
    {
        var server = new RemoteServer();

        Assert.Equal(RemoteServer.DefaultPort, server.Port);
        Assert.Equal(RemoteSignIn.Password, server.KnownSignIn);
        Assert.Equal(RemoteProtocol.Sftp, server.KnownProtocol);
        Assert.False(server.IsUsable);
    }

    [Fact]
    public void NewIdsAreValidAndDiffer()
    {
        string first = RemoteServer.NewId();

        Assert.True(RemoteServer.IsValidId(first));
        Assert.NotEqual(first, RemoteServer.NewId());
        Assert.False(RemoteServer.IsValidId("7d2c4b1e"));
        Assert.False(RemoteServer.IsValidId("zz2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e"));
        Assert.False(RemoteServer.IsValidId(@"..\..\7d2c4b1e9f0a4c3b8e5d6a7f1b2c"));
    }

    [Fact]
    public void AServerMissingWhatAConnectionNeedsIsntUsable()
    {
        Assert.True(Usable().IsUsable);
        Assert.False((Usable() with { Id = "not-an-id" }).IsUsable);
        Assert.False((Usable() with { Name = "" }).IsUsable);
        Assert.False((Usable() with { Name = new string('n', RemoteFoldersSettings.MaximumNameLength + 1) }).IsUsable);
        Assert.False((Usable() with { Host = "" }).IsUsable);
        Assert.False((Usable() with { Port = 0 }).IsUsable);
        Assert.False((Usable() with { Port = 65_536 }).IsUsable);
        Assert.False((Usable() with { UserName = "" }).IsUsable);
        Assert.False((Usable() with { RemoteFolder = "" }).IsUsable);
        Assert.False((Usable() with { Folder = "" }).IsUsable);
        Assert.False((Usable() with { Folder = @"Remote Folders\Home server" }).IsUsable);
        Assert.False((Usable() with { HostKey = null }).IsUsable);
        Assert.False((Usable() with { HostKey = new RemoteHostKey { Type = "ssh-ed25519" } }).IsUsable);
    }

    [Fact]
    public void AKeySignInNeedsAKeyFile()
    {
        Assert.False((Usable() with { SignIn = "Key" }).IsUsable);
        Assert.False((Usable() with { SignIn = "Key", KeyFile = "id_ed25519" }).IsUsable);
        Assert.True((Usable() with { SignIn = "Key", KeyFile = @"C:\Users\Ana\.ssh\id_ed25519" }).IsUsable);
    }

    [Fact]
    public void UnknownValuesAreKeptAndTheServerIsntUsed()
    {
        var settings = RemoteFoldersSettings.Read(Section(Example.Replace("\"Sftp\"", "\"WebDav\"").Replace("\"Key\"", "\"Agent\"")));

        var server = Assert.Single(settings.Servers);
        Assert.Null(server.KnownProtocol);
        Assert.Null(server.KnownSignIn);
        Assert.False(server.IsUsable);
        Assert.Empty(settings.UsableServers);

        var written = settings.Write();
        var first = written.GetProperty("servers")[0];
        Assert.Equal("WebDav", first.GetProperty("protocol").GetString());
        Assert.Equal("Agent", first.GetProperty("signIn").GetString());
    }

    [Fact]
    public void ValuesAreReadIgnoringCase()
    {
        var server = Usable() with { Protocol = "sftp", SignIn = "password" };

        Assert.Equal(RemoteProtocol.Sftp, server.KnownProtocol);
        Assert.Equal(RemoteSignIn.Password, server.KnownSignIn);
        Assert.True(server.IsUsable);
    }

    [Fact]
    public void ASecondServerWithTheSameIdOrFolderIsntUsed()
    {
        var first = Usable();
        var sameId = Usable(folder: @"C:\Users\Ana\Remote Folders\Other");
        var sameFolder = Usable(id: "00000000000000000000000000000001", folder: @"c:\users\ana\remote folders\home server\");
        var other = Usable(id: "00000000000000000000000000000002", folder: @"C:\Users\Ana\Remote Folders\Work");
        var settings = new RemoteFoldersSettings { Enabled = true, Servers = [first, sameId, sameFolder, other] };

        Assert.Equal([first, other], settings.UsableServers);
    }

    [Fact]
    public void NullsAreLeftOutAndTheListIsLimited()
    {
        string servers = string.Join(",", Enumerable.Range(0, RemoteFoldersSettings.MaximumServers + 3).Select(_ => "{ \"name\": \"n\" }"));
        var settings = RemoteFoldersSettings.Read(Section($$"""{ "enabled": true, "servers": [null, {{servers}}] }"""));

        Assert.Equal(RemoteFoldersSettings.MaximumServers, settings.Servers.Count);
        Assert.All(settings.Servers, server => Assert.Equal("n", server.Name));
    }

    [Fact]
    public void WrittenSettingsAreReadBackEqual()
    {
        var settings = RemoteFoldersSettings.Read(Section(Example));

        var again = RemoteFoldersSettings.Read(settings.Write());

        Assert.Equal(settings, again);
        Assert.Equal(settings.GetHashCode(), again.GetHashCode());
        Assert.NotEqual(settings, again.WithEnabled(false));
    }

    [Fact]
    public void ASectionThatDoesntMatchTheFormatThrows()
    {
        Assert.Throws<JsonException>(() => RemoteFoldersSettings.Read(Section("""{ "enabled": true, "servers": [{ "port": "twenty-two" }] }""")));
    }
}
