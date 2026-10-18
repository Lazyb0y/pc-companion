using System;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Whether this process runs as administrator, and whether its user can.
/// </summary>
public static class Elevation
{
    /// <summary>Whether this process runs as administrator.</summary>
    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>
    /// Whether this process got admin rights through UAC, so the user's other programs run
    /// without them. With UAC off, an administrator's programs all have admin rights.
    /// </summary>
    public static bool IsUacElevated => ElevationType == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;

    /// <summary>
    /// Whether this process runs without admin rights only because of UAC: an administrator's
    /// program that wasn't elevated. Then a program of the user's that is elevated gets them
    /// through UAC, unlike when UAC is off.
    /// </summary>
    public static bool IsUacLimited => ElevationType == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;

    /// <summary>
    /// Whether the user can get admin rights by confirming a UAC prompt: an administrator
    /// account, rather than a standard account that would need another account's password.
    /// </summary>
    public static bool CanElevate => IsElevated || IsUacLimited;

    private static unsafe TOKEN_ELEVATION_TYPE? ElevationType
    {
        get
        {
            if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess_SafeHandle(), TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            {
                return null;
            }

            using (token)
            {
                TOKEN_ELEVATION_TYPE type;
                return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevationType, &type, (uint)sizeof(TOKEN_ELEVATION_TYPE), out _)
                    ? type
                    : null;
            }
        }
    }

    /// <summary>The security identifier of the user this process runs as, such as <c>S-1-5-21-…</c>.</summary>
    public static string CurrentUserSid => WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

    /// <summary>
    /// Whether <paramref name="sid"/> is the security identifier of a user account, rather than
    /// of a group, a built-in account or anything that isn't one.
    /// </summary>
    public static bool IsAccountSid(string? sid)
    {
        if (string.IsNullOrEmpty(sid))
        {
            return false;
        }

        try
        {
            return new SecurityIdentifier(sid).IsAccountSid();
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
