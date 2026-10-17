using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;

namespace PCCompanion.Infrastructure.Features;

/// <summary>
/// Passes each feature its section of the settings, keeping features apart: one that fails is
/// stopped for the rest of the session and the others keep running. Call it on the host's UI thread.
/// </summary>
public sealed partial class FeatureRunner(IEnumerable<IFeature> features, ILogger<FeatureRunner> logger) : IDisposable
{
    private readonly IFeature[] _features = [.. features];
    private readonly HashSet<IFeature> _stopped = [];
    private bool _disposed;

    /// <summary>
    /// Starts, stops or reconfigures every feature that can run, at startup and after every
    /// settings change.
    /// </summary>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var feature in _features.Where(feature => !_stopped.Contains(feature)))
        {
            try
            {
                if (feature.Support is { IsSupported: false } support)
                {
                    LogNotSupported(logger, feature.Info.Id, support.ReasonKey);
                    _stopped.Add(feature);
                    continue;
                }

                feature.Apply(settings.Features.TryGetValue(feature.Info.Id, out var section) ? section : null);
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception, feature.Info.Id);
                _stopped.Add(feature);
                Dispose(feature);
            }
        }
    }

    /// <summary>
    /// Stops every feature.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var feature in _features.Where(feature => !_stopped.Contains(feature)))
        {
            Dispose(feature);
        }
    }

    private void Dispose(IFeature feature)
    {
        try
        {
            feature.Dispose();
        }
        catch (Exception exception)
        {
            LogStopFailed(logger, exception, feature.Info.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature isn't supported on this PC ({ReasonKey}), so it isn't started.")]
    private static partial void LogNotSupported(ILogger logger, string featureId, string? reasonKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "The {FeatureId} feature failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string featureId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The {FeatureId} feature failed to stop.")]
    private static partial void LogStopFailed(ILogger logger, Exception exception, string featureId);
}
