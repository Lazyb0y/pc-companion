using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Display.Tests;

public sealed class ProfileRecognitionTests
{
    private static readonly DisplayRefreshRate Rate60 = new(60000, 1000);

    [Fact]
    public void ASerialNumberIsKeptOnlyAsAHash()
    {
        var hash = CurrentSetup.HashSerial(new DisplaySerialNumber(0x01020304, "AB12CD34"));

        Assert.Matches("^[0-9a-f]{16}$", hash);
        Assert.Equal(hash, CurrentSetup.HashSerial(new DisplaySerialNumber(0x01020304, "AB12CD34")));
        Assert.NotEqual(hash, CurrentSetup.HashSerial(new DisplaySerialNumber(0x01020304, "AB12CD35")));
        Assert.NotEqual(CurrentSetup.HashSerial(new DisplaySerialNumber(1, "")), CurrentSetup.HashSerial(new DisplaySerialNumber(0, "1")));
        Assert.Equal("", CurrentSetup.HashSerial(null));
    }

    [Fact]
    public void AProfileMadeFromTheSetupHasEveryConnectedDisplayWithTheMainOneFirst()
    {
        var side = Connected(2, "BOE0A1E", DisplayConnection.BuiltIn, name: "");
        var main = Connected(1);
        var off = Connected(3, "SAM7088", DisplayConnection.Hdmi, name: "SAMSUNG");
        var current = Current(
            [side, main, off],
            [Path(side, 1, 1920, 0, hdr: DisplayHdr.NotSupported), Path(main, 0, 0, 0, hdr: DisplayHdr.Off)],
            (main, "5f1c0e9a2b7d4c61"));

        var profile = current.ToProfile("  Desk ");

        Assert.Equal("Desk", profile.Name);
        Assert.Equal([main.DevicePath, side.DevicePath, off.DevicePath], profile.Displays.Select(display => display.Connection));
        Assert.Equal(
            new ProfileDisplay
            {
                Model = "DELA0A3",
                Serial = "5f1c0e9a2b7d4c61",
                Connection = main.DevicePath,
                Name = "DELL U2720Q",
                On = true,
                Main = true,
                Width = 1920,
                Height = 1080,
                RefreshRate = new ProfileRefreshRate { Numerator = 60000, Denominator = 1000 },
                Hdr = false,
            },
            profile.Displays[0]);
        Assert.True(profile.Displays[1].BuiltIn);
        Assert.False(profile.Displays[1].Main);
        Assert.Null(profile.Displays[1].Hdr);
        Assert.Equal(new ProfileDisplay { Model = "SAM7088", Connection = off.DevicePath, Name = "SAMSUNG" }, profile.Displays[2]);
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.Check(profile, []));
    }

    [Fact]
    public void AProfileKeepsTheResolutionOfADisplayTurnedOnItsSide()
    {
        var main = Connected(1);
        var portrait = Connected(2);
        var current = Current([main, portrait], [Path(main, 0, 0, 0), Path(portrait, 1, 1920, 0, area: new Size(1080, 1920), orientation: DisplayOrientation.Portrait)]);

        var display = current.ToProfile("Desk").Displays[1];

        Assert.Equal((1920, 1080, DisplayOrientation.Portrait), (display.Width, display.Height, display.Orientation));
        Assert.Equal(new Rectangle(1920, 0, 1080, 1920), display.Area());
    }

    [Fact]
    public void OfDisplaysThatShowTheSamePictureOnlyOneIsTheMainDisplay()
    {
        var first = Connected(1);
        var second = Connected(2);
        var current = Current([first, second], [Path(first, 0, 0, 0), Path(second, 0, 0, 0)]);

        var profile = current.ToProfile("Presenting");

        Assert.Single(profile.Displays, display => display.Main);
        Assert.Equal(DisplayProfileProblem.None, DisplayProfileCheck.Check(profile, []));
    }

    [Fact]
    public void AProfileMatchesTheSetupItWasMadeFrom()
    {
        var current = Desk(out _, out _);

        Assert.True(ProfileRecognition.Matches(current.ToProfile("Desk"), current));
    }

    public static TheoryData<string> Changes() => ["position", "resolution", "refresh rate", "orientation", "hdr", "display turned off", "display turned on"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void AProfileDoesntMatchAnotherSetup(string change)
    {
        var current = Desk(out var main, out var side);
        var profile = current.ToProfile("Desk");
        var paths = current.Setup.Paths.ToList();
        var sidePath = paths[1];
        paths[1] = change switch
        {
            "position" => sidePath with { Area = sidePath.Area with { Y = 100 } },
            "resolution" => sidePath with { Area = sidePath.Area with { Width = 1680, Height = 1050 } },
            "refresh rate" => sidePath with { RefreshRate = new DisplayRefreshRate(60000, 1001) },
            "orientation" => sidePath with { Orientation = DisplayOrientation.LandscapeFlipped },
            "hdr" => sidePath with { Hdr = DisplayHdr.On },
            _ => sidePath,
        };
        if (change == "display turned off")
        {
            paths.RemoveAt(1);
        }

        var displays = current.Setup.Displays.ToList();
        if (change == "display turned on")
        {
            var third = Connected(3, "SAM7088");
            displays.Add(third);
            paths.Add(Path(third, 2, 3840, 0));
        }

        Assert.False(ProfileRecognition.Matches(profile, current with { Setup = new DisplaySetup(displays, paths) }));
    }

    [Fact]
    public void RefreshRatesMatchToTwoDecimalPlaces()
    {
        var current = Desk(out _, out _);
        var profile = current.ToProfile("Desk");
        var paths = current.Setup.Paths.Select(path => path with { RefreshRate = new DisplayRefreshRate(59940, 1000) }).ToList();

        Assert.True(ProfileRecognition.Matches(
            profile with { Displays = [.. profile.Displays.Select(display => display with { RefreshRate = new ProfileRefreshRate { Numerator = 60000, Denominator = 1001 } })] },
            current with { Setup = current.Setup with { Paths = paths } }));
    }

    [Fact]
    public void HdrCountsOnlyWhereTheProfileAndTheDisplayHaveIt()
    {
        var current = Desk(out _, out _);
        var profile = current.ToProfile("Desk");
        var withoutHdr = current with { Setup = current.Setup with { Paths = [.. current.Setup.Paths.Select(path => path with { Hdr = DisplayHdr.NotSupported })] } };

        Assert.True(ProfileRecognition.Matches(profile with { Displays = [.. profile.Displays.Select(display => display with { Hdr = true })] }, withoutHdr));
        Assert.True(ProfileRecognition.Matches(profile with { Displays = [.. profile.Displays.Select(display => display with { Hdr = null })] }, current));
    }

    [Fact]
    public void ADisplayIsRecognizedByItsSerialNumberOnAnyConnection()
    {
        var profile = new DisplayProfile { Displays = [Wanted("DELA0A3", "aaaaaaaaaaaaaaaa", Connected(1).DevicePath)] };
        var elsewhere = Connected(2);
        var other = Connected(1);
        var current = Current([other, elsewhere], [], (elsewhere, "aaaaaaaaaaaaaaaa"), (other, "bbbbbbbbbbbbbbbb"));

        Assert.Equal([elsewhere], ProfileRecognition.Recognize(profile, current).Displays);
    }

    [Fact]
    public void IdenticalDisplaysWithTheSameSerialNumberAreRecognizedByTheirConnections()
    {
        var left = Connected(1);
        var right = Connected(2);
        var profile = new DisplayProfile { Displays = [Wanted("DELA0A3", "aaaaaaaaaaaaaaaa", right.DevicePath), Wanted("DELA0A3", "aaaaaaaaaaaaaaaa", left.DevicePath)] };
        var current = Current([left, right], [], (left, "aaaaaaaaaaaaaaaa"), (right, "aaaaaaaaaaaaaaaa"));

        var recognition = ProfileRecognition.Recognize(profile, current);

        Assert.Equal([right, left], recognition.Displays);
        Assert.False(recognition.InConnectionOrder);
    }

    [Fact]
    public void ADisplayIsRecognizedByItsModelWhenItsTheOnlyOneConnected()
    {
        var profile = new DisplayProfile { Displays = [Wanted("DELA0A3", "aaaaaaaaaaaaaaaa", Connected(1).DevicePath)] };
        var another = Connected(5);

        Assert.Equal([another], ProfileRecognition.Recognize(profile, Current([another, Connected(6, "SAM7088")], [], (another, "cccccccccccccccc"))).Displays);
    }

    [Fact]
    public void IdenticalDisplaysThatCantBeToldApartAreTakenInTheOrderOfTheirConnections()
    {
        var profile = new DisplayProfile { Displays = [Wanted("DELA0A3", "", Connected(2).DevicePath), Wanted("DELA0A3", "", Connected(1).DevicePath)] };
        var first = Connected(7);
        var second = Connected(8);

        var recognition = ProfileRecognition.Recognize(profile, Current([second, first], []));

        Assert.Equal([second, first], recognition.Displays);
        Assert.True(recognition.InConnectionOrder);
        Assert.True(recognition.CanBeUsed);
    }

    [Fact]
    public void ABuiltInDisplayIsRecognizedOnAnyConnection()
    {
        var builtIn = Connected(9, "BOE0A1E", DisplayConnection.BuiltIn);
        var external = Connected(4, "BOE0A1E", DisplayConnection.Hdmi);
        var profile = new DisplayProfile { Displays = [Wanted("BOE0A1E", "", Connected(1, "BOE0A1E").DevicePath) with { BuiltIn = true }] };

        Assert.Equal([builtIn], ProfileRecognition.Recognize(profile, Current([external, builtIn], [])).Displays);
    }

    [Fact]
    public void ADisplayWithoutAnEdidIsRecognizedByItsConnectionOnly()
    {
        var display = Connected(1, "");
        var profile = new DisplayProfile { Displays = [Wanted("", "", display.DevicePath)] };

        Assert.Equal([display], ProfileRecognition.Recognize(profile, Current([display], [])).Displays);
        Assert.False(ProfileRecognition.Recognize(profile, Current([Connected(2, "")], [])).CanBeUsed);
    }

    [Fact]
    public void AProfileCanBeUsedWhenEveryDisplayThatsOnInItIsConnected()
    {
        var connected = Connected(1);
        var profile = new DisplayProfile
        {
            Displays = [Wanted("DELA0A3", "", connected.DevicePath), Wanted("SAM7088", "", Connected(2, "SAM7088").DevicePath) with { On = false }],
        };

        var recognition = ProfileRecognition.Recognize(profile, Current([connected], []));
        Assert.True(recognition.CanBeUsed);
        Assert.Equal([connected, null], recognition.Displays);

        Assert.False(ProfileRecognition.Recognize(profile with { Displays = [.. profile.Displays.Select(display => display with { On = true })] }, Current([connected], [])).CanBeUsed);
    }

    [Fact]
    public void ADisplayThatsOffNeverTakesOneThatADisplayThatsOnNeeds()
    {
        var profile = new DisplayProfile
        {
            Displays = [Wanted("DELA0A3", "bbbbbbbbbbbbbbbb", Connected(2).DevicePath) with { On = false }, Wanted("DELA0A3", "aaaaaaaaaaaaaaaa", Connected(1).DevicePath)],
        };
        var only = Connected(3);

        var recognition = ProfileRecognition.Recognize(profile, Current([only], [], (only, "cccccccccccccccc")));

        Assert.Equal([null, only], recognition.Displays);
        Assert.True(recognition.CanBeUsed);
    }

    // A Dell display as the main display and a Samsung display to its right.
    private static CurrentSetup Desk(out ConnectedDisplay main, out ConnectedDisplay side)
    {
        main = Connected(1);
        side = Connected(2, "SAM7088", DisplayConnection.Hdmi, "SAMSUNG");
        return Current([main, side], [Path(main, 0, 0, 0, hdr: DisplayHdr.Off), Path(side, 1, 1920, 0, hdr: DisplayHdr.Off)]);
    }

    private static CurrentSetup Current(IReadOnlyList<ConnectedDisplay> displays, IReadOnlyList<DisplayPath> paths, params (ConnectedDisplay Display, string Serial)[] serials) =>
        new(new DisplaySetup(displays, paths), serials.ToDictionary(pair => pair.Display.DevicePath, pair => pair.Serial));

    private static ConnectedDisplay Connected(uint target, string model = "DELA0A3", DisplayConnection connection = DisplayConnection.DisplayPort, string name = "DELL U2720Q") =>
        new(
            new DisplayTarget(1, target),
            $@"\\?\DISPLAY#{(model.Length > 0 ? model : "Default_Monitor")}#5&1f2e3d4c&0&UID{target}#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}",
            model,
            name,
            connection,
            [new DisplaySource(1, 0), new DisplaySource(1, 1), new DisplaySource(1, 2)]);

    private static DisplayPath Path(ConnectedDisplay display, uint source, int x, int y, Size? area = null, DisplayOrientation orientation = DisplayOrientation.Landscape, DisplayHdr hdr = DisplayHdr.NotSupported) =>
        new(new DisplaySource(1, source), display.Target, new Rectangle(new Point(x, y), area ?? new Size(1920, 1080)), Rate60, orientation, hdr);

    private static ProfileDisplay Wanted(string model, string serial, string connection) => new()
    {
        Model = model,
        Serial = serial,
        Connection = connection,
        On = true,
        Main = true,
        Width = 1920,
        Height = 1080,
        RefreshRate = new ProfileRefreshRate { Numerator = 60, Denominator = 1 },
    };
}
