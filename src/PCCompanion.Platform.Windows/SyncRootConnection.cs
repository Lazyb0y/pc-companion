using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.CloudFilters;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A sync provider's connection to a registered sync root: while it's open, Windows sends the
/// folder's requests to <see cref="ICloudFileRequests"/>, on Windows' own threads, which need no
/// message loop. Disposing it disconnects: Windows then refuses to open online-only files there.
/// </summary>
public sealed unsafe class SyncRootConnection : IDisposable
{
    // CfConnectSyncRoot through a function pointer: CsWin32 marshals its callback table from
    // delegates for the call only, while this table stays in memory for as long as the process runs.
    private static readonly delegate* unmanaged[Stdcall]<char*, Registration*, void*, CF_CONNECT_FLAGS, CF_CONNECTION_KEY*, int> Connect_ = LoadConnect();
    private static readonly Registration* Table = BuildTable();
    private static readonly ConcurrentDictionary<nint, SyncRootConnection> Connections = new();
    private static long s_lastId;

    private readonly nint _id;
    private readonly ICloudFileRequests _requests;
    private readonly Action<Exception> _failed;
    private CF_CONNECTION_KEY _key;
    private int _disposed;

    private SyncRootConnection(ICloudFileRequests requests, Action<Exception> failed)
    {
        _id = (nint)Interlocked.Increment(ref s_lastId);
        _requests = requests;
        _failed = failed;
    }

    /// <summary>
    /// Connects to the sync root at <paramref name="folder"/>. Requests come with the requesting
    /// program and full paths, and Windows never downloads a file just because this process reads it.
    /// </summary>
    /// <param name="failed">Called with an exception <paramref name="requests"/> threw, after the request was refused for it.</param>
    /// <exception cref="COMException">Windows refused, for example because the folder isn't a registered sync root, or another provider is connected to it.</exception>
    public static SyncRootConnection Connect(string folder, ICloudFileRequests requests, Action<Exception> failed)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(failed);

        var connection = new SyncRootConnection(requests, failed);
        Connections[connection._id] = connection;
        CF_CONNECTION_KEY key;
        int result;
        fixed (char* path = folder)
        {
            result = Connect_(
                path,
                Table,
                (void*)connection._id,
                CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO | CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH | CF_CONNECT_FLAGS.CF_CONNECT_FLAG_BLOCK_SELF_IMPLICIT_HYDRATION,
                &key);
        }

        if (result != 0)
        {
            Connections.TryRemove(connection._id, out _);
            Marshal.ThrowExceptionForHR(result);
        }

        connection._key = key;
        return connection;
    }

    /// <summary>Disconnects. Requests that were handed on and not answered yet get nothing more from Windows.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = PInvoke.CfDisconnectSyncRoot(_key);
        Connections.TryRemove(_id, out _);
    }

    private static delegate* unmanaged[Stdcall]<char*, Registration*, void*, CF_CONNECT_FLAGS, CF_CONNECTION_KEY*, int> LoadConnect()
    {
        nint library = NativeLibrary.Load("cldapi.dll", typeof(SyncRootConnection).Assembly, DllImportSearchPath.System32);
        return (delegate* unmanaged[Stdcall]<char*, Registration*, void*, CF_CONNECT_FLAGS, CF_CONNECTION_KEY*, int>)NativeLibrary.GetExport(library, "CfConnectSyncRoot");
    }

    private static Registration* BuildTable()
    {
        var table = (Registration*)NativeMemory.AllocZeroed(9, (nuint)sizeof(Registration));
        table[0] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, &OnFetchData);
        table[1] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, &OnCancelFetchData);
        table[2] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, &OnFetchPlaceholders);
        table[3] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, &OnDelete);
        table[4] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, &OnRename);
        table[5] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE, &OnDehydrate);
        table[6] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION, &OnFileClosed);
        table[7] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION, &OnRenameCompleted);
        table[8] = new(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NONE, null);
        return table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnFetchData(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnCancelFetchData(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnFetchPlaceholders(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnDelete(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnRename(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnDehydrate(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnRenameCompleted(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION, info, parameters);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnFileClosed(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters) =>
        Dispatch(CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION, info, parameters);

    // Runs on Windows' thread, so nothing may escape it: an exception there would end the process.
    private static void Dispatch(CF_CALLBACK_TYPE type, CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* parameters)
    {
        CloudFileRequest? request = null;
        SyncRootConnection? connection = null;
        try
        {
            if (!Connections.TryGetValue((nint)info->CallbackContext, out connection))
            {
                return;
            }

            var requests = connection._requests;
            switch (type)
            {
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA:
                    var fetchData = new FetchDataRequest(info, parameters);
                    request = fetchData;
                    requests.FetchData(fetchData);
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA:
                    requests.CancelFetchData(new CancelFetchDataNotice(info, parameters));
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS:
                    var listing = new FetchPlaceholdersRequest(info, parameters);
                    request = listing;
                    requests.FetchPlaceholders(listing);
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE:
                    var delete = new DeleteRequest(info, parameters);
                    request = delete;
                    requests.Delete(delete);
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME:
                    var rename = new RenameRequest(info, parameters);
                    request = rename;
                    requests.Rename(rename);
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE:
                    var dehydrate = new DehydrateRequest(info, parameters);
                    request = dehydrate;
                    requests.Dehydrate(dehydrate);
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION:
                    requests.FileClosed(new FileClosedNotice(info, parameters));
                    break;
                case CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION:
                    requests.RenameCompleted(new RenameCompletedNotice(info, parameters));
                    break;
            }
        }
        catch (Exception exception)
        {
            try
            {
                if (request is { IsAnswered: false })
                {
                    request.Refuse(CloudFileStatus.Unsuccessful);
                }

                connection?._failed(exception);
            }
            catch (Exception)
            {
                // The report itself failed; nothing more can be done on Windows' thread.
            }
        }
    }

    // CF_CALLBACK_REGISTRATION with a function pointer, which CsWin32 declares as a delegate.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Registration(CF_CALLBACK_TYPE type, delegate* unmanaged[Stdcall]<CF_CALLBACK_INFO*, CF_CALLBACK_PARAMETERS*, void> callback)
    {
        public readonly CF_CALLBACK_TYPE Type = type;
        public readonly delegate* unmanaged[Stdcall]<CF_CALLBACK_INFO*, CF_CALLBACK_PARAMETERS*, void> Callback = callback;
    }
}
