using System;
using System.IO;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// What the host and <c>PCCompanion.RemoteFolders.exe</c>, the process that serves Remote Folders'
/// folders, share about it. See "Threads and processes" in docs/features/files/remote-folders.md.
/// </summary>
public static class RemoteFoldersProcess
{
    /// <summary>The provider's executable, installed next to the host.</summary>
    public const string FileName = "PCCompanion.RemoteFolders.exe";

    /// <summary>The lock the running provider holds, one per user session.</summary>
    public const string InstanceLockName = @"Local\PCCompanion.RemoteFolders";

    /// <summary>The signal that asks the running provider to stop, one per user session.</summary>
    public const string StopSignalName = @"Local\PCCompanion.RemoteFolders.Stop";

    /// <summary>The argument followed by the host's process ID: the provider exits when that process ends.</summary>
    public const string HostArgument = "--host";

    /// <summary>The provider's name in its sync roots' IDs, <c>PCCompanion!&lt;user SID&gt;!&lt;server ID&gt;</c>.</summary>
    public const string ProviderName = "PCCompanion";

    /// <summary>The start of the target names of the credentials Remote Folders keeps in Credential Manager.</summary>
    public const string CredentialPrefix = "PCCompanion/RemoteFolders/";

    /// <summary>The full path of the provider's executable, next to the program that asks.</summary>
    public static string ExecutablePath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>The target name of a server's credential.</summary>
    public static string CredentialTarget(string serverId) => CredentialPrefix + serverId;
}
