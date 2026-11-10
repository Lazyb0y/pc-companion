using Microsoft.Win32;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test reads keys of its own, which stand for the user's and the machine's.
public sealed class WallpaperPoliciesTests
{
    [Fact]
    public void WithoutPoliciesTheBackgroundIsntManaged()
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();

        Assert.False(WallpaperPolicies.AreSet(user.Key, machine.Key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreventChangingDesktopBackgroundManagesIt(bool forTheUser)
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        using (var key = (forTheUser ? user : machine).Key.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop"))
        {
            key.SetValue("NoChangingWallPaper", 0, RegistryValueKind.DWord);
            Assert.False(WallpaperPolicies.AreSet(user.Key, machine.Key));

            key.SetValue("NoChangingWallPaper", 1, RegistryValueKind.DWord);
        }

        Assert.True(WallpaperPolicies.AreSet(user.Key, machine.Key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDesktopWallpaperPolicyManagesIt(bool forTheUser)
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        using (var key = (forTheUser ? user : machine).Key.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System"))
        {
            key.SetValue("Wallpaper", "");
            Assert.False(WallpaperPolicies.AreSet(user.Key, machine.Key));

            key.SetValue("Wallpaper", @"C:\Company\Background.jpg");
        }

        Assert.True(WallpaperPolicies.AreSet(user.Key, machine.Key));
    }

    [Theory]
    [InlineData("DesktopImagePath", @"C:\Company\Background.jpg")]
    [InlineData("DesktopImageUrl", "https://example.com/Background.jpg")]
    [InlineData("DesktopImageStatus", 1)]
    public void DeviceManagementsBackgroundManagesIt(string name, object value)
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        using (var key = machine.Key.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP"))
        {
            key.SetValue(name, value);
        }

        Assert.True(WallpaperPolicies.AreSet(user.Key, machine.Key));
    }
}
