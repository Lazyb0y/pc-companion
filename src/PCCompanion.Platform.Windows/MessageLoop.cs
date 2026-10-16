using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The Windows message loop of the current thread, which delivers messages to its windows.
/// </summary>
public static class MessageLoop
{
    /// <summary>Delivers messages until <see cref="Quit"/> is called on this thread.</summary>
    public static void Run()
    {
        while (true)
        {
            var result = PInvoke.GetMessage(out var message, HWND.Null, 0, 0);
            if (result.Value == 0)
            {
                return;
            }

            if (result.Value == -1)
            {
                throw new Win32Exception();
            }

            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    /// <summary>Ends <see cref="Run"/> once the messages already posted to this thread are delivered.</summary>
    public static void Quit() => PInvoke.PostQuitMessage(0);
}
