using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCCompanion.Core;

/// <summary>
/// A program and its arguments, as a host running as administrator hands them to a copy of itself
/// without admin rights, through a pipe: Windows starts that copy with a command line of 1,024
/// characters at most, and arguments can hold several thousand characters of copied text. See
/// <see cref="HostProcess.RunProgramArgument"/>.
/// </summary>
/// <param name="Program">The full path of the program file.</param>
/// <param name="Arguments">The arguments, each passed to the program on its own.</param>
public sealed record ProgramToStart(string Program, IReadOnlyList<string> Arguments)
{
    /// <summary>The longest hand-off a copy reads: the longest path, and arguments with room for copied text.</summary>
    public const int MaximumLength = 256 * 1024;

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, ProgramToStartJsonContext.Default.ProgramToStart);

    /// <summary>
    /// Reads a hand-off, or returns <see langword="null"/> when it can't be read, for example from a
    /// program that isn't PC Companion.
    /// </summary>
    public static ProgramToStart? FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            // The reader checks nullable annotations on properties but not on a list's items.
            return JsonSerializer.Deserialize(json, ProgramToStartJsonContext.Default.ProgramToStart) is { } program
                && program.Program.Length > 0
                && program.Arguments.All(argument => argument is not null)
                    ? program
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ProgramToStart))]
internal sealed partial class ProgramToStartJsonContext : JsonSerializerContext;
