using System;
using System.Collections.Generic;
using System.ComponentModel;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Opens web pages, such as a link Clipboard Actions offers or the release page the update
/// notification opens, never with admin rights. A browser started by a host running as
/// administrator would run as administrator too, so such a host hands the address to an
/// <see cref="UnelevatedCopy"/> started with <see cref="HostProcess.OpenWebPageArgument"/>, which
/// opens the page. Through the copy's pipe, long addresses fit too, such as a Clipboard Actions
/// rule's with the copied text in it.
/// </summary>
internal static class WebPageCommand
{
    private const string PipeName = "PCCompanion.OpenWebPage";
    private const int NotOpened = 1; // The shell didn't open it.
    private const int AccessDenied = 5; // ERROR_ACCESS_DENIED
    private const int InvalidData = 13; // ERROR_INVALID_DATA

    /// <summary>
    /// Opens <paramref name="address"/>, an <c>http</c> or <c>https</c> address, from the running
    /// host, on the calling thread: a host running as administrator waits up to 30 seconds for its
    /// copy. Returns false when it isn't one or Windows refused.
    /// </summary>
    public static bool Open(Uri address)
    {
        if (!HostProcess.IsWebAddress(address))
        {
            return false;
        }

        try
        {
            return Elevation.IsUacElevated
                ? UnelevatedCopy.Run(PipeName, HostProcess.WebAddressHandOff(address), HostProcess.WebPageArguments) == 0
                : ShellActions.OpenLink(address);
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Runs the copy started with <see cref="HostProcess.OpenWebPageArgument"/>, and returns its exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        // Started with admin rights, for example by hand, it opens nothing.
        if (Elevation.IsUacElevated)
        {
            return AccessDenied;
        }

        if (UnelevatedCopy.Read(HostProcess.ReadWebPagePipe(arguments), HostProcess.MaximumWebAddressLength) is not { } handOff
            || HostProcess.ReadWebAddress(handOff) is not { } address)
        {
            return InvalidData;
        }

        return ShellActions.OpenLink(address) ? 0 : NotOpened;
    }
}
