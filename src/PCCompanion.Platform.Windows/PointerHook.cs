using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Keeps chosen clicks and wheel moves in an area of the screen from the apps under the pointer,
/// with one low-level mouse hook that every feature shares. Windows waits for a low-level hook at
/// every pointer event in every program, and removes one that's slow without telling it, so the
/// hook runs on a thread of its own, is set only while some hook has a claim, and only compares
/// events with the claims there. Windows gives it no input while an app running as administrator
/// is in front, unless this process runs as administrator too. The first hook starts the thread
/// and the last one disposed ends it.
/// </summary>
public sealed class PointerHook : IDisposable
{
    private readonly MessageWindow _window;
    private bool _disposed;

    /// <param name="window">A window on the thread that gets <see cref="Kept"/> and <see cref="Failed"/>.</param>
    /// <exception cref="Win32Exception">The hook's thread couldn't create its window.</exception>
    public PointerHook(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        Registry.Add(this);
    }

    /// <summary>
    /// Raised on the window's thread for each middle-click or wheel notch this hook's claim kept.
    /// The release of a kept middle-click is kept too, without an event. Guard the handler.
    /// </summary>
    public event EventHandler<KeptPointerInputEventArgs>? Kept;

    /// <summary>
    /// Raised on the window's thread when the hook's thread failed. The hook is then removed and
    /// every claim dropped, until a hook claims again. Guard the handler.
    /// </summary>
    public event EventHandler<ThreadExceptionEventArgs>? Failed;

    /// <summary>
    /// Whether the hook is set, as far as this process knows: Windows can remove it without telling.
    /// </summary>
    public static bool IsSet => Registry.IsSet;

    /// <summary>
    /// What the hook did with the middle-click or wheel notch stamped <paramref name="time"/>, as
    /// <see cref="PointerInputEventArgs.Time"/> reports it: kept, let through, or never seen. Raw
    /// input can report an event before the hook has seen it, so ask a moment later.
    /// </summary>
    public static PointerInputOutcome Outcome(uint time) => Registry.Keeper.Outcome(time);

    /// <summary>
    /// Removes the hook and sets it again while some hook has a claim, as after Windows removed it.
    /// </summary>
    public static void SetAgain() => Registry.SetAgain();

    /// <summary>
    /// Keeps <paramref name="claim"/>'s clicks and wheel moves from now on, in place of this hook's
    /// earlier claim; <see langword="null"/> keeps nothing more for it. Call it from any thread.
    /// </summary>
    public void Claim(PointerClaim? claim)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Registry.Claim(this, claim);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Registry.Remove(this);
    }

    private void Report(KeptPointerInputEventArgs input) => _window.BeginInvoke(() =>
    {
        if (!_disposed)
        {
            Kept?.Invoke(this, input);
        }
    });

    private void ReportFailure(Exception exception) => _window.BeginInvoke(() =>
    {
        if (!_disposed)
        {
            Failed?.Invoke(this, new ThreadExceptionEventArgs(exception));
        }
    });

    // The process's one hook, its thread and the hooks that share it.
    private static class Registry
    {
        private const string WindowClassName = "PCCompanion.PointerHook";

        private static readonly Lock Gate = new();

        // Replaced, never changed, so the hook reads a whole set on its thread without a lock.
        private static Claims s_claims = Claims.None;
        private static PointerHook[] s_hooks = [];
        private static HookThread? s_thread;
        private static volatile bool s_isSet;

        // Lives as long as the process, so what the hook did stays known while its thread restarts.
        public static PointerKeeper Keeper { get; } = new();

        public static bool IsSet => s_isSet;

        public static void Add(PointerHook hook)
        {
            lock (Gate)
            {
                s_thread ??= new HookThread();

                s_hooks = [.. s_hooks, hook];
            }
        }

        public static void Remove(PointerHook hook)
        {
            HookThread? ending = null;
            lock (Gate)
            {
                s_hooks = Array.FindAll(s_hooks, other => other != hook);
                SetClaims(s_claims.Without(hook));
                if (s_hooks.Length == 0)
                {
                    ending = s_thread;
                    s_thread = null;
                }
            }

            ending?.End();
        }

        public static void Claim(PointerHook hook, PointerClaim? claim)
        {
            lock (Gate)
            {
                SetClaims(claim is null ? s_claims.Without(hook) : s_claims.With(hook, claim));
            }
        }

        public static void SetAgain()
        {
            lock (Gate)
            {
                if (s_claims.Count > 0)
                {
                    s_thread?.Request(thread => thread.SetAgain());
                }
            }
        }

        // Called with the gate held. The hook is set when the first claim comes and removed with the last.
        private static void SetClaims(Claims claims)
        {
            var wasClaimed = s_claims.Count > 0;
            Volatile.Write(ref s_claims, claims);
            if (claims.Count > 0 && !wasClaimed)
            {
                s_thread?.Request(thread => thread.Set());
            }
            else if (claims.Count == 0 && wasClaimed)
            {
                s_thread?.Request(thread => thread.Unset());
            }
        }

        // The hook's thread, with a window that takes requests and a message loop that delivers the
        // hook's calls.
        private sealed class HookThread
        {
            private readonly HOOKPROC _procedure;
            private readonly Thread _thread;
            private volatile MessageWindow? _window;
            private UnhookWindowsHookExSafeHandle? _hook;
            private Exception? _startFailure;

            public HookThread()
            {
                _procedure = OnHook;

                // A background thread, so one that doesn't end doesn't keep the process running.
                using var ready = new ManualResetEventSlim();
                _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Pointer hook" };
                _thread.Start();
                ready.Wait();
                if (_startFailure is not null)
                {
                    throw _startFailure;
                }
            }

            public void Request(Action<HookThread> request) => _window?.BeginInvoke(() => Guard(() => request(this)));

            public void End()
            {
                Request(thread =>
                {
                    thread.Unset();
                    MessageLoop.Quit();
                });
                _thread.Join(TimeSpan.FromSeconds(2));
            }

            public void Set()
            {
                if (_hook is { IsInvalid: false } || Volatile.Read(ref s_claims).Count == 0)
                {
                    return;
                }

                _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _procedure, PInvoke.GetModuleHandle((string?)null), 0);
                if (_hook.IsInvalid)
                {
                    throw new Win32Exception();
                }

                s_isSet = true;
            }

            public void Unset()
            {
                _hook?.Dispose();
                _hook = null;
                s_isSet = false;
            }

            public void SetAgain()
            {
                Unset();
                Set();
            }

            private void Run(ManualResetEventSlim ready)
            {
                MessageWindow window;
                try
                {
                    window = new MessageWindow(WindowClassName);
                }
                catch (Exception exception)
                {
                    _startFailure = exception;
                    ready.Set();
                    return;
                }

                using (window)
                {
                    _window = window;
                    ready.Set();
                    try
                    {
                        MessageLoop.Run();
                    }
                    catch (Exception exception)
                    {
                        Fail(exception);
                    }
                    finally
                    {
                        Unset();
                        _window = null;
                        GC.KeepAlive(_procedure);
                    }
                }
            }

            // A failure on the thread stops the hook and is reported to every hook, which keep nothing more.
            private void Guard(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }

            private void Fail(Exception exception)
            {
                Unset();
                PointerHook[] hooks;
                lock (Gate)
                {
                    hooks = s_hooks;
                    Volatile.Write(ref s_claims, Claims.None);
                }

                foreach (var hook in hooks)
                {
                    hook.ReportFailure(exception);
                }
            }

            // Windows waits for this at every pointer event, so it only compares and posts.
            private unsafe LRESULT OnHook(int code, WPARAM wParam, LPARAM lParam)
            {
                if (code >= 0 && Input((uint)wParam.Value) is { } input)
                {
                    var keep = false;
                    try
                    {
                        keep = Decide(input, (MSLLHOOKSTRUCT*)lParam.Value);
                    }
                    catch (Exception exception)
                    {
                        Request(thread => thread.Fail(exception));
                    }

                    if (keep)
                    {
                        return (LRESULT)1;
                    }
                }

                return PInvoke.CallNextHookEx(null, code, wParam, lParam);
            }

            private static unsafe bool Decide(PointerInput input, MSLLHOOKSTRUCT* data)
            {
                var claims = Volatile.Read(ref s_claims);
                var point = new Point(data->pt.X, data->pt.Y);
                var keys = HeldKeys();
                var decision = Keeper.Decide(claims.List, input, point, keys, data->time);
                if (decision.Claim >= 0)
                {
                    var isWheel = input == PointerInput.Wheel;
                    var trigger = new PointerTrigger(isWheel ? PointerButton.Wheel : PointerButton.Middle, keys);
                    var wheelDelta = isWheel ? unchecked((short)(data->mouseData >> 16)) : (short)0;
                    claims.Owners[decision.Claim].Report(new KeptPointerInputEventArgs(trigger, point, wheelDelta, data->time));
                }

                return decision.Keep;
            }

            private static PointerInput? Input(uint message) => message switch
            {
                PInvoke.WM_MBUTTONDOWN => PointerInput.MiddleDown,
                PInvoke.WM_MBUTTONUP => PointerInput.MiddleUp,
                PInvoke.WM_MOUSEWHEEL => PointerInput.Wheel,
                _ => null,
            };

            private static PointerKeys HeldKeys() =>
                (InputState.IsModifierHeld(ModifierKey.Shift) ? PointerKeys.Shift : PointerKeys.None)
                | (InputState.IsModifierHeld(ModifierKey.Control) ? PointerKeys.Control : PointerKeys.None)
                | (InputState.IsModifierHeld(ModifierKey.Alt) ? PointerKeys.Alt : PointerKeys.None)
                | (InputState.IsModifierHeld(ModifierKey.Windows) ? PointerKeys.Windows : PointerKeys.None);
        }

        // Each hook's claim, in the order they came, so an earlier claim wins where two overlap.
        private sealed class Claims(PointerHook[] owners, PointerClaim[] list)
        {
            public static readonly Claims None = new([], []);

            public PointerHook[] Owners { get; } = owners;

            public PointerClaim[] List { get; } = list;

            public int Count => List.Length;

            public Claims With(PointerHook hook, PointerClaim claim)
            {
                var index = Array.IndexOf(Owners, hook);
                if (index < 0)
                {
                    return new Claims([.. Owners, hook], [.. List, claim]);
                }

                var list = (PointerClaim[])List.Clone();
                list[index] = claim;
                return new Claims(Owners, list);
            }

            public Claims Without(PointerHook hook)
            {
                var index = Array.IndexOf(Owners, hook);
                return index < 0
                    ? this
                    : new Claims([.. Owners[..index], .. Owners[(index + 1)..]], [.. List[..index], .. List[(index + 1)..]]);
            }
        }
    }
}

/// <summary>A middle-click or wheel notch that a <see cref="PointerHook"/>'s claim kept from the apps.</summary>
/// <param name="trigger">The click or wheel move, with the keys held.</param>
/// <param name="point">Where the pointer was, in physical pixels.</param>
/// <param name="wheelDelta">How far the wheel turned, positive away from the user, 120 a notch; 0 for a click.</param>
/// <param name="time">When the input happened, as Windows stamps it.</param>
public sealed class KeptPointerInputEventArgs(PointerTrigger trigger, Point point, short wheelDelta, uint time) : EventArgs
{
    public PointerTrigger Trigger { get; } = trigger;

    public Point Point { get; } = point;

    public short WheelDelta { get; } = wheelDelta;

    public uint Time { get; } = time;
}
