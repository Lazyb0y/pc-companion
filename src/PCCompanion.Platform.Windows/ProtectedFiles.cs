using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Whether files and folders can be changed only by administrators, as in Program Files.
/// </summary>
public static class ProtectedFiles
{
    private const FileSystemRights ChangeRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // SYSTEM, Administrators and TrustedInstaller. CREATOR OWNER only applies to new items, which
    // can't be created without one of the rights checked here.
    private static readonly string[] TrustedSids =
    [
        "S-1-5-18",
        "S-1-5-32-544",
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
        "S-1-3-0",
    ];

    /// <summary>
    /// Whether <paramref name="path"/>, a file or a folder, is owned by and can be changed only
    /// by SYSTEM, Administrators or TrustedInstaller. A missing path isn't protected.
    /// </summary>
    public static bool IsProtected(string path)
    {
        FileSystemSecurity security;
        if (Directory.Exists(path))
        {
            security = new DirectoryInfo(path).GetAccessControl();
        }
        else if (File.Exists(path))
        {
            security = new FileInfo(path).GetAccessControl();
        }
        else
        {
            return false;
        }

        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !TrustedSids.Contains(owner.Value))
        {
            return false;
        }

        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .All(rule => rule.AccessControlType == AccessControlType.Deny
                || (rule.FileSystemRights & ChangeRights) == 0
                || TrustedSids.Contains(rule.IdentityReference.Value));
    }
}
