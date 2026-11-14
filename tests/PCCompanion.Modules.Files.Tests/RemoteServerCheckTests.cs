using System;
using System.Collections.Generic;
using PCCompanion.Modules.Files.RemoteFolders;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class RemoteServerCheckTests
{
    [Theory]
    [InlineData("Home server", ServerProblem.None)]
    [InlineData("  Home server  ", ServerProblem.None)]
    [InlineData("nas.example.lan", ServerProblem.None)]
    [InlineData("", ServerProblem.NoName)]
    [InlineData("   ", ServerProblem.NoName)]
    [InlineData("a:b", ServerProblem.InvalidName)]
    [InlineData("fe80::1", ServerProblem.InvalidName)]
    [InlineData("CON", ServerProblem.InvalidName)]
    [InlineData("Work.", ServerProblem.InvalidName)]
    [InlineData("WORK", ServerProblem.NameInUse)]
    public void ANameMustSuitAFolderAndDifferFromTheOthers(string name, ServerProblem expected) =>
        Assert.Equal(expected, RemoteServerCheck.CheckName(name, ["Work "]));

    [Fact]
    public void ANameMayHaveSixtyCharacters()
    {
        Assert.Equal(ServerProblem.None, RemoteServerCheck.CheckName(new string('a', 60), []));
        Assert.Equal(ServerProblem.NameTooLong, RemoteServerCheck.CheckName(new string('a', 61), []));
    }

    [Theory]
    [InlineData("nas.example.lan", ServerProblem.None)]
    [InlineData(" nas ", ServerProblem.None)]
    [InlineData("192.168.1.10", ServerProblem.None)]
    [InlineData("fe80::1", ServerProblem.None)]
    [InlineData("", ServerProblem.NoAddress)]
    [InlineData("nas example", ServerProblem.InvalidAddress)]
    [InlineData("sftp://nas", ServerProblem.InvalidAddress)]
    [InlineData("ana@nas", ServerProblem.InvalidAddress)]
    public void AnAddressIsAHostNameOrAnIpAddress(string address, ServerProblem expected) =>
        Assert.Equal(expected, RemoteServerCheck.CheckAddress(address));

    [Theory]
    [InlineData("22", 22)]
    [InlineData(" 2222 ", 2222)]
    [InlineData("65535", 65535)]
    [InlineData("0", null)]
    [InlineData("65536", null)]
    [InlineData("-22", null)]
    [InlineData("+22", null)]
    [InlineData("22.0", null)]
    [InlineData("", null)]
    [InlineData("\u0662\u0662", null)]
    public void APortIsANumberFrom1To65535(string text, int? expected) =>
        Assert.Equal(expected, RemoteServerCheck.ParsePort(text));

    [Fact]
    public void AUserNameIsNeeded()
    {
        Assert.Equal(ServerProblem.None, RemoteServerCheck.CheckUserName("ana"));
        Assert.Equal(ServerProblem.NoUserName, RemoteServerCheck.CheckUserName(" "));
        Assert.Equal(ServerProblem.UserNameTooLong, RemoteServerCheck.CheckUserName(new string('a', 257)));
    }

    [Fact]
    public void AKeyFileIsAFullPathToAFileThatsThere()
    {
        static bool Exists(string path) => path == @"C:\Users\Ana\.ssh\id_ed25519";

        Assert.Equal(ServerProblem.None, RemoteServerCheck.CheckKeyFile(@"C:\Users\Ana\.ssh\id_ed25519", Exists));
        Assert.Equal(ServerProblem.NoKeyFile, RemoteServerCheck.CheckKeyFile("", Exists));
        Assert.Equal(ServerProblem.KeyFileNotFound, RemoteServerCheck.CheckKeyFile(@"C:\Users\Ana\.ssh\id_rsa", Exists));
        Assert.Equal(ServerProblem.KeyFileNotFound, RemoteServerCheck.CheckKeyFile(@".ssh\id_ed25519", _ => true));
    }

    [Fact]
    public void ANewFolderGetsANumberWhenItsNameIsTaken()
    {
        const string Root = @"C:\Users\Ana\Remote Folders";
        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Root + @"\home server" };

        Assert.Equal(Root + @"\Work", RemoteServerCheck.NewFolder(Root, "Work", [], there.Contains));
        Assert.Equal(Root + @"\Home server (2)", RemoteServerCheck.NewFolder(Root, "Home server", [], there.Contains));
        Assert.Equal(Root + @"\Home server (3)", RemoteServerCheck.NewFolder(Root, "Home server", [Root + @"\Home server (2)\"], there.Contains));
        Assert.Equal(Root + @"\nas.example.lan (2)", RemoteServerCheck.NewFolder(Root, "nas.example.lan", [Root + @"\nas.example.lan"], there.Contains));
        Assert.Null(RemoteServerCheck.NewFolder(Root, "Home server", [], _ => true));
    }

    [Fact]
    public void TheFoldersAreInRemoteFoldersInTheProfile() =>
        Assert.Equal(@"C:\Users\Ana\Remote Folders", RemoteServerCheck.RootFolder(@"C:\Users\Ana"));

    [Fact]
    public void TheAddressReadsAsSshWritesIt()
    {
        Assert.Equal("ana@nas.example.lan:22", RemoteServerCheck.Address(new RemoteServer { UserName = "ana", Host = "nas.example.lan" }));
        Assert.Equal("ana@[fe80::1]:2222", RemoteServerCheck.Address(new RemoteServer { UserName = "ana", Host = "fe80::1", Port = 2222 }));
    }

    [Theory]
    [InlineData("ssh-ed25519", "ED25519")]
    [InlineData("ecdsa-sha2-nistp256", "ECDSA")]
    [InlineData("ecdsa-sha2-nistp521", "ECDSA")]
    [InlineData("rsa-sha2-512", "RSA")]
    [InlineData("rsa-sha2-256", "RSA")]
    [InlineData("something-new", "something-new")]
    public void AKeyReadsAsOpenSshShowsIt(string type, string shown) =>
        Assert.Equal(shown + " SHA256:cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI", RemoteServerCheck.KeyText(new RemoteHostKey { Type = type, Sha256 = "cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI" }));
}
