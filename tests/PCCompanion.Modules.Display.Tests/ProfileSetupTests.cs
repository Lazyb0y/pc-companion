using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Modules.Display.Tests.TestDisplays;

namespace PCCompanion.Modules.Display.Tests;

public sealed class ProfileSetupTests
{
    [Fact]
    public void AProfileMadeFromTheSetupBuildsTheSameSetup()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        var current = Current([main, side], [Path(main, 1, 0, 0, hdr: DisplayHdr.Off), Path(side, 0, 1920, 0)]);
        var profile = current.ToProfile("Desk");

        var setup = ProfileSetup.Build(profile, current, ProfileRecognition.Recognize(profile, current));

        Assert.Equal(current.Setup.Paths.OrderBy(path => path.Target.Id), setup!.OrderBy(path => path.Target.Id));
    }

    [Fact]
    public void ASetupIsBuiltOnTheConnectionsTheDisplaysAreOnNow()
    {
        var profile = Profile(OnAt(Connected(1).DevicePath, 0, 0, main: true), OnAt(Connected(2, "SAM7088").DevicePath, 1920, 0));
        var main = Connected(7);
        var side = Connected(8, "SAM7088");
        var current = Current([main, side], [Path(main, 0, 0, 0)]);

        var setup = ProfileSetup.Build(profile, current, ProfileRecognition.Recognize(profile, current))!;

        Assert.Equal([main.Target, side.Target], setup.Select(path => path.Target));
        Assert.Equal([new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1080)], setup.Select(path => path.Area));
        Assert.Equal(new DisplaySource(1, 0), setup[0].Source);
        Assert.NotEqual(setup[0].Source, setup[1].Source);
        Assert.Equal(new DisplayRefreshRate(60000, 1001), setup[1].RefreshRate);
    }

    [Fact]
    public void DisplaysThatShowTheSamePictureShareASource()
    {
        var first = Connected(1);
        var second = Connected(2) with { Sources = [new DisplaySource(1, 1), new DisplaySource(1, 2)] };
        var profile = Profile(OnAt(first.DevicePath, 0, 0, main: true), OnAt(second.DevicePath, 0, 0));
        var current = Current([first, second], [Path(first, 0, 0, 0)]);

        var setup = ProfileSetup.Build(profile, current, ProfileRecognition.Recognize(profile, current))!;

        Assert.Equal(setup[0].Source, setup[1].Source);
        Assert.Equal(new DisplaySource(1, 1), setup[0].Source);
    }

    [Fact]
    public void WithoutASourceForEachPictureNoSetupIsBuilt()
    {
        var first = Connected(1) with { Sources = [new DisplaySource(1, 0)] };
        var second = Connected(2) with { Sources = [new DisplaySource(2, 0)] };
        var profile = Profile(OnAt(first.DevicePath, 0, 0, main: true), OnAt(second.DevicePath, 0, 0));
        var current = Current([first, second], []);

        Assert.Null(ProfileSetup.Build(profile, current, ProfileRecognition.Recognize(profile, current)));
    }

    [Fact]
    public void DisplaysThatArentOnInTheProfileGetNoPath()
    {
        var main = Connected(1);
        var other = Connected(2, "SAM7088");
        var profile = Profile(OnAt(main.DevicePath, 0, 0, main: true), Off(other.DevicePath, "SAM7088"));
        var current = Current([main, other, Connected(3, "BOE0A1E")], [Path(main, 0, 0, 0), Path(other, 1, 1920, 0)]);

        Assert.Equal([main.Target], ProfileSetup.Build(profile, current, ProfileRecognition.Recognize(profile, current))!.Select(path => path.Target));
    }

    [Fact]
    public void ASetupCarriesTheHdrTheProfileWants()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        var profile = Profile(OnAt(main.DevicePath, 0, 0, main: true) with { Hdr = true }, OnAt(side.DevicePath, 1920, 0));
        var current = Current([main, side], []);

        var setup = ProfileSetup.Build(profile, current, ProfileRecognition.Recognize(profile, current))!;

        Assert.Equal([DisplayHdr.On, DisplayHdr.NotSupported], setup.Select(path => path.Hdr));
    }

    [Fact]
    public void MovingDisplaysOrChangingTheMainDisplayDoesntAsk()
    {
        var before = Desk();
        var swapped = new DisplayPath[]
        {
            before.Paths[0] with { Area = before.Paths[0].Area with { X = 1920 } },
            before.Paths[1] with { Area = before.Paths[1].Area with { X = 0 } },
        };

        Assert.False(ProfileSetup.AsksToKeep(before, before.Paths));
        Assert.False(ProfileSetup.AsksToKeep(before, swapped));
        Assert.False(ProfileSetup.AsksToKeep(before, [before.Paths[0], before.Paths[1] with { Area = before.Paths[1].Area with { Y = 300 } }]));
    }

    public static TheoryData<string> RiskyChanges() => ["resolution", "refresh rate", "orientation", "hdr", "display turned off", "display turned on"];

    [Theory]
    [MemberData(nameof(RiskyChanges))]
    public void AChangeThatCouldLeaveADisplayBlackAsks(string change)
    {
        var before = Desk();
        var side = before.Paths[1];
        IReadOnlyList<DisplayPath> after = change switch
        {
            "resolution" => [before.Paths[0], side with { Area = side.Area with { Width = 1680, Height = 1050 } }],
            "refresh rate" => [before.Paths[0], side with { RefreshRate = new DisplayRefreshRate(144, 1) }],
            "orientation" => [before.Paths[0], side with { Orientation = DisplayOrientation.LandscapeFlipped }],
            "hdr" => [before.Paths[0], side with { Hdr = DisplayHdr.On }],
            "display turned off" => [before.Paths[0]],
            _ => [.. before.Paths, Path(Connected(3, "BOE0A1E"), 2, 3840, 0)],
        };

        Assert.True(ProfileSetup.AsksToKeep(before, after));
    }

    [Fact]
    public void HdrThatADisplayDoesntSupportDoesntAsk()
    {
        var before = Desk();
        var withoutHdr = before with { Paths = [before.Paths[0], before.Paths[1] with { Hdr = DisplayHdr.NotSupported }] };

        Assert.False(ProfileSetup.AsksToKeep(withoutHdr, [before.Paths[0], before.Paths[1] with { Hdr = DisplayHdr.On }]));
    }

    [Fact]
    public void HdrIsSetWhereTheDisplayHasItOtherwise()
    {
        var after = Desk();

        // The main display doesn't support HDR; the other has it off.
        var wanted = new[] { after.Paths[0] with { Hdr = DisplayHdr.On }, after.Paths[1] with { Hdr = DisplayHdr.On } };

        Assert.Equal([(after.Paths[1].Target, true)], ProfileSetup.HdrToSet(wanted, after));
        Assert.Empty(ProfileSetup.HdrToSet(after.Paths, after));
        Assert.Empty(ProfileSetup.HdrToSet([after.Paths[1] with { Hdr = DisplayHdr.NotSupported }], after));
    }

    [Fact]
    public void AModeThatsntListedForADisplayIsFound()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        var profile = Profile(
            OnAt(main.DevicePath, 0, 0, main: true),
            OnAt(side.DevicePath, 1920, 0) with { Width = 3840, Height = 2160 },
            OnAt(Connected(3, "BOE0A1E").DevicePath, 5760, 0) with { Model = "BOE0A1E" });
        var current = Current([main, side, Connected(3, "BOE0A1E")], [Path(main, 0, 0, 0), Path(side, 1, 1920, 0)]);
        IReadOnlyList<DisplayMode> Modes(DisplaySource source) => [new DisplayMode(1920, 1080, 59), new DisplayMode(1280, 720, 60)];

        // 59.94 Hz is listed as 59; the third display is off, so its modes can't be listed.
        Assert.Equal([1], ProfileSetup.ModesNotOffered(profile, current, ProfileRecognition.Recognize(profile, current), Modes));
    }

    [Fact]
    public void TheDisplaysThatWindowsAppliedInItsOwnWayAreFound()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        var current = Current([main, side], [Path(main, 0, 0, 0), Path(side, 1, 1920, 0)]);
        var profile = current.ToProfile("Desk");
        var applied = current with { Setup = current.Setup with { Paths = [current.Setup.Paths[0], current.Setup.Paths[1] with { RefreshRate = new DisplayRefreshRate(30, 1) }] } };

        Assert.Empty(ProfileRecognition.DisplaysNotAsInProfile(profile, current, ProfileRecognition.Recognize(profile, current)));
        Assert.Equal([1], ProfileRecognition.DisplaysNotAsInProfile(profile, applied, ProfileRecognition.Recognize(profile, applied)));
    }

    // A Dell display as the main display, and a Samsung display with HDR off to its right.
    private static DisplaySetup Desk()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        return new DisplaySetup([main, side], [Path(main, 0, 0, 0), Path(side, 1, 1920, 0, hdr: DisplayHdr.Off)]);
    }

    private static DisplayProfile Profile(params ProfileDisplay[] displays) => new() { Name = "Desk", Displays = displays };

    private static ProfileDisplay OnAt(string connection, int x, int y, bool main = false) => Off(connection, connection.Split('#')[1]) with
    {
        On = true,
        Main = main,
        X = x,
        Y = y,
        Width = 1920,
        Height = 1080,
        RefreshRate = new ProfileRefreshRate { Numerator = 60000, Denominator = 1001 },
    };

    private static ProfileDisplay Off(string connection, string model) => new() { Model = model, Connection = connection };
}
