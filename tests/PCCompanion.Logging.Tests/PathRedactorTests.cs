using System;
using Xunit;

namespace PCCompanion.Logging.Tests;

public sealed class PathRedactorTests
{
    private readonly PathRedactor _redactor = new(@"C:\Users\Alex");

    [Theory]
    [InlineData(@"Can't open C:\Users\Alex\AppData\Local\PCCompanion\settings.json.", @"Can't open %USERPROFILE%\AppData\Local\PCCompanion\settings.json.")]
    [InlineData(@"c:\users\alex\Documents", @"%USERPROFILE%\Documents")]
    [InlineData(@"Home is C:\Users\Alex", @"Home is %USERPROFILE%")]
    [InlineData(@"'C:\Users\Alex' and C:\Users\Alex/Desktop", @"'%USERPROFILE%' and %USERPROFILE%/Desktop")]
    public void ReplacesTheUserProfile(string text, string expected)
    {
        Assert.Equal(expected, _redactor.Redact(text));
    }

    [Theory]
    [InlineData(@"C:\Users\Alexandra\Documents")]
    [InlineData(@"C:\Users\Alex.Old\Documents")]
    [InlineData(@"D:\Users\Alex\Documents")]
    [InlineData("No paths here.")]
    public void LeavesOtherTextAsItIs(string text)
    {
        Assert.Equal(text, _redactor.Redact(text));
    }

    [Fact]
    public void IgnoresATrailingSeparator()
    {
        Assert.Equal(@"%USERPROFILE%\Music", new PathRedactor(@"C:\Users\Alex\").Redact(@"C:\Users\Alex\Music"));
    }

    [Fact]
    public void TreatsTheProfileAsPlainText()
    {
        var redactor = new PathRedactor(@"C:\Users\A.B (Work)");

        Assert.Equal(@"%USERPROFILE%\x", redactor.Redact(@"C:\Users\A.B (Work)\x"));
        Assert.Equal(@"C:\Users\AxB (Work)\x", redactor.Redact(@"C:\Users\AxB (Work)\x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DoesNothingWithoutAProfile(string? userProfile)
    {
        Assert.Equal(@"C:\Users\Alex", new PathRedactor(userProfile).Redact(@"C:\Users\Alex"));
    }

    [Fact]
    public void ForCurrentUserRedactsTheCurrentProfile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(@"%USERPROFILE%\x", PathRedactor.ForCurrentUser().Redact(profile + @"\x"));
    }
}
