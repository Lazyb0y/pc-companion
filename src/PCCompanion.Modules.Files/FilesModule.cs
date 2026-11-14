using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.Modules.Files;

/// <summary>
/// Files: features that organize the user's files.
/// </summary>
public static class FilesModule
{
    public const string Id = "files";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddFilesModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeature, FolderRulesFeature>();
        services.AddSingleton<IFeature, DriveActionsFeature>();
        services.AddSingleton<IFeature, RemoteFoldersFeature>();
        return services;
    }
}
