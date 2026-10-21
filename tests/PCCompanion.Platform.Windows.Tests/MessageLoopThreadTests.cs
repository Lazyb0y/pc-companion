using System;
using System.Linq;
using System.Threading;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test's threads create hidden windows with a class name no other test uses, and record what
// they do instead of doing anything.
public sealed class MessageLoopThreadTests
{
    private readonly Recorder _recorder = new();

    [Fact]
    public void TheThreadIsABackgroundStaThread()
    {
        var thread = Start("first", "red");
        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("first started")));

        StopAndJoin(thread);

        Assert.Equal((ApartmentState.STA, true, "Test thread first"), thread.OpenedOn);
    }

    [Fact]
    public void TheFirstSettingsApplyThenOnlyChangedOnes()
    {
        var thread = Start("first", "red");
        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("first started")));

        thread.Update("red");
        thread.Update("blue");
        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("first apply blue")));
        StopAndJoin(thread);

        Assert.Equal(["first open", "first apply red", "first started", "first apply blue", "first stopping", "first dispose", "first stopped"], _recorder.Events);
        Assert.False(thread.HasFailed);
    }

    [Fact]
    public void ANewThreadWaitsForTheOneBefore()
    {
        // Two windows of one class can't exist at once, so the second would fail without waiting.
        var className = UniqueClassName();
        var first = Start("first", "red", className);
        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("first started")));
        first.Stop();

        var second = Start("second", "blue", className, first);

        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("second started")));
        var events = _recorder.Events.ToList();
        Assert.True(events.IndexOf("first stopped") < events.IndexOf("second open"));
        StopAndJoin(second);
        Assert.False(second.HasFailed);
    }

    [Fact]
    public void AThreadStoppedBeforeItHasAWindowOpensNothing()
    {
        var first = Start("first", "red");
        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("first started")));
        var second = Start("second", "blue", previous: first);

        // The second thread waits for the first, so it's asked to stop before it creates its window.
        second.Stop();
        StopAndJoin(first);

        Assert.True(second.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(["second stopped"], _recorder.Events.Where(text => text.StartsWith("second", StringComparison.Ordinal)));
        Assert.False(second.HasFailed);
    }

    [Fact]
    public void AFailureEndsTheThreadAndLetsGoOfWhatItOpened()
    {
        var thread = Start("first", "red");
        Assert.True(_recorder.WaitUntil(recorder => recorder.Events.Contains("first started")));

        thread.Update(RecordingThread.Failing);

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(thread.HasFailed);
        Assert.Equal(["first open", "first apply red", "first started", "first failed", "first dispose", "first stopped"], _recorder.Events);
    }

    [Fact]
    public void AFailureAsTheThreadStartsEndsIt()
    {
        var thread = Start("first", RecordingThread.Failing);

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(thread.HasFailed);
        Assert.Equal(["first open", "first dispose", "first failed", "first stopped"], _recorder.Events);
    }

    private static void StopAndJoin(RecordingThread thread)
    {
        thread.Stop();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    }

    private RecordingThread Start(string label, string settings, string? className = null, RecordingThread? previous = null) =>
        new(label, settings, className ?? UniqueClassName(), _recorder, previous);

    private sealed class Recorder : RecordingFake<Recorder>
    {
        public void Add(string happened) => Record(happened);
    }

    // Records each step as "<label> <step>"; applying the settings Failing throws.
    private sealed class RecordingThread : MessageLoopThread<string>
    {
        public const string Failing = "fail";

        private readonly string _label;
        private readonly Recorder _recorder;

        public RecordingThread(string label, string settings, string className, Recorder recorder, RecordingThread? previous)
            : base("Test thread " + label, className, settings, previous)
        {
            _label = label;
            _recorder = recorder;
            Start();
        }

        /// <summary>The apartment, whether it's a background thread, and the name of the thread that opened.</summary>
        public (ApartmentState Apartment, bool IsBackground, string? Name) OpenedOn { get; private set; }

        protected override IDisposable? Open(MessageWindow window)
        {
            var thread = Thread.CurrentThread;
            OpenedOn = (thread.GetApartmentState(), thread.IsBackground, thread.Name);
            Add("open");
            return new Opened(() => Add("dispose"));
        }

        protected override void Apply(string settings)
        {
            if (settings == Failing)
            {
                throw new InvalidOperationException("The test fails the thread.");
            }

            Add($"apply {settings}");
        }

        protected override void OnStopping() => Add("stopping");

        protected override void OnStarted() => Add("started");

        protected override void OnStopped() => Add("stopped");

        protected override void OnFailed(Exception exception) => Add("failed");

        private void Add(string step) => _recorder.Add($"{_label} {step}");
    }

    private sealed class Opened(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
