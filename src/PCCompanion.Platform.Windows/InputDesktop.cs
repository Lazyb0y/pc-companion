using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.StationsAndDesktops;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The desktop that gets the keyboard and the pointer: the user's own, or the secure desktop that
/// Windows shows the sign-in screen and UAC prompts on.
/// </summary>
public static class InputDesktop
{
    private const string UsersDesktopName = "Default";

    /// <summary>
    /// Whether the user's own desktop gets input. False while the secure desktop does, which
    /// programs can't open, and when the desktop can't be read.
    /// </summary>
    public static unsafe bool IsUsersOwn()
    {
        var desktop = PInvoke.OpenInputDesktop(0, false, DESKTOP_ACCESS_FLAGS.DESKTOP_READOBJECTS);
        if (desktop.IsNull)
        {
            return false;
        }

        try
        {
            // A desktop's name is at most 256 characters with the terminating null.
            const int Length = 257;
            var name = stackalloc char[Length];
            uint needed = 0;
            return PInvoke.GetUserObjectInformation((HANDLE)(nint)desktop.Value, USER_OBJECT_INFORMATION_INDEX.UOI_NAME, name, Length * sizeof(char), &needed)
                && new string(name) == UsersDesktopName;
        }
        finally
        {
            PInvoke.CloseDesktop(desktop);
        }
    }
}
