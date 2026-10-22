using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;

namespace PCCompanion.App.Features.NetworkProfiles;

/// <summary>The page's and the profile editor's text for profiles and their problems.</summary>
public static class NetworkProfilesText
{
    private static readonly CompositeFormat TooLongFormat = CompositeFormat.Parse(Strings.NetworkProfilesTooLong);
    private static readonly CompositeFormat ServerFormat = CompositeFormat.Parse(Strings.NetworkProfilesSummaryServer);
    private static readonly CompositeFormat ScriptFormat = CompositeFormat.Parse(Strings.NetworkProfilesSummaryScript);
    private static readonly CompositeFormat ProblemFormat = CompositeFormat.Parse(Strings.NetworkProfilesSummaryProblem);

    /// <summary>What to do about a problem, or an empty string for none.</summary>
    public static string Problem(ProfileProblem problem) => problem switch
    {
        ProfileProblem.None => string.Empty,
        ProfileProblem.NoName => Strings.NetworkProfilesNameMissing,
        ProfileProblem.NameTooLong => TooLong(NetworkProfileCheck.MaximumNameLength),
        ProfileProblem.InvalidName => Strings.NetworkProfilesNameInvalid,
        ProfileProblem.NameInUse => Strings.NetworkProfilesNameInUse,
        ProfileProblem.NoServer => Strings.NetworkProfilesServerMissing,
        ProfileProblem.InvalidServer => Strings.NetworkProfilesServerInvalid,
        ProfileProblem.NoScriptAddress => Strings.NetworkProfilesScriptMissing,
        ProfileProblem.ScriptNotWebAddress => Strings.NetworkProfilesScriptNotWeb,
        ProfileProblem.InvalidException => Strings.NetworkProfilesExceptionInvalid,
        _ => TooLong(NetworkProfileCheck.MaximumTextLength),
    };

    /// <summary>
    /// What a profile does, such as "Proxy server proxy.example.com:8080", or why it isn't in the
    /// menu. The parts that are on are listed with the list separator of the user's region.
    /// </summary>
    public static string Summary(ProxyProfile proxy, ProfileProblem problem)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        if (problem != ProfileProblem.None)
        {
            return string.Format(CultureInfo.CurrentCulture, ProblemFormat, Problem(problem));
        }

        var parts = new List<string>();
        if (proxy.UseServer)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, ServerFormat, proxy.Server.Trim()));
        }

        if (proxy.UseScript)
        {
            var script = Uri.TryCreate(proxy.ScriptAddress.Trim(), UriKind.Absolute, out var address) ? address.Host : proxy.ScriptAddress.Trim();
            parts.Add(string.Format(CultureInfo.CurrentCulture, ScriptFormat, script));
        }

        if (proxy.AutoDetect)
        {
            parts.Add(Strings.NetworkProfilesSummaryDetect);
        }

        return parts.Count == 0 ? Strings.NetworkProfilesSummaryNoProxy : string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", parts);
    }

    private static string TooLong(int maximum) =>
        string.Format(CultureInfo.CurrentCulture, TooLongFormat, maximum.ToString("N0", CultureInfo.CurrentCulture));
}
