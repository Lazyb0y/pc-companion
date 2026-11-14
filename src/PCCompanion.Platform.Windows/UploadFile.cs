using System;
using System.IO;
using Windows.Win32;
using Windows.Win32.Storage.CloudFilters;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A file in a sync root opened for an upload, through a handle that Windows closes as soon as a
/// program opens the file, so an upload never blocks the user's programs. While it's open, no
/// program has the file open, so marking it in sync through the same handle can't hide a change.
/// </summary>
public sealed class UploadFile : IDisposable
{
    // HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED), as for a read-only file opened for writing.
    private const int AccessDenied = unchecked((int)0x80070005);

    private readonly OplockHandle _handle;

    private UploadFile(OplockHandle handle, long length)
    {
        _handle = handle;
        Length = length;
    }

    /// <summary>HRESULT_FROM_WIN32(ERROR_OPLOCK_HANDLE_CLOSED): a program opened the file, so Windows closed the handle.</summary>
    public static int Closed => OplockHandle.Closed;

    /// <summary>The file's size when it was opened.</summary>
    public long Length { get; }

    /// <summary>Whether Windows closed the handle because a program opened the file.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Opens <paramref name="path"/> only if no program has it open: otherwise it fails with a
    /// sharing violation, and the upload waits.
    /// </summary>
    /// <returns>Windows' HRESULT, 0 when <paramref name="file"/> is open.</returns>
    public static int Open(string path, out UploadFile? file)
    {
        file = null;
        int result = OplockHandle.Open(path, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE | CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_WRITE_ACCESS, out var handle);
        if (result == AccessDenied)
        {
            // A read-only file can still be read, and marked in sync.
            result = OplockHandle.Open(path, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE, out handle);
        }

        if (result != 0)
        {
            return result;
        }

        long length = 0;
        result = handle!.Use(win32 =>
        {
            length = RandomAccess.GetLength(win32);
            return 0;
        });
        if (result != 0)
        {
            handle.Dispose();
            return result;
        }

        file = new UploadFile(handle, length);
        return 0;
    }

    /// <summary>
    /// A stream that reads the file from the start. When a program opens the file meanwhile, a
    /// read fails with an <see cref="IOException"/> and <see cref="IsClosed"/> becomes true.
    /// </summary>
    public Stream OpenRead() => new Reader(this);

    /// <summary>
    /// Marks the file as the same as on the server, with <paramref name="identity"/>: a new file
    /// becomes a placeholder, and a placeholder gets the new identity. Its size and dates stay.
    /// </summary>
    /// <returns>Windows' HRESULT; <see cref="Closed"/> when a program opened the file meanwhile, so it may have changed.</returns>
    public int MarkInSync(byte[] identity, bool isPlaceholder)
    {
        ArgumentNullException.ThrowIfNull(identity);
        int result = _handle.Use(win32 => isPlaceholder
            ? PInvoke.CfUpdatePlaceholder(win32, (CF_FS_METADATA?)null, identity, default, CF_UPDATE_FLAGS.CF_UPDATE_FLAG_MARK_IN_SYNC).Value
            : PInvoke.CfConvertToPlaceholder(win32, identity, CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC).Value);
        IsClosed |= result == Closed;
        return result;
    }

    public void Dispose() => _handle.Dispose();

    private int Read(Span<byte> buffer, long offset)
    {
        int read = _handle.Read(buffer, offset);
        if (read < 0)
        {
            IsClosed = true;
            throw new IOException("A program opened the file, so Windows closed the upload's handle.", Closed);
        }

        return read;
    }

    private sealed class Reader(UploadFile file) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => file.Length;

        public override long Position
        {
            get => _position;
            set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            int read = file.Read(buffer, _position);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => file.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
