using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// A screen reader's view of a tab strip, read with UI Automation on another thread while the test's
// thread, which owns the window, runs its messages, as Windows calls the providers there. The
// window is fully transparent and far off the screen.
public sealed class TabListAutomationTests
{
    private const int TreeScopeChildren = 2;
    private const int NamePropertyId = 30005;
    private const int ControlTypePropertyId = 30003;
    private const int PositionInSetPropertyId = 30152;
    private const int SizeOfSetPropertyId = 30153;
    private const int TabControlType = 50018;
    private const int TabItemControlType = 50019;
    private const int SelectionItemPatternId = 10010;
    private static readonly Rectangle OffScreen = new(-30000, -30000, 4, 2);

    [Fact]
    public void ScreenReadersFindATabListWithATabItemPerTab()
    {
        using var messages = new MessageWindow(UniqueClassName());
        using var window = new LayeredWindow(UniqueClassName(), "Strip", topmost: false);
        window.Show(new uint[8], OffScreen);
        var tabs = new FakeTabs("Notes and 2 more", ["Notes", "Inbox", "Report"], selected: 1);
        using var automation = new TabListAutomation(window, tabs);

        var read = Elsewhere(() => Read(window.Handle));
        RunUntil(messages, () => read.Done);

        var (list, items) = read.Value;
        Assert.Equal(("Notes and 2 more", TabControlType), list);
        Assert.Equal(
            [
                ("Notes", TabItemControlType, 1, 3, false),
                ("Inbox", TabItemControlType, 2, 3, true),
                ("Report", TabItemControlType, 3, 3, false),
            ],
            items);
    }

    [Fact]
    public void ScreenReadersCanChooseATab()
    {
        using var messages = new MessageWindow(UniqueClassName());
        using var window = new LayeredWindow(UniqueClassName(), "Strip", topmost: false);
        window.Show(new uint[8], OffScreen);
        var tabs = new FakeTabs("Notes and 1 more", ["Notes", "Inbox"], selected: 0);
        using var automation = new TabListAutomation(window, tabs);

        var choose = Elsewhere(() => Choose(window.Handle, 1));
        RunUntil(messages, () => choose.Done);

        Assert.True(choose.Value);
        Assert.Equal([1], tabs.Chosen);
    }

    [Fact]
    public void ChangedTabsAreReadAgain()
    {
        using var messages = new MessageWindow(UniqueClassName());
        using var window = new LayeredWindow(UniqueClassName(), "Strip", topmost: false);
        window.Show(new uint[8], OffScreen);
        var tabs = new FakeTabs("Notes and 1 more", ["Notes", "Inbox"], selected: 0);
        using var automation = new TabListAutomation(window, tabs);

        var first = Elsewhere(() => Read(window.Handle));
        RunUntil(messages, () => first.Done);
        tabs.Names = ["Notes", "Inbox", "Report"];
        automation.TabsChanged();
        automation.SelectionChanged();
        automation.Announce("Tab 3 of 3");
        var second = Elsewhere(() => Read(window.Handle));
        RunUntil(messages, () => second.Done);

        Assert.Equal(2, first.Value.Items.Count);
        Assert.Equal(["Notes", "Inbox", "Report"], second.Value.Items.ConvertAll(item => item.Name));
    }

    [Fact]
    public void DisposeTwiceIsFine()
    {
        using var window = new LayeredWindow(UniqueClassName(), "Strip", topmost: false);
        var automation = new TabListAutomation(window, new FakeTabs("Tabs", ["Notes"], selected: 0));

        automation.Dispose();
        automation.Dispose();
        automation.TabsChanged();
        automation.Announce("Tab 1 of 1");
    }

    private static ((string Name, int Type) List, List<(string Name, int Type, int Position, int Size, bool Selected)> Items) Read(nint window)
    {
        var automation = (IUIAutomation)new CUIAutomation();
        Check(automation.ElementFromHandle(window, out var list));
        Check(automation.CreateTrueCondition(out var all));
        Check(list.FindAll(TreeScopeChildren, all, out var children));
        Check(children.get_Length(out var count));
        var items = new List<(string, int, int, int, bool)>();
        for (var index = 0; index < count; index++)
        {
            Check(children.GetElement(index, out var item));
            Check(item.GetCurrentPattern(SelectionItemPatternId, out var pattern));
            Check(((IUIAutomationSelectionItemPattern)pattern).get_CurrentIsSelected(out var selected));
            items.Add(((string)Property(item, NamePropertyId), (int)Property(item, ControlTypePropertyId), (int)Property(item, PositionInSetPropertyId), (int)Property(item, SizeOfSetPropertyId), selected != 0));
        }

        return (((string)Property(list, NamePropertyId), (int)Property(list, ControlTypePropertyId)), items);
    }

    private static bool Choose(nint window, int index)
    {
        var automation = (IUIAutomation)new CUIAutomation();
        Check(automation.ElementFromHandle(window, out var list));
        Check(automation.CreateTrueCondition(out var all));
        Check(list.FindAll(TreeScopeChildren, all, out var children));
        Check(children.GetElement(index, out var item));
        Check(item.GetCurrentPattern(SelectionItemPatternId, out var pattern));
        return ((IUIAutomationSelectionItemPattern)pattern).Select() == 0;
    }

    private static object Property(IUIAutomationElement element, int propertyId)
    {
        Check(element.GetCurrentPropertyValue(propertyId, out var value));
        return value;
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    // Runs work on a thread of its own, so the test's thread can run the window's messages meanwhile.
    private static Outcome<T> Elsewhere<T>(Func<T> work)
    {
        var outcome = new Outcome<T>();
        new Thread(() =>
        {
            try
            {
                outcome.Result = work();
            }
            catch (Exception exception)
            {
                outcome.Error = exception;
            }

            outcome.Done = true;
        }) { IsBackground = true }.Start();
        return outcome;
    }

    private sealed class Outcome<T>
    {
        private volatile bool _done;

        public T? Result { get; set; }

        public Exception? Error { get; set; }

        public bool Done
        {
            get => _done;
            set => _done = value;
        }

        // The work's result, or its exception, once it's done.
        public T Value => Error is null ? Result! : throw new InvalidOperationException("The work failed.", Error);
    }

    private sealed class FakeTabs(string name, string[] names, int selected) : ITabList
    {
        public string[] Names { get; set; } = names;

        public List<int> Chosen { get; } = [];

        public string Name => name;

        public int Count => Names.Length;

        public int Selected => selected;

        public string NameOf(int index) => Names[index];

        public Rectangle BoundsOf(int index) => new(OffScreen.X + index, OffScreen.Y, 1, 1);

        public void Choose(int index) => Chosen.Add(index);
    }

    // The few parts of UI Automation's client API the tests use, in the interfaces' order; the
    // placeholders hold the places of methods the tests don't call.
    [ComImport]
    [Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation
    {
    }

    [ComImport]
    [Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements();

        [PreserveSig] int CompareRuntimeIds();

        [PreserveSig] int GetRootElement();

        [PreserveSig] int ElementFromHandle(nint window, out IUIAutomationElement element);

        [PreserveSig] int ElementFromPoint();

        [PreserveSig] int GetFocusedElement();

        [PreserveSig] int GetRootElementBuildCache();

        [PreserveSig] int ElementFromHandleBuildCache();

        [PreserveSig] int ElementFromPointBuildCache();

        [PreserveSig] int GetFocusedElementBuildCache();

        [PreserveSig] int CreateTreeWalker();

        [PreserveSig] int GetControlViewWalker();

        [PreserveSig] int GetContentViewWalker();

        [PreserveSig] int GetRawViewWalker();

        [PreserveSig] int GetRawViewCondition();

        [PreserveSig] int GetControlViewCondition();

        [PreserveSig] int GetContentViewCondition();

        [PreserveSig] int CreateCacheRequest();

        [PreserveSig] int CreateTrueCondition([MarshalAs(UnmanagedType.IUnknown)] out object condition);
    }

    [ComImport]
    [Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();

        [PreserveSig] int GetRuntimeId();

        [PreserveSig] int FindFirst();

        [PreserveSig] int FindAll(int scope, [MarshalAs(UnmanagedType.IUnknown)] object condition, out IUIAutomationElementArray found);

        [PreserveSig] int FindFirstBuildCache();

        [PreserveSig] int FindAllBuildCache();

        [PreserveSig] int BuildUpdatedCache();

        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);

        [PreserveSig] int GetCurrentPropertyValueEx();

        [PreserveSig] int GetCachedPropertyValue();

        [PreserveSig] int GetCachedPropertyValueEx();

        [PreserveSig] int GetCurrentPatternAs();

        [PreserveSig] int GetCachedPatternAs();

        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
    }

    [ComImport]
    [Guid("14314595-b4bc-4055-95f2-58f2e42c9855")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        [PreserveSig] int get_Length(out int length);

        [PreserveSig] int GetElement(int index, out IUIAutomationElement element);
    }

    [ComImport]
    [Guid("a8efa66a-0fda-421a-9194-38021f3578ea")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationSelectionItemPattern
    {
        [PreserveSig] int Select();

        [PreserveSig] int AddToSelection();

        [PreserveSig] int RemoveFromSelection();

        [PreserveSig] int get_CurrentIsSelected(out int selected);
    }
}
