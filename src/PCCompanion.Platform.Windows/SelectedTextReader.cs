using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reads the text selected in the element that has the keyboard focus, with UI Automation, the
/// interface screen readers use. It runs on a thread of its own, in COM's multithreaded apartment
/// and with no windows, since its calls wait for the app, and keeps its UI Automation object
/// between reads. Check the app with <see cref="SelectedText.CheckWindow"/> first. Create, use and
/// dispose it on one thread.
/// </summary>
/// <remarks>
/// Only the focused element, or the nearest element around it with text, is asked: whether it's a
/// password box, its selection and the selection's text, and its last line's place. Never the
/// document's whole text or anything else, which can keep a browser busy for seconds.
/// </remarks>
public sealed class SelectedTextReader : IDisposable
{
    /// <summary>How long a call waits to connect to an app; Windows' default is 2 seconds.</summary>
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a call waits for an app's answer; Windows' default is 20 seconds.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How often a browser that hasn't turned on its accessibility support yet is asked again.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

    // How many elements up from the focus the text is looked for: Edge's PDF viewer has its text one up.
    private const int Levels = 10;

    // Browsers built on Chromium give this framework.
    private const string ChromiumFramework = "Chrome";

    private readonly Channel<Request> _requests = Channel.CreateUnbounded<Request>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private bool _disposed;

    public SelectedTextReader()
    {
        var thread = new Thread(Work) { IsBackground = true, Name = "Selected text reader" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>
    /// Reads the text selected in the element that has the keyboard focus, on the reader's thread,
    /// after any earlier read. A browser built on Chromium turns on its accessibility support when a
    /// program first asks for it; until then it's asked again every <see cref="RetryInterval"/>,
    /// until <paramref name="retryFor"/> has passed. The task never fails: an error of the app's
    /// gives <see cref="SelectedTextStatus.NotShared"/>.
    /// </summary>
    public Task<SelectedTextResult> ReadAsync(TimeSpan retryFor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = new Request(retryFor);
        _requests.Writer.TryWrite(request);
        return request.Answer.Task;
    }

    /// <summary>Ends the reader's thread after the read it's doing, without waiting for it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requests.Writer.TryComplete();
    }

    private void Work()
    {
        IUIAutomation2? automation = null;
        try
        {
            while (_requests.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_requests.Reader.TryRead(out var request))
                {
                    request.Answer.SetResult(Read(ref automation, request.RetryFor));
                }
            }
        }
        finally
        {
            if (automation is not null)
            {
                Marshal.ReleaseComObject(automation);
            }
        }
    }

    private static SelectedTextResult Read(ref IUIAutomation2? automation, TimeSpan retryFor)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            automation ??= Create();
            while (true)
            {
                var result = ReadOnce(automation, out var browserNotReady);
                if (!browserNotReady || clock.Elapsed + RetryInterval > retryFor)
                {
                    return result;
                }

                Thread.Sleep(RetryInterval);
            }
        }
        catch (COMException exception) when (exception.HResult == unchecked((int)PInvoke.UIA_E_TIMEOUT))
        {
            return new SelectedTextResult(SelectedTextStatus.TimedOut);
        }
        catch (Exception exception)
        {
            // The app ended, or its answer isn't what it says or reported an error, which .NET
            // turns into exceptions of many kinds.
            return new SelectedTextResult(SelectedTextStatus.NotShared, ErrorCode: exception.HResult);
        }
    }

    private static IUIAutomation2 Create()
    {
        var automation = (IUIAutomation2)new CUIAutomation8();
        automation.ConnectionTimeout = (uint)ConnectionTimeout.TotalMilliseconds;
        automation.TransactionTimeout = (uint)RequestTimeout.TotalMilliseconds;
        return automation;
    }

    private static SelectedTextResult ReadOnce(IUIAutomation2 automation, out bool browserNotReady)
    {
        browserNotReady = false;
        using var held = new HeldObjects();
        var focused = held.Add(automation.GetFocusedElement());
        if (focused.CurrentIsPassword)
        {
            return new SelectedTextResult(SelectedTextStatus.Password);
        }

        var walker = held.Add(automation.ControlViewWalker);
        IUIAutomationElement? element = focused;
        IUIAutomationTextPattern? pattern = null;
        for (var level = 0; element is not null && level <= Levels; level++)
        {
            var found = held.Add(element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId));
            if (found is IUIAutomationTextPattern text)
            {
                pattern = text;
                break;
            }

            element = held.Add(walker.GetParentElement(element));
        }

        if (pattern is null || element is null)
        {
            browserNotReady = IsBrowserWithoutText(focused);
            return new SelectedTextResult(SelectedTextStatus.NotShared);
        }

        if (!ReferenceEquals(element, focused) && element.CurrentIsPassword)
        {
            return new SelectedTextResult(SelectedTextStatus.Password);
        }

        return ReadSelection(pattern, held);
    }

    private static SelectedTextResult ReadSelection(IUIAutomationTextPattern pattern, HeldObjects held)
    {
        var ranges = held.Add(pattern.GetSelection());
        var count = ranges.Length;
        var text = new StringBuilder();
        IUIAutomationTextRange? last = null;
        for (var index = 0; index < count && text.Length <= SelectedText.MaxLength; index++)
        {
            last = held.Add(ranges.GetElement(index));
            var part = Take(last.GetText(SelectedText.MaxLength + 1 - text.Length));
            if (part.Length == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append("\r\n");
            }

            text.Append(part);
        }

        if (text.Length == 0)
        {
            return new SelectedTextResult(SelectedTextStatus.NothingSelected);
        }

        return text.Length > SelectedText.MaxLength
            ? new SelectedTextResult(SelectedTextStatus.TooLong)
            : new SelectedTextResult(SelectedTextStatus.Read, text.ToString(), LastLine(last!));
    }

    // Until a browser built on Chromium has turned on its accessibility support, the focus is its
    // window's own pane, with no text.
    private static bool IsBrowserWithoutText(IUIAutomationElement focused) =>
        focused.CurrentControlType == UIA_CONTROLTYPE_ID.UIA_PaneControlTypeId && Take(focused.CurrentFrameworkId) == ChromiumFramework;

    // Where the menu opens is only a nicety, so an app that can't give it still has its text read.
    private static unsafe System.Drawing.Rectangle? LastLine(IUIAutomationTextRange range)
    {
        SAFEARRAY* array = null;
        try
        {
            array = range.GetBoundingRectangles();
            if (array is null
                || PInvoke.SafeArrayGetLBound(array, 1, out var low).Failed
                || PInvoke.SafeArrayGetUBound(array, 1, out var high).Failed
                || high < low)
            {
                return null;
            }

            void* data;
            if (PInvoke.SafeArrayAccessData(array, &data).Failed)
            {
                return null;
            }

            try
            {
                return SelectedText.LastLine(new ReadOnlySpan<double>(data, high - low + 1));
            }
            finally
            {
                PInvoke.SafeArrayUnaccessData(array);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (array is not null)
            {
                PInvoke.SafeArrayDestroy(array);
            }
        }
    }

    // The caller frees a string UI Automation returns.
    private static unsafe string Take(BSTR text)
    {
        try
        {
            return text.ToString() ?? string.Empty;
        }
        finally
        {
            Marshal.FreeBSTR((nint)text.Value);
        }
    }

    private sealed record Request(TimeSpan RetryFor)
    {
        public TaskCompletionSource<SelectedTextResult> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // The app's objects a read holds, released when it ends, so the app can free them.
    private sealed class HeldObjects : IDisposable
    {
        private readonly HashSet<object> _objects = new(ReferenceEqualityComparer.Instance);

        public T Add<T>(T value)
        {
            if (value is not null)
            {
                _objects.Add(value);
            }

            return value;
        }

        public void Dispose()
        {
            foreach (var value in _objects)
            {
                if (Marshal.IsComObject(value))
                {
                    Marshal.ReleaseComObject(value);
                }
            }
        }
    }
}
