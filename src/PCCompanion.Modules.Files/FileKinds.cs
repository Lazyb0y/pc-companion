using System;
using System.Collections.Generic;

namespace PCCompanion.Modules.Files;

/// <summary>A preset that fills in the Type condition's extensions.</summary>
public enum FileKind
{
    Documents,
    Pictures,
    Videos,
    Music,
    Archives,
    Installers,

    /// <summary>What cameras and phones save: photos, raw photos and videos.</summary>
    CameraFiles,
}

/// <summary>
/// The extensions each <see cref="FileKind"/> adds, which the user sees and can change, so that
/// nothing depends on what the installed apps registered.
/// </summary>
public static class FileKinds
{
    public static IReadOnlyList<string> Extensions(FileKind kind) => kind switch
    {
        FileKind.Documents => ["pdf", "doc", "docx", "odt", "rtf", "txt", "md", "xls", "xlsx", "ods", "csv", "ppt", "pptx", "odp", "epub"],
        FileKind.Pictures => ["jpg", "jpeg", "png", "gif", "bmp", "webp", "heic", "heif", "avif", "tif", "tiff", "svg"],
        FileKind.Videos => ["mp4", "mkv", "mov", "avi", "wmv", "webm", "m4v", "mpg", "mpeg"],
        FileKind.Music => ["mp3", "wav", "flac", "aac", "m4a", "ogg", "opus", "wma"],
        FileKind.Archives => ["zip", "7z", "rar", "tar", "gz", "bz2", "xz", "zst", "cab"],
        FileKind.Installers => ["exe", "msi", "msix", "msixbundle", "appx", "appxbundle"],
        FileKind.CameraFiles => ["jpg", "jpeg", "heic", "heif", "png", "dng", "cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "raf", "orf", "rw2", "pef", "srw", "x3f", "3fr", "iiq", "mp4", "mov", "m4v", "avi", "mts", "m2ts", "3gp", "mpg"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown file kind."),
    };
}
