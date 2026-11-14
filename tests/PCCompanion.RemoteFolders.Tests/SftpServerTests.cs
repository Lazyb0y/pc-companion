using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

/// <summary>
/// The parts of the SFTP connection that need no server; connecting to one is checked in Windows
/// Sandbox, with OpenSSH installed there.
/// </summary>
public sealed class SftpServerTests
{
    private static ConnectionInfo NewConnection() => new("example.invalid", 22, "ana", new NoneAuthenticationMethod("ana"));

    [Fact]
    public void OnlyAllowedAlgorithmsAreLeftInTheirOrder()
    {
        var connection = NewConnection();

        SshAlgorithms.Apply(connection, trustedKeyType: null);

        Assert.Equal(SshAlgorithms.KeyExchange.Where(connection.KeyExchangeAlgorithms.ContainsKey), connection.KeyExchangeAlgorithms.Keys);
        Assert.Equal(SshAlgorithms.HostKeys.Where(connection.HostKeyAlgorithms.ContainsKey), connection.HostKeyAlgorithms.Keys);
        Assert.Equal(SshAlgorithms.Ciphers.Where(connection.Encryptions.ContainsKey), connection.Encryptions.Keys);
        Assert.Equal(SshAlgorithms.Macs.Where(connection.HmacAlgorithms.ContainsKey), connection.HmacAlgorithms.Keys);
        Assert.Equal(["none"], connection.CompressionAlgorithms.Keys);
    }

    [Fact]
    public void SshNetOffersTheMostPreferredAlgorithms()
    {
        var connection = NewConnection();

        SshAlgorithms.Apply(connection, trustedKeyType: null);

        Assert.Equal("mlkem768x25519-sha256", connection.KeyExchangeAlgorithms.Keys.First());
        Assert.Equal("ssh-ed25519", connection.HostKeyAlgorithms.Keys.First());
        Assert.Equal("chacha20-poly1305@openssh.com", connection.Encryptions.Keys.First());
        Assert.Contains("aes256-gcm@openssh.com", connection.Encryptions.Keys);
    }

    [Theory]
    [InlineData("diffie-hellman-group14-sha1")]
    [InlineData("diffie-hellman-group1-sha1")]
    [InlineData("ssh-rsa")]
    [InlineData("ssh-dss")]
    [InlineData("aes128-cbc")]
    [InlineData("3des-cbc")]
    [InlineData("hmac-sha1")]
    [InlineData("hmac-md5")]
    [InlineData("zlib@openssh.com")]
    public void WeakAlgorithmsAreRefused(string name)
    {
        var connection = NewConnection();

        SshAlgorithms.Apply(connection, trustedKeyType: null);

        Assert.DoesNotContain(name, connection.KeyExchangeAlgorithms.Keys.Concat(connection.HostKeyAlgorithms.Keys).Concat(connection.Encryptions.Keys).Concat(connection.HmacAlgorithms.Keys).Concat(connection.CompressionAlgorithms.Keys));
    }

    [Theory]
    [InlineData("ssh-ed25519", new[] { "ssh-ed25519" })]
    [InlineData("ecdsa-sha2-nistp256", new[] { "ecdsa-sha2-nistp256" })]
    [InlineData("rsa-sha2-512", new[] { "rsa-sha2-512", "rsa-sha2-256" })]
    [InlineData("rsa-sha2-256", new[] { "rsa-sha2-512", "rsa-sha2-256" })]
    [InlineData("ssh-dss", new string[0])]
    public void AConfirmedKeyLimitsTheKeysAskedFor(string trusted, string[] expected)
    {
        var connection = NewConnection();

        SshAlgorithms.Apply(connection, trusted);

        Assert.Equal(expected, connection.HostKeyAlgorithms.Keys);
    }

    [Fact]
    public void ErrorsAreSortedByWhatTheUserCanDo()
    {
        Assert.Equal(RemoteError.SignInFailed, SftpServer.Map(new SshAuthenticationException("Permission denied (password).")).Error);
        Assert.Equal(RemoteError.KeyFile, SftpServer.Map(new SshPassPhraseNullOrEmptyException("passphrase")).Error);
        Assert.Equal(RemoteError.Unreachable, SftpServer.Map(new SocketException((int)SocketError.ConnectionRefused)).Error);
        Assert.Equal(RemoteError.Unreachable, SftpServer.Map(new SshConnectionException("Client not connected.")).Error);
        Assert.Equal(RemoteError.Unreachable, SftpServer.Map(new SshOperationTimeoutException("timeout")).Error);
        Assert.Equal(RemoteError.NoSafeAlgorithm, SftpServer.Map(new SshConnectionException("No matching key exchange algorithm.", DisconnectReason.KeyExchangeFailed)).Error);
        Assert.Equal(RemoteError.NotFound, SftpServer.Map(new SftpPathNotFoundException("No such file.")).Error);
        Assert.Equal(RemoteError.PermissionDenied, SftpServer.Map(new SftpPermissionDeniedException("Permission denied.")).Error);
        Assert.Equal(RemoteError.Failed, SftpServer.Map(new SshException("Failure")).Error);
        Assert.Equal(RemoteError.KeyFile, SftpServer.Map(new FileNotFoundException("id_ed25519")).Error);

        var known = new RemoteServerException(RemoteError.KeyNotTrusted, "changed");
        Assert.Same(known, SftpServer.Map(known));
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(0, false, false)]
    public void OnlyASingleHiddenQuestionGetsThePassword(int questions, bool isEchoed, bool expected)
    {
        Assert.Equal(expected, PasswordPrompt.IsOnlyThePassword(questions, isEchoed));
    }

    [Fact]
    public void ASignInDoesntPrintItsSecrets()
    {
        var signIn = new SftpSignIn("nas.example.lan", 22, "ana", "secret-password", @"C:\Users\Ana\.ssh\id_ed25519", "secret-passphrase");

        string text = signIn.ToString();

        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ana", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nas.example.lan", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingKeyFileIsAKeyFileError()
    {
        var signIn = new SftpSignIn("example.invalid", 22, "ana", null, @"C:\nothing-here\id_ed25519", null);

        var exception = await Assert.ThrowsAsync<RemoteServerException>(() => SftpServer.ConnectAsync(signIn, new ServerKey("ssh-ed25519", "x"), TestContext.Current.CancellationToken));

        Assert.Equal(RemoteError.KeyFile, exception.Error);
    }
}
