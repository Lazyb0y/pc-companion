using System;
using System.Linq;
using PCCompanion.Modules.Input.Notice;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// Draws into memory only; nothing is shown.
public sealed class NoticePainterTests
{
    private static readonly NoticeLine CapsOn = new("Caps Lock on", NoticeIcons.Lock);
    private static readonly NoticeLine NumOff = new("Num Lock off", NoticeIcons.Unlock);
    private static readonly NoticeStyle Dark = new(1, 1, NoticeColors.Dark, NoticeFonts.Windows11, RoundedCorners: true, RightToLeft: false);

    [Fact]
    public void TheNoticeIs40EffectivePixelsHighAtAnyScale()
    {
        var small = NoticePainter.Measure([CapsOn], Dark);
        var large = NoticePainter.Measure([CapsOn], Dark with { Scale = 2 });

        Assert.Equal(40, small.Height);
        Assert.Equal(80, large.Height);
        Assert.InRange(large.Width, (small.Width * 2) - 6, (small.Width * 2) + 6);
    }

    [Fact]
    public void ALargerTextSizeMakesItWiderAndTaller()
    {
        var normal = NoticePainter.Measure([CapsOn], Dark);
        var larger = NoticePainter.Measure([CapsOn], Dark with { TextScale = 2.25 });

        Assert.True(larger.Width > normal.Width);
        Assert.True(larger.Height > normal.Height);
    }

    [Fact]
    public void EachKeyGetsALine()
    {
        var layout = NoticePainter.Measure([CapsOn, NumOff], Dark);

        Assert.Equal(2 * layout.RowHeight, layout.Height);
    }

    [Fact]
    public void TheBadgeIsSmaller()
    {
        var notice = NoticePainter.Measure([CapsOn], Dark);
        var badge = NoticePainter.Measure([new NoticeLine("Caps Lock", NoticeIcons.Lock)], Dark with { Badge = true });

        Assert.Equal(28, badge.Height);
        Assert.True(badge.Width < notice.Width);
    }

    [Fact]
    public void InRightToLeftLanguagesTheIconIsOnTheRight()
    {
        var leftToRight = NoticePainter.Measure([CapsOn], Dark);
        var rightToLeft = NoticePainter.Measure([CapsOn], Dark with { RightToLeft = true });

        Assert.Equal(leftToRight.Width, rightToLeft.Width);
        Assert.True(leftToRight.IconX < leftToRight.TextX);
        Assert.True(rightToLeft.IconX > rightToLeft.TextX);

        // Mirrored: the padding, the icon and the 8-pixel gap swap sides.
        Assert.Equal(leftToRight.IconX, rightToLeft.TextX);
        Assert.Equal(leftToRight.Width, rightToLeft.IconX + leftToRight.TextX - 8);
    }

    [Theory]
    [InlineData(0x2C2C2Cu, 0xFFFFFFu)]
    [InlineData(0xF9F9F9u, 0x1A1A1Au)]
    public void TheBoxIsOpaqueInTheWindowsModesColorsWithTextInTheTextColor(uint background, uint text)
    {
        var colors = background == 0x2C2C2Cu ? NoticeColors.Dark : NoticeColors.Light;
        var image = NoticePainter.Paint([CapsOn], Dark with { Colors = colors });

        Assert.Equal(image.Width * image.Height, image.Pixels.Length);
        Assert.Equal(0xFF000000u | background, image.Pixels[(image.Height / 2 * image.Width) + 3]);
        Assert.Contains(0xFF000000u | text, image.Pixels);
    }

    [Fact]
    public void RoundedCornersAreTransparentAndSquareOnesAreNot()
    {
        var rounded = NoticePainter.Paint([CapsOn], Dark);
        var square = NoticePainter.Paint([CapsOn], Dark with { RoundedCorners = false, Fonts = NoticeFonts.Windows10 });

        Assert.Equal(0u, rounded.Pixels[0]);
        Assert.Equal(0xFF000000u | 0x474747u, square.Pixels[0]);
    }

    [Fact]
    public void HighContrastUsesTheThemesColorsWithAWiderBorderAndSquareCorners()
    {
        var colors = NoticeColors.HighContrastTheme(window: 0x000000, text: 0xFFFF00);
        var image = NoticePainter.Paint([CapsOn], Dark with { Colors = colors });

        Assert.Equal(0xFFFFFF00u, image.Pixels[0]);
        Assert.Equal(0xFFFFFF00u, image.Pixels[image.Width + 1]);
        Assert.Equal(0xFF000000u, image.Pixels[(image.Height / 2 * image.Width) + 3]);
    }

    [Fact]
    public void AnIconAndTextAreDrawnOnEachLine()
    {
        var image = NoticePainter.Paint([CapsOn, NumOff], Dark);
        var rowHeight = image.Height / 2;

        bool HasText(int fromRow) => image.Pixels.Skip(fromRow * image.Width).Take(rowHeight * image.Width).Contains(0xFFFFFFFFu);
        Assert.True(HasText(0));
        Assert.True(HasText(rowHeight));
    }

    [Fact]
    public void ALineWithoutAnIconHasItsTextWhereTheOtherLinesHaveTheirs()
    {
        NoticeLine[] lines = [new("German (Germany)", NoticeIcons.Keyboard), new("German", null)];
        var layout = NoticePainter.Measure(lines, Dark);
        var image = NoticePainter.Paint(lines, Dark);

        Assert.Equal(NoticePainter.Measure([lines[0], lines[1] with { Icon = NoticeIcons.Keyboard }], Dark), layout);

        // Ink is anything but the box's color, between the border's rows.
        bool HasInk(int top, int left, int right) =>
            Enumerable.Range(top + 2, layout.RowHeight - 4).Any(y => Enumerable.Range(left, right - left).Any(x => image.Pixels[(y * image.Width) + x] != (0xFF000000u | NoticeColors.Dark.Background)));
        Assert.True(HasInk(0, layout.IconX, layout.TextX));
        Assert.False(HasInk(layout.RowHeight, layout.IconX, layout.TextX));
        Assert.True(HasInk(layout.RowHeight, layout.TextX, layout.Width - layout.IconX));
    }

    [Fact]
    public void ANoticeNeedsALine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticePainter.Measure([], Dark));
    }

    [Fact]
    public void Windows11AndWindows10HaveTheirOwnFonts()
    {
        Assert.Equal(NoticeFonts.Windows11, NoticeFonts.For(22000));
        Assert.Equal(NoticeFonts.Windows10, NoticeFonts.For(19045));
    }
}
