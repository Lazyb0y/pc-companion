using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Registry;

namespace PCCompanion.Platform.Windows;

/// <summary>A sync root as Windows keeps it for every user: its ID, its navigation-pane entry, and each user's folder.</summary>
/// <param name="NamespaceClsid">The CLSID of its navigation-pane entry, as <c>{…}</c>, if any.</param>
public sealed record RegisteredSyncRoot(string Id, string? NamespaceClsid, IReadOnlyList<(string Sid, string Folder)> Users);

/// <summary>
/// Removes a provider's sync roots for every user of the PC, as setup does when it uninstalls an
/// install for all users: each folder is unregistered with the Cloud Files filter, each user's
/// navigation-pane entry is removed from their registry, loading their hive for the moment when
/// they aren't signed in, and the registration is deleted. It runs as SYSTEM, where Windows'
/// own unregistering doesn't find other users' folders.
/// </summary>
public static class SyncRootCleanup
{
    /// <summary>Where Windows keeps every user's sync roots.</summary>
    public const string ManagerKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

    private const string NameSpaceKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace";
    private const string HiddenOnDesktopKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    /// <summary>The sync roots under <paramref name="manager"/> whose IDs start with <paramref name="idPrefix"/>.</summary>
    public static IReadOnlyList<RegisteredSyncRoot> Read(RegistryKey manager, string idPrefix)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);

        var roots = new List<RegisteredSyncRoot>();
        foreach (string id in manager.GetSubKeyNames())
        {
            if (!id.StartsWith(idPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            using var root = manager.OpenSubKey(id);
            if (root is null)
            {
                continue;
            }

            var users = new List<(string, string)>();
            using (var userRoots = root.OpenSubKey("UserSyncRoots"))
            {
                foreach (string sid in userRoots?.GetValueNames() ?? [])
                {
                    if (userRoots!.GetValue(sid) is string folder && Elevation.IsAccountSid(sid))
                    {
                        users.Add((sid, folder));
                    }
                }
            }

            string? clsid = root.GetValue("NamespaceCLSID") as string;
            roots.Add(new RegisteredSyncRoot(id, IsClsid(clsid) ? clsid : null, users));
        }

        return roots;
    }

    /// <summary>
    /// Removes a navigation-pane entry from a user's registry: <paramref name="user"/> is their
    /// <c>HKEY_CURRENT_USER</c>, <paramref name="classes"/> their <c>HKEY_CURRENT_USER\Software\Classes</c>.
    /// Only the keys of <paramref name="clsid"/> are touched.
    /// </summary>
    public static void RemoveUserEntry(RegistryKey user, RegistryKey? classes, string clsid)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!IsClsid(clsid))
        {
            throw new ArgumentException("Not a CLSID.", nameof(clsid));
        }

        using (var nameSpace = user.OpenSubKey(NameSpaceKey, writable: true))
        {
            nameSpace?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);
        }

        using (var hidden = user.OpenSubKey(HiddenOnDesktopKey, writable: true))
        {
            hidden?.DeleteValue(clsid, throwOnMissingValue: false);
        }

        using var clsids = classes?.OpenSubKey("CLSID", writable: true);
        clsids?.DeleteSubKeyTree(clsid, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Removes every user's sync roots whose IDs start with <paramref name="idPrefix"/>, as SYSTEM.
    /// Each step that fails is skipped, so the rest still goes.
    /// </summary>
    /// <returns>How many sync roots it removed.</returns>
    public static int RemoveForAllUsers(string idPrefix)
    {
        IReadOnlyList<RegisteredSyncRoot> roots;
        using (var manager = Registry.LocalMachine.OpenSubKey(ManagerKey))
        {
            if (manager is null)
            {
                return 0;
            }

            roots = Read(manager, idPrefix);
        }

        bool mayLoadHives = EnableHivePrivileges();
        foreach (var root in roots)
        {
            foreach (var (sid, folder) in root.Users)
            {
                // Online-only files go; files on the PC stay in what's then an ordinary folder.
                _ = SyncRoots.UnregisterFolder(folder);
                if (root.NamespaceClsid is { } clsid)
                {
                    EditUser(sid, mayLoadHives, (user, classes) => RemoveUserEntry(user, classes, clsid));
                }
            }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(ManagerKey + "\\" + root.Id, throwOnMissingSubKey: false);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
            {
                // Left registered; Windows ignores a root whose folder is gone.
            }
        }

        return roots.Count;
    }

    private static bool IsClsid(string? text) => text is { Length: 38 } && text[0] == '{' && text[^1] == '}' && Guid.TryParse(text, out _);

    // The user's own keys when they're signed in; otherwise their hive files, loaded for the moment.
    private static void EditUser(string sid, bool mayLoadHives, Action<RegistryKey, RegistryKey?> edit)
    {
        try
        {
            using var signedIn = Registry.Users.OpenSubKey(sid, writable: true);
            if (signedIn is not null)
            {
                using var classes = Registry.Users.OpenSubKey(sid + "_Classes", writable: true);
                edit(signedIn, classes);
                return;
            }

            if (!mayLoadHives || ProfileFolder(sid) is not { } profile)
            {
                return;
            }

            string mount = "PCCompanion.Cleanup." + sid;
            if (!Load(mount, Path.Join(profile, "NTUSER.DAT")))
            {
                return;
            }

            bool classesLoaded = Load(mount + "_Classes", Path.Join(profile, @"AppData\Local\Microsoft\Windows\UsrClass.dat"));
            try
            {
                using var user = Registry.Users.OpenSubKey(mount, writable: true);
                using var classes = classesLoaded ? Registry.Users.OpenSubKey(mount + "_Classes", writable: true) : null;
                if (user is not null)
                {
                    edit(user, classes);
                }
            }
            finally
            {
                // The keys above are closed first: a hive with keys open can't be unloaded.
                if (classesLoaded)
                {
                    Unload(mount + "_Classes");
                }

                Unload(mount);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException or Win32Exception)
        {
            // That user's entry stays; the folder it shows is an ordinary one now.
        }
    }

    private static string? ProfileFolder(string sid)
    {
        using var profile = Registry.LocalMachine.OpenSubKey(ProfileListKey + "\\" + sid);
        return profile?.GetValue("ProfileImagePath") is string path ? Environment.ExpandEnvironmentVariables(path) : null;
    }

    private static bool Load(string mount, string file) =>
        File.Exists(file) && PInvoke.RegLoadKey(Registry.Users.Handle, mount, file) == WIN32_ERROR.NO_ERROR;

    private static void Unload(string mount) => PInvoke.RegUnLoadKey(Registry.Users.Handle, mount);

    // Loading another user's hive needs the backup and restore privileges, which SYSTEM has but must turn on.
    private static unsafe bool EnableHivePrivileges()
    {
        using var process = PInvoke.GetCurrentProcess_SafeHandle();
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return false;
        }

        using (token)
        {
            foreach (string name in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
            {
                if (!PInvoke.LookupPrivilegeValue(null, name, out var luid))
                {
                    return false;
                }

                var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
                privileges.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };
                if (!PInvoke.AdjustTokenPrivileges((HANDLE)token.DangerousGetHandle(), false, &privileges, 0, null, null)
                    || Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.NO_ERROR)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
