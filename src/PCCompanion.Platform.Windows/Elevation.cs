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
    /// Whether the user can get admin rights by confirming a UAC prompt: an administrator
    /// account, rather than a standard account that would need another account's password.
    /// </summary>
    public static unsafe bool CanElevate
    {
        get
        {
            if (IsElevated)
            {
                return true;
            }

            if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess_SafeHandle(), TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            {
                return false;
            }

            using (token)
            {
                TOKEN_ELEVATION_TYPE type;
                return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevationType, &type, (uint)sizeof(TOKEN_ELEVATION_TYPE), out _)
                    && type == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited;
            }
        }
    }

    /// <summary>The security identifier of the user this process runs as, such as <c>S-1-5-21-…</c>.</summary>
    public static string CurrentUserSid => WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
}
