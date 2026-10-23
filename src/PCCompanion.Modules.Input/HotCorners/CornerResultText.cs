using System;
using System.Globalization;
using System.Text;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// How the Hot Corners page shows a recent result, in the user interface language and the user's
/// regional format, such as "Top left, Task View: skipped because an app was full screen".
/// </summary>
public static class CornerResultText
{
    private static readonly CompositeFormat ResultFormat = CompositeFormat.Parse(Strings.HotCornersResult);
    private static readonly CompositeFormat TimeAndCountFormat = CompositeFormat.Parse(Strings.HotCornersResultTimeAndCount);

    public static string Describe(CornerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Format(CultureInfo.CurrentCulture, ResultFormat, CornerName(result.Corner), HotCornerActionNames.NameOf(result.Action), Outcome(result.Kind));
    }

    /// <summary>When it happened, such as "12:41:05", and how many times in a row, if more than once.</summary>
    public static string When(CornerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var time = result.Time.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
        return result.Count > 1
            ? string.Format(CultureInfo.CurrentCulture, TimeAndCountFormat, time, PluralStrings.Format("HotCornersResultCount", result.Count))
            : time;
    }

    private static string CornerName(ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => Strings.HotCornersTopLeft,
        ScreenCorner.TopRight => Strings.HotCornersTopRight,
        ScreenCorner.BottomLeft => Strings.HotCornersBottomLeft,
        _ => Strings.HotCornersBottomRight,
    };

    private static string Outcome(CornerResultKind kind) => kind switch
    {
        CornerResultKind.Ran => Strings.HotCornersResultRan,
        CornerResultKind.LeftEarly => Strings.HotCornersResultLeftEarly,
        CornerResultKind.MouseButtonHeld => Strings.HotCornersResultMouseButtonHeld,
        CornerResultKind.KeyNotHeld => Strings.HotCornersResultKeyNotHeld,
        CornerResultKind.PointerConfined => Strings.HotCornersResultPointerConfined,
        CornerResultKind.PointerHidden => Strings.HotCornersResultPointerHidden,
        CornerResultKind.FullScreenApp => Strings.HotCornersResultFullScreenApp,
        CornerResultKind.AppExcluded => Strings.HotCornersResultAppExcluded,
        CornerResultKind.Refused => Strings.HotCornersResultRefused,
        CornerResultKind.ProgramAsAdministrator => Strings.HotCornersResultProgramAsAdministrator,
        CornerResultKind.ProgramNotChosen => Strings.HotCornersResultProgramNotChosen,
        CornerResultKind.ProgramNotFound => Strings.HotCornersResultProgramNotFound,
        CornerResultKind.ProgramNotExe => Strings.HotCornersResultProgramNotExe,
        _ => Strings.HotCornersResultProgramCantStart,
    };
}
