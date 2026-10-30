using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.TextServices;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// An input method's identifier, as Windows writes it in the user's language list: a keyboard
/// layout's language and layout, such as <c>0407:00000407</c> for German, or a language alone, such
/// as <c>0411</c>, for all of that language's input method editors (IMEs), which a switch can choose
/// only by language. It stays the same across sessions, unlike the layout's handle.
/// </summary>
/// <param name="Language">The language ID, such as <c>0x0407</c>.</param>
/// <param name="Layout">The keyboard layout ID, such as <c>0x00020409</c>, or <see langword="null"/> for a language's IMEs.</param>
public readonly record struct InputMethodId(ushort Language, uint? Layout)
{
    /// <summary>Whether this is a language's IMEs rather than a keyboard layout.</summary>
    public bool IsImeLanguage => Layout is null;

    /// <summary>
    /// Reads an identifier written as <see cref="ToString"/> writes it, in either case. Returns
    /// false for anything else, such as a hand-edited value, or language 0.
    /// </summary>
    public static bool TryParse(string? text, out InputMethodId id)
    {
        id = default;
        if (text is null || (text.Length != 4 && text.Length != 13) || (text.Length == 13 && text[4] != ':'))
        {
            return false;
        }

        if (!ushort.TryParse(text.AsSpan(0, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var language) || language == 0)
        {
            return false;
        }

        if (text.Length == 4)
        {
            id = new InputMethodId(language, null);
            return true;
        }

        if (!uint.TryParse(text.AsSpan(5), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var layout))
        {
            return false;
        }

        id = new InputMethodId(language, layout);
        return true;
    }

    /// <summary>The identifier as Windows writes it, such as <c>0407:00000407</c> or <c>0411</c>.</summary>
    public override string ToString() =>
        Layout is { } layout
            ? string.Create(CultureInfo.InvariantCulture, $"{Language:X4}:{layout:X8}")
            : Language.ToString("X4", CultureInfo.InvariantCulture);
}

/// <summary>One of the user's input methods, with the names Windows' input switcher shows for it.</summary>
/// <param name="Handle">The layout's handle in this session, or 0 for an input method the user doesn't have.</param>
/// <param name="LanguageName">The language's name in Windows' display language, such as "German (Germany)".</param>
/// <param name="LayoutName">The keyboard layout's name, such as "German", or <see langword="null"/> for a language's IMEs or when it can't be read.</param>
public sealed record InputMethod(InputMethodId Id, nint Handle, string LanguageName, string? LayoutName);

/// <summary>
/// The user's input methods: their keyboard layouts and the languages of their IMEs, as Windows'
/// input switcher lists them. Reading them never changes an app's input method or the user's list.
/// </summary>
public static class InputMethods
{
    private const string KeyboardLayoutsPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

    /// <summary>
    /// The user's input methods, in the order of the input switcher. Reads Windows' text input
    /// framework, then each keyboard layout's identifier on a short-lived thread of its own, and
    /// the names from the registry: about 10 ms, so call it off the UI thread. A handle that Windows
    /// lists but whose identifier can't be read is left out.
    /// </summary>
    public static IReadOnlyList<InputMethod> Read()
    {
        var handles = KeyboardLayout.Installed();

        // The text input framework says which handles are keyboard layouts; any other stands for all
        // of its language's IMEs, as a switch to it gets the one of them the user used last.
        var keyboards = OnThreadOfItsOwn(ApartmentState.STA, KeyboardLayoutHandles);
        var layouts = OnThreadOfItsOwn(ApartmentState.MTA, () => LayoutIds(handles.Where(handle => keyboards.Contains(Low32(handle)))));

        var methods = new List<InputMethod>(handles.Count);
        foreach (var handle in handles)
        {
            var language = (ushort)(Low32(handle) & 0xFFFF);
            InputMethodId id;
            if (layouts.TryGetValue(Low32(handle), out var layout))
            {
                id = new InputMethodId(language, layout);
            }
            else if (!keyboards.Contains(Low32(handle)))
            {
                id = new InputMethodId(language, null);
            }
            else
            {
                continue;
            }

            if (methods.All(method => method.Id != id))
            {
                methods.Add(Describe(id) with { Handle = handle });
            }
        }

        return methods;
    }

    /// <summary>
    /// The names Windows' input switcher shows for <paramref name="id"/>, also when the user doesn't
    /// have it, with no handle. Reads the registry, so call it off the UI thread.
    /// </summary>
    public static InputMethod Describe(InputMethodId id) =>
        new(id, 0, LanguageName(id.Language), id.Layout is { } layout ? LayoutName(layout) : null);

    /// <summary>
    /// The handle of <paramref name="id"/> among <paramref name="methods"/>, as <see cref="Read"/>
    /// returned them, or 0 when the user doesn't have it.
    /// </summary>
    public static nint HandleOf(InputMethodId id, IReadOnlyList<InputMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return methods.FirstOrDefault(method => method.Id == id)?.Handle ?? 0;
    }

    /// <summary>
    /// The identifier of the input method with <paramref name="handle"/> among
    /// <paramref name="methods"/>, or <see langword="null"/> when it isn't one of them.
    /// </summary>
    public static InputMethodId? IdOf(nint handle, IReadOnlyList<InputMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);

        return methods.FirstOrDefault(method => Low32(method.Handle) == Low32(handle))?.Id;
    }

    // Some APIs, such as the text input framework, return handles sign-extended to 64 bits.
    private static uint Low32(nint handle) => unchecked((uint)handle);

    private static unsafe HashSet<uint> KeyboardLayoutHandles()
    {
        PInvoke.CoCreateInstance<ITfInputProcessorProfileMgr>(PInvoke.CLSID_TF_InputProcessorProfiles, null, CLSCTX.CLSCTX_INPROC_SERVER, out var manager).ThrowOnFailure();
        try
        {
            manager.EnumProfiles(0, out var profiles);
            try
            {
                var handles = new HashSet<uint>();
                TF_INPUTPROCESSORPROFILE profile;
                while (true)
                {
                    profiles.Next(1, &profile, out var fetched);
                    if (fetched == 0)
                    {
                        return handles;
                    }

                    // Windows also lists a profile of language 0, which is no input method.
                    if (profile.dwProfileType == PInvoke.TF_PROFILETYPE_KEYBOARDLAYOUT && (profile.dwFlags & PInvoke.TF_IPP_FLAG_ENABLED) != 0 && profile.langid != 0)
                    {
                        handles.Add(Low32(profile.hkl));
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(profiles);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    // Windows names a keyboard layout only for the thread it's active on, so each is activated on
    // this thread, which is the thread's own change: no app's input method changes, as the spike
    // showed. The handle overload is used, since the safe handle one unloads the layout when it's
    // disposed.
    private static Dictionary<uint, uint> LayoutIds(IEnumerable<nint> handles)
    {
        var layouts = new Dictionary<uint, uint>();
        Span<char> name = stackalloc char[(int)PInvoke.KL_NAMELENGTH];
        foreach (var handle in handles)
        {
            if (!PInvoke.ActivateKeyboardLayout((HKL)handle, 0).IsNull
                && PInvoke.GetKeyboardLayoutName(name)
                && uint.TryParse(name[..8], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var layout))
            {
                layouts[Low32(handle)] = layout;
            }
        }

        return layouts;
    }

    private static string LanguageName(ushort language)
    {
        Span<char> locale = stackalloc char[(int)PInvoke.LOCALE_NAME_MAX_LENGTH];
        var length = PInvoke.LCIDToLocaleName(language, locale, 0);
        if (length <= 1)
        {
            return language.ToString("X4", CultureInfo.InvariantCulture);
        }

        var localeName = new string(locale[..(length - 1)]);
        Span<char> name = stackalloc char[256];
        var nameLength = PInvoke.GetLocaleInfoEx(localeName, PInvoke.LOCALE_SLOCALIZEDDISPLAYNAME, name);
        return nameLength > 1 ? new string(name[..(nameLength - 1)]) : localeName;
    }

    // Microsoft doesn't document these values, so a layout's name is only shown, never relied on.
    private static string? LayoutName(uint layout)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{KeyboardLayoutsPath}\{layout.ToString("X8", CultureInfo.InvariantCulture)}");
        if (key is null)
        {
            return null;
        }

        if (key.GetValue("Layout Display Name") is string display && display.StartsWith('@'))
        {
            Span<char> name = stackalloc char[256];
            if (PInvoke.SHLoadIndirectString(Environment.ExpandEnvironmentVariables(display), name).Succeeded)
            {
                var end = name.IndexOf('\0');
                var text = new string(end < 0 ? name : name[..end]);
                if (text.Length > 0)
                {
                    return text;
                }
            }
        }

        return key.GetValue("Layout Text") is string { Length: > 0 } fallback ? fallback : null;
    }

    private static T OnThreadOfItsOwn<T>(ApartmentState apartment, Func<T> read)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = read();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Input methods",
        };
        thread.SetApartmentState(apartment);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
