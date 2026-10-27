using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;

namespace PCCompanion.Platform.Windows;

/// <summary>The server ends of the pipes through which this process serves the other programs of the user's account.</summary>
internal static class NamedPipes
{
    private const int BufferSize = 64 * 1024;

    // Outbound only, so the pipe takes no input. Only the user's account and the system may open
    // it, whatever rights this process has: an elevated process's default owner is the
    // Administrators group, which a program without admin rights can't use.
    public static unsafe NamedPipeServerStream CreateOutbound(string name)
    {
        string user;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            user = identity.User!.Value;
        }

        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;{user})(A;;GA;;;SY)");
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);

        fixed (byte* securityDescriptor = binary)
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = securityDescriptor,
            };

            using var handle = PInvoke.CreateNamedPipe(
                @"\\.\pipe\" + name,
                FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_OUTBOUND | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
                NAMED_PIPE_MODE.PIPE_TYPE_BYTE | NAMED_PIPE_MODE.PIPE_READMODE_BYTE | NAMED_PIPE_MODE.PIPE_WAIT | NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS,
                PInvoke.PIPE_UNLIMITED_INSTANCES,
                BufferSize,
                0,
                0,
                attributes);
            if (handle.IsInvalid)
            {
                throw new Win32Exception();
            }

            var pipe = new SafePipeHandle(handle.DangerousGetHandle(), ownsHandle: true);
            handle.SetHandleAsInvalid();
            return new NamedPipeServerStream(PipeDirection.Out, isAsync: true, isConnected: false, pipe);
        }
    }
}
