using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class FolderOrderTests
{
    private const string Folder = @"C:\Pictures\Night";
    private static readonly IReadOnlyList<string> Pictures = [.. Enumerable.Range(1, 5).Select(i => $@"{Folder}\{i}.jpg")];

    [Fact]
    public void EveryPictureShowsOnceBeforeAnyRepeats()
    {
        var seeds = new Random(7);
        FolderPlace? place = null;
        var shown = new List<string>();

        for (var i = 0; i < Pictures.Count; i++)
        {
            (var picture, place) = FolderOrder.Next(Folder, Pictures, place, () => seeds.Next());
            shown.Add(picture);
        }

        Assert.Equal(Pictures.Order(), shown.Order());
    }

    [Fact]
    public void TheOrderIsShuffled()
    {
        var orders = Enumerable.Range(1, 10).Select(seed => FolderOrder.Next(Folder, Pictures, null, () => seed).Picture).Distinct();

        Assert.True(orders.Count() > 1);
    }

    [Fact]
    public void ANewOrderDoesntStartWithThePictureThatShowedLast()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var seeds = new Random(seed);
            FolderPlace? place = null;
            string last = "";
            for (var i = 0; i < Pictures.Count; i++)
            {
                (last, place) = FolderOrder.Next(Folder, Pictures, place, () => seeds.Next());
            }

            Assert.NotEqual(last, FolderOrder.Next(Folder, Pictures, place, () => seeds.Next()).Picture);
        }
    }

    [Fact]
    public void ANewPictureStartsANewOrder()
    {
        var (_, place) = FolderOrder.Next(Folder, Pictures, null, () => 1);
        IReadOnlyList<string> more = [.. Pictures, $@"{Folder}\6.jpg"];

        var (_, next) = FolderOrder.Next(Folder, more, place, () => 2);

        Assert.Equal(2, next.Seed);
        Assert.Equal(1, next.Next);
        Assert.NotEqual(place.Pictures, next.Pictures);
    }

    [Fact]
    public void APlaceOutOfRangeStartsANewOrder()
    {
        var (_, place) = FolderOrder.Next(Folder, Pictures, null, () => 1);

        Assert.Equal(1, FolderOrder.Next(Folder, Pictures, place with { Next = 99 }, () => 3).Place.Next);
        Assert.Equal(3, FolderOrder.Next(Folder, Pictures, place with { Next = -1 }, () => 3).Place.Seed);
    }

    [Fact]
    public void ThePictureShownLastStaysWithinAPeriod()
    {
        Assert.Null(FolderOrder.Current(Folder, Pictures, null));

        var (first, place) = FolderOrder.Next(Folder, Pictures, null, () => 1);

        Assert.Equal(first, FolderOrder.Current(Folder, Pictures, place));
        Assert.Null(FolderOrder.Current(Folder, [.. Pictures, $@"{Folder}\6.jpg"], place));
        Assert.Null(FolderOrder.Current(@"C:\Elsewhere", Pictures, place));
    }

    [Fact]
    public void AFolderWithOnePictureShowsItEachTime()
    {
        IReadOnlyList<string> one = [$@"{Folder}\1.jpg"];
        var (first, place) = FolderOrder.Next(Folder, one, null, () => 1);
        var (second, _) = FolderOrder.Next(Folder, one, place, () => 2);

        Assert.Equal(one[0], first);
        Assert.Equal(one[0], second);
    }
}
