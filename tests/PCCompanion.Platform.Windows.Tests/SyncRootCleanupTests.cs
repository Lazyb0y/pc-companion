using System;
using Microsoft.Win32;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>
/// Reads and edits registry keys of the test's own, under <c>HKEY_CURRENT_USER\Software\PCCompanion.Tests</c>,
/// laid out as Windows lays out its sync roots and a user's navigation-pane entries. Removing every
/// user's sync roots runs as SYSTEM, so that is checked in Windows Sandbox only.
/// </summary>
public sealed class SyncRootCleanupTests : IDisposable
{
    private const string Ours = "{11111111-2222-3333-4444-555555555555}";
    private const string Theirs = "{66666666-7777-8888-9999-AAAAAAAAAAAA}";
    private const string Ana = "S-1-5-21-1-2-3-1001";
    private const string Ben = "S-1-5-21-1-2-3-1002";
    private readonly string _path = @"Software\PCCompanion.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;

    public SyncRootCleanupTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    private void SyncRoot(string id, string? clsid, params (string Sid, string Folder)[] users)
    {
        using var root = _root.CreateSubKey(@"SyncRootManager\" + id);
        if (clsid is not null)
        {
            root.SetValue("NamespaceCLSID", clsid);
        }

        using var userRoots = root.CreateSubKey("UserSyncRoots");
        foreach (var (sid, folder) in users)
        {
            userRoots.SetValue(sid, folder);
        }
    }

    [Fact]
    public void TheProvidersSyncRootsAreReadWithEachUsersFolder()
    {
        SyncRoot("PCCompanion!" + Ana + "!1", Ours, (Ana, @"C:\Users\Ana\Remote Folders\Home"));
        SyncRoot("PCCompanion!" + Ben + "!2", "not a clsid", (Ben, @"C:\Users\Ben\Remote Folders\Work"), ("S-1-5-18", @"C:\Windows"));
        SyncRoot("OneDrive!" + Ana + "!Personal", Theirs, (Ana, @"C:\Users\Ana\OneDrive"));

        using var manager = _root.OpenSubKey("SyncRootManager")!;
        var roots = SyncRootCleanup.Read(manager, "PCCompanion!");

        Assert.Equal(2, roots.Count);
        var home = Assert.Single(roots, root => root.Id.EndsWith("!1", StringComparison.Ordinal));
        Assert.Equal(Ours, home.NamespaceClsid);
        Assert.Equal([(Ana, @"C:\Users\Ana\Remote Folders\Home")], home.Users);
        var work = Assert.Single(roots, root => root.Id.EndsWith("!2", StringComparison.Ordinal));
        Assert.Null(work.NamespaceClsid);
        Assert.Equal([(Ben, @"C:\Users\Ben\Remote Folders\Work")], work.Users);
    }

    [Fact]
    public void OnlyTheEntryOfTheGivenClsidLeavesTheUsersRegistry()
    {
        using var user = _root.CreateSubKey("User");
        using var classes = _root.CreateSubKey("Classes");
        foreach (string clsid in new[] { Ours, Theirs })
        {
            user.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\" + clsid).Dispose();
            using var hidden = user.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
            hidden.SetValue(clsid, 1);
            classes.CreateSubKey(@"CLSID\" + clsid + @"\Instance\InitPropertyBag").Dispose();
        }

        SyncRootCleanup.RemoveUserEntry(user, classes, Ours);

        using var nameSpace = user.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace")!;
        Assert.Equal([Theirs], nameSpace.GetSubKeyNames());
        using var hiddenAfter = user.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel")!;
        Assert.Equal([Theirs], hiddenAfter.GetValueNames());
        using var clsids = classes.OpenSubKey("CLSID")!;
        Assert.Equal([Theirs], clsids.GetSubKeyNames());
    }

    [Fact]
    public void AUserWithoutTheEntryIsLeftAlone()
    {
        using var user = _root.CreateSubKey("User");

        SyncRootCleanup.RemoveUserEntry(user, null, Ours);

        Assert.Empty(user.GetSubKeyNames());
    }

    [Fact]
    public void SomethingThatIsntAClsidIsRefused()
    {
        using var user = _root.CreateSubKey("User");

        Assert.Throws<ArgumentException>(() => SyncRootCleanup.RemoveUserEntry(user, null, @"..\Something"));
    }
}
