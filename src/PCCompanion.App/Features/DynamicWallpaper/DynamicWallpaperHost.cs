using System;
using System.Threading.Tasks;
using PCCompanion.Modules.Desktop.DynamicWallpaper;

namespace PCCompanion.App.Features.DynamicWallpaper;

/// <summary>
/// Asks the host about Dynamic Wallpaper through its pipe, off the UI thread. Only the host knows
/// whether the feature stepped aside, a policy manages the background or a picture can't be used;
/// it serves the pipe while Dynamic Wallpaper is on.
/// </summary>
public static class DynamicWallpaperHost
{
    /// <summary>How long the state may take.</summary>
    public static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long Resume may take: the host waits up to 10 seconds for Explorer.</summary>
    public static readonly TimeSpan ResumeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The host's answer, or <see langword="null"/> when it doesn't answer, as while Dynamic Wallpaper is off.</summary>
    public static Task<DynamicWallpaperAnswer?> AskAsync(DynamicWallpaperRequestKind kind) =>
        Task.Run(() => DynamicWallpaperPipe.AskAsync(
            DynamicWallpaperPipe.Name,
            new DynamicWallpaperRequest { Kind = kind },
            kind == DynamicWallpaperRequestKind.Resume ? ResumeTimeout : StateTimeout));
}
