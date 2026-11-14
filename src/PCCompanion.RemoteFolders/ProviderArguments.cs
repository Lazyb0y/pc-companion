using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using PCCompanion.Core.Settings;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.RemoteFolders;

/// <summary>What the provider was started with.</summary>
/// <param name="HostProcessId">The host's process ID, after <see cref="RemoteFoldersProcess.HostArgument"/>; the provider exits when it ends.</param>
public sealed record ProviderArguments(int? HostProcessId)
{
    public static ProviderArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (string.Equals(args[i], RemoteFoldersProcess.HostArgument, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                && id > 0)
            {
                return new ProviderArguments(id);
            }
        }

        return new ProviderArguments(HostProcessId: null);
    }
}

/// <summary>Remote Folders' section of the settings, as the provider reads it.</summary>
public static class ProviderSettings
{
    /// <summary>
    /// The feature's settings, the defaults when there's no section, or <see langword="null"/> with
    /// <paramref name="error"/> when the section can't be read.
    /// </summary>
    public static RemoteFoldersSettings? Read(AppSettings settings, out JsonException? error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        error = null;
        try
        {
            return RemoteFoldersSettings.Read(settings.Features.TryGetValue(RemoteFoldersFeature.Definition.Id, out var section) ? section : null);
        }
        catch (JsonException exception)
        {
            error = exception;
            return null;
        }
    }
}
