using System;
using System.Diagnostics;

namespace PCCompanion.Platform.Windows;

/// <summary>A handler that held a watched thread longer than the limit.</summary>
/// <param name="Source">
/// What handled it: the class name of a <see cref="MessageWindow"/>, which names its feature, such
/// as <c>PCCompanion.HotCorners</c>, or <see cref="SlowHandlerWatch.WindowEventSource"/>.
/// </param>
/// <param name="Message">The window message, or the window event, it handled.</param>
public readonly record struct SlowHandler(string Source, uint Message, TimeSpan Elapsed);

/// <summary>
/// Times what a thread's message loop runs, on a thread that called <see cref="Watch"/>: each
/// message a <see cref="MessageWindow"/> handles and each event a <see cref="WinEventHook"/>
/// reports, and reports those that take longer than the limit. Time a handler spends in a modal
/// loop of its own, such as an open menu's, doesn't count; see <see cref="EnterModal"/>. On other
/// threads it does nothing, and while nothing is slow it only reads the clock.
/// </summary>
public static class SlowHandlerWatch
{
    /// <summary>The source of a slow <see cref="WinEventHook"/> report.</summary>
    public const string WindowEventSource = "a window event hook";

    [ThreadStatic]
    private static Action<SlowHandler>? t_report;

    [ThreadStatic]
    private static long t_limit;

    // The time spent in modal loops on this thread, in Stopwatch ticks.
    [ThreadStatic]
    private static long t_modal;

    /// <summary>
    /// Starts reporting, on the calling thread, handlers that take longer than
    /// <paramref name="limit"/>. <paramref name="report"/> runs on this thread, only for those.
    /// </summary>
    public static void Watch(TimeSpan limit, Action<SlowHandler> report)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(report);

        t_limit = (long)(limit.TotalSeconds * Stopwatch.Frequency);
        t_report = report;
    }

    /// <summary>Stops reporting on the calling thread.</summary>
    public static void StopWatching() => t_report = null;

    /// <summary>
    /// Leaves the time until the result is disposed out of the handler that runs now, as while a
    /// modal loop runs, such as an open menu's: what that loop runs is timed by itself.
    /// </summary>
    public static ModalTime EnterModal() => new(t_report is null ? 0 : Stopwatch.GetTimestamp());

    internal static Timing Begin() => t_report is null ? default : new Timing(Stopwatch.GetTimestamp(), t_modal);

    internal static void End(Timing timing, string source, uint message)
    {
        if (timing.Start == 0 || t_report is not { } report)
        {
            return;
        }

        var elapsed = Stopwatch.GetTimestamp() - timing.Start - (t_modal - timing.Modal);
        if (elapsed > t_limit)
        {
            report(new SlowHandler(source, message, TimeSpan.FromSeconds((double)elapsed / Stopwatch.Frequency)));
        }
    }

    internal static void LeaveModal(long start)
    {
        if (start != 0)
        {
            t_modal += Stopwatch.GetTimestamp() - start;
        }
    }

    internal readonly record struct Timing(long Start, long Modal);
}

/// <summary>Time that <see cref="SlowHandlerWatch"/> leaves out until disposed; see <see cref="SlowHandlerWatch.EnterModal"/>.</summary>
public readonly struct ModalTime : IDisposable, IEquatable<ModalTime>
{
    private readonly long _start;

    internal ModalTime(long start) => _start = start;

    public static bool operator ==(ModalTime left, ModalTime right) => left.Equals(right);

    public static bool operator !=(ModalTime left, ModalTime right) => !left.Equals(right);

    public void Dispose() => SlowHandlerWatch.LeaveModal(_start);

    public bool Equals(ModalTime other) => _start == other._start;

    public override bool Equals(object? obj) => obj is ModalTime other && Equals(other);

    public override int GetHashCode() => _start.GetHashCode();
}
