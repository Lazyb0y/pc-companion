using System.Text;
using PCCompanion.Modules.Files.RemoteFolders;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class RemoteFoldersPipeTests
{
    [Fact]
    public void ARequestReadsBackAsWritten()
    {
        var request = new RemoteFoldersRequest
        {
            Kind = RemoteFoldersRequestKind.TestServer,
            Server = new RemoteServer { Host = "nas.example.lan", UserName = "ana", SignIn = "Key", KeyFile = @"C:\Users\Ana\.ssh\id_ed25519" },
            Secret = "passphrase",
            Key = new RemoteHostKey { Type = "ssh-ed25519", Sha256 = "cw03EqRJF5BI2sgaxpgwrVhqZvn675XZNMGjF9bIUoI" },
        };

        var read = RemoteFoldersPipe.ReadRequest(RemoteFoldersPipe.Write(request))!;

        Assert.Equal(request.Kind, read.Kind);
        Assert.Equal(request.Server, read.Server);
        Assert.Equal("passphrase", read.Secret);
        Assert.Equal(request.Key, read.Key);
    }

    [Fact]
    public void AnAnswerNamesItsStatesInWords()
    {
        var answer = new RemoteFoldersAnswer
        {
            Servers = [new RemoteServerStatus { Id = "1", State = RemoteServerState.SignInFailed, Hidden = new System.Collections.Generic.Dictionary<HiddenReason, int> { [HiddenReason.InvalidName] = 2 } }],
            Done = true,
        };

        byte[] json = RemoteFoldersPipe.Write(answer);
        var read = RemoteFoldersPipe.ReadAnswer(json)!;

        Assert.Contains("\"state\":\"SignInFailed\"", Encoding.UTF8.GetString(json), System.StringComparison.Ordinal);
        Assert.Equal(RemoteServerState.SignInFailed, read.Servers[0].State);
        Assert.Equal(2, read.Servers[0].Hidden[HiddenReason.InvalidName]);
        Assert.True(read.Done);
    }

    [Fact]
    public void AMessageThatIsntJsonIsNoRequest()
    {
        Assert.Null(RemoteFoldersPipe.ReadRequest(Encoding.UTF8.GetBytes("hello")));
        Assert.Null(RemoteFoldersPipe.ReadAnswer(Encoding.UTF8.GetBytes("[1, 2")));
    }
}
