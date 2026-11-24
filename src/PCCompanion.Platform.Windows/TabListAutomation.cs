using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Accessibility;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What a row of tabs that the app draws shows, for <see cref="TabListAutomation"/>, which reads it
/// on the window's thread.
/// </summary>
public interface ITabList
{
    /// <summary>The list's name, as screen readers read it, such as what the tabs belong to.</summary>
    string Name { get; }

    int Count { get; }

    /// <summary>The tab that's chosen, from 0, or -1 when none is.</summary>
    int Selected { get; }

    string NameOf(int index);

    /// <summary>Where the tab is on the screen, in physical pixels.</summary>
    Rectangle BoundsOf(int index);

    /// <summary>Chooses the tab, as a click on it does.</summary>
    void Choose(int index);
}

/// <summary>
/// UI Automation for a <see cref="LayeredWindow"/> that shows a row of tabs, such as a window's tab
/// strip: screen readers find a tab list with a tab item per tab, which they can choose, and hear
/// what the app announces. Windows calls it on the window's thread, which must run a message loop.
/// Create, use and dispose it on that thread.
/// </summary>
public sealed class TabListAutomation : IDisposable
{
    // UIA_E_ELEMENTNOTAVAILABLE: the tab a screen reader asks about is gone.
    private const int ElementNotAvailable = unchecked((int)0x80040201);

    private readonly LayeredWindow _window;
    private readonly ITabList _tabs;
    private readonly ListProvider _root;
    private TabProvider?[] _items = [];
    private int _nextId = 1;
    private bool _disposed;

    public TabListAutomation(LayeredWindow window, ITabList tabs)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(tabs);

        _window = window;
        _tabs = tabs;
        _root = new ListProvider(this);
        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Says that tabs came, went, moved or were renamed, so screen readers read the list again.
    /// </summary>
    public void TabsChanged()
    {
        if (_disposed)
        {
            return;
        }

        _items = new TabProvider?[_tabs.Count];
        if (PInvoke.UiaClientsAreListening())
        {
            unsafe
            {
                _ = PInvoke.UiaRaiseStructureChangedEvent(_root, StructureChangeType.StructureChangeType_ChildrenInvalidated, null, 0);
            }
        }
    }

    /// <summary>Says that another tab was chosen.</summary>
    public void SelectionChanged()
    {
        if (_disposed || !PInvoke.UiaClientsAreListening() || ItemAt(_tabs.Selected) is not { } selected)
        {
            return;
        }

        _ = PInvoke.UiaRaiseAutomationEvent(selected, UIA_EVENT_ID.UIA_SelectionItem_ElementSelectedEventId);
    }

    /// <summary>
    /// Has screen readers say <paramref name="text"/>, such as the position of a tab that was
    /// chosen, after what they're saying now.
    /// </summary>
    public void Announce(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_disposed || !PInvoke.UiaClientsAreListening())
        {
            return;
        }

        var display = Marshal.StringToBSTR(text);
        var activity = Marshal.StringToBSTR("PCCompanion.TabList");
        try
        {
            unsafe
            {
                _ = PInvoke.UiaRaiseNotificationEvent(_root, NotificationKind.NotificationKind_ActionCompleted, NotificationProcessing.NotificationProcessing_ImportantMostRecent, new BSTR((char*)display), new BSTR((char*)activity));
            }
        }
        finally
        {
            Marshal.FreeBSTR(display);
            Marshal.FreeBSTR(activity);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;

        // Windows lets go of the providers it holds for the window.
        if (_window.Handle != 0)
        {
            _ = PInvoke.UiaReturnRawElementProvider(new HWND(_window.Handle), 0, 0, null);
        }

        _ = PInvoke.UiaDisconnectProvider(_root);
    }

    private TabProvider? ItemAt(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return null;
        }

        if (_items.Length != _tabs.Count)
        {
            _items = new TabProvider?[_tabs.Count];
        }

        return _items[index] ??= new TabProvider(this, index, _nextId++);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message != PInvoke.WM_GETOBJECT || (int)e.LParam != PInvoke.UiaRootObjectId)
        {
            return;
        }

        e.Result = PInvoke.UiaReturnRawElementProvider(new HWND(_window.Handle), e.WParam, e.LParam, _root);
        e.Handled = true;
    }

    // A runtime ID made of UiaAppendRuntimeId and the tab's own number, which Windows makes unique
    // by adding the window's.
    private static unsafe SAFEARRAY* RuntimeId(int id)
    {
        var array = PInvoke.SafeArrayCreateVector(VARENUM.VT_I4, 0, 2);
        int index = 0, value = (int)PInvoke.UiaAppendRuntimeId;
        _ = PInvoke.SafeArrayPutElement(array, &index, &value);
        index = 1;
        value = id;
        _ = PInvoke.SafeArrayPutElement(array, &index, &value);
        return array;
    }

    // The tab list: the window itself, whose host provider gives its place and runtime ID.
    [ComVisible(true)]
    private sealed class ListProvider(TabListAutomation owner) : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot, ISelectionProvider
    {
        private IRawElementProviderSimple? _host;

        public ProviderOptions ProviderOptions => ProviderOptions.ProviderOptions_ServerSideProvider;

        public IRawElementProviderSimple HostRawElementProvider
        {
            get
            {
                if (_host is null)
                {
                    Marshal.ThrowExceptionForHR(PInvoke.UiaHostProviderFromHwnd(new HWND(owner._window.Handle), out var host));
                    _host = host;
                }

                return _host;
            }
        }

        // Windows takes the window's place for the list's own.
        public UiaRect BoundingRectangle => default;

        public IRawElementProviderFragmentRoot FragmentRoot => this;

        public BOOL CanSelectMultiple => false;

        public BOOL IsSelectionRequired => true;

        public object? GetPatternProvider(UIA_PATTERN_ID patternId) => patternId == UIA_PATTERN_ID.UIA_SelectionPatternId ? this : null;

        public object? GetPropertyValue(UIA_PROPERTY_ID propertyId) => propertyId switch
        {
            UIA_PROPERTY_ID.UIA_ControlTypePropertyId => (int)UIA_CONTROLTYPE_ID.UIA_TabControlTypeId,
            UIA_PROPERTY_ID.UIA_NamePropertyId => owner._tabs.Name,
            UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId => false,
            _ => null,
        };

        public IRawElementProviderFragment? Navigate(NavigateDirection direction) => direction switch
        {
            NavigateDirection.NavigateDirection_FirstChild => owner.ItemAt(0),
            NavigateDirection.NavigateDirection_LastChild => owner.ItemAt(owner._tabs.Count - 1),
            _ => null,
        };

        public unsafe SAFEARRAY* GetRuntimeId() => null;

        public unsafe SAFEARRAY* GetEmbeddedFragmentRoots() => null;

        public void SetFocus()
        {
        }

        public IRawElementProviderFragment ElementProviderFromPoint(double x, double y)
        {
            for (var index = 0; index < owner._tabs.Count; index++)
            {
                if (owner._tabs.BoundsOf(index).Contains((int)x, (int)y))
                {
                    return owner.ItemAt(index)!;
                }
            }

            return this;
        }

        // The tabs never take the keyboard focus.
        public IRawElementProviderFragment? GetFocus() => null;

        public unsafe SAFEARRAY* GetSelection()
        {
            var selected = owner.ItemAt(owner._tabs.Selected);
            var array = PInvoke.SafeArrayCreateVector(VARENUM.VT_UNKNOWN, 0, selected is null ? 0u : 1u);
            if (selected is not null)
            {
                var unknown = Marshal.GetIUnknownForObject(selected);
                try
                {
                    var index = 0;
                    _ = PInvoke.SafeArrayPutElement(array, &index, (void*)unknown);
                }
                finally
                {
                    Marshal.Release(unknown);
                }
            }

            return array;
        }
    }

    // A tab; one that's gone answers that it isn't available any more.
    [ComVisible(true)]
    private sealed class TabProvider(TabListAutomation owner, int index, int id) : IRawElementProviderSimple, IRawElementProviderFragment, ISelectionItemProvider
    {
        public ProviderOptions ProviderOptions => ProviderOptions.ProviderOptions_ServerSideProvider;

        public IRawElementProviderSimple? HostRawElementProvider => null;

        public UiaRect BoundingRectangle
        {
            get
            {
                Check();
                var bounds = owner._tabs.BoundsOf(index);
                return new UiaRect { left = bounds.Left, top = bounds.Top, width = bounds.Width, height = bounds.Height };
            }
        }

        public IRawElementProviderFragmentRoot FragmentRoot => owner._root;

        public BOOL IsSelected => IsCurrent && owner._tabs.Selected == index;

        public IRawElementProviderSimple SelectionContainer => owner._root;

        private bool IsCurrent => !owner._disposed && index < owner._items.Length && owner._items[index] == this;

        public object? GetPatternProvider(UIA_PATTERN_ID patternId) => patternId == UIA_PATTERN_ID.UIA_SelectionItemPatternId ? this : null;

        public object? GetPropertyValue(UIA_PROPERTY_ID propertyId)
        {
            Check();
            return propertyId switch
            {
                UIA_PROPERTY_ID.UIA_ControlTypePropertyId => (int)UIA_CONTROLTYPE_ID.UIA_TabItemControlTypeId,
                UIA_PROPERTY_ID.UIA_NamePropertyId => owner._tabs.NameOf(index),
                UIA_PROPERTY_ID.UIA_IsKeyboardFocusablePropertyId => false,
                UIA_PROPERTY_ID.UIA_PositionInSetPropertyId => index + 1,
                UIA_PROPERTY_ID.UIA_SizeOfSetPropertyId => owner._tabs.Count,
                _ => null,
            };
        }

        public IRawElementProviderFragment? Navigate(NavigateDirection direction)
        {
            Check();
            return direction switch
            {
                NavigateDirection.NavigateDirection_Parent => owner._root,
                NavigateDirection.NavigateDirection_NextSibling => owner.ItemAt(index + 1),
                NavigateDirection.NavigateDirection_PreviousSibling => owner.ItemAt(index - 1),
                _ => null,
            };
        }

        public unsafe SAFEARRAY* GetRuntimeId() => RuntimeId(id);

        public unsafe SAFEARRAY* GetEmbeddedFragmentRoots() => null;

        public void SetFocus()
        {
        }

        public void Select()
        {
            Check();
            owner._tabs.Choose(index);
        }

        // One tab is always chosen, so adding to the choice chooses it, and taking it out isn't possible.
        public void AddToSelection() => Select();

        public void RemoveFromSelection() => throw new InvalidOperationException("One tab is always chosen.");

        private void Check()
        {
            if (!IsCurrent)
            {
                Marshal.ThrowExceptionForHR(ElementNotAvailable);
            }
        }
    }
}
