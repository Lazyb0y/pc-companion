using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class MenuThemeTests
{
    [Theory]
    [InlineData(18362, false)]
    [InlineData(19040, false)]
    [InlineData(19041, true)]
    [InlineData(22631, true)]
    [InlineData(MenuTheme.MaxTestedBuild, true)]
    [InlineData(MenuTheme.MaxTestedBuild + 1, false)]
    public void OnlyTestedBuildsAreSupported(int build, bool supported)
    {
        Assert.Equal(supported, MenuTheme.IsSupportedBuild(build));
    }

    [Theory]
    [InlineData(false, false, MenuMode.Light)]
    [InlineData(false, true, MenuMode.Dark)]
    [InlineData(true, false, MenuMode.Default)]
    [InlineData(true, true, MenuMode.Default)]
    public void MenusFollowTheWindowsModeExceptInHighContrast(bool highContrast, bool windowsModeDark, MenuMode expected)
    {
        Assert.Equal(expected, MenuTheme.ForWindowsMode(highContrast, windowsModeDark));
    }

    // Changes only this test process's menus, and it shows none; the default mode is set back.
    [Fact]
    public void TheModeIsSetOnSupportedBuilds()
    {
        try
        {
            Assert.Equal(MenuTheme.IsSupported, MenuTheme.Set(MenuMode.Dark));
            Assert.Equal(MenuTheme.IsSupported, MenuTheme.Set(MenuMode.Light));
        }
        finally
        {
            MenuTheme.Set(MenuMode.Default);
        }
    }

    [Fact]
    public void TheWindowsModeIsRead()
    {
        Assert.True(Enum.IsDefined(MenuTheme.ForWindowsMode()));
    }
}
