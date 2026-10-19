using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Starts a program without admin rights from a process that runs as administrator, with the
/// token of the Windows shell, which runs as the same user without them.
/// </summary>
public static class UnelevatedProcess
{
    private const int NotFound = 1168; // ERROR_NOT_FOUND

    /// <summary>
    /// Starts the program at <paramref name="path"/>, with no arguments, in its own folder.
    /// </summary>
    /// <exception cref="Win32Exception">There's no shell to take the token from, or Windows refused to start the program.</exception>
    public static void Start(string path) => Start(path, arguments: null);

    /// <summary>
    /// Starts the program at <paramref name="path"/> in its own folder, without waiting for it.
    /// The <paramref name="arguments"/> are passed as they are, so they must need no quotes.
    /// </summary>
    /// <exception cref="Win32Exception">There's no shell to take the token from, or Windows refused to start the program.</exception>
    public static void Start(string path, string? arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var process = Create(path, arguments, sameUser: false);
        PInvoke.CloseHandle(process.hThread);
        PInvoke.CloseHandle(process.hProcess);
    }

    /// <summary>
    /// Runs the program at <paramref name="path"/> in its own folder, as the same user as this
    /// process, and waits up to <paramref name="timeout"/> for it to exit. The
    /// <paramref name="arguments"/> are passed as they are, so they must need no quotes.
    /// </summary>
    /// <exception cref="Win32Exception">
    /// There's no shell of this user without admin rights to take the token from, or Windows
    /// refused to start the program.
    /// </exception>
    public static void Run(string path, string arguments, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(arguments);

        var process = Create(path, arguments, sameUser: true);
        PInvoke.CloseHandle(process.hThread);
        _ = PInvoke.WaitForSingleObject(process.hProcess, (uint)timeout.TotalMilliseconds);
        PInvoke.CloseHandle(process.hProcess);
    }

    private static unsafe PROCESS_INFORMATION Create(string path, string? arguments, bool sameUser)
    {
        var shell = PInvoke.GetShellWindow();
        if (shell.IsNull)
        {
            throw new Win32Exception(NotFound, "There's no shell window to take a token from.");
        }

        uint shellProcessId;
        if (PInvoke.GetWindowThreadProcessId(shell, &shellProcessId) == 0)
        {
            throw new Win32Exception();
        }

        if (sameUser && Elevation.IsProcessElevated(shellProcessId) != false)
        {
            throw new Win32Exception(NotFound, "The shell runs as administrator, or its rights can't be read.");
        }

        using var shellProcess = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, shellProcessId);
        if (shellProcess.IsInvalid)
        {
            throw new Win32Exception();
        }

        var shellAccess = sameUser ? TOKEN_ACCESS_MASK.TOKEN_DUPLICATE | TOKEN_ACCESS_MASK.TOKEN_QUERY : TOKEN_ACCESS_MASK.TOKEN_DUPLICATE;
        if (!PInvoke.OpenProcessToken(shellProcess, shellAccess, out var shellToken))
        {
            throw new Win32Exception();
        }

        using (shellToken)
        {
            if (sameUser && !IsCurrentUser(shellToken))
            {
                throw new Win32Exception(NotFound, "The shell runs as another user.");
            }

            const TOKEN_ACCESS_MASK access = TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_ASSIGN_PRIMARY | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE
                | TOKEN_ACCESS_MASK.TOKEN_ADJUST_DEFAULT | TOKEN_ACCESS_MASK.TOKEN_ADJUST_SESSIONID;
            if (!PInvoke.DuplicateTokenEx(shellToken, access, null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out var token))
            {
                throw new Win32Exception();
            }

            using (token)
            {
                var commandLine = ($"\"{path}\"" + (arguments is null ? string.Empty : " " + arguments) + "\0").ToCharArray();
                var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
                PROCESS_INFORMATION process;
                fixed (char* commandLineText = commandLine)
                fixed (char* application = path)
                fixed (char* folder = Path.GetDirectoryName(path))
                {
                    if (!PInvoke.CreateProcessWithToken((HANDLE)token.DangerousGetHandle(), 0, application, commandLineText, 0, null, folder, &startup, &process))
                    {
                        throw new Win32Exception();
                    }
                }

                return process;
            }
        }
    }

    private static bool IsCurrentUser(SafeHandle token)
    {
        using var shellUser = new WindowsIdentity(token.DangerousGetHandle());
        using var currentUser = WindowsIdentity.GetCurrent();
        return shellUser.User is not null && shellUser.User == currentUser.User;
    }
}
