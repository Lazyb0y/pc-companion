using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.WinInet;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The current user's proxy settings for the network: the LAN settings, which Windows uses for
/// Ethernet and Wi-Fi, as Settings &gt; Network &amp; internet &gt; Proxy shows them. A part keeps
/// its value while it's off, as in Windows Settings.
/// </summary>
/// <param name="AutoDetect">Automatically detect settings.</param>
/// <param name="UseScript">Use setup script.</param>
/// <param name="ScriptAddress">The setup script's address.</param>
/// <param name="UseServer">Use a proxy server.</param>
/// <param name="Server">
/// The proxy server as Windows stores it, such as <c>proxy.example.com:8080</c>, or a server for
/// each protocol, such as <c>http=proxy.example.com:8080;https=proxy.example.com:8443</c>.
/// </param>
/// <param name="Bypass">
/// The addresses that don't use the proxy server, separated by semicolons, with <c>&lt;local&gt;</c>
/// for local (intranet) addresses.
/// </param>
public sealed record ProxySettings(bool AutoDetect, bool UseScript, string ScriptAddress, bool UseServer, string Server, string Bypass);

/// <summary>
/// Reads and changes the <see cref="ProxySettings"/> with the documented WinINet functions, as
/// Windows Settings does, never through the registry. Both load WinINet on first use and read or
/// write the registry, so don't call them on the host's UI thread. See "Changing the settings" in
/// docs/features/network/network-profiles.md.
/// </summary>
public static class InternetProxy
{
    // INTERNET_PER_CONN_FLAGS_UI (Windows 7 and later): the flags as the user set them, which
    // INTERNET_PER_CONN_FLAGS can report differently for automatic detection. CsWin32's enum
    // doesn't have it.
    private const INTERNET_PER_CONN FlagsAsSet = (INTERNET_PER_CONN)10;

    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static ProxySettings Read() =>
        Query(FlagsAsSet) ?? Query(INTERNET_PER_CONN.INTERNET_PER_CONN_FLAGS) ?? throw new Win32Exception();

    /// <summary>
    /// Changes the settings, then tells programs that use WinINet to read them again. Each part is
    /// turned on or off, and the values of the parts that are on are written; the parts that are
    /// off keep the values Windows has, as when the user turns them off in Windows Settings.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static unsafe void Write(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var flags = PInvoke.PROXY_TYPE_DIRECT
            | (settings.AutoDetect ? PInvoke.PROXY_TYPE_AUTO_DETECT : 0)
            | (settings.UseScript ? PInvoke.PROXY_TYPE_AUTO_PROXY_URL : 0)
            | (settings.UseServer ? PInvoke.PROXY_TYPE_PROXY : 0);

        var options = stackalloc INTERNET_PER_CONN_OPTIONW[4];
        var count = 0u;
        options[count++] = new INTERNET_PER_CONN_OPTIONW { dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_FLAGS, Value = { dwValue = flags } };

        fixed (char* server = settings.Server, bypass = settings.Bypass, script = settings.ScriptAddress)
        {
            if (settings.UseServer)
            {
                options[count++] = new INTERNET_PER_CONN_OPTIONW { dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_PROXY_SERVER, Value = { pszValue = server } };
                options[count++] = new INTERNET_PER_CONN_OPTIONW { dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_PROXY_BYPASS, Value = { pszValue = bypass } };
            }

            if (settings.UseScript)
            {
                options[count++] = new INTERNET_PER_CONN_OPTIONW { dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_AUTOCONFIG_URL, Value = { pszValue = script } };
            }

            // No connection name: the LAN settings.
            var list = new INTERNET_PER_CONN_OPTION_LISTW { dwSize = (uint)sizeof(INTERNET_PER_CONN_OPTION_LISTW), dwOptionCount = count, pOptions = options };
            if (!PInvoke.InternetSetOption(null, PInvoke.INTERNET_OPTION_PER_CONNECTION_OPTION, &list, list.dwSize))
            {
                throw new Win32Exception();
            }
        }

        PInvoke.InternetSetOption(null, PInvoke.INTERNET_OPTION_SETTINGS_CHANGED, null, 0);
        PInvoke.InternetSetOption(null, PInvoke.INTERNET_OPTION_REFRESH, null, 0);
    }

    // The settings with the flags from flagsOption, or null when Windows refused.
    private static unsafe ProxySettings? Query(INTERNET_PER_CONN flagsOption)
    {
        var options = stackalloc INTERNET_PER_CONN_OPTIONW[4];
        options[0].dwOption = flagsOption;
        options[1].dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_PROXY_SERVER;
        options[2].dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_PROXY_BYPASS;
        options[3].dwOption = INTERNET_PER_CONN.INTERNET_PER_CONN_AUTOCONFIG_URL;

        // No connection name: the LAN settings.
        var list = new INTERNET_PER_CONN_OPTION_LISTW { dwSize = (uint)sizeof(INTERNET_PER_CONN_OPTION_LISTW), dwOptionCount = 4, pOptions = options };
        var size = list.dwSize;
        if (!PInvoke.InternetQueryOption(null, PInvoke.INTERNET_OPTION_PER_CONNECTION_OPTION, &list, &size))
        {
            return null;
        }

        var server = Take(options[1].Value.pszValue);
        var bypass = Take(options[2].Value.pszValue);
        var script = Take(options[3].Value.pszValue);
        var flags = options[0].Value.dwValue;
        return new ProxySettings(
            AutoDetect: (flags & PInvoke.PROXY_TYPE_AUTO_DETECT) != 0,
            UseScript: (flags & PInvoke.PROXY_TYPE_AUTO_PROXY_URL) != 0,
            ScriptAddress: script,
            UseServer: (flags & PInvoke.PROXY_TYPE_PROXY) != 0,
            Server: server,
            Bypass: bypass);
    }

    // WinINet allocates the strings it returns with GlobalAlloc, for the caller to free.
    private static unsafe string Take(PWSTR value)
    {
        if (value.Value is null)
        {
            return string.Empty;
        }

        var text = value.ToString();
        PInvoke.GlobalFree((HGLOBAL)(void*)value.Value);
        return text;
    }
}
