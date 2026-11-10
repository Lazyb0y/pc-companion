using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class DisplayRecognitionTests
{
    [Fact]
    public void ASerialNumberIsKeptOnlyAsAHash()
    {
        var hash = DisplayRecognition.HashSerial(new DisplaySerialNumber(0x01020304, "AB12CD34"));

        Assert.Matches("^[0-9a-f]{16}$", hash);
        Assert.Equal(hash, DisplayRecognition.HashSerial(new DisplaySerialNumber(0x01020304, "AB12CD34")));
        Assert.NotEqual(hash, DisplayRecognition.HashSerial(new DisplaySerialNumber(0x01020304, "AB12CD35")));
        Assert.NotEqual(DisplayRecognition.HashSerial(new DisplaySerialNumber(1, "")), DisplayRecognition.HashSerial(new DisplaySerialNumber(0, "1")));
        Assert.Equal("", DisplayRecognition.HashSerial(null));
    }
}
