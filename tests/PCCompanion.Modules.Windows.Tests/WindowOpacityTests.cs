using System;
using System.Collections.Generic;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowOpacityTests
{
    private const nint Window = 10;
    private static readonly LayeredAttributes ConsolesOwn = new(0, 255, LayeredEffects.Alpha);

    private readonly FakeWindowOpacitySystem _system = new();
    private readonly Queue<Action> _work = new();
    private readonly List<Exception> _failures = [];
    private readonly WindowOpacity _opacity;

    public WindowOpacityTests()
    {
        _opacity = new WindowOpacity(_system, _work.Enqueue, action => action(), _failures.Add);
    }

    [Fact]
    public void AStepDownGivesAWindowTheLayeredStyleAfterMarkingIt()
    {
        Assert.Equal(OpacityStep.Changed, _opacity.Step(Window, -1));
        Assert.Equal(1, _system.Marks[Window]);
        Assert.False(_system.IsLayered(Window));

        RunWork();

        Assert.Equal(90, _system.OpacityOf(Window));
        Assert.Equal(90, _opacity.Level(Window));
    }

    [Fact]
    public void TheOpacityStaysBetween20PercentAndWhereItWas()
    {
        Assert.Equal(OpacityStep.AtLimit, _opacity.Step(Window, 1));
        Assert.Empty(_system.Marks);

        _opacity.Step(Window, -20);
        RunWork();
        Assert.Equal(20, _system.OpacityOf(Window));
        Assert.Equal(OpacityStep.AtLimit, _opacity.Step(Window, -1));
    }

    [Fact]
    public void BackAt100PercentTheStyleAndTheMarkGo()
    {
        _opacity.Step(Window, -2);
        RunWork();

        _opacity.Step(Window, 2);
        RunWork();

        Assert.False(_system.IsLayered(Window));
        Assert.Empty(_system.Marks);
        Assert.Null(_opacity.Level(Window));
    }

    [Fact]
    public void StepsThatComeWhileAChangeRunsAddUp()
    {
        _opacity.Step(Window, -1);
        _opacity.Step(Window, -1);
        _opacity.Step(Window, -1);
        Assert.Single(_work);

        RunWork();

        Assert.Equal(70, _system.OpacityOf(Window));
    }

    [Fact]
    public void AWindowThatSetsItsOwnOpacityGetsItBackAndKeepsItsStyle()
    {
        _system.Layered.Add(Window);
        _system.Shown[Window] = ConsolesOwn;

        _opacity.Step(Window, -3);
        RunWork();
        Assert.Equal(70, _system.OpacityOf(Window));

        _opacity.Step(Window, 3);
        RunWork();

        Assert.Equal(ConsolesOwn, _system.Shown[Window]);
        Assert.True(_system.IsLayered(Window));
        Assert.Equal(0, _system.StyleChanges);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void AWindowThatDrawsItselfIsLeftAlone()
    {
        _system.Layered.Add(Window);
        _system.DrawItself.Add(Window);

        Assert.Equal(OpacityStep.DrawsItself, _opacity.Step(Window, -1));
        RunWork();

        Assert.Empty(_system.Broken);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void AWindowWindowsRefusesToMarkIsntChanged()
    {
        _system.RefusesMarks = true;

        Assert.Equal(OpacityStep.Refused, _opacity.Step(Window, -1));

        Assert.Empty(_work);
    }

    [Fact]
    public void PuttingBackShowsTheWindowsAsBeforeAtOnceAndTheStyleGoesAfter()
    {
        _opacity.Step(Window, -5);
        RunWork();

        Assert.Equal(1, _opacity.PutBackAll());

        Assert.Equal(100, _system.OpacityOf(Window));
        Assert.True(_system.IsLayered(Window));
        RunWork();
        Assert.False(_system.IsLayered(Window));
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void AChangeAskedForBeforeTheWindowsWerePutBackIsDropped()
    {
        _opacity.Step(Window, -5);

        _opacity.PutBackAll();
        RunWork();

        Assert.Equal(100, _system.OpacityOf(Window));
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void PuttingBackNowLeavesTheMarksForTheNextStart()
    {
        _opacity.Step(Window, -5);
        RunWork();

        Assert.Equal(1, _opacity.PutBackNow());

        Assert.Equal(100, _system.OpacityOf(Window));
        Assert.True(_system.Marks.ContainsKey(Window));
    }

    [Fact]
    public void TheNextStartPutsBackWindowsLeftMarked()
    {
        const nint Console = 20;
        _system.Layered.Add(Console);
        _system.Shown[Console] = new LayeredAttributes(0x00FF00, 230, LayeredEffects.Alpha | LayeredEffects.ColorKey);
        _opacity.Step(Window, -5);
        _opacity.Step(Console, -5);
        RunWork();

        var recovered = WindowOpacity.Recover(_system, _work.Enqueue, _failures.Add);
        RunWork();

        Assert.Equal(2, recovered);
        Assert.False(_system.IsLayered(Window));
        Assert.Equal(new LayeredAttributes(0x00FF00, 230, LayeredEffects.Alpha | LayeredEffects.ColorKey), _system.Shown[Console]);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void AWindowMarkedBeforeGoesOnFromItsOpacityNow()
    {
        _opacity.Step(Window, -5);
        RunWork();
        var later = new WindowOpacity(_system, _work.Enqueue, action => action(), _failures.Add);

        later.Step(Window, 1);
        RunWork();

        Assert.Equal(60, _system.OpacityOf(Window));
    }

    [Fact]
    public void AFailureIsReported()
    {
        var failing = new WindowOpacity(new ThrowingSystem(), _work.Enqueue, action => action(), _failures.Add);

        failing.Step(Window, -1);
        while (_work.TryDequeue(out var work))
        {
            work();
        }

        Assert.Single(_failures);
    }

    private void RunWork()
    {
        while (_work.TryDequeue(out var work))
        {
            work();
        }

        Assert.Empty(_failures);
    }

    // A window whose style can't be read, as when something goes wrong in Windows.
    private sealed class ThrowingSystem : IWindowOpacitySystem
    {
        private readonly FakeWindowOpacitySystem _inner = new();

        public bool IsLayered(nint window) => false;

        public LayeredAttributes? Attributes(nint window) => null;

        public bool SetAttributes(nint window, LayeredAttributes attributes) => throw new InvalidOperationException("Made up.");

        public bool SetLayered(nint window, bool layered) => true;

        public bool Mark(nint window, nint value) => _inner.Mark(window, value);

        public nint MarkValue(nint window) => _inner.MarkValue(window);

        public void RemoveMark(nint window) => _inner.RemoveMark(window);

        public IReadOnlyList<nint> FindMarked() => _inner.FindMarked();
    }
}
