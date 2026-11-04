using System;
using System.IO;

namespace PCCompanion.Modules.Files;

/// <summary>What the conditions read of a file, from its folder's listing.</summary>
/// <param name="Name">The file's name, with its extension.</param>
/// <param name="Size">The size in bytes.</param>
public sealed record FileFacts(string Name, long Size, DateTimeOffset Created, DateTimeOffset Modified)
{
    /// <summary>The name without the extension.</summary>
    public string BaseName => Path.GetFileNameWithoutExtension(Name);

    /// <summary>The extension without the dot, or an empty text for a file without one.</summary>
    public string Extension => Path.GetExtension(Name) is { Length: > 1 } extension ? extension[1..] : string.Empty;
}
