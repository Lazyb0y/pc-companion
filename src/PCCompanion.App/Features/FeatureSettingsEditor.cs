using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Settings;

namespace PCCompanion.App.Features;

/// <summary>
/// Reads and changes the features' sections of the settings. Every change is saved right away.
/// </summary>
public sealed partial class FeatureSettingsEditor(AppSettings settings, SettingsStore store, ILogger<FeatureSettingsEditor> logger)
{
    /// <summary>
    /// Reads a feature's section with <paramref name="read"/>, or its defaults when the section
    /// can't be read; saving a change then replaces the section.
    /// </summary>
    public T Read<T>(string featureId, Func<JsonElement?, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        try
        {
            return read(Section(featureId));
        }
        catch (JsonException exception)
        {
            LogUnreadableSection(logger, exception, featureId);
            return read(null);
        }
    }

    public bool IsEnabled(string featureId) => FeatureSettings.IsEnabled(Section(featureId));

    /// <summary>
    /// Turns a feature on or off. Returns false when the change couldn't be saved.
    /// </summary>
    public bool SetEnabled(string featureId, bool enabled) =>
        Save(featureId, FeatureSettings.SetEnabled(Section(featureId), enabled));

    /// <summary>
    /// Replaces a feature's section. Returns false when the change couldn't be saved.
    /// </summary>
    public bool Save(string featureId, JsonElement section)
    {
        settings.Features[featureId] = section;
        try
        {
            store.Save(settings);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(logger, exception, featureId);
            return false;
        }
    }

    private JsonElement? Section(string featureId) =>
        settings.Features.TryGetValue(featureId, out var section) ? section : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {FeatureId} settings can't be read, so the defaults are shown.")]
    private static partial void LogUnreadableSection(ILogger logger, JsonException exception, string featureId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't save the {FeatureId} settings.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string featureId);
}
