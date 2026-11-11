using System;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Pauses PC Companion's shortcuts while the settings app records one, so that pressing one records
/// it instead of running it. Each request pauses them for <see cref="Limit"/> at most, and the
/// settings app asks again while it records, so a settings app that closes or stops never leaves
/// them paused. The settings app asks through a pipe, <see cref="PipeName"/>, with only "pause" or
/// "resume". See "The settings app and the host" in
/// docs/features/input/shortcuts-and-mouse-buttons.md. Call it from any thread.
/// </summary>
/// <param name="pause">Pauses the shortcuts, such as <see cref="HotKey.Pause"/>.</param>
/// <param name="resume">Registers them again, such as <see cref="HotKey.Resume"/>.</param>
public sealed class ShortcutPause(TimeProvider time, Action pause, Action resume) : IDisposable
{
    /// <summary>How long one request pauses the shortcuts.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>The longest request the pipe reads.</summary>
    public const int MaximumRequestLength = 16;

    private static readonly byte[] PauseRequest = "pause"u8.ToArray();
    private static readonly byte[] ResumeRequest = "resume"u8.ToArray();
    private static readonly byte[] Done = "ok"u8.ToArray();

    private readonly Lock _gate = new();
    private ITimer? _timer;
    private bool _paused;

    /// <summary>The name of the pipe for this user session.</summary>
    public static string PipeName => SnapshotPipe.ForSession("PCCompanion.Shortcuts.Pause");

    /// <summary>
    /// Asks the host to pause the shortcuts, or to resume them, for the settings app. Returns
    /// false when the host doesn't answer, as when it isn't running.
    /// </summary>
    public static async Task<bool> AskAsync(bool pause, CancellationToken cancellationToken = default)
    {
        var answer = await RequestPipe.AskAsync(PipeName, pause ? PauseRequest : ResumeRequest, TimeSpan.FromMilliseconds(500), Done.Length, cancellationToken).ConfigureAwait(false);
        return answer is not null && answer.AsSpan().SequenceEqual(Done);
    }

    /// <summary>
    /// Answers a request that came through the pipe: "pause" pauses the shortcuts, or extends the
    /// pause, and "resume" ends it. Anything else changes nothing and gets an empty answer.
    /// </summary>
    public byte[] Answer(byte[] request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AsSpan().SequenceEqual(PauseRequest))
        {
            Pause();
            return Done;
        }

        if (request.AsSpan().SequenceEqual(ResumeRequest))
        {
            Resume();
            return Done;
        }

        return [];
    }

    /// <summary>Pauses the shortcuts for <see cref="Limit"/> from now, or extends the pause.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                _paused = true;
                pause();
            }

            _timer ??= time.CreateTimer(_ => Resume(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(Limit, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Registers the shortcuts again, if they're paused.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_paused)
            {
                _paused = false;
                resume();
            }
        }
    }

    public void Dispose() => Resume();
}
