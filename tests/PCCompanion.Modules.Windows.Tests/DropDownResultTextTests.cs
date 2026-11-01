using System;
using System.Globalization;
using System.Linq;
using PCCompanion.Modules.Windows.DropDownApps;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class DropDownResultTextTests : IDisposable
{
    private static readonly DateTimeOffset Time = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _language = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _format = CultureInfo.CurrentCulture;

    public DropDownResultTextTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _language;
        CultureInfo.CurrentCulture = _format;
    }

    [Fact]
    public void AResultNamesTheAppAtItsPlaceInTheList()
    {
        var apps = new[] { new DropDownApp { Name = "Terminal", AppId = "Terminal.App" } };

        Assert.Equal(
            "Terminal: runs as administrator, so it can't drop down while PC Companion doesn't",
            DropDownResultText.Describe(new DropDownResult(Time, 0, DropDownOutcome.RunsAsAdministrator, 1), apps));
    }

    [Fact]
    public void AnAppWithoutANameIsNamedByItsProgram()
    {
        var apps = new[] { new DropDownApp { Program = @"C:\Apps\Notes.exe" } };

        Assert.StartsWith("Notes: ", DropDownResultText.Describe(new DropDownResult(Time, 0, DropDownOutcome.Shown, 1), apps), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAppNoLongerListedSaysSo()
    {
        Assert.Equal("An app no longer listed: hidden", DropDownResultText.Describe(new DropDownResult(Time, 3, DropDownOutcome.Hidden, 1), []));
    }

    [Fact]
    public void EachOutcomeHasTextOfItsOwn()
    {
        var texts = Enum.GetValues<DropDownOutcome>()
            .Select(outcome => DropDownResultText.Describe(new DropDownResult(Time, 0, outcome, 1), [new DropDownApp { Name = "App" }]))
            .ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void AResultThatCameSeveralTimesSaysHowMany()
    {
        Assert.EndsWith("(3 times)", DropDownResultText.When(new DropDownResult(Time, 0, DropDownOutcome.Shown, 3)), StringComparison.Ordinal);
        Assert.DoesNotContain("(", DropDownResultText.When(new DropDownResult(Time, 0, DropDownOutcome.Shown, 1)), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyWhatTheShortcutWasPressedForCountsAsDone()
    {
        Assert.True(DropDownResultText.IsDone(DropDownOutcome.Started));
        Assert.False(DropDownResultText.IsDone(DropDownOutcome.NoWindowInTime));
    }
}
