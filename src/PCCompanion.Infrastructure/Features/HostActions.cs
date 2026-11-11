using System;
using PCCompanion.Core.Features;

namespace PCCompanion.Infrastructure.Features;

/// <summary>
/// Gives the feature that runs actions from shortcuts and mouse buttons the running features'
/// actions, through <see cref="FeatureRunner"/>. The host attaches its feature runner and its UI
/// thread once its window exists, since the features are created before the runner; until then
/// no action can run.
/// </summary>
public sealed class HostActions : IHostActions
{
    private volatile Connection? _connection;

    public event EventHandler? Changed;

    /// <param name="runOnUiThread">Queues an action on the host's UI thread, from any thread.</param>
    public void Attach(FeatureRunner runner, Action<Action> runOnUiThread)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(runOnUiThread);

        _connection = new Connection(runner, runOnUiThread);
        runner.Applied += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        // The settings were applied before the runner was attached.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool CanRun(FeatureAction action) => _connection is { } connection && connection.Runner.CanRun(action);

    public void Run(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_connection is { } connection)
        {
            connection.RunOnUiThread(() => connection.Runner.Run(action));
        }
    }

    private sealed record Connection(FeatureRunner Runner, Action<Action> RunOnUiThread);
}
