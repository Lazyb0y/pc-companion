using System;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Why Keep displays on ended.
/// </summary>
public enum KeepDisplaysOnEnd
{
    PointerLeft,
    ActionChanged,
    SessionLocked,
    HotCornersStopped,
}

/// <summary>
/// Runs Keep displays on: keeps the displays on while the pointer stays in the corner that
/// triggered it. All calls must come from one thread. See "Keep displays on" in
/// docs/features/input/hot-corners.md.
/// </summary>
/// <param name="keepOn">
/// Starts keeping the displays on and returns what stops it, or <see langword="null"/> when
/// Windows refused.
/// </param>
public sealed partial class DisplayKeeper(ILogger logger, Func<IDisposable?> keepOn)
{
    private IDisposable? _keeping;
    private ScreenCorner _corner;

    public bool IsKeepingOn => _keeping is not null;

    /// <summary>
    /// Starts for the corner that triggered, or goes on when the displays are already kept on.
    /// Returns false when Windows refused.
    /// </summary>
    public bool Start(ScreenCorner corner)
    {
        _corner = corner;
        _keeping ??= keepOn();
        return _keeping is not null;
    }

    /// <summary>
    /// Ends unless the pointer is still in the corner and the corner still has this action. Call
    /// after every pointer event and every change to the settings or the displays.
    /// </summary>
    /// <param name="pointerCorner">The corner the pointer is in, or <see langword="null"/>.</param>
    public void Check(ScreenCorner? pointerCorner, HotCornersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_keeping is null)
        {
            return;
        }

        if (settings.For(_corner).Action != HotCornerAction.KeepDisplaysOn)
        {
            End(KeepDisplaysOnEnd.ActionChanged);
        }
        else if (pointerCorner != _corner)
        {
            End(KeepDisplaysOnEnd.PointerLeft);
        }
    }

    /// <summary>
    /// Ends now, for example because the session was locked. Does nothing when the displays aren't kept on.
    /// </summary>
    public void End(KeepDisplaysOnEnd reason)
    {
        if (_keeping is not { } keeping)
        {
            return;
        }

        _keeping = null;
        keeping.Dispose();
        LogEnded(logger, reason);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keep displays on ended: {Reason}.")]
    private static partial void LogEnded(ILogger logger, KeepDisplaysOnEnd reason);
}
