using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// Registering a sync root adds a folder to File Explorer, and listing them makes a hidden window
/// that other tests count, so both are checked in Windows Sandbox only; these tests build IDs.
/// </summary>
public sealed class SyncRootsTests
{
    private const string Sid = "S-1-5-21-2047949552-857980807-821054962-1001";

    [Fact]
    public void AnIdJoinsItsPartsWithExclamationMarks()
    {
        Assert.Equal($"PCCompanion!{Sid}!7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e", SyncRoots.MakeId("PCCompanion", Sid, "7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e"));
    }

    [Theory]
    [InlineData("", Sid, "a")]
    [InlineData("PCCompanion", "", "a")]
    [InlineData("PCCompanion", Sid, "")]
    [InlineData("PC!Companion", Sid, "a")]
    [InlineData("PCCompanion", Sid, "a!b")]
    public void PartsMustBeNonEmptyWithoutExclamationMarks(string provider, string sid, string account)
    {
        Assert.Throws<ArgumentException>(() => SyncRoots.MakeId(provider, sid, account));
    }

    [Fact]
    public void AnIdIsAtMost174Characters()
    {
        string longest = new('a', SyncRoots.MaximumIdLength - "PCCompanion!".Length - Sid.Length - 1);

        Assert.Equal(SyncRoots.MaximumIdLength, SyncRoots.MakeId("PCCompanion", Sid, longest).Length);
        Assert.Throws<ArgumentException>(() => SyncRoots.MakeId("PCCompanion", Sid, longest + "a"));
    }
}
