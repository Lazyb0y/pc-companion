using System;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class DynamicWallpaperCheckTests
{
    [Theory]
    [InlineData(@"C:\Pictures\Day.jpg")]
    [InlineData(@"D:\Day.JPEG")]
    [InlineData(@"C:\Pictures\Day.jfif")]
    [InlineData(@"C:\Pictures\Day.png")]
    [InlineData(@"C:\Pictures\Day.bmp")]
    [InlineData(@"C:\Pictures\Day.dib")]
    [InlineData(@"C:\Pictures\Day.tif")]
    [InlineData(@"C:\Pictures\Day.tiff")]
    public void PicturesOnADriveCanBeUsed(string path) =>
        Assert.Equal(SourceProblem.None, DynamicWallpaperCheck.Check(new WallpaperSource { Picture = path }));

    [Theory]
    [InlineData(@"C:\Pictures\Day.heic", SourceProblem.UnsupportedFormat)]
    [InlineData(@"C:\Pictures\Day.webp", SourceProblem.UnsupportedFormat)]
    [InlineData(@"C:\Pictures\Day", SourceProblem.UnsupportedFormat)]
    [InlineData(@"Pictures\Day.jpg", SourceProblem.NotFullPath)]
    [InlineData(@"C:Day.jpg", SourceProblem.NotFullPath)]
    [InlineData(@"\Pictures\Day.jpg", SourceProblem.NotFullPath)]
    [InlineData(@"\\server\share\Day.jpg", SourceProblem.NetworkPath)]
    [InlineData(@"//server/share/Day.jpg", SourceProblem.NetworkPath)]
    [InlineData(@"\\?\C:\Day.jpg", SourceProblem.InvalidPath)]
    [InlineData(@"\\.\C:\Day.jpg", SourceProblem.InvalidPath)]
    [InlineData(@"C:\Pictures\Day.jpg:hidden", SourceProblem.InvalidPath)]
    [InlineData("C:\\Pictures\\Day\n.jpg", SourceProblem.InvalidPath)]
    [InlineData(@"C:\Pictures\*.jpg", SourceProblem.InvalidPath)]
    public void OtherPicturesCant(string path, SourceProblem problem) =>
        Assert.Equal(problem, DynamicWallpaperCheck.Check(new WallpaperSource { Picture = path }));

    [Fact]
    public void AFolderNeedsAFullPathOnly()
    {
        Assert.Equal(SourceProblem.None, DynamicWallpaperCheck.Check(new WallpaperSource { Folder = @"C:\Pictures\Night" }));
        Assert.Equal(SourceProblem.NetworkPath, DynamicWallpaperCheck.Check(new WallpaperSource { Folder = @"\\server\Night" }));
    }

    [Fact]
    public void APeriodNeedsAPictureOrAFolderButNotBoth()
    {
        Assert.Equal(SourceProblem.NoPicture, DynamicWallpaperCheck.Check(new WallpaperSource()));
        Assert.Equal(SourceProblem.NoPicture, DynamicWallpaperCheck.Check(new WallpaperSource { Picture = "  " }));
        Assert.Equal(SourceProblem.PictureAndFolder, DynamicWallpaperCheck.Check(new WallpaperSource { Picture = @"C:\Day.jpg", Folder = @"C:\Night" }));
    }

    [Fact]
    public void TimesMustBeTimesAndDifferent()
    {
        WallpaperPeriod[] times = [new() { At = "07:00" }, new() { At = "7:00" }, new() { At = "24:00" }, new() { At = "19:00" }, new() { At = "07:00" }, new()];

        Assert.Equal(
            [TimeProblem.None, TimeProblem.InvalidTime, TimeProblem.InvalidTime, TimeProblem.None, TimeProblem.SameTime, TimeProblem.InvalidTime],
            DynamicWallpaperCheck.CheckTimes(times));
    }

    [Fact]
    public void TimesAreWrittenWithHoursAndMinutes()
    {
        Assert.True(DynamicWallpaperCheck.TryParseTime("23:59", out var time));
        Assert.Equal(new TimeOnly(23, 59), time);
        Assert.Equal("07:05", DynamicWallpaperCheck.FormatTime(new TimeOnly(7, 5, 30)));
    }

    [Theory]
    [InlineData(23.8, 90.4, true)]
    [InlineData(-90, -180, true)]
    [InlineData(90, 180, true)]
    [InlineData(90.1, 0, false)]
    [InlineData(0, -180.1, false)]
    [InlineData(double.NaN, 0, false)]
    [InlineData(0, double.PositiveInfinity, false)]
    public void CoordinatesMustBeAPlace(double latitude, double longitude, bool place) =>
        Assert.Equal(place, DynamicWallpaperCheck.AreCoordinates(latitude, longitude));

    [Fact]
    public void MissingCoordinatesArentAPlace()
    {
        Assert.False(DynamicWallpaperCheck.AreCoordinates(null, 90.4));
        Assert.False(DynamicWallpaperCheck.AreCoordinates(23.8, null));
    }

    [Fact]
    public void CoordinatesAreKeptWithOneDecimal() =>
        Assert.Equal(new Coordinates(23.8, -90.5), DynamicWallpaperCheck.Round(new Coordinates(23.7639, -90.46)));

    [Fact]
    public void ADisplayNeedsValuesThatRecognizeIt()
    {
        var display = new WallpaperDisplay { Model = "DELF11B", Serial = "5f1c0e9a2b7d4c61", Connection = @"\\?\DISPLAY#DELF11B#5&2a1f3e84&0&UID4352", Name = "DELL E2420H" };

        Assert.True(DynamicWallpaperCheck.IsValid(display));
        Assert.True(DynamicWallpaperCheck.IsValid(display with { Model = "", Serial = "" }));
        Assert.False(DynamicWallpaperCheck.IsValid(display with { Model = "", Connection = "" }));
        Assert.False(DynamicWallpaperCheck.IsValid(display with { Model = "dell" }));
        Assert.False(DynamicWallpaperCheck.IsValid(display with { Serial = "123" }));
        Assert.False(DynamicWallpaperCheck.IsValid(display with { Name = "DELL\nE2420H" }));
        Assert.False(DynamicWallpaperCheck.IsValid(display with { Connection = new string('x', 513) }));
    }
}
