using System.Text.Json;

namespace PCCompanion.Testing;

/// <summary>
/// JSON for tests. Import it with <c>using static PCCompanion.Testing.TestJson;</c>.
/// </summary>
public static class TestJson
{
    /// <summary>A section of the settings file, such as a feature's, from its JSON text.</summary>
    public static JsonElement Section(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
