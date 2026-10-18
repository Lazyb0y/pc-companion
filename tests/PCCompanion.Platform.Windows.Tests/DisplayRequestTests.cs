using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Holds a power request for a moment; it changes no setting and ends when disposed.
public sealed class DisplayRequestTests
{
    [Fact]
    public void ARequestStartsAndEnds()
    {
        var request = DisplayRequest.Start("PC Companion tests: checking display requests.");

        Assert.NotNull(request);
        request.Dispose();
        request.Dispose();
    }
}
