using System.Collections;
using System.Globalization;
using System.Linq;
using Xunit;

namespace PCCompanion.Localization.Tests;

public sealed class StringResourceTests
{
    [Fact]
    public void EnglishStringsAreNotEmpty()
    {
        var resources = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);

        Assert.NotNull(resources);
        var empty = resources.Cast<DictionaryEntry>()
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value as string))
            .Select(entry => (string)entry.Key);
        Assert.Empty(empty);
    }
}
