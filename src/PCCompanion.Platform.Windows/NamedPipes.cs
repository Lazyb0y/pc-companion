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

    /// <summary>A pipe that takes no input, for <see cref="SnapshotPipe"/>.</summary>
    public static NamedPipeServerStream CreateOutbound(string name) =>
        Create(
            name,
            PipeDirection.Out,
            FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_OUTBOUND,
            NAMED_PIPE_MODE.PIPE_TYPE_BYTE | NAMED_PIPE_MODE.PIPE_READMODE_BYTE,
            0);

    /// <summary>A pipe in both directions that passes whole messages, for <see cref="RequestPipe"/>.</summary>
    public static NamedPipeServerStream CreateDuplex(string name)
    {
        var pipe = Create(
            name,
            PipeDirection.InOut,
            FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_DUPLEX,
            NAMED_PIPE_MODE.PIPE_TYPE_MESSAGE | NAMED_PIPE_MODE.PIPE_READMODE_MESSAGE,
            BufferSize);

        // A stream made from a handle reads bytes until told otherwise.
        try
        {
            pipe.ReadMode = PipeTransmissionMode.Message;
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    // Only the user's account and the system may open the pipe, whatever rights this process has:
    // an elevated process's default owner is the Administrators group, which a program without
    // admin rights can't use.
    private static unsafe NamedPipeServerStream Create(string name, PipeDirection direction, FILE_FLAGS_AND_ATTRIBUTES access, NAMED_PIPE_MODE mode, uint inBufferSize)
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
                access | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
                mode | NAMED_PIPE_MODE.PIPE_WAIT | NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS,
                PInvoke.PIPE_UNLIMITED_INSTANCES,
                BufferSize,
                inBufferSize,
                0,
                attributes);
            if (handle.IsInvalid)
            {
                throw new Win32Exception();
            }

            var pipe = new SafePipeHandle(handle.DangerousGetHandle(), ownsHandle: true);
            handle.SetHandleAsInvalid();
            return new NamedPipeServerStream(direction, isAsync: true, isConnected: false, pipe);
        }
    }
}
