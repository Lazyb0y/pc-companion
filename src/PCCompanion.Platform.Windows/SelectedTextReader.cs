using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reads the text selected in the element that has the keyboard focus, and tells whether the
/// element under a point is a text box, with UI Automation, the interface screen readers use. It
/// runs on a thread of its own, in COM's multithreaded apartment and with no windows, since its
/// calls wait for the app, and keeps its UI Automation object between requests. Requests run one at
/// a time, reads before questions about a point. Check the app with
/// <see cref="SelectedText.CheckWindow"/> first. Create, use and dispose it on one thread.
/// </summary>
/// <remarks>
/// Only the focused element, or the nearest element around it with text, is asked: whether it's a
/// password box, its selection and the selection's text, and its last line's place. Never the
/// document's whole text or anything else, which can keep a browser busy for seconds. A question
/// about a point reads only what kind of element is there, never its text.
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

    // Reads and the changes that follow a paste come first, then questions about the pointer.
    private readonly ConcurrentQueue<Request> _first = new();
    private readonly ConcurrentQueue<Request> _questions = new();
    private readonly SemaphoreSlim _waiting = new(0);
    private volatile bool _ending;
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
    /// <param name="maxLength">The most characters read; more gives <see cref="SelectedTextStatus.TooLong"/>.</param>
    /// <param name="clickedAt">
    /// Where the text will be pasted, if anywhere. When the selection is all of the text of the box
    /// there, the read gives <see cref="SelectedTextStatus.WholeBoxSelected"/>.
    /// </param>
    public Task<SelectedTextResult> ReadAsync(TimeSpan retryFor, int maxLength = SelectedText.MaxLength, Point? clickedAt = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        var request = new ReadRequest(retryFor, maxLength, clickedAt);
        _first.Enqueue(request);
        _waiting.Release();
        return request.Answer.Task;
    }

    /// <summary>
    /// Puts the text cursor of the text box under <paramref name="point"/>, in physical pixels,
    /// where the point is, with the box's text pattern (<c>RangeFromPoint</c> and <c>Select</c>), as
    /// for a rich edit box, whose own message for it can't be sent from another program. Runs
    /// before questions about a point. The task gives false when the box couldn't be found or
    /// refused, and never fails.
    /// </summary>
    public Task<bool> PlaceCursorAsync(Point point) => Change(point, focus: false);

    /// <summary>
    /// Gives the keyboard focus to the text box under <paramref name="point"/>, in physical pixels
    /// (<c>SetFocus</c>). Runs before questions about a point. The task gives false when the box
    /// couldn't be found or refused, and never fails.
    /// </summary>
    public Task<bool> FocusAsync(Point point) => Change(point, focus: true);

    private Task<bool> Change(Point point, bool focus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = new ChangeRequest(point, focus);
        _first.Enqueue(request);
        _waiting.Release();
        return request.Answer.Task;
    }

    /// <summary>
    /// Tells whether the element under <paramref name="point"/>, in physical pixels, is a text box,
    /// on the reader's thread, after any waiting read; see <see cref="TextBoxes"/>. A browser that
    /// hasn't turned on its accessibility support yet is asked again, as for a read, until
    /// <paramref name="retryFor"/> has passed. The task never fails.
    /// </summary>
    public Task<TextBoxAnswer> AskAsync(Point point, TimeSpan retryFor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var request = new PointRequest(point, retryFor);
        _questions.Enqueue(request);
        _waiting.Release();
        return request.Answer.Task;
    }

    /// <summary>Ends the reader's thread after the requests it has, without waiting for it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ending = true;
        _waiting.Release();
    }

    private void Work()
    {
        IUIAutomation2? automation = null;
        try
        {
            while (true)
            {
                _waiting.Wait();
                if (_first.TryDequeue(out var request) || _questions.TryDequeue(out request))
                {
                    request.Run(ref automation);
                }
                else if (_ending)
                {
                    break;
                }
            }
        }
        finally
        {
            if (automation is not null)
            {
                Marshal.ReleaseComObject(automation);
            }

            _waiting.Dispose();
        }
    }

    private static SelectedTextResult Read(ref IUIAutomation2? automation, ReadRequest request)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            automation ??= Create();
            while (true)
            {
                var result = ReadOnce(automation, request, out var browserNotReady);
                if (!browserNotReady || clock.Elapsed + RetryInterval > request.RetryFor)
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

    private static TextBoxAnswer Ask(ref IUIAutomation2? automation, PointRequest request)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            automation ??= Create();
            while (true)
            {
                var answer = AskOnce(automation, request.Point, out var browserNotReady);
                if (!browserNotReady || clock.Elapsed + RetryInterval > request.RetryFor)
                {
                    return answer;
                }

                Thread.Sleep(RetryInterval);
            }
        }
        catch (COMException exception) when (exception.HResult == unchecked((int)PInvoke.UIA_E_TIMEOUT))
        {
            return new TextBoxAnswer(TextBoxStatus.TimedOut);
        }
        catch (Exception exception)
        {
            return new TextBoxAnswer(TextBoxStatus.NotShared, ErrorCode: exception.HResult);
        }
    }

    private static bool Change(ref IUIAutomation2? automation, ChangeRequest request)
    {
        try
        {
            automation ??= Create();
            using var held = new HeldObjects();
            if (FocusableAt(automation, request.Point, held) is not { } element)
            {
                return false;
            }

            if (request.Focus)
            {
                element.SetFocus();
                return true;
            }

            if (held.Add(element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId)) is not IUIAutomationTextPattern pattern)
            {
                return false;
            }

            held.Add(pattern.RangeFromPoint(request.Point)).Select();
            return true;
        }
        catch (Exception)
        {
            // The app ended, refused or didn't answer in time; the caller says nothing was pasted.
            return false;
        }
    }

    private static IUIAutomation2 Create()
    {
        var automation = (IUIAutomation2)new CUIAutomation8();
        automation.ConnectionTimeout = (uint)ConnectionTimeout.TotalMilliseconds;
        automation.TransactionTimeout = (uint)RequestTimeout.TotalMilliseconds;
        return automation;
    }

    private static SelectedTextResult ReadOnce(IUIAutomation2 automation, ReadRequest request, out bool browserNotReady)
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

        var result = ReadSelection(pattern, request.MaxLength, held, out var only);
        return result.Status == SelectedTextStatus.Read && request.ClickedAt is { } point && only is not null
            && IsWholeBox(automation, element, pattern, only, point, held)
            ? new SelectedTextResult(SelectedTextStatus.WholeBoxSelected)
            : result;
    }

    // `only` is the selection's one part, or null when it has several.
    private static SelectedTextResult ReadSelection(IUIAutomationTextPattern pattern, int maxLength, HeldObjects held, out IUIAutomationTextRange? only)
    {
        var ranges = held.Add(pattern.GetSelection());
        var count = ranges.Length;
        var text = new StringBuilder();
        IUIAutomationTextRange? last = null;
        for (var index = 0; index < count && text.Length <= maxLength; index++)
        {
            last = held.Add(ranges.GetElement(index));
            var part = Take(last.GetText(maxLength + 1 - text.Length));
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

        only = count == 1 ? last : null;
        if (text.Length == 0)
        {
            return new SelectedTextResult(SelectedTextStatus.NothingSelected);
        }

        return text.Length > maxLength
            ? new SelectedTextResult(SelectedTextStatus.TooLong)
            : new SelectedTextResult(SelectedTextStatus.Read, text.ToString(), LastLine(last!));
    }

    // Whether the selection is all of the text of the box at the clicked point. Only boxes are
    // checked, never documents, whose whole range can be long; the ends are compared without reading
    // any more text.
    private static bool IsWholeBox(IUIAutomation2 automation, IUIAutomationElement textElement, IUIAutomationTextPattern pattern, IUIAutomationTextRange selection, Point point, HeldObjects held)
    {
        try
        {
            if (textElement.CurrentControlType == UIA_CONTROLTYPE_ID.UIA_DocumentControlTypeId
                || FocusableAt(automation, point, held) is not { } box
                || !automation.CompareElements(box, textElement))
            {
                return false;
            }

            var whole = held.Add(pattern.DocumentRange);
            return selection.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, whole, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) == 0
                && selection.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, whole, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) == 0;
        }
        catch (Exception)
        {
            // The check only keeps a box's own text from being pasted into it; the text stays usable.
            return false;
        }
    }

    private static TextBoxAnswer AskOnce(IUIAutomation2 automation, Point point, out bool browserNotReady)
    {
        browserNotReady = false;
        using var held = new HeldObjects();
        var atPoint = held.Add(automation.ElementFromPoint(point));
        if (IsBrowserWithoutText(atPoint))
        {
            browserNotReady = true;
            return new TextBoxAnswer(TextBoxStatus.NotTextBox);
        }

        if (Focusable(automation, atPoint, held) is not { } element)
        {
            return new TextBoxAnswer(TextBoxStatus.NotTextBox);
        }

        var value = held.Add(element.GetCurrentPattern(UIA_PATTERN_ID.UIA_ValuePatternId)) as IUIAutomationValuePattern;
        var facts = new FocusableElement(
            element.CurrentIsEnabled,
            element.CurrentIsPassword,
            element.CurrentControlType == UIA_CONTROLTYPE_ID.UIA_DocumentControlTypeId,
            held.Add(element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId)) is IUIAutomationTextPattern,
            value is null ? null : (bool)value.CurrentIsReadOnly);
        var status = TextBoxes.Classify(facts);
        return new TextBoxAnswer(status, status == TextBoxStatus.TextBox ? (nint)element.CurrentNativeWindowHandle : 0);
    }

    private static IUIAutomationElement? FocusableAt(IUIAutomation2 automation, Point point, HeldObjects held) =>
        Focusable(automation, held.Add(automation.ElementFromPoint(point)), held);

    // The element, or the nearest element above it that takes the keyboard focus.
    private static IUIAutomationElement? Focusable(IUIAutomation2 automation, IUIAutomationElement atPoint, HeldObjects held)
    {
        var walker = held.Add(automation.ControlViewWalker);
        IUIAutomationElement? element = atPoint;
        for (var level = 0; element is not null && level <= TextBoxes.Levels; level++)
        {
            if (element.CurrentIsKeyboardFocusable)
            {
                return element;
            }

            element = held.Add(walker.GetParentElement(element));
        }

        return null;
    }

    // Until a browser built on Chromium has turned on its accessibility support, the focus, or what's
    // under the pointer, is its window's own pane, with no text.
    private static bool IsBrowserWithoutText(IUIAutomationElement element) =>
        element.CurrentControlType == UIA_CONTROLTYPE_ID.UIA_PaneControlTypeId && Take(element.CurrentFrameworkId) == ChromiumFramework;

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

    private abstract class Request
    {
        public abstract void Run(ref IUIAutomation2? automation);
    }

    private sealed class ReadRequest(TimeSpan retryFor, int maxLength, Point? clickedAt) : Request
    {
        public TimeSpan RetryFor { get; } = retryFor;

        public int MaxLength { get; } = maxLength;

        public Point? ClickedAt { get; } = clickedAt;

        public TaskCompletionSource<SelectedTextResult> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Run(ref IUIAutomation2? automation) => Answer.SetResult(Read(ref automation, this));
    }

    private sealed class PointRequest(Point point, TimeSpan retryFor) : Request
    {
        public Point Point { get; } = point;

        public TimeSpan RetryFor { get; } = retryFor;

        public TaskCompletionSource<TextBoxAnswer> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Run(ref IUIAutomation2? automation) => Answer.SetResult(Ask(ref automation, this));
    }

    private sealed class ChangeRequest(Point point, bool focus) : Request
    {
        public Point Point { get; } = point;

        public bool Focus { get; } = focus;

        public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Run(ref IUIAutomation2? automation) => Answer.SetResult(Change(ref automation, this));
    }

    // The app's objects a request holds, released when it ends, so the app can free them.
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
