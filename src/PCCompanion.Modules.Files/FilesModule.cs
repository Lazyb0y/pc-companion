using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Files.FolderRules;

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
        return services;
    }
}
