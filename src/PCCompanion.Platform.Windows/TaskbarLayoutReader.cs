using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Lists what's on the taskbar with UI Automation: the bounds, automation ID and class name of
/// each element, never its name, which can hold a window's title. Create, use and dispose it on
/// one thread, which mustn't be one that UI Automation reads, such as a thread whose windows the
/// taskbar shows.
/// </summary>
/// <remarks>
/// The automation IDs and class names aren't a documented API, so callers use them only on the
/// Windows builds they were tested on.
/// </remarks>
public sealed class TaskbarLayoutReader : IDisposable
{
    private readonly IUIAutomation _automation;
    private readonly IUIAutomationCacheRequest _cache;
    private readonly IUIAutomationCondition _all;
    private bool _disposed;

    /// <exception cref="COMException">UI Automation isn't available.</exception>
    public TaskbarLayoutReader()
    {
        _automation = (IUIAutomation)new CUIAutomation();
        _cache = _automation.CreateCacheRequest();

        // Only these properties are read, in one call, and the elements keep no link to the taskbar.
        _cache.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
        _cache.AddProperty(UIA_PROPERTY_ID.UIA_AutomationIdPropertyId);
        _cache.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
        _cache.AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
        _all = _automation.CreateTrueCondition();
    }

    /// <summary>
    /// Lists the taskbar's elements that have a size, in physical pixels, or returns
    /// <see langword="null"/> when the taskbar didn't answer, as while it restarts.
    /// </summary>
    public IReadOnlyList<TaskbarElement>? Read(nint taskbar)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IUIAutomationElement? root = null;
        IUIAutomationElementArray? found = null;
        try
        {
            root = _automation.ElementFromHandle(new HWND(taskbar));
            found = root.FindAllBuildCache(TreeScope.TreeScope_Descendants, _all, _cache);
            var elements = new List<TaskbarElement>(found.Length);
            for (var index = 0; index < found.Length; index++)
            {
                var element = found.GetElement(index);
                try
                {
                    var bounds = element.CachedBoundingRectangle;
                    if (bounds.right > bounds.left && bounds.bottom > bounds.top)
                    {
                        elements.Add(new TaskbarElement(
                            Rectangle.FromLTRB(bounds.left, bounds.top, bounds.right, bounds.bottom),
                            Take(element.CachedAutomationId),
                            Take(element.CachedClassName)));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(element);
                }
            }

            return elements;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (found is not null)
            {
                Marshal.ReleaseComObject(found);
            }

            if (root is not null)
            {
                Marshal.ReleaseComObject(root);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Marshal.ReleaseComObject(_all);
        Marshal.ReleaseComObject(_cache);
        Marshal.ReleaseComObject(_automation);
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
}

/// <summary>An element on the taskbar, such as a button, in physical pixels.</summary>
public sealed record TaskbarElement(Rectangle Bounds, string AutomationId, string ClassName);
