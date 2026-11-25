using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class RememberedPlacesTests : IDisposable
{
    private static readonly Rectangle WorkArea = new(0, 0, 2000, 1000);
    private static readonly RuleDisplay Desk = new() { Which = "Chosen", Model = "DEL41A3", Serial = "4c1f9e0b2a7d3e85", Name = "DELL U2720Q" };

    private readonly RememberedPlaces _places = new();
    private readonly TemporaryFolder _folder = new();
    private int _changes;

    public RememberedPlacesTests()
    {
        _places.Changed += (_, _) => _changes++;
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void APlaceIsKeptInFractionsOfTheWorkAreaForEachSetOfDisplays()
    {
        _places.Remember("mail", "DESK", Desk, new Rectangle(500, 100, 1000, 800), WorkArea, maximized: true);
        _places.Remember("mail", "LAPTOP", Desk, new Rectangle(0, 0, 500, 500), WorkArea, maximized: false);

        var place = _places.Find("mail", "DESK")!;
        Assert.Equal((0.25, 0.1, 0.5, 0.8, true), (place.Left, place.Top, place.Width, place.Height, place.Maximized));
        Assert.Equal(new Rectangle(1000, 200, 2000, 1600), place.FrameIn(new Rectangle(0, 0, 4000, 2000)));
        Assert.Equal("DEL41A3", place.Display!.Model);
        Assert.Equal(0.25, _places.Find("mail", "LAPTOP")!.Width);
        Assert.Null(_places.Find("mail", "TV"));
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void TheSamePlaceAgainChangesNothingAndANewOneReplacesTheOld()
    {
        _places.Remember("mail", "DESK", Desk, new Rectangle(500, 100, 1000, 800), WorkArea, maximized: false);
        _places.Remember("mail", "DESK", Desk, new Rectangle(500, 100, 1000, 800), WorkArea, maximized: false);
        Assert.Equal(1, _changes);

        _places.Remember("mail", "DESK", Desk, new Rectangle(0, 0, 1000, 800), WorkArea, maximized: false);
        Assert.Equal(0, _places.Find("mail", "DESK")!.Left);
        Assert.Single(_places.Saved.Single().Places);
    }

    [Fact]
    public void ARuleKeepsTenSetsOfDisplaysAndTheOneUsedLongestAgoGoes()
    {
        for (var set = 0; set < RememberedPlaces.PlacesPerRule + 1; set++)
        {
            _places.Remember("mail", $"SET{set}", Desk, new Rectangle(0, 0, 100, 100), WorkArea, maximized: false);
        }

        Assert.Equal(RememberedPlaces.PlacesPerRule, _places.Saved.Single().Places.Count);
        Assert.Null(_places.Find("mail", "SET0"));
        Assert.NotNull(_places.Find("mail", "SET10"));
    }

    [Fact]
    public void APlaceThatSticksOutIsKeptInsideTheWorkArea()
    {
        _places.Remember("mail", "DESK", Desk, new Rectangle(1500, 900, 1000, 800), WorkArea, maximized: false);

        Assert.Equal(new Rectangle(1000, 200, 1000, 800), _places.Find("mail", "DESK")!.FrameIn(WorkArea));
    }

    [Fact]
    public void AnOpacityIsKeptForARule()
    {
        _places.RememberOpacity("mail", 60);
        _places.RememberOpacity("mail", 60);

        Assert.Equal(60, _places.OpacityOf("mail"));
        Assert.Null(_places.OpacityOf("notes"));
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void RulesThatAreGoneAreForgotten()
    {
        _places.RememberOpacity("mail", 60);
        _places.RememberOpacity("notes", 40);

        _places.Keep(new HashSet<string> { "notes" });

        Assert.Null(_places.OpacityOf("mail"));
        Assert.Equal(40, _places.OpacityOf("notes"));
    }

    [Fact]
    public void ARulesKeyIsItsIdOrElseItsName()
    {
        Assert.Equal("1f2e", RememberedPlaces.KeyOf(new WindowRule { Id = "1f2e", Name = "Mail" }));
        Assert.Equal("name:Mail", RememberedPlaces.KeyOf(new WindowRule { Name = "Mail" }));
    }

    [Fact]
    public void TheFileKeepsWhatsRememberedAndWhatCantBeUsedIsLeftOutAsItsLoaded()
    {
        var store = new RememberedPlacesStore(Path.Combine(_folder.FullPath, RememberedPlacesStore.FileName));
        _places.Remember("mail", "DESK", Desk, new Rectangle(500, 100, 1000, 800), WorkArea, maximized: true);
        _places.RememberOpacity("notes", 40);
        store.Save(_places.Saved);

        var loaded = new RememberedPlaces();
        loaded.Load(store.Load(out var readable));

        Assert.True(readable);
        Assert.Equal(_places.Find("mail", "DESK"), loaded.Find("mail", "DESK"));
        Assert.Equal(40, loaded.OpacityOf("notes"));
        Assert.DoesNotContain("Title", File.ReadAllText(store.FilePath), StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(store.FilePath, """{ "version": 1, "rules": [ null, { "rule": "", "places": [] }, { "rule": "bad", "places": [ { "displays": "X", "left": 2, "top": 0, "width": 0.5, "height": 0.5, "display": { "model": "M" } }, null ], "opacity": 5 } ] }""");
        var bad = new RememberedPlaces();
        bad.Load(store.Load(out readable));
        Assert.True(readable);
        Assert.Empty(bad.Saved);

        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Empty(store.Load(out readable));
        Assert.False(readable);

        store.Delete();
        Assert.False(File.Exists(store.FilePath));
        Assert.Empty(store.Load(out readable));
        Assert.True(readable);
    }
}
