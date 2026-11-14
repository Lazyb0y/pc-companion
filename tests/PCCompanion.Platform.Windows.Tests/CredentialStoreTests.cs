using System;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// Uses only credentials of its own, under a prefix with a new GUID, and deletes them.
/// </summary>
public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _prefix = $"PCCompanion.Tests/{Guid.NewGuid():N}/";

    public void Dispose()
    {
        foreach (string target in CredentialStore.Targets(_prefix))
        {
            CredentialStore.Delete(target);
        }
    }

    [Fact]
    public void AStoredSecretIsReadBack()
    {
        CredentialStore.Write(_prefix + "a", "ana", "correct horse battery staple", "Home server");

        Assert.Equal("correct horse battery staple", CredentialStore.Read(_prefix + "a"));
    }

    [Fact]
    public void WritingAgainReplacesTheSecret()
    {
        CredentialStore.Write(_prefix + "a", "ana", "first", "Home server");
        CredentialStore.Write(_prefix + "a", "ana", "second", "Home server");

        Assert.Equal("second", CredentialStore.Read(_prefix + "a"));
    }

    [Fact]
    public void SecretsKeepEveryCharacter()
    {
        const string Secret = "pässwörd 密码 كلمة 🔑 \t\"'\\";
        CredentialStore.Write(_prefix + "a", "ana", Secret, "");

        Assert.Equal(Secret, CredentialStore.Read(_prefix + "a"));
    }

    [Fact]
    public void AnEmptySecretIsKept()
    {
        CredentialStore.Write(_prefix + "a", "ana", "", "");

        Assert.Equal("", CredentialStore.Read(_prefix + "a"));
    }

    [Fact]
    public void AMissingCredentialReadsAsNullAndDeletesAsFalse()
    {
        Assert.Null(CredentialStore.Read(_prefix + "missing"));
        Assert.False(CredentialStore.Delete(_prefix + "missing"));
    }

    [Fact]
    public void DeletingRemovesIt()
    {
        CredentialStore.Write(_prefix + "a", "ana", "secret", "");

        Assert.True(CredentialStore.Delete(_prefix + "a"));
        Assert.Null(CredentialStore.Read(_prefix + "a"));
    }

    [Fact]
    public void TargetsListsOnlyThoseWithThePrefix()
    {
        CredentialStore.Write(_prefix + "a", "ana", "1", "");
        CredentialStore.Write(_prefix + "b", "ana", "2", "");

        Assert.Equal([_prefix + "a", _prefix + "b"], [.. CredentialStore.Targets(_prefix).Order(StringComparer.Ordinal)]);
        Assert.Empty(CredentialStore.Targets(_prefix + "none/"));
    }

    [Fact]
    public void TooLongASecretOrCommentIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialStore.Write(_prefix + "a", "ana", new string('s', CredentialStore.MaximumSecretLength + 1), ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => CredentialStore.Write(_prefix + "a", "ana", "", new string('c', CredentialStore.MaximumCommentLength + 1)));
        CredentialStore.Write(_prefix + "a", "ana", new string('s', CredentialStore.MaximumSecretLength), new string('c', CredentialStore.MaximumCommentLength));
        Assert.Equal(CredentialStore.MaximumSecretLength, CredentialStore.Read(_prefix + "a")?.Length);
    }
}
