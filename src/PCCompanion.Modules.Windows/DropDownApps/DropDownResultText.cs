using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// How the Drop-down Apps page shows a recent result, in the user interface language and the
/// user's regional format, such as "Terminal: runs as administrator, so it can't drop down while
/// PC Companion doesn't".
/// </summary>
public static class DropDownResultText
{
    private static readonly CompositeFormat ResultFormat = CompositeFormat.Parse(Strings.DropDownAppsResult);
    private static readonly CompositeFormat TimeAndCountFormat = CompositeFormat.Parse(Strings.DropDownAppsResultTimeAndCount);

    /// <summary>The result, with the name of the app at its place in <paramref name="apps"/>, the list as it is now.</summary>
    public static string Describe(DropDownResult result, IReadOnlyList<DropDownApp> apps)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(apps);

        var name = result.AppIndex >= 0 && result.AppIndex < apps.Count ? NameOf(apps[result.AppIndex]) : Strings.DropDownAppsResultRemovedApp;
        return string.Format(CultureInfo.CurrentCulture, ResultFormat, name, Outcome(result.Outcome));
    }

    /// <summary>When it happened, such as "12:41:05", and how many times in a row, if more than once.</summary>
    public static string When(DropDownResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var time = result.Time.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
        return result.Count > 1
            ? string.Format(CultureInfo.CurrentCulture, TimeAndCountFormat, time, PluralStrings.Format("DropDownAppsResultCount", result.Count))
            : time;
    }

    /// <summary>Whether the shortcut did what it was pressed for.</summary>
    public static bool IsDone(DropDownOutcome outcome) => outcome is DropDownOutcome.Shown or DropDownOutcome.Hidden
        or DropDownOutcome.BroughtToFront or DropDownOutcome.BroughtFromOtherDesktop or DropDownOutcome.Started;

    // The name the list shows, as the app list does.
    private static string NameOf(DropDownApp app) =>
        app.Name.Trim() is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(app.Program) ?? app.AppId ?? string.Empty;

    private static string Outcome(DropDownOutcome outcome) => outcome switch
    {
        DropDownOutcome.Shown => Strings.DropDownAppsResultShown,
        DropDownOutcome.Hidden => Strings.DropDownAppsResultHidden,
        DropDownOutcome.BroughtToFront => Strings.DropDownAppsResultBroughtToFront,
        DropDownOutcome.BroughtFromOtherDesktop => Strings.DropDownAppsResultBroughtFromOtherDesktop,
        DropDownOutcome.Started => Strings.DropDownAppsResultStarted,
        DropDownOutcome.RunsAsAdministrator => Strings.DropDownAppsResultRunsAsAdministrator,
        DropDownOutcome.SharedAppFrame => Strings.DropDownAppsResultSharedAppFrame,
        DropDownOutcome.DialogOpen => Strings.DropDownAppsResultDialogOpen,
        DropDownOutcome.ProgramMissing => Strings.DropDownAppsResultProgramMissing,
        DropDownOutcome.AppIdNotFound => Strings.DropDownAppsResultAppIdNotFound,
        DropDownOutcome.StartFailed => Strings.DropDownAppsResultStartFailed,
        DropDownOutcome.NeedsAdminRights => Strings.DropDownAppsResultNeedsAdminRights,
        DropDownOutcome.NoWindowInTime => Strings.DropDownAppsResultNoWindowInTime,
        DropDownOutcome.StayedOnOtherDesktop => Strings.DropDownAppsResultStayedOnOtherDesktop,
        _ => Strings.DropDownAppsResultShortcutTaken,
    };
}
