using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads this PC's displays and their setup; nothing is changed.
public sealed class DisplayConfigurationTests
{
    [Fact]
    public void OneDisplayThatsOnIsAtTheOrigin()
    {
        var setup = DisplayConfiguration.Read();

        Assert.Single(setup.Paths.Where(path => path.Area.Location == Point.Empty).Select(path => path.Source).Distinct());
    }

    [Fact]
    public void EachSourceInUseIsADisplayOfTheDesktop()
    {
        // Displays that show the same picture share a source, and are one display of the desktop.
        var setup = DisplayConfiguration.Read();

        Assert.Equal(Displays.GetAreas().Count, setup.Paths.Select(path => path.Source).Distinct().Count());
    }

    [Fact]
    public void DisplaysThatAreOnAreConnectedToTheirSource()
    {
        var setup = DisplayConfiguration.Read();

        Assert.NotEmpty(setup.Paths);
        Assert.All(setup.Paths, path =>
            Assert.Contains(setup.Displays, display => display.Target == path.Target && display.Sources.Contains(path.Source)));
    }

    [Fact]
    public void DisplaysThatAreOnHaveAnAreaAndARefreshRate()
    {
        Assert.All(DisplayConfiguration.Read().Paths, path =>
        {
            Assert.False(path.Area.IsEmpty);
            Assert.InRange(path.RefreshRate.Hertz, 1, 1000);
        });
    }

    [Fact]
    public void ADisplaysModelIsTheOneInItsDevicePath()
    {
        // As in Windows' hardware IDs: \\?\DISPLAY#DELA0A3#...
        Assert.All(DisplayConfiguration.Read().Displays.Where(display => display.Model.Length > 0), display =>
            Assert.Contains($"#{display.Model}#", display.DevicePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheModeInUseIsAmongTheListedModes()
    {
        Assert.All(DisplayConfiguration.Read().Paths, path =>
        {
            var turned = path.Orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;
            var (width, height) = turned ? (path.Area.Height, path.Area.Width) : (path.Area.Width, path.Area.Height);

            Assert.Contains(DisplayConfiguration.ListModes(path.Source), mode =>
                mode.Width == width && mode.Height == height && Math.Abs(mode.RefreshRate - path.RefreshRate.Hertz) < 1);
        });
    }

    [Fact]
    public async Task SerialNumberTextHasOnlyPrintableCharacters()
    {
        foreach (var display in DisplayConfiguration.Read().Displays)
        {
            var serialNumber = await DisplayEdid.ReadSerialNumberAsync(display.DevicePath);

            Assert.True(serialNumber is null || serialNumber.Value.Text.All(c => c is >= ' ' and <= '~'));
        }
    }

    [Fact]
    public void WindowsWouldApplyTheSetupInUse()
    {
        var paths = DisplayConfiguration.Read().Paths;

        if (DisplayConfiguration.IsRemoteSession)
        {
            // Windows refuses every change in a remote session, even to the setup in use.
            Assert.Equal(87, Assert.Throws<Win32Exception>(() => DisplayConfiguration.Check(paths)).NativeErrorCode);
        }
        else
        {
            DisplayConfiguration.Check(paths);
        }
    }

    [Fact]
    public void WindowsRefusesAResolutionTheDisplayDoesntOffer()
    {
        var paths = DisplayConfiguration.Read().Paths;
        var main = paths.First(path => path.Area.Location == Point.Empty);
        Assert.DoesNotContain(DisplayConfiguration.ListModes(main.Source), mode => mode.Width == 1234 && mode.Height == 567);

        Assert.Throws<Win32Exception>(() => DisplayConfiguration.Check(
            [.. paths.Select(path => path.Source == main.Source ? path with { Area = new Rectangle(0, 0, 1234, 567) } : path)]));
    }

    [Fact]
    public void WindowsRefusesARefreshRateTheDisplayDoesntOffer()
    {
        var paths = DisplayConfiguration.Read().Paths;
        var main = paths.First(path => path.Area.Location == Point.Empty);
        Assert.DoesNotContain(DisplayConfiguration.ListModes(main.Source), mode => mode.RefreshRate == 7);

        Assert.Throws<Win32Exception>(() => DisplayConfiguration.Check(
            [.. paths.Select(path => path.Source == main.Source ? path with { RefreshRate = new DisplayRefreshRate(7, 1) } : path)]));
    }

    [Fact]
    public void ASetupWithNoDisplayOnIsRefusedBeforeWindowsIsAsked()
    {
        Assert.Throws<ArgumentException>(() => DisplayConfiguration.Check([]));
    }

    [Fact]
    public void ASourceTheDisplayCantShowIsRefusedBeforeWindowsIsAsked()
    {
        var paths = DisplayConfiguration.Read().Paths;

        Assert.Throws<ArgumentException>(() => DisplayConfiguration.Check(
            [.. paths.Select((path, i) => i == 0 ? path with { Source = path.Source with { Id = 999 } } : path)]));
    }

    [Fact]
    public void DisplaysThatShareASourceNeedTheSameArea()
    {
        var path = DisplayConfiguration.Read().Paths[0];

        Assert.Throws<ArgumentException>(() => DisplayConfiguration.Check(
            [path, path with { Area = path.Area with { Width = path.Area.Width / 2 } }]));
    }

    [Theory]
    [InlineData(60000u, 1001u, 59.94)]
    [InlineData(144000u, 1000u, 144)]
    [InlineData(60u, 0u, 0)]
    public void ARefreshRateIsAFractionOfHertz(uint numerator, uint denominator, double hertz)
    {
        Assert.Equal(hertz, new DisplayRefreshRate(numerator, denominator).Hertz, 2);
    }

    [Fact]
    public void AnEdidGivesItsSerialNumberAndText()
    {
        Assert.Equal(new DisplaySerialNumber(0x01020304, "AB12CD34"), DisplayEdid.SerialNumberOf(Edid(0x01020304, "AB12CD34\n   ")));
    }

    [Fact]
    public void AnEdidCanHaveASerialNumberWithoutText()
    {
        Assert.Equal(new DisplaySerialNumber(7, ""), DisplayEdid.SerialNumberOf(Edid(7, null)));
    }

    [Fact]
    public void AnEdidCanHaveSerialNumberTextWithoutANumber()
    {
        Assert.Equal(new DisplaySerialNumber(0, "XYZ"), DisplayEdid.SerialNumberOf(Edid(0, "XYZ\n", descriptor: 3)));
    }

    [Fact]
    public void AnEdidWithoutASerialNumberGivesNone()
    {
        Assert.Null(DisplayEdid.SerialNumberOf(Edid(0, null)));
    }

    [Fact]
    public void SerialNumberTextLeavesOutOtherCharacters()
    {
        Assert.Equal("ABC", DisplayEdid.SerialNumberOf(Edid(0, "A\tB\u00e9C\nD"))?.Text);
    }

    [Fact]
    public void WhatIsntAnEdidGivesNoSerialNumber()
    {
        var edid = Edid(7, "XYZ\n");

        Assert.Null(DisplayEdid.SerialNumberOf(edid.AsSpan(0, 127)));
        Assert.Null(DisplayEdid.SerialNumberOf([]));
        edid[1] = 0;
        Assert.Null(DisplayEdid.SerialNumberOf(edid));
    }

    // The first block of an EDID, with the display's name in the first of its four descriptors.
    private static byte[] Edid(uint number, string? text, int descriptor = 1)
    {
        var edid = new byte[128];
        byte[] header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        header.CopyTo(edid, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(edid.AsSpan(12), number);
        edid[54 + 3] = 0xFC;
        Encoding.ASCII.GetBytes("DELL U2720Q\n").CopyTo(edid, 54 + 5);
        if (text is not null)
        {
            var offset = 54 + (18 * descriptor);
            edid[offset + 3] = 0xFF;
            Encoding.Latin1.GetBytes(text).CopyTo(edid, offset + 5);
        }

        return edid;
    }
}
