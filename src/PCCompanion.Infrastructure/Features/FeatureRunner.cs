using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;

namespace PCCompanion.Infrastructure.Features;

/// <summary>
/// Passes each feature its section of the settings, the PC Companion icon's menu its items, and
/// each action that a shortcut, mouse button or launcher runs to the feature that offers it, keeping
/// features apart: one that fails is stopped for the rest of the session and the others keep
/// running. Call it on the host's UI thread.
/// </summary>
public sealed partial class FeatureRunner(IEnumerable<IFeature> features, ILogger<FeatureRunner> logger) : IDisposable
{
    private readonly IFeature[] _features = [.. features];
    private readonly HashSet<IFeature> _stopped = [];

    // The features whose sections turned them on when the settings were last applied.
    private readonly HashSet<IFeature> _on = [];

    // The items of the menu that opened last, by their menu IDs.
    private readonly Dictionary<int, MenuItemOwner> _menuItems = [];
    private bool _disposed;

    /// <summary>Raised after <see cref="Apply"/> applied the settings to every feature.</summary>
    public event EventHandler? Applied;

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

                JsonElement? section = settings.Features.TryGetValue(feature.Info.Id, out var value) ? value : null;
                if (FeatureSettings.IsEnabled(section))
                {
                    _on.Add(feature);
                }
                else
                {
                    _on.Remove(feature);
                }

                feature.Apply(section);
            }
            catch (Exception exception)
            {
                Stop(feature, exception);
            }
        }

        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether a running feature offers <paramref name="action"/> and can run it now.</summary>
    public bool CanRun(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Offering(action) is not { } owner)
        {
            return false;
        }

        try
        {
            return owner.Actions.CanRun(action);
        }
        catch (Exception exception)
        {
            Stop(owner.Feature, exception);
            return false;
        }
    }

    /// <summary>
    /// Whether a running feature offers <paramref name="action"/> and can run it now, and it doesn't
    /// act on the window in front, so a launcher or the command can run it.
    /// </summary>
    public bool CanRunFromLauncher(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Offering(action) is not { } owner)
        {
            return false;
        }

        try
        {
            return !owner.Actions.OnWindowInFront(action) && owner.Actions.CanRun(action);
        }
        catch (Exception exception)
        {
            Stop(owner.Feature, exception);
            return false;
        }
    }

    /// <summary>
    /// Whether the feature with the ID <paramref name="featureId"/> runs: its section turned it on,
    /// it's supported on this PC and it hasn't failed.
    /// </summary>
    public bool IsRunning(string featureId)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _features.Any(feature => feature.Info.Id == featureId && _on.Contains(feature) && !_stopped.Contains(feature));
    }

    /// <summary>
    /// Runs <paramref name="action"/> through the running feature that offers it. Returns false
    /// when no running feature does, as after the feature was turned off.
    /// </summary>
    public bool Run(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Offering(action) is not { } owner)
        {
            LogActionOfNoFeature(logger, action.FeatureId, action.ActionId);
            return false;
        }

        LogActionRun(logger, action.FeatureId, action.ActionId);
        try
        {
            owner.Actions.Run(action);
        }
        catch (Exception exception)
        {
            Stop(owner.Feature, exception);
        }

        return true;
    }

    /// <summary>
    /// The items the running features add to the PC Companion icon's menu, in the features' order,
    /// each that can be chosen with a menu ID of its own, from <paramref name="firstId"/> on, which
    /// <see cref="ChooseMenuItem"/> takes. Call it each time the menu opens.
    /// </summary>
    public IReadOnlyList<HostMenuItem> GetMenuItems(int firstId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _menuItems.Clear();
        var nextId = firstId;
        var items = new List<HostMenuItem>();
        foreach (var feature in _features.Where(feature => !_stopped.Contains(feature)).ToArray())
        {
            if (feature is not IHostMenuItems menu)
            {
                continue;
            }

            var owned = new Dictionary<int, MenuItemOwner>();
            try
            {
                items.AddRange(Renumber(menu.GetMenuItems(), new MenuItemOwner(feature, menu, 0), owned, ref nextId));
            }
            catch (Exception exception)
            {
                Stop(feature, exception);
                continue;
            }

            foreach (var (id, owner) in owned)
            {
                _menuItems.Add(id, owner);
            }
        }

        return items;
    }

    /// <summary>
    /// Passes the item with the menu ID <paramref name="id"/>, from the menu that opened last, to
    /// its feature. Returns false when it isn't a feature's item.
    /// </summary>
    public bool ChooseMenuItem(int id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_menuItems.TryGetValue(id, out var owner))
        {
            return false;
        }

        _menuItems.Clear();

        // The settings can change while the menu is open, which runs the message loop, and stop the feature.
        if (_stopped.Contains(owner.Feature))
        {
            LogMenuItemOfStoppedFeature(logger, owner.Feature.Info.Id);
            return true;
        }

        LogMenuItemChosen(logger, owner.Feature.Info.Id, owner.ItemId);
        try
        {
            owner.Menu.ChooseMenuItem(owner.ItemId);
        }
        catch (Exception exception)
        {
            Stop(owner.Feature, exception);
        }

        return true;
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

    // The running feature with the action's feature ID, when it offers actions.
    private (IFeature Feature, IFeatureActions Actions)? Offering(FeatureAction action) =>
        _features.FirstOrDefault(feature => feature.Info.Id == action.FeatureId && !_stopped.Contains(feature)) is { } feature
        && feature is IFeatureActions actions
            ? (feature, actions)
            : null;

    private void Stop(IFeature feature, Exception exception)
    {
        LogFailed(logger, exception, feature.Info.Id);
        _stopped.Add(feature);
        Dispose(feature);
    }

    // The feature's items with menu IDs from nextId on, each recorded in owned. An item without an
    // ID of its own can't be chosen, and shows dimmed.
    private static List<HostMenuItem> Renumber(IReadOnlyList<HostMenuItem> items, MenuItemOwner owner, Dictionary<int, MenuItemOwner> owned, ref int nextId)
    {
        var renumbered = new List<HostMenuItem>(items.Count);
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                renumbered.Add(item);
            }
            else if (item.Items is { } submenuItems)
            {
                renumbered.Add(HostMenuItem.Submenu(item.Text, Renumber(submenuItems, owner, owned, ref nextId)) with { IsEnabled = item.IsEnabled });
            }
            else if (item.Id == 0)
            {
                renumbered.Add(item with { IsEnabled = false });
            }
            else
            {
                var id = nextId++;
                owned.Add(id, owner with { ItemId = item.Id });
                renumbered.Add(item with { Id = id });
            }
        }

        return renumbered;
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature's menu item {ItemId} was chosen.")]
    private static partial void LogMenuItemChosen(ILogger logger, string featureId, int itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature's menu item was chosen after the feature stopped, so it does nothing.")]
    private static partial void LogMenuItemOfStoppedFeature(ILogger logger, string featureId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature's action {ActionId} runs.")]
    private static partial void LogActionRun(ILogger logger, string featureId, string actionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature's action {ActionId} does nothing, since no running feature offers it.")]
    private static partial void LogActionOfNoFeature(ILogger logger, string featureId, string actionId);

    // A feature's item in the menu, with the feature's own ID for it.
    private sealed record MenuItemOwner(IFeature Feature, IHostMenuItems Menu, int ItemId);
}
