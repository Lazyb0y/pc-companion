using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.CloudFilters;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The answers a sync provider gives Windows, as <c>NTSTATUS</c> values. Windows turns each into
/// the error the program that asked gets; anything else becomes <see cref="Unsuccessful"/>.
/// </summary>
public static class CloudFileStatus
{
    public const int Success = 0;

    /// <summary>"The cloud sync provider failed user authentication" (error 386).</summary>
    public const int AuthenticationFailed = unchecked((int)0xC000CF0F);

    /// <summary>"The cloud sync provider failed to perform the operation due to network being unavailable" (error 388).</summary>
    public const int NetworkUnavailable = unchecked((int)0xC000CF11);

    /// <summary>"The cloud operation was unsuccessful" (error 389).</summary>
    public const int Unsuccessful = unchecked((int)0xC000CF12);

    /// <summary>"The operation cannot be performed on cloud files in use" (error 391).</summary>
    public const int InUse = unchecked((int)0xC000CF14);

    /// <summary>"Access to the cloud file is denied" (error 395).</summary>
    public const int AccessDenied = unchecked((int)0xC000CF18);
}

/// <summary>The program whose access to a file led to a request, as Windows reports it.</summary>
public sealed record RequestingProgram(int ProcessId, string ImagePath);

/// <summary>
/// A request or notice from Windows about a file or folder in a sync root. It keeps a copy of what
/// the callback said, so a request can be answered later, from any thread, after the callback has
/// returned. Each request is answered once, except downloads, which send their data in parts.
/// </summary>
public abstract class CloudFileRequest
{
    private readonly CF_CONNECTION_KEY _connection;
    private readonly long _transferKey;
    private readonly long _requestKey;
    private int _answered;

    private protected unsafe CloudFileRequest(CF_CALLBACK_INFO* info)
    {
        _connection = info->ConnectionKey;
        _transferKey = info->TransferKey;
        _requestKey = info->RequestKey;
        Path = FullPath(info, info->NormalizedPath);
        Identity = info->FileIdentity is null || info->FileIdentityLength == 0
            ? []
            : new ReadOnlySpan<byte>(info->FileIdentity, (int)info->FileIdentityLength).ToArray();
        FileSize = info->FileSize;
        Program = info->ProcessInfo is null || info->ProcessInfo->ImagePath.Value is null
            ? null
            : new RequestingProgram((int)info->ProcessInfo->ProcessId, info->ProcessInfo->ImagePath.ToString());
    }

    /// <summary>The full path of the file or folder.</summary>
    public string Path { get; }

    /// <summary>What the provider stored in the placeholder when it made it; empty for the sync root itself.</summary>
    public IReadOnlyList<byte> Identity { get; }

    /// <summary>The file's size in its placeholder.</summary>
    public long FileSize { get; }

    /// <summary>The program whose access led to the request, when Windows says.</summary>
    public RequestingProgram? Program { get; }

    /// <summary>Whether the request has had its answer.</summary>
    public bool IsAnswered => Volatile.Read(ref _answered) != 0;

    /// <summary>Gives a request that has had no answer yet the answer for a failure, <see cref="CloudFileStatus.Unsuccessful"/> unless given another.</summary>
    public abstract int Refuse(int status = CloudFileStatus.Unsuccessful);

    // The volume's letter and the path on it, or, on a volume without a letter, such as one mounted
    // in a folder, the volume's GUID path.
    private protected static unsafe string FullPath(CF_CALLBACK_INFO* info, PCWSTR path)
    {
        string onVolume = path.Value is null ? string.Empty : path.ToString();
        if (info->VolumeDosName.Value is not null && info->VolumeDosName.Length > 0)
        {
            return info->VolumeDosName.ToString() + onVolume;
        }

        return info->VolumeGuidName.Value is null ? onVolume : info->VolumeGuidName.ToString().TrimEnd('\\') + onVolume;
    }

    // Records the answer; a second one is refused, as Windows would refuse it.
    private protected bool TryAnswer() => Interlocked.Exchange(ref _answered, 1) == 0;

    private protected unsafe int Execute(CF_OPERATION_TYPE type, ref CF_OPERATION_PARAMETERS parameters)
    {
        var operation = new CF_OPERATION_INFO
        {
            StructSize = (uint)sizeof(CF_OPERATION_INFO),
            Type = type,
            ConnectionKey = _connection,
            TransferKey = _transferKey,
            RequestKey = _requestKey,
        };
        return PInvoke.CfExecute(operation, ref parameters).Value;
    }

    // CF_SIZE_OF_OP_PARAM: the size up to the end of the union's member in use.
    private protected static unsafe uint ParameterSize<T>()
        where T : unmanaged =>
        (uint)(Marshal.OffsetOf<CF_OPERATION_PARAMETERS>(nameof(CF_OPERATION_PARAMETERS.Anonymous)) + sizeof(T));

    private protected int ReportProgress(long total, long completed) =>
        PInvoke.CfReportProviderProgress(_connection, _transferKey, total, completed).Value;
}

/// <summary>A program read an online-only file: send its data.</summary>
public sealed class FetchDataRequest : CloudFileRequest
{
    internal unsafe FetchDataRequest(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        ref var fetch = ref parameters->FetchData;
        RequiredOffset = fetch.RequiredFileOffset;
        RequiredLength = fetch.RequiredLength;
        OptionalOffset = fetch.OptionalFileOffset;
        OptionalLength = fetch.OptionalLength;
        IsExplicit = (fetch.Flags & CF_CALLBACK_FETCH_DATA_FLAGS.CF_CALLBACK_FETCH_DATA_FLAG_EXPLICIT_HYDRATION) != 0;
    }

    /// <summary>Where the range Windows needs starts.</summary>
    public long RequiredOffset { get; }

    public long RequiredLength { get; }

    /// <summary>A further range the provider may send with it.</summary>
    public long OptionalOffset { get; }

    public long OptionalLength { get; }

    /// <summary>Whether the provider asked for the download itself, as for a file kept on this device.</summary>
    public bool IsExplicit { get; }

    /// <summary>
    /// Sends one part of the data. Its offset and length must be multiples of 4,096, except a part
    /// that ends at the end of the file. Returns Windows' HRESULT.
    /// </summary>
    public unsafe int Transfer(long offset, ReadOnlySpan<byte> data)
    {
        fixed (byte* buffer = data)
        {
            var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._TransferData_e__Struct>() };
            parameters.TransferData.Buffer = buffer;
            parameters.TransferData.Offset = offset;
            parameters.TransferData.Length = data.Length;
            parameters.TransferData.CompletionStatus = (NTSTATUS)CloudFileStatus.Success;
            return Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA, ref parameters);
        }
    }

    /// <summary>Tells Windows the download goes on, which restarts its 60-second wait. Returns Windows' HRESULT.</summary>
    public int Progress(long total, long completed) => ReportProgress(total, completed);

    /// <summary>Fails the download with <paramref name="status"/>, which the program gets as its error.</summary>
    public override unsafe int Refuse(int status = CloudFileStatus.Unsuccessful)
    {
        if (!TryAnswer())
        {
            return 0;
        }

        var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._TransferData_e__Struct>() };
        parameters.TransferData.Buffer = null;
        parameters.TransferData.Offset = RequiredOffset;
        parameters.TransferData.Length = RequiredLength;
        parameters.TransferData.CompletionStatus = (NTSTATUS)status;
        return Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA, ref parameters);
    }

    /// <summary>Marks the download as done after its last <see cref="Transfer"/>, so it isn't refused afterwards.</summary>
    public void Done() => TryAnswer();
}

/// <summary>Windows stopped waiting for a download, because it timed out or the user cancelled it.</summary>
public sealed class CancelFetchDataNotice : CloudFileRequest
{
    internal unsafe CancelFetchDataNotice(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        Offset = parameters->Cancel.FetchData.FileOffset;
        Length = parameters->Cancel.FetchData.Length;
        IsTimeout = (parameters->Cancel.Flags & CF_CALLBACK_CANCEL_FLAGS.CF_CALLBACK_CANCEL_FLAG_IO_TIMEOUT) != 0;
        TryAnswer();
    }

    public long Offset { get; }

    public long Length { get; }

    /// <summary>Whether Windows gave up after its 60 seconds, rather than the user cancelling.</summary>
    public bool IsTimeout { get; }

    public override int Refuse(int status = CloudFileStatus.Unsuccessful) => 0;
}

/// <summary>A program listed a folder: send its entries.</summary>
public sealed class FetchPlaceholdersRequest : CloudFileRequest
{
    internal unsafe FetchPlaceholdersRequest(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        Pattern = parameters->FetchPlaceholders.Pattern.Value is null ? "*" : parameters->FetchPlaceholders.Pattern.ToString();
    }

    /// <summary>Which names the program asked for, such as <c>*</c>.</summary>
    public string Pattern { get; }

    /// <summary>
    /// Answers with the folder's entries. When <paramref name="complete"/>, Windows treats the folder
    /// as complete and doesn't ask again; otherwise it asks each time a program lists it, and entries
    /// already there are left as they are. Returns Windows' HRESULT, and each entry's result in
    /// <paramref name="results"/>, which must be as long as <paramref name="entries"/>.
    /// </summary>
    public unsafe int Answer(IReadOnlyList<PlaceholderEntry> entries, bool complete, Span<int> results)
    {
        if (!TryAnswer())
        {
            return 0;
        }

        using var native = new NativePlaceholders(entries);
        fixed (CF_PLACEHOLDER_CREATE_INFO* array = native.Infos)
        {
            var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._TransferPlaceholders_e__Struct>() };
            parameters.TransferPlaceholders.Flags = complete
                ? CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION
                : CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_NONE;
            parameters.TransferPlaceholders.CompletionStatus = (NTSTATUS)CloudFileStatus.Success;
            parameters.TransferPlaceholders.PlaceholderTotalCount = entries.Count;
            parameters.TransferPlaceholders.PlaceholderArray = entries.Count == 0 ? null : array;
            parameters.TransferPlaceholders.PlaceholderCount = (uint)entries.Count;
            int result = Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS, ref parameters);
            native.CopyResults(results);
            return result;
        }
    }

    /// <summary>Fails the listing with <paramref name="status"/>; the program gets an empty folder, File Explorer the error.</summary>
    public override unsafe int Refuse(int status = CloudFileStatus.Unsuccessful)
    {
        if (!TryAnswer())
        {
            return 0;
        }

        var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._TransferPlaceholders_e__Struct>() };
        parameters.TransferPlaceholders.CompletionStatus = (NTSTATUS)status;
        return Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS, ref parameters);
    }
}

/// <summary>A program is about to delete a placeholder: allow or refuse it.</summary>
public sealed class DeleteRequest : CloudFileRequest
{
    internal unsafe DeleteRequest(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        IsDirectory = (parameters->Delete.Flags & CF_CALLBACK_DELETE_FLAGS.CF_CALLBACK_DELETE_FLAG_IS_DIRECTORY) != 0;
        IsUndelete = (parameters->Delete.Flags & CF_CALLBACK_DELETE_FLAGS.CF_CALLBACK_DELETE_FLAG_IS_UNDELETE) != 0;
    }

    public bool IsDirectory { get; }

    /// <summary>Whether a program took back an earlier delete of the same file before closing it.</summary>
    public bool IsUndelete { get; }

    /// <summary>Lets the delete go ahead. Returns Windows' HRESULT.</summary>
    public int Allow() => Acknowledge(CloudFileStatus.Success);

    public override int Refuse(int status = CloudFileStatus.AccessDenied) => Acknowledge(status);

    private unsafe int Acknowledge(int status)
    {
        if (!TryAnswer())
        {
            return 0;
        }

        var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._AckDelete_e__Struct>() };
        parameters.AckDelete.CompletionStatus = (NTSTATUS)status;
        return Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE, ref parameters);
    }
}

/// <summary>A program is about to rename or move a placeholder: allow or refuse it.</summary>
public sealed class RenameRequest : CloudFileRequest
{
    internal unsafe RenameRequest(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        var flags = parameters->Rename.Flags;
        TargetPath = FullPath(info, parameters->Rename.TargetPath);
        IsDirectory = (flags & CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_IS_DIRECTORY) != 0;
        IsSourceInSyncRoot = (flags & CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_SOURCE_IN_SCOPE) != 0;
        IsTargetInSyncRoot = (flags & CF_CALLBACK_RENAME_FLAGS.CF_CALLBACK_RENAME_FLAG_TARGET_IN_SCOPE) != 0;
    }

    /// <summary>The full path it gets.</summary>
    public string TargetPath { get; }

    public bool IsDirectory { get; }

    public bool IsSourceInSyncRoot { get; }

    /// <summary>Whether it stays in the sync root; otherwise it's moving out of it.</summary>
    public bool IsTargetInSyncRoot { get; }

    /// <summary>Lets the rename go ahead. Returns Windows' HRESULT.</summary>
    public int Allow() => Acknowledge(CloudFileStatus.Success);

    public override int Refuse(int status = CloudFileStatus.AccessDenied) => Acknowledge(status);

    private unsafe int Acknowledge(int status)
    {
        if (!TryAnswer())
        {
            return 0;
        }

        var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._AckRename_e__Struct>() };
        parameters.AckRename.CompletionStatus = (NTSTATUS)status;
        return Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME, ref parameters);
    }
}

/// <summary>Why Windows wants to remove a file's copy on the PC.</summary>
public enum DehydrationReason
{
    Unknown,

    /// <summary>The user chose Free up space.</summary>
    UserManual,

    /// <summary>The drive is low on space.</summary>
    LowSpace,

    /// <summary>Storage Sense found the file unused.</summary>
    Inactivity,

    /// <summary>Windows is being upgraded.</summary>
    WindowsUpgrade,
}

/// <summary>Windows is about to remove a file's copy on the PC: allow or refuse it.</summary>
public sealed class DehydrateRequest : CloudFileRequest
{
    internal unsafe DehydrateRequest(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        Reason = parameters->Dehydrate.Reason switch
        {
            CF_CALLBACK_DEHYDRATION_REASON.CF_CALLBACK_DEHYDRATION_REASON_USER_MANUAL => DehydrationReason.UserManual,
            CF_CALLBACK_DEHYDRATION_REASON.CF_CALLBACK_DEHYDRATION_REASON_SYSTEM_LOW_SPACE => DehydrationReason.LowSpace,
            CF_CALLBACK_DEHYDRATION_REASON.CF_CALLBACK_DEHYDRATION_REASON_SYSTEM_INACTIVITY => DehydrationReason.Inactivity,
            CF_CALLBACK_DEHYDRATION_REASON.CF_CALLBACK_DEHYDRATION_REASON_SYSTEM_OS_UPGRADE => DehydrationReason.WindowsUpgrade,
            _ => DehydrationReason.Unknown,
        };
    }

    public DehydrationReason Reason { get; }

    /// <summary>Lets it go ahead. Returns Windows' HRESULT.</summary>
    public int Allow() => Acknowledge(CloudFileStatus.Success);

    public override int Refuse(int status = CloudFileStatus.InUse) => Acknowledge(status);

    private unsafe int Acknowledge(int status)
    {
        if (!TryAnswer())
        {
            return 0;
        }

        byte[] identity = [.. Identity];
        fixed (byte* bytes = identity)
        {
            var parameters = new CF_OPERATION_PARAMETERS { ParamSize = ParameterSize<CF_OPERATION_PARAMETERS._Anonymous_e__Union._AckDehydrate_e__Struct>() };
            parameters.AckDehydrate.CompletionStatus = (NTSTATUS)status;
            parameters.AckDehydrate.FileIdentity = identity.Length == 0 ? null : bytes;
            parameters.AckDehydrate.FileIdentityLength = (uint)identity.Length;
            return Execute(CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DEHYDRATE, ref parameters);
        }
    }
}

/// <summary>A program closed a placeholder it had open.</summary>
public sealed class FileClosedNotice : CloudFileRequest
{
    internal unsafe FileClosedNotice(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
        : base(info)
    {
        IsDeleted = (parameters->CloseCompletion.Flags & CF_CALLBACK_CLOSE_COMPLETION_FLAGS.CF_CALLBACK_CLOSE_COMPLETION_FLAG_DELETED) != 0;
        TryAnswer();
    }

    /// <summary>Whether it was deleted as it closed.</summary>
    public bool IsDeleted { get; }

    public override int Refuse(int status = CloudFileStatus.Unsuccessful) => 0;
}

/// <summary>
/// What a sync provider does with Windows' requests. Each method is called on one of Windows'
/// threads, and should only hand the request on and return: a request can be answered later, from
/// any thread. A method that throws gets the request refused for it.
/// </summary>
public interface ICloudFileRequests
{
    void FetchData(FetchDataRequest request);

    void CancelFetchData(CancelFetchDataNotice notice);

    void FetchPlaceholders(FetchPlaceholdersRequest request);

    void Delete(DeleteRequest request);

    void Rename(RenameRequest request);

    void Dehydrate(DehydrateRequest request);

    void FileClosed(FileClosedNotice notice);
}
