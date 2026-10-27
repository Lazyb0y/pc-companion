using System;
using System.Linq;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Display.Tests;

public sealed class DisplayProfileCheckTests
{
    [Theory]
    [InlineData("Desk", DisplayProfileProblem.None)]
    [InlineData("  Desk  ", DisplayProfileProblem.None)]
    [InlineData("", DisplayProfileProblem.NoName)]
    [InlineData("   ", DisplayProfileProblem.NoName)]
    [InlineData("Desk\nTV", DisplayProfileProblem.InvalidName)]
    [InlineData("TV", DisplayProfileProblem.NameInUse)]
    [InlineData(" tv ", DisplayProfileProblem.NameInUse)]
    public void Names(string name, DisplayProfileProblem expected)
    {
        Assert.Equal(expected, DisplayProfileCheck.CheckName(name, ["TV"]));
    }

    [Fact]
    public void NamesAreUpTo60Characters()
    {
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckName(new string('a', 60), []));
        Assert.Equal(DisplayProfileProblem.NameTooLong, DisplayProfileCheck.CheckName(new string('a', 61), []));
    }

    [Fact]
    public void AProfilesNameIsCheckedBeforeItsDisplays()
    {
        var profile = new DisplayProfile { Name = "", Displays = [] };

        Assert.Equal(DisplayProfileProblem.NoName, DisplayProfileCheck.Check(profile, []));
    }

    [Fact]
    public void ADeskWithTwoDisplaysSideBySideIsValid()
    {
        var profile = new DisplayProfile { Name = "Desk", Displays = [MainDisplay(), On(1920, 0)] };

        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.Check(profile, ["TV"]));
    }

    [Fact]
    public void AProfileHasUpTo16Displays()
    {
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), .. Enumerable.Range(0, 15).Select(_ => Off())]));
        Assert.Equal(DisplayProfileProblem.TooManyDisplays, DisplayProfileCheck.CheckDisplays([MainDisplay(), .. Enumerable.Range(0, 16).Select(_ => Off())]));
    }

    [Theory]
    [InlineData("dela0a3", "", "path", "")]
    [InlineData("DELA0A", "", "path", "")]
    [InlineData("DE1A0A3", "", "path", "")]
    [InlineData("DELA0G3", "", "path", "")]
    [InlineData("DELA0A3", "5F1C0E9A2B7D4C61", "path", "")]
    [InlineData("DELA0A3", "5f1c0e9a2b7d4c6", "path", "")]
    [InlineData("DELA0A3", "", "path\twith a tab", "")]
    [InlineData("DELA0A3", "", "path", "DELL\nU2720Q")]
    [InlineData("", "", "", "")]
    public void ADisplayNeedsAValidIdentity(string model, string serial, string connection, string name)
    {
        var display = Off() with { Model = model, Serial = serial, Connection = connection, Name = name };

        Assert.Equal(DisplayProfileProblem.InvalidDisplay, DisplayProfileCheck.CheckDisplays([MainDisplay(), display]));
    }

    [Fact]
    public void ADisplayIsRecognizedByItsModelOrItsConnection()
    {
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), Off() with { Model = "" }]));
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), Off() with { Connection = "" }]));
    }

    [Fact]
    public void ConnectionsAndNamesHaveALimit()
    {
        Assert.Equal(DisplayProfileProblem.InvalidDisplay, DisplayProfileCheck.CheckDisplays([MainDisplay(), Off() with { Connection = new string('a', 513) }]));
        Assert.Equal(DisplayProfileProblem.InvalidDisplay, DisplayProfileCheck.CheckDisplays([MainDisplay(), Off() with { Name = new string('a', 65) }]));
    }

    [Fact]
    public void TheSameConnectionCantBeInAProfileTwice()
    {
        var main = MainDisplay();

        Assert.Equal(DisplayProfileProblem.SameDisplayTwice, DisplayProfileCheck.CheckDisplays([main, Off() with { Connection = main.Connection.ToUpperInvariant() }]));
    }

    [Fact]
    public void AtLeastOneDisplayIsOn()
    {
        Assert.Equal(DisplayProfileProblem.NoDisplayOn, DisplayProfileCheck.CheckDisplays([Off(), Off()]));
        Assert.Equal(DisplayProfileProblem.NoDisplayOn, DisplayProfileCheck.CheckDisplays([]));
    }

    public static TheoryData<ProfileDisplay> OutOfRange() =>
    [
        MainDisplay() with { Width = 0 },
        MainDisplay() with { Height = 16385 },
        MainDisplay() with { RefreshRate = new ProfileRefreshRate { Numerator = 0, Denominator = 1 } },
        MainDisplay() with { RefreshRate = new ProfileRefreshRate { Numerator = 60, Denominator = 0 } },
        MainDisplay() with { RefreshRate = new ProfileRefreshRate { Numerator = 1001, Denominator = 1 } },
        MainDisplay() with { Orientation = (DisplayOrientation)7 },
        MainDisplay() with { X = 31000 },
        MainDisplay() with { Y = -33000 },
    ];

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void ValuesOfDisplaysThatAreOnAreInRange(ProfileDisplay display)
    {
        Assert.Equal(DisplayProfileProblem.OutOfRange, DisplayProfileCheck.CheckDisplays([display]));
    }

    [Fact]
    public void TheModeOfADisplayThatsOffIsntChecked()
    {
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), Off() with { Width = 0, Main = true, X = 50 }]));
    }

    [Fact]
    public void OneDisplayIsTheMainDisplayAtTheTopLeftCorner()
    {
        Assert.Equal(DisplayProfileProblem.NoMainDisplay, DisplayProfileCheck.CheckDisplays([On(0, 0)]));
        Assert.Equal(DisplayProfileProblem.SeveralMainDisplays, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(1920, 0) with { Main = true }]));
        Assert.Equal(DisplayProfileProblem.MainNotAtOrigin, DisplayProfileCheck.CheckDisplays([On(0, 0), On(1920, 0) with { Main = true }]));
    }

    [Fact]
    public void DisplaysWithTheSameAreaShowTheSamePicture()
    {
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(0, 0), On(1920, 0), On(1920, 0)]));
    }

    [Fact]
    public void OtherwiseDisplaysDontOverlap()
    {
        Assert.Equal(DisplayProfileProblem.Overlap, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(1000, 0)]));
        Assert.Equal(DisplayProfileProblem.Overlap, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(0, 0) with { Width = 1280, Height = 720 }]));
    }

    [Fact]
    public void DisplaysLeaveNoGap()
    {
        Assert.Equal(DisplayProfileProblem.Gap, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(2000, 0)]));
        Assert.Equal(DisplayProfileProblem.Gap, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(1920, 0), On(5000, 0)]));
    }

    [Fact]
    public void DisplaysTouchAtAnEdgeOrACorner()
    {
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(-1920, 300)]));
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(500, -1080)]));
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), On(1920, 1080)]));
    }

    [Fact]
    public void ADisplayTurnedOnItsSideTakesItsTurnedArea()
    {
        var portrait = On(-1080, 0) with { Orientation = DisplayOrientation.Portrait };

        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.CheckDisplays([MainDisplay(), portrait]));
        Assert.Equal(DisplayProfileProblem.Gap, DisplayProfileCheck.CheckDisplays([MainDisplay(), portrait with { Orientation = DisplayOrientation.Landscape, X = -2000 }]));
    }

    private static ProfileDisplay MainDisplay() => On(0, 0) with { Main = true };

    private static ProfileDisplay On(int x, int y) => Off() with
    {
        On = true,
        X = x,
        Y = y,
        Width = 1920,
        Height = 1080,
        RefreshRate = new ProfileRefreshRate { Numerator = 60000, Denominator = 1001 },
    };

    private static ProfileDisplay Off() => new()
    {
        Model = "DELA0A3",
        Serial = "5f1c0e9a2b7d4c61",
        Connection = $@"\\?\DISPLAY#DELA0A3#{Guid.NewGuid():N}",
        Name = "DELL U2720Q",
    };
}
