using PCCompanion.Core.Text;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

// Every character is 10 units wide.
public sealed class EndTrimmingTests
{
    private const string Path = @"C:\Windows\System32\charmap.exe";

    [Theory]
    [InlineData("", 0)]
    [InlineData(Path, 310)]
    [InlineData(Path, 1000)]
    public void TextThatFitsIsKept(string text, double width)
    {
        Assert.Equal(text, EndTrimming.KeepEnd(text, width, Measure, @"\"));
    }

    [Theory]
    [InlineData(300, @"…\Windows\System32\charmap.exe")]
    [InlineData(200, @"…\charmap.exe")]
    [InlineData(210, @"…System32\charmap.exe")]
    [InlineData(220, @"…\System32\charmap.exe")]
    [InlineData(120, @"…charmap.exe")]
    [InlineData(50, @"….exe")]
    [InlineData(5, @"…")]
    public void APathKeepsItsEndFromAFolderBoundary(double width, string expected)
    {
        Assert.Equal(expected, EndTrimming.KeepEnd(Path, width, Measure, @"\"));
    }

    [Fact]
    public void WithoutSeparatorsTheLongestEndIsKept()
    {
        Assert.Equal(@"…stem32\charmap.exe", EndTrimming.KeepEnd(Path, 190, Measure));
    }

    private static double Measure(string text) => text.Length * 10;
}
