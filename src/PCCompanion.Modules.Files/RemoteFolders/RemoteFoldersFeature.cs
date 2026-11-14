using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// Shows the files on the user's SFTP servers in File Explorer, through the Cloud Files API. The
/// files' work runs in a process of its own, <c>PCCompanion.RemoteFolders.exe</c>, which the host
/// starts while the feature is on. See docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class RemoteFoldersFeature(ILogger<RemoteFoldersFeature> logger) : IFeature
{
    private RemoteFoldersSettings? _applied;

    public static FeatureInfo Definition { get; } = new("remoteFolders", FilesModule.Id, "RemoteFoldersName", "RemoteFoldersDescription", "");

    /// <summary>Whether this PC can run Remote Folders, for the settings app, which doesn't create the feature.</summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    /// <summary>Whether the feature is on with settings it could read.</summary>
    public bool IsRunning => _applied is not null;

    public void Apply(JsonElement? settings)
    {
        RemoteFoldersSettings read;
        try
        {
            read = RemoteFoldersSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            _applied = null;
            return;
        }

        if (!read.Enabled)
        {
            _applied = null;
            return;
        }

        if (read != _applied)
        {
            LogApplied(logger, read.Servers.Count, read.UsableServers.Count);
        }

        _applied = read;
    }

    public void Dispose() => _applied = null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Remote Folders settings can't be read, so Remote Folders is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remote Folders settings applied: {Servers} servers, {Usable} of them usable.")]
    private static partial void LogApplied(ILogger logger, int servers, int usable);
}
