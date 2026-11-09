using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace PCCompanion.Architecture.Tests;

/// <summary>
/// The settings app's images of the feature icons are made from the drawings in docs/design/icons
/// by scripts/feature-icons.ps1, which records the drawings it made them from in drawings.txt.
/// </summary>
public sealed partial class FeatureIconImagesTests
{
    private const string Drawings = "docs/design/icons";
    private const string Images = "src/PCCompanion.App/Assets/Icons";

    // The sizes feature-icons.ps1 makes each image at, which the settings app's FeatureIcon picks from.
    private static readonly int[] PixelSizes = [20, 25, 30, 32, 40, 48, 64];

    private readonly string _root = RepositoryProjects.FindRoot();

    [Fact]
    public void TheImagesWereMadeFromTheDrawingsAsTheyAreNow()
    {
        var current = FeatureDrawings().Select(path => $"{Path.GetFileName(path)} {Fingerprint(path)}").ToList();
        var recorded = File.ReadAllLines(Path.Combine(_root, Images, "drawings.txt")).Where(line => line.Length > 0).ToList();

        var stale = current.Except(recorded, StringComparer.Ordinal)
            .Concat(recorded.Except(current, StringComparer.Ordinal))
            .Select(line => line.Split(' ')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(drawing => $"{drawing}: run scripts/feature-icons.ps1 to make the images from the drawings as they are now");

        Assert.Empty(stale);
    }

    [Fact]
    public void EveryFeatureIconHasItsImagesAtEverySize()
    {
        var missing = FeatureDrawings()
            .Select(path => DrawingName().Match(Path.GetFileName(path)).Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(name => PixelSizes.SelectMany(size => new[] { $"{name}-{size}.png", $"{name}-{size}-mono.png" }))
            .Where(image => !File.Exists(Path.Combine(_root, Images, image)));

        Assert.Empty(missing);
    }

    // The feature icons' drawings and their one-color versions, such as hot-corners-16.svg and
    // hot-corners-16-mono.svg, but not the app icon's.
    private IEnumerable<string> FeatureDrawings() =>
        Directory.EnumerateFiles(Path.Combine(_root, Drawings), "*.svg")
            .Where(path => DrawingName().Match(Path.GetFileName(path)) is { Success: true } match && match.Groups["name"].Value != "app")
            .Order(StringComparer.Ordinal);

    // The drawing's text with its line endings as Git stores them, as feature-icons.ps1 takes it.
    private static string Fingerprint(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal))));

    [GeneratedRegex(@"^(?<name>.+)-(?<size>\d+)(-mono)?\.svg$")]
    private static partial Regex DrawingName();
}
