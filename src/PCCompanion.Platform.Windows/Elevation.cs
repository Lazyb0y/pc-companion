using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Whether this process, or another one, runs as administrator, and whether its user can.
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

    /// <summary>
    /// Whether the process with ID <paramref name="processId"/> runs as administrator, or
    /// <see langword="null"/> when that can't be read, for example because the process has ended.
    /// Processes of the same user can read it without admin rights.
    /// </summary>
    public static unsafe bool? IsProcessElevated(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid || !PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return null;
        }

        using (token)
        {
            TOKEN_ELEVATION elevation;
            return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(TOKEN_ELEVATION), out _)
                ? elevation.TokenIsElevated != 0
                : null;
        }
    }

    /// <summary>
    /// Whether the process with ID <paramref name="processId"/> got admin rights through UAC, as
    /// <see cref="IsUacElevated"/> says of this one, or <see langword="null"/> when that can't be
    /// read. Processes of the same user can read it without admin rights.
    /// </summary>
    public static bool? IsProcessUacElevated(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        return process.IsInvalid ? null : ElevationTypeOf(process) is { } type ? type == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull : null;
    }

    /// <summary>
    /// Whether the process with ID <paramref name="processId"/> runs at this process's integrity
    /// level: both without admin rights, or both as administrator. It's false for a process in an
    /// AppContainer, such as a Store app, and when the level can't be read.
    /// </summary>
    public static bool HasSameIntegrityLevel(uint processId) =>
        IntegrityLevel(PInvoke.GetCurrentProcess_SafeHandle()) is { } own && IntegrityLevel(processId) == own;

    // The integrity level, the last part of the token's mandatory label, such as 0x2000 for medium.
    private static uint? IntegrityLevel(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        return process.IsInvalid ? null : IntegrityLevel(process);
    }

    private static unsafe uint? IntegrityLevel(SafeHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return null;
        }

        using (token)
        {
            // The label holds a SID, which is at most 68 bytes.
            const int Size = 128;
            var buffer = stackalloc byte[Size];
            if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, buffer, Size, out _))
            {
                return null;
            }

            var sid = ((TOKEN_MANDATORY_LABEL*)buffer)->Label.Sid;
            var count = *PInvoke.GetSidSubAuthorityCount(sid);
            return count > 0 ? *PInvoke.GetSidSubAuthority(sid, count - 1u) : null;
        }
    }

    private static TOKEN_ELEVATION_TYPE? ElevationType => ElevationTypeOf(PInvoke.GetCurrentProcess_SafeHandle());

    private static unsafe TOKEN_ELEVATION_TYPE? ElevationTypeOf(SafeHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
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
