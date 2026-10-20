using System;
using System.Globalization;
using System.Linq;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.SystemMeters;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class MeterTextTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static TheoryData<string> AllLanguages { get; } = [.. Languages.Available.Select(language => language.Name)];

    [Theory]
    [InlineData(41.5, 42)]
    [InlineData(0.4, 0)]
    [InlineData(99.6, 100)]
    [InlineData(130, 100)]
    [InlineData(-2, 0)]
    public void PercentagesAreWholeNumbers(double percent, int whole)
    {
        Assert.Equal(whole, MeterText.WholePercent(percent));
    }

    [Theory]
    [InlineData(0, "0 Kbps")]
    [InlineData(400, "0.4 Kbps")]
    [InlineData(56_000, "56.0 Kbps")]
    [InlineData(120_000, "120 Kbps")]
    [InlineData(999_960, "1.0 Mbps")]
    [InlineData(1_200_000, "1.2 Mbps")]
    [InlineData(940_000_000, "940 Mbps")]
    [InlineData(2_500_000_000, "2.5 Gbps")]
    public void RatesAreInBitsPerSecondAsTaskManagerShowsThem(double bitsPerSecond, string text)
    {
        using var language = new UiLanguage("en");

        Assert.Equal(text, MeterText.Rate(bitsPerSecond, English));
    }

    [Fact]
    public void NumbersFollowTheRegionalSettingsAndUnitsTheLanguage()
    {
        using var language = new UiLanguage("de");
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("56,0 KBit/s", MeterText.Rate(56_000, german));
        Assert.Equal("9,7 GB", MeterText.Size(10_415_216_640, german));
    }

    [Fact]
    public void PercentagesUseTheCulturesFormat()
    {
        Assert.Equal("42%", MeterText.Percent(42, English));
        Assert.Matches(@"^42\s%$", MeterText.Percent(42, CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void TooltipsNameTheMeterAndGiveItsValues()
    {
        using var language = new UiLanguage("en");

        Assert.Equal("CPU: 42%", MeterText.CpuToolTip(42.2, English));
        Assert.Equal(
            "Memory: 75%\n12.0 GB of 16.0 GB in use",
            MeterText.MemoryToolTip(new MemoryUse(75, 12UL << 30, 16UL << 30), English));
        Assert.Equal(
            "Network\nReceive: 1.2 Mbps\nSend: 80.0 Kbps",
            MeterText.NetworkToolTip(new NetworkRates(1_200_000, 80_000), English));
    }

    // Windows cuts a notification icon's tooltip after 127 characters.
    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void TooltipsFitInEveryLanguage(string name)
    {
        using var language = new UiLanguage(name);
        var culture = CultureInfo.GetCultureInfo(name);

        Assert.InRange(MeterText.CpuToolTip(100, culture).Length, 1, 127);
        Assert.InRange(MeterText.MemoryToolTip(new MemoryUse(100, 1023UL << 30, 1024UL << 30), culture).Length, 1, 127);
        Assert.InRange(MeterText.NetworkToolTip(new NetworkRates(999_000_000, 999_000_000), culture).Length, 1, 127);
    }

    // Sets the language that Strings uses on this thread, until disposed.
    private sealed class UiLanguage : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public UiLanguage(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
