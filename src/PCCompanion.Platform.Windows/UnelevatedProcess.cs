using System;
using System.ComponentModel;
using System.IO;
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
    public static unsafe void Start(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

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

        using var shellProcess = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, shellProcessId);
        if (shellProcess.IsInvalid)
        {
            throw new Win32Exception();
        }

        if (!PInvoke.OpenProcessToken(shellProcess, TOKEN_ACCESS_MASK.TOKEN_DUPLICATE, out var shellToken))
        {
            throw new Win32Exception();
        }

        using (shellToken)
        {
            const TOKEN_ACCESS_MASK access = TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_ASSIGN_PRIMARY | TOKEN_ACCESS_MASK.TOKEN_DUPLICATE
                | TOKEN_ACCESS_MASK.TOKEN_ADJUST_DEFAULT | TOKEN_ACCESS_MASK.TOKEN_ADJUST_SESSIONID;
            if (!PInvoke.DuplicateTokenEx(shellToken, access, null, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out var token))
            {
                throw new Win32Exception();
            }

            using (token)
            {
                var commandLine = ($"\"{path}\"" + "\0").ToCharArray();
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

                PInvoke.CloseHandle(process.hThread);
                PInvoke.CloseHandle(process.hProcess);
            }
        }
    }
}
