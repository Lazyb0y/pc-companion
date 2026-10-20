using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Opens web pages for features, such as a link Clipboard Actions offers, never with admin rights.
/// A browser started by a host running as administrator would run as administrator too, so such a
/// host starts a copy of itself with <see cref="HostProcess.OpenWebPageArgument"/> without admin
/// rights, as it does for the release page, and the copy opens the page.
/// </summary>
internal static class WebPageCommand
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, HostProcess.FileName);

    /// <summary>
    /// Opens <paramref name="address"/>, an <c>http</c> or <c>https</c> address, from the running
    /// host. Returns false when it isn't one or Windows refused.
    /// </summary>
    public static bool Open(Uri address)
    {
        if (!HostProcess.IsWebAddress(address))
        {
            return false;
        }

        try
        {
            if (Elevation.IsUacElevated)
            {
                UnelevatedProcess.Start(HostPath, HostProcess.WebPageArguments(address));
                return true;
            }

            return ShellActions.OpenLink(address);
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

    /// <summary>Runs the copy started with <see cref="HostProcess.OpenWebPageArgument"/>.</summary>
    public static void Run(IReadOnlyList<string> arguments)
    {
        // Started with admin rights, for example by hand, it does nothing.
        if (Elevation.IsUacElevated || HostProcess.ReadWebPageAddress(arguments) is not { } address)
        {
            return;
        }

        ShellActions.OpenLink(address);
    }
}
